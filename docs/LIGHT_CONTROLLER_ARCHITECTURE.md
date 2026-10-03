# TÀI LIỆU KỸ THUẬT & GIẢI THÍCH MÃ NGUỒN HỆ THỐNG LIGHT CONTROLLER
**Dự án:** Industrial Vision System (C# .NET 8 WPF MVVM)  
**Thiết bị thực tế:** Rsee PW-D-24W20-8TE (8 Kênh CH1–CH8, Giao tiếp LAN TCP/IP)

---

## 1. TỔNG QUAN KIẾN TRÚC (ARCHITECTURE OVERVIEW)

Hệ thống điều khiển đèn được thiết kế theo nguyên lý **Clean Architecture** và **Dependency Inversion (SOLID)**:
- Tầng giao diện (UI / ViewModel) **không bao giờ phụ thuộc trực tiếp** vào driver phần cứng hay Socket TCP.
- Tầng ViewModel chỉ giao tiếp thông qua abstraction interface [`ILightController`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Interfaces/ILightController.cs).
- Việc hoán đổi giữa thiết bị thật [`RseeLightController`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Light/RseeLightController.cs) và thiết bị giả lập [`MockLightController`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Light/MockLightController.cs) được thực hiện hoàn toàn tại tầng Dependency Injection ([`ServiceRegistration.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/ServiceRegistration.cs)).

```
┌────────────────────────────────────────────────────────┐
│                       MainWindow.xaml                  │
│   (Network Toolbar: IP, Port, Ping Button, 8 CH Cards) │
└───────────────────────────▲────────────────────────────┘
                            │ Data Binding
┌───────────────────────────┴────────────────────────────┐
│                      MainViewModel                     │
│    (LightChannels, PingLightCommand, SaveSettings)     │
└───────────────────────────▲────────────────────────────┘
                            │ Calls Interface
┌───────────────────────────┴────────────────────────────┐
│                     ILightController                   │
│        (ConnectAsync, SetChannelAsync, TurnOn/Off)     │
└─────────────▲────────────────────────────▲─────────────┘
              │ Implements                 │ Implements
┌─────────────┴──────────┐   ┌─────────────┴─────────────┐
│   RseeLightController  │   │     MockLightController   │
│ (TCP Socket Client LAN)│   │        (In-Memory)        │
└─────────────▲──────────┘   └───────────────────────────┘
              │ RJ45 LAN (TCP/IP)
┌─────────────┴──────────────────────────────────────────┐
│      Bộ điều khiển Rsee PW-D-24W20-8TE (8 Kênh)         │
└────────────────────────────────────────────────────────┘
```

---

## 2. DANH SÁCH CÁC FILE LIÊN QUAN VÀ GIẢI THÍCH CHI TIẾT

### 2.1. Tầng Abstraction & Models (IndustrialVision.Core)

#### 1. [`ILightController.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Interfaces/ILightController.cs)
*   **Vị trí:** `src/IndustrialVision.Core/Interfaces/ILightController.cs`
*   **Nhiệm vụ:** Định nghĩa hợp đồng (contract) chung cho mọi bộ điều khiển đèn trong hệ thống.
*   **Các thành phần chính:**
    *   `ConnectionStatus Status { get; }`: Trạng thái kết nối hiện tại (`Disconnected`, `Connecting`, `Connected`, `Ready`, `Error`).
    *   `bool IsConnected { get; }`: Cờ kiểm tra nhanh xem thiết bị đã sẵn sàng gửi lệnh hay chưa.
    *   `event EventHandler<ConnectionStatus> StatusChanged`: Event bắn ra khi trạng thái mạng thay đổi để UI tự động cập nhật màu sắc đèn báo.
    *   `ConnectAsync(CancellationToken)`: Kết nối tới phần cứng.
    *   `DisconnectAsync()`: Ngắt kết nối và giải phóng tài nguyên.
    *   `SetChannelAsync(int channel, int intensity, CancellationToken)`: Đặt độ sáng cho kênh (kênh 1–8, độ sáng 0–255).
    *   `TurnOnAsync(int channel, CancellationToken)`: Bật kênh đèn.
    *   `TurnOffAsync(int channel, CancellationToken)`: Tắt kênh đèn.
    *   `TurnOffAllAsync(CancellationToken)`: Tắt đồng loạt cả 8 kênh.
    *   `int ChannelCount { get; }`, `GetChannelIntensity(int channel)`, `IsChannelOn(int channel)`: Các hàm truy vấn trạng thái kênh.

#### 2. [`LightConfiguration.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Configuration/LightConfiguration.cs)
*   **Vị trí:** `src/IndustrialVision.Core/Configuration/LightConfiguration.cs`
*   **Nhiệm vụ:** Lớp ánh xạ cấu hình từ file `Config/light.json`.
*   **Các thuộc tính:**
    *   `IpAddress`: Địa chỉ IP bộ điều khiển trên mạng LAN (ví dụ: `192.168.1.100`).
    *   `Port`: Cổng TCP lắng nghe của bộ điều khiển (ví dụ: `5000`).
    *   `Protocol`: Bộ tập lệnh giao tiếp (`ASCII_WORDOP`, `ASCII_OPT`, `HEX`).
    *   `ChannelCount`: Số lượng kênh phần cứng (8 kênh).
    *   `DefaultIntensity`: Độ sáng mặc định khi khởi động.
    *   `CommandTimeoutMs`: Thời gian chờ socket phản hồi (mili-giây).
    *   `Channels`: Danh sách cấu hình chi tiết cho từng kênh (tên kênh, độ sáng ban đầu, trạng thái kích hoạt).

#### 3. [`IndustrialVisionExceptions.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Exceptions/IndustrialVisionExceptions.cs)
*   **Vị trí:** `src/IndustrialVision.Core/Exceptions/IndustrialVisionExceptions.cs`
*   **Nhiệm vụ:** Định nghĩa lớp ngoại lệ chuyên biệt `LightControllerException` kế thừa từ `IndustrialVisionException`. Mọi lỗi rớt mạng socket, lỗi timeout, sai kênh đều được đóng gói thành ngoại lệ này kèm thông tin chi tiết.

---

### 2.2. Tầng Driver Phần Cứng (IndustrialVision.Light)

#### 4. [`RseeLightController.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Light/RseeLightController.cs)
*   **Vị trí:** `src/IndustrialVision.Light/RseeLightController.cs`
*   **Nhiệm vụ:** Driver điều khiển trực tiếp phần cứng **Rsee PW-D-24W20-8TE** qua kết nối mạng LAN.
*   **Cơ chế hoạt động:**
    1.  **Giao tiếp Socket TCP/IP Client:**
        *   Sử dụng `System.Net.Sockets.TcpClient` và `NetworkStream`.
        *   Thiết lập `NoDelay = true` (thuật toán Nagle tắt) để các gói tin điều khiển đèn xuất đi tức thì, không bị trễ khung hình kiểm tra thị giác.
        *   Quản lý timeout kết nối thông qua `CancellationTokenSource`.
    2.  **Đảm bảo an toàn đa luồng (Thread-Safety):**
        *   Sử dụng `SemaphoreSlim _lock = new(1, 1)` bọc quanh các thao tác ghi/đọc socket. Nhờ đó, nếu PLC kích hoạt chụp ảnh và người dùng cùng bấm nút trên UI cùng lúc thì các gói tin TCP vẫn được xếp hàng truyền đi tuần tự, không bao giờ bị đan xen hay hỏng frame.
    3.  **Hỗ trợ đa giao thức tập lệnh công nghiệp (Multi-Protocol):**
        *   **Chuẩn ASCII WORDOP (Mặc định cho Rsee / KST / Wordop):**
            *   Mỗi kênh 1..8 tương ứng với ký tự `A` đến `H`.
            *   Lệnh đặt độ sáng: `S<KýTự><ĐộSáng4ChữSố>#` (Ví dụ: Kênh 1 đặt độ sáng 100 -> `SA0100#`, Kênh 8 đặt độ sáng 255 -> `SH0255#`).
            *   Lệnh bật kênh: Gửi giá trị độ sáng mục tiêu + `SH<Kênh>#`.
            *   Lệnh tắt kênh: Gửi `S<KýTự>0000#` + `SL<Kênh>#`.
        *   **Chuẩn ASCII OPT:** Sử dụng cấu trúc `$3<Kênh><ĐộSáng4ChữSố>#` (Ví dụ `$310100#`), bật `$1x#`, tắt `$2x#`.
        *   **Chuẩn HEX (Nhị phân):** Gửi frame byte `[0xAA, 0x01, channel, intensity, checksum, 0x55]`.
    4.  **Cơ chế phản hồi không nghẽn luồng:**
        *   Phương thức `SendAndReceiveBytesAsync` tự động kiểm tra `_stream.DataAvailable`. Nếu bộ điều khiển có trả lời (ví dụ `OK`, `$`), driver sẽ đọc và ghi debug log; nếu thiết bị không gửi phản hồi (chế độ write-only), hệ thống tiếp tục hoạt động mà không bị chặn (non-blocking).

---

### 2.3. Tầng Cấu Hình & Tiêm Phụ Thuộc (Infrastructure & App DI)

#### 5. [`light.json`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/Config/light.json)
*   **Vị trí:** `src/IndustrialVision.App/Config/light.json`
*   **Nhiệm vụ:** Lưu trữ toàn bộ thông số mạng và 8 kênh đèn dưới dạng JSON.
```json
{
  "Light": {
    "IpAddress": "192.168.1.100",
    "Port": 5000,
    "Protocol": "ASCII_WORDOP",
    "ChannelCount": 8,
    "DefaultIntensity": 100,
    "CommandTimeoutMs": 3000,
    "Channels": [
      { "Channel": 1, "Name": "CH1", "Intensity": 100, "Enabled": true },
      ...
      { "Channel": 8, "Name": "CH8", "Intensity": 100, "Enabled": true }
    ]
  }
}
```

#### 6. [`ServiceRegistration.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/ServiceRegistration.cs)
*   **Vị trí:** `src/IndustrialVision.App/ServiceRegistration.cs`
*   **Nhiệm vụ:** Khởi tạo IoC Container của Microsoft Dependency Injection.
*   **Điểm thay đổi quan trọng:**
    *   Đăng ký `services.AddSingleton<ILightController, RseeLightController>();` cho cả chế độ thông thường và chế độ mô phỏng các phần cứng khác.
    *   Giúp ứng dụng sẵn sàng test đèn thật mà không phụ thuộc vào việc Camera hay PLC đã có mặt hay chưa.

---

### 2.4. Tầng Giao Diện Người Dùng (WPF MVVM)

#### 7. [`LightChannelViewModel.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/ViewModels/LightChannelViewModel.cs)
*   **Vị trí:** `src/IndustrialVision.App/ViewModels/LightChannelViewModel.cs`
*   **Nhiệm vụ:** Đại diện cho trạng thái của từng kênh đèn (1 trong 8 kênh) trên giao diện.
*   **Tính năng:**
    *   Thuộc tính `Intensity`: Hỗ trợ Two-Way Data Binding với Slider. Khi người dùng kéo Slider, nếu kênh đang bật và đã kết nối, giá trị mới sẽ được truyền ngay lập tức xuống đèn qua `ApplyIntensityAsync()`.
    *   Thuộc tính `IsOn`: Theo dõi trạng thái Bật/Tắt của kênh.
    *   Lệnh `ToggleCommand`: Đảo trạng thái ON/OFF của cổng đèn.

#### 8. [`MainViewModel.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/ViewModels/MainViewModel.cs)
*   **Vị trí:** `src/IndustrialVision.App/ViewModels/MainViewModel.cs`
*   **Nhiệm vụ:** Điều phối toàn bộ nghiệp vụ kiểm tra và quản lý giao tiếp điều khiển đèn.
*   **Các thành phần điều khiển đèn & mạng:**
    *   `LightIpAddress` & `LightPort`: Cho phép người dùng xem và trực tiếp chỉnh sửa IP / Port ngay trên màn hình.
    *   `PingLightCommand` -> `PingLightAsync()`: Sử dụng thư viện `System.Net.NetworkInformation.Ping` để bắn gói tin ICMP tới IP của bộ điều khiển, đo thời gian phản hồi (roundtrip latency) và cập nhật `PingResultText`.
    *   `SaveLightSettingsCommand` -> `SaveLightSettingsAsync()`: Cập nhật IP/Port vào cấu hình và tự động ghi đè file `Config/light.json`.
    *   `LightChannels`: Danh sách `ObservableCollection<LightChannelViewModel>` chứa đúng 8 kênh CH1..CH8.
    *   `TurnOnAllLightsCommand` & `TurnOffAllLightsCommand`: Bật / Tắt toàn bộ 8 kênh bằng 1 click.
    *   `CycleTestLightsCommand`: Lệnh test tuần tự: tự động bật và tắt lần lượt từ CH1 -> CH8 (mỗi cổng 400ms) để kỹ thuật viên kiểm tra bằng mắt thường sự hoạt động của từng đèn.

#### 9. [`MainWindow.xaml`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/Views/MainWindow.xaml)
*   **Vị trí:** `src/IndustrialVision.App/Views/MainWindow.xaml`
*   **Bố cục giao diện bảng đèn (Light Controller Panel):**
    *   **Thanh tiêu đề:** Tên bộ điều khiển `💡 LIGHT CONTROLLER (Rsee PW-D-24W20-8TE • 8CH LAN)` + Đèn báo trạng thái kết nối.
    *   **Thanh công cụ mạng & Ping (Row 1):** Ô nhập IP, ô nhập Port, nút `📡 PING`, Badge hiển thị kết quả Ping (Xanh/Đỏ/Thời gian phản hồi), nút `💾 SAVE IP`.
    *   **Hàng nút thao tác nhanh:** `🔌 CONNECT`, `⚡ TEST CH1-8`, `☀ ALL ON`, `🌑 ALL OFF`.
    *   **Lưới 8 kênh (Row 2 - UniformGrid 8 cột):** Mỗi cột là 1 card điều khiển gồm tên kênh (CH1..CH8), độ sáng hiện tại `/255`, thanh trượt Slider mượt mà và nút bấm ON/OFF chuyển màu linh hoạt.

#### 10. [`Converters.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/Resources/Converters.cs)
*   **Vị trí:** `src/IndustrialVision.App/Resources/Converters.cs`
*   **Nhiệm vụ:** Chuyển đổi dữ liệu từ ViewModel thành hiển thị trực quan trên XAML:
    *   `BoolToColorConverter`: Kênh bật -> màu xanh lá `#4CAF50`, kênh tắt -> màu xám `#313244`.
    *   `BoolToOnOffTextConverter`: Chuyển `true` -> chữ "ON", `false` -> chữ "OFF".
    *   `NullableBoolToColorConverter`: Kết quả Ping thành công -> Xanh lá, thất bại -> Đỏ, chưa ping -> Xám.
