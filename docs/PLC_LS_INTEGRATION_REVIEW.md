# TÀI LIỆU KỸ THUẬT & GIẢI THÍCH MÃ NGUỒN TÍCH HỢP PLC LS

**Dự án:** Industrial Vision System (C# .NET 8 WPF MVVM)  
**Phân hệ:** Điều khiển & Truyền thông PLC LS (Industrial Automation Handshake)  
**Tài liệu tham chiếu:** [LIGHT_CONTROLLER_ARCHITECTURE.md](file:///d:/ATS/CameraBasler-LightControl-PLC/docs/LIGHT_CONTROLLER_ARCHITECTURE.md)

---

## MỤC LỤC
1. [Tổng quan Kiến trúc Phân tầng (Architecture Overview)](#1-tổng-quan-kiến-trúc-phân-tầng)
2. [Các Nội dung Code hỗ trợ Kiểm tra (What the Code Tests)](#2-các-nội-dung-code-hỗ-trợ-kiểm-tra)
3. [Danh mục các File Mã nguồn & Giải thích Chi tiết (Source Code Breakdown)](#3-danh-mục-các-file-mã-nguồn--giải-thích-chi-tiết)
4. [Nguyên lý Xử lý Kỹ thuật Trọng yếu (Key Engineering Principles)](#4-nguyên-lý-xử-lý-kỹ-thuật-trọng-yếu)
5. [Quy trình Handshake Chu kỳ Kiểm tra (Inspection Cycle Handshake)](#5-quy-trình-handshake-chu-kỳ-kiểm-tra)
6. [Hướng dẫn Thực hành Test (Thử nghiệm Offline & Online)](#6-hướng-dẫn-thực-hành-test)
7. [Các bước Cấu hình khi nhận Thông tin Phần cứng Thật](#7-các-bước-cấu-hình-khi-nhận-thông-tin-phần-cứng-thật)

---

## 1. TỔNG QUAN KIẾN TRÚC PHÂN TẦNG

Module PLC được thiết kế theo chuẩn **Clean Architecture** và **Dependency Inversion Principle (SOLID)**, đảm bảo:
* **Không phụ thuộc cứng (Decoupling)**: Tầng giao diện UI/ViewModel không gọi trực tiếp socket hay byte protocol của PLC.
* **Không đoán mò thông số (No Guesswork)**: Mọi thông số (Model, Protocol, Địa chỉ ô nhớ Bit/Word/String) đều được đưa vào cấu hình `plc.json` với tiền tố tường minh `NEEDS_PLC_INFORMATION`, sẵn sàng hoán đổi driver khi có tài liệu chính thức từ khách hàng.
* **Tách bạch nhiệm vụ (Separation of Concerns)**:
  * Driver tầng thấp ([`ILsPlcDriver`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Drivers/ILsPlcDriver.cs)): Chỉ chuyên gửi/nhận frame byte qua mạng.
  * Dịch vụ điều phối ([`PlcService`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/PlcService.cs)): Xử lý timeout, retry (lỗi đường truyền), thread-safety. Lỗi do **request sai** (địa chỉ sai / PLC từ chối `NAK`) ném `PlcRequestException`: không retry và **không** làm trạng thái PLC chuyển sang `Error`. *Lưu ý: chưa có cơ chế tự kết nối lại ở tầng service (xem mục 2.A.3).*
  * Dịch vụ bắt xung ([`PlcTriggerMonitor`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Trigger/PlcTriggerMonitor.cs)): Luồng ngầm độc lập bắt sườn lên $0 \rightarrow 1$ với chu kỳ 20ms.
  * Dịch vụ nhịp tim ([`PlcHeartbeatService`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Heartbeat/PlcHeartbeatService.cs)): Luồng ngầm định kỳ gửi xung sống 1s.
  * Dịch vụ liên động ([`PlcHandshakeService`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Handshake/PlcHandshakeService.cs)): Điều khiển logic Ready, Busy, Capture Complete, OK, NG, Error.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        MainWindow.xaml (HMI)                           │
│  - Live Signals: [READY] [TRG] [BUSY] [CAPT] [OK] [NG] [ERR] [BEAT]    │
│  - Tab "PLC LS CONTROL & MANUAL TEST": Đọc/Ghi Bit, Word, String, Trg  │
└───────────────────────────────────▲────────────────────────────────────┘
                                    │ Data Binding & RelayCommands
┌───────────────────────────────────┴────────────────────────────────────┐
│                             MainViewModel                              │
│       - Quản lý trạng thái, Command thủ công, Chu trình Auto/Manual    │
└───────────────▲───────────────────▲───────────────────▲────────────────┘
                │                   │                   │
┌───────────────┴────────┐ ┌────────┴──────────┐ ┌──────┴────────────────┐
│  IPlcHandshakeService  │ │ PlcTriggerMonitor │ │  PlcHeartbeatService  │
│  (Ready, Busy, OK/NG)  │ │ (Rising Edge 0->1)│ │ (Watchdog 1s Toggle)  │
└───────────────▲────────┘ └────────▲──────────┘ └──────▲────────────────┘
                │                   │                   │
                └───────────────────┼───────────────────┘
                                    │ Gọi IPlcService
┌───────────────────────────────────┴────────────────────────────────────┐
│                              IPlcService                               │
│       (ReadBitAsync, WriteBitAsync, ReadWordAsync, WriteStringAsync)   │
└───────────────────────────▲───────────────────▲────────────────────────┘
                            │                   │
         ┌──────────────────┴──┐             ┌──┴──────────────────┐
         │     PlcService      │             │   MockPlcService    │
         │ (Industrial Engine) │             │ (In-Memory Testing) │
         └──────────▲──────────┘             └─────────────────────┘
                    │
┌───────────────────┴──────────────────┐
│             ILsPlcDriver             │
│ (Low-level LS Socket Protocol Layer) │
└───────────────────▲──────────────────┘
                    │
         ┌──────────┴───────────────┐
         │ LsXgtDedicatedDriver (XGT, thật) │ <- Protocol = XGT_DEDICATED
         │ LsPlcPlaceholderDriver (giữ chỗ) │ <- Protocol chưa cấu hình
         └──────────────────────────┘
```

---

## 2. CÁC NỘI DUNG CODE HỖ TRỢ KIỂM TRA (WHAT THE CODE TESTS)

Toàn bộ hệ thống mã nguồn đã cài đặt sẵn các kịch bản test sau, người dùng có thể thao tác trực tiếp trên giao diện:

### A. Kiểm tra Truyền thông & Kết nối (Connection & Health Test)
1. **Test Kết nối / Ngắt kết nối (`ConnectPlcCommand`, `DisconnectPlcCommand`)**:
   - Khởi tạo socket TCP/IP tới địa chỉ IP & Port cấu hình.
   - Bật cờ `READY = 1` thông báo cho PLC biết PC đã mở phần mềm và sẵn sàng.
   - Khi ngắt kết nối: Tự động hạ `READY = 0`, dừng toàn bộ luồng giám sát ngầm.
2. **Test Nhịp tim duy trì sự sống (`PlcHeartbeatService`)**:
   - Gửi định kỳ xung đảo bit ($0 \rightarrow 1 \rightarrow 0 \rightarrow 1$) hoặc đếm tăng dần (Counter) xuống địa chỉ Heartbeat cấu hình.
   - Đèn `[BEAT]` trên giao diện nhấp nháy xanh đồng bộ theo mỗi nhịp gửi thành công.
3. **Mất kết nối (hành vi thực tế của code)**:
   - Driver `LsXgtDedicatedDriver` tự đóng socket khi gặp lỗi đường truyền và **kết nối lại lười (lazy)** ở lần gọi kế tiếp.
   - Tuy nhiên `PlcService` sẽ đặt `Status = Error` sau khi hết `RetryCount`; khi đó `PlcTriggerMonitor`/`PlcHeartbeatService` (chỉ chạy khi `IsConnected`) sẽ đứng yên. **Chưa có auto-reconnect**: cần bấm `DISCONNECT` rồi `CONNECT PLC` lại. *(Hạn chế đã biết, cần bổ sung nếu lên máy thật.)*

### B. Kiểm tra Đọc/Ghi Thủ công Từng Ô nhớ (Manual Diagnostics Tool)
Ngay trên Tab **"⚡ PLC LS CONTROL & MANUAL TEST"**:
1. **Test Đọc/Ghi Bit (Boolean 0/1)**:
   - Nhập địa chỉ theo **tên biến XGT đầy đủ** bắt đầu bằng `%` (ví dụ dạng `%MX..`). Dạng viết tắt như `M100` sẽ bị **từ chối**, không đoán.
   - Tick/bỏ tick ô **Value = 1 (ON)** rồi bấm **Write Bit**; bấm **Read Bit** để đọc.
2. **Test Đọc/Ghi Word (Số nguyên 16-bit `ushort`)**:
   - Nhập địa chỉ word (dạng `%MW..`, `%DW..`), nhập số vào ô **Word**.
   - Bấm **Read Word** (hiển thị kèm Hex) / **Write Word**.
3. **Test Đọc/Ghi Chuỗi ký tự (String/ASCII Text)**:
   - Địa chỉ dạng byte `%DB..` hoặc word `%DW..` (word n được đổi thành byte 2n); nhập `Len` (số byte đọc).
   - Bấm **Read Str** / **Write Str**. *Giả định cần xác nhận với người lập trình PLC:* ghi ASCII + ký tự kết thúc NUL, đệm cho chẵn số byte.

### C. Kiểm tra Bắt xung Trigger Sườn lên (Rising Edge Detection Test)
1. **Test Bắt sườn lên $0 \rightarrow 1$**:
   - Background Polling Task đọc chân Trigger mỗi $20\text{ ms}$.
   - Khi trạng thái chuyển từ $0$ lên $1$, cờ `[TRG]` sáng cam, event `TriggerFired` lập tức kích hoạt chu kỳ chụp và xử lý OCR.
2. **Test Chống chụp lặp (Anti-Double Capture / Arming mechanism)**:
   - Sau khi bắt được sườn lên, biến `_isArmed` được đặt về `false`.
   - Dù PLC có giữ chân Trigger = 1 trong bao lâu thì máy chỉ chụp **đúng 1 lần duy nhất**.
   - Chỉ khi PLC hạ chân Trigger về 0 (Falling Edge), luồng mới re-arm sẵn sàng cho lần chụp tiếp theo.

### D. Kiểm tra Chu trình Phối hợp Handshake Tự động (Interlock Workflow Test)
Bấm nút **[⚡ TRIGGER CYCLE]** trên Tab PLC hoặc nút **[⚡ TRIGGER ONCE]** trên thanh Controls để kích hoạt toàn bộ chuỗi:
1. **Bước 1**: Clear cờ kết quả cũ (`OK = 0`, `NG = 0`, `CaptureComplete = 0`), kéo cờ `BUSY = 1`.
2. **Bước 2**: Kích hoạt bộ điều khiển đèn Rsee bật các kênh sáng định cấu hình.
3. **Bước 3**: Camera Basler chụp ảnh khung hình.
4. **Bước 4**: Bật cờ `CAPTURE COMPLETE = 1` (Báo cho cơ cấu băng tải/trục xoay của PLC có thể dịch chuyển phôi đi tiếp ngay mà không cần đợi AI xử lý).
5. **Bước 5**: Tắt đèn (tiết kiệm nhiệt và tuổi thọ LED).
6. **Bước 6**: Gọi AI/OCR nhận diện ký tự từ ảnh.
7. **Bước 7**:
   - Nếu OCR đọc chuẩn $\rightarrow$ Kích cờ `OK = 1`, tăng biến đếm `OkCount`.
   - Nếu OCR đọc sai/không ra chữ $\rightarrow$ Kích cờ `NG = 1`, tăng biến đếm `NgCount`.
   - Ghi chuỗi văn bản nhận diện được vào thanh ghi Result của PLC (nếu cấu hình).
8. **Bước 8**: Giữ cờ kết quả $100\text{ ms}$ (Result Hold Time), sau đó hạ cờ `BUSY = 0`, xác nhận `READY = 1`.

---

## 3. DANH MỤC CÁC FILE MÃ NGUỒN & GIẢI THÍCH CHI TIẾT

Toàn bộ các file liên quan đến PLC được tổ chức chuyên biệt, phân định rõ ràng giữa Hợp đồng (Interfaces), Mô hình dữ liệu (Models), Driver kết nối và Logic ứng dụng (Handshake & Trigger):

| STT | Tên File | Vị trí thư mục | Nhiệm vụ chính |
| :--- | :--- | :--- | :--- |
| 1 | [`IPlcService.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Interfaces/IPlcService.cs) | `Core/Interfaces/` | Hợp đồng giao tiếp cấp cao: Định nghĩa các hàm `ConnectAsync`, `DisconnectAsync`, `ReadBitAsync`, `WriteBitAsync`, `ReadWordAsync`, `WriteWordAsync`, `ReadStringAsync`, `WriteStringAsync`. |
| 2 | [`PlcAddressMap.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Models/PlcAddressMap.cs) | `Core/Models/` | Bảng ánh xạ địa chỉ ô nhớ: Chứa địa chỉ của 10 tín hiệu liên động (`Ready`, `Trigger`, `Busy`, `CaptureComplete`, `Result`, `OK`, `NG`, `Error`, `Reset`, `Heartbeat`). |
| 3 | [`PlcConfiguration.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Configuration/PlcConfiguration.cs) | `Core/Configuration/` | Lớp cấu hình nạp từ `plc.json`: Quản lý IP, Port, Model, Protocol, Timeout, RetryCount, Heartbeat và AddressMap. |
| 4 | [`ILsPlcDriver.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Drivers/ILsPlcDriver.cs) | `Plc/Drivers/` | Giao diện driver tầng thấp: Trừu tượng hóa việc đóng/mở Socket và đóng gói Byte Frame theo giao thức của hãng LS. |
| 5 | [`LsPlcPlaceholderDriver.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Drivers/LsPlcPlaceholderDriver.cs) | `Plc/Drivers/` | Driver giữ chỗ an toàn: Báo lỗi có chủ đích nếu `Protocol` trong `plc.json` chưa được cấu hình/không được hỗ trợ. |
| 5b | [`LsXgtDedicatedDriver.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Drivers/LsXgtDedicatedDriver.cs) | `Plc/Drivers/` | **Driver thật** cho PLC LS giao thức *XGT Dedicated* (FEnet, TCP, port 2004): dựng frame 20 byte header (`LSIS-XGT`, BCC), lệnh đọc `0x54`/ghi `0x58`, kiểu Bit/Word/Continuous (chuỗi). **Không hardcode IP/Port/địa chỉ**; địa chỉ thiếu `%` bị từ chối; PLC trả `NAK` được báo kèm mã hex thô (`[PLC_NAK] ... errorCode=0x..`). Chọn tự động trong `ServiceRegistration` khi `Protocol` = `XGT_DEDICATED`. **Viết theo tài liệu giao thức, chưa kiểm chứng trên PLC thật.** |
| 6 | [`MockPlcService.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/MockPlcService.cs) | `Plc/` | Giả lập PLC trong bộ nhớ: Lưu trữ trạng thái Bit và Word trong Dictionary `ConcurrentDictionary`, cho phép test toàn bộ giao diện và chu trình khi chưa có phần cứng. |
| 7 | [`PlcService.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/PlcService.cs) | `Plc/` | Điều phối viên trung tâm: Kiểm tra địa chỉ hợp lệ, quản lý luồng gửi/nhận an toàn (`SemaphoreSlim`), tự động thử lại khi timeout (`RetryCount`), phân loại mã lỗi công nghiệp (`PLC_TIMEOUT`, `PLC_CONNECTION_ERROR`). |
| 8 | [`IPlcHandshakeService.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Handshake/IPlcHandshakeService.cs) | `Plc/Handshake/` | Giao diện điều khiển cờ liên động: `SetReadyAsync`, `SetBusyAsync`, `SetCaptureCompleteAsync`, `SetResultOkAsync`, `SetResultNgAsync`, `SetErrorAsync`, `WriteOcrResultAsync`. |
| 9 | [`PlcHandshakeService.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Handshake/PlcHandshakeService.cs) | `Plc/Handshake/` | Hiện thực logic bắt tay: Đọc cấu hình từ `PlcAddressMap` để ghi đúng ô nhớ, bỏ qua an toàn nếu địa chỉ chưa cấu hình. |
| 10 | [`PlcTriggerMonitor.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Trigger/PlcTriggerMonitor.cs) | `Plc/Trigger/` | Luồng quét Trigger độc lập: Sử dụng vòng lặp bất đồng bộ chu kỳ cao ($20\text{ ms}$) phát hiện sườn lên $0 \rightarrow 1$ và tự động giải phóng khi tắt máy. |
| 11 | [`PlcHeartbeatService.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/Heartbeat/PlcHeartbeatService.cs) | `Plc/Heartbeat/` | Luồng nhịp tim độc lập: Bắn tín hiệu đảo trạng thái (Toggle) hoặc đếm tăng dần (Counter) định kỳ mỗi 1000ms. |
| 12 | [`plc.json`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/Config/plc.json) | `App/Config/` | File cấu hình JSON: Khai báo IP, Port và bảng địa chỉ sạch sẽ không bị hardcode. |
| 13 | [`MainViewModel.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/ViewModels/MainViewModel.cs) | `App/ViewModels/` | ViewModel trung tâm: Tiếp nhận event từ `PlcTriggerMonitor`, điều phối chu trình `ExecuteInspectionCycleAsync`, cung cấp các thuộc tính và lệnh test cho UI. |
| 14 | [`MainWindow.xaml`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/Views/MainWindow.xaml) | `App/Views/` | Giao diện người máy HMI: Hiển thị 8 đèn tín hiệu I/O thời gian thực và tích hợp Tab điều khiển & test PLC thủ công. |

---

## 4. NGUYÊN LÝ XỬ LÝ KỸ THUẬT TRỌNG YẾU

### A. Thuật toán Bắt Xung Sườn Lên (Rising Edge) trong `PlcTriggerMonitor.cs`
Trong môi trường thực tế, PLC kích hoạt cờ Trigger thường giữ tín hiệu trong $50 - 200\text{ ms}$ hoặc lâu hơn tùy chu kỳ quét (Scan Cycle) của PLC. Nếu đọc liên tục mà không có cơ chế chốt sườn lên, ứng dụng sẽ chụp ảnh liên tục hàng chục lần cho cùng một sản phẩm.

```csharp
bool currentState = await _plc.ReadBitAsync(address, token);

// 1. Sườn lên (Rising Edge): Trước đó là 0, hiện tại là 1, và đã sẵn sàng (Armed)
if (!_lastState && currentState && _isArmed)
{
    _isArmed = false; // Ngắt chốt ngay lập tức -> Chống chụp lặp
    TriggerFired?.Invoke(this, EventArgs.Empty);
}
// 2. Sườn xuống (Falling Edge): Trước đó là 1, hiện tại đã hạ về 0 -> Tái kích hoạt
else if (_lastState && !currentState)
{
    _isArmed = true; // Sẵn sàng đón sản phẩm tiếp theo
}

_lastState = currentState;
```

### B. Cơ chế Tự bảo vệ khi Chạy Đa luồng (Thread-Safety & Race Condition)
Cả `PlcTriggerMonitor` (quét liên tục), `PlcHeartbeatService` (bắn nhịp tim mỗi giây), và các thao tác bấm nút thủ công trên màn hình đều gọi vào cùng một kết nối PLC. Nếu 2 luồng cùng ghi socket đồng thời, gói tin mạng sẽ bị vỡ vụn.

Giải pháp trong [`PlcService.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Plc/PlcService.cs):
```csharp
private readonly SemaphoreSlim _lock = new(1, 1);

public async Task WriteBitAsync(string address, bool value, CancellationToken cancellationToken)
{
    await _lock.WaitAsync(cancellationToken);
    try
    {
        await ExecuteWithRetryAsync(() => _driver.WriteBitAsync(address, value, cancellationToken), cancellationToken);
    }
    finally
    {
        _lock.Release();
    }
}
```
Khóa nhị phân `SemaphoreSlim(1,1)` đảm bảo **chỉ duy nhất một yêu cầu được phép gửi trên đường truyền socket tại một thời điểm**.

---

## 5. QUY TRÌNH HANDSHAKE CHU KỲ KIỂM TRA

Sơ đồ thời gian (Timing Diagram) của chu trình kiểm tra:

```text
PLC Trigger:  ─────┐┌───────────────────────────────
                   ││
PC Ready:     ─────┘│                       ┌───────
                    │                       │
PC Busy:            ┌───────────────────────┘
                    │
Light ON:           ┌─────┐
                    │     │
Camera Capture:     │ ┌───┐
                    │ │   │
Capture Complete:   │ └───┼───────┐
                    │     │       │ (PLC có thể di chuyển phôi đi tiếp)
OCR AI Engine:      │     └───────┼────────┐
                    │             │        │
Result (OK/NG):     │             │        ┌───────┐
                    │             │        │       │ (Hold 100ms)
                    └─────────────┴────────┴───────┘
```

1. **Chuẩn bị**: PC Ready = `ON`. PLC đưa phôi vào vị trí chụp.
2. **Kích hoạt**: PLC bật Trigger = `ON`.
3. **Tiếp nhận**: PC phát hiện sườn lên $\rightarrow$ Kéo Ready = `OFF`, kéo Busy = `ON`.
4. **Bật đèn & Chụp**: Bật kênh đèn cấu hình $\rightarrow$ Delay ổn định ánh sáng 50ms $\rightarrow$ Chụp ảnh $\rightarrow$ Tắt đèn.
5. **Mở khóa phôi**: PC bật Capture Complete = `ON`. PLC lập tức biết phôi đã chụp xong và có thể chuẩn bị nhả phôi, **giúp giảm đáng kể Tact Time của máy**.
6. **Xử lý AI**: Module OCR phân tích ảnh, trích xuất chuỗi ký tự.
7. **Trả kết quả**: PC kích hoạt cờ tương ứng: `OK = ON` hoặc `NG = ON`, đồng thời ghi chuỗi ký tự đọc được vào thanh ghi dữ liệu của PLC.
8. **Hoàn tất chu kỳ**: Giữ cờ kết quả 100ms để PLC chốt dữ liệu, sau đó giải phóng Busy = `OFF`, bật lại Ready = `ON`.

---

## 6. HƯỚNG DẪN THỰC HÀNH TEST

### Chế độ 1: Thử nghiệm Giả lập Offline (Không cần cắm PLC thật)
1. Mở file [`appsettings.json`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/Config/appsettings.json), đặt:
   ```json
   "SimulationMode": true
   ```
2. Khởi chạy ứng dụng:
   ```powershell
   $env:PATH = "C:\Users\TV AMG 2026\AppData\Local\Microsoft\dotnet;" + $env:PATH
   dotnet run --project src/IndustrialVision.App
   ```
3. Trên màn hình chính:
   * Bấm nút **[▶ CONNECT ALL]**: Đèn trạng thái `PLC` và `CAMERA` chuyển màu xanh **READY**.
   * Trên panel **PLC SIGNALS**: Đèn `[READY]` bật màu xanh lá cây.
   * Chuyển sang Tab **"⚡ PLC LS CONTROL & MANUAL TEST"**:
     * Nhập địa chỉ `M100`, bấm **Write Bit** $\rightarrow$ Kết quả báo `Wrote [M100] = 1`.
     * Bấm **Read Bit** $\rightarrow$ Kết quả báo `Bit [M100] = 1 (ON)`.
     * Nhập địa chỉ `D200`, giá trị `5678`, bấm **Write Word** $\rightarrow$ Bấm **Read Word** xác nhận `5678 (0x162E)`.
   * Bấm nút **[⚡ TRIGGER CYCLE]**:
     * Quan sát đèn nháy tuần tự: `[BUSY]` (Xanh dương) $\rightarrow$ `[CAPT]` (Xanh lơ) $\rightarrow$ `[OK]` (Xanh lá).
     * Màn hình log hiển thị chi tiết thời gian thực hiện chu trình (vài chục ms).

### Chế độ 2: Thử nghiệm Online với PLC LS Thật (XGB XBM-DN32HP qua LAN)

Cấu hình hiện tại: `Model = XGB XBM-DN32HP`, `Protocol = XGT_DEDICATED`, `Port = 2004`, `SimulationMode = false`. **IP không có sẵn trong file — nhập trên giao diện.** Camera và OCR vẫn là Mock; chỉ PLC và đèn Rsee là thật.

1. **Chuẩn bị PLC**: trong XG5000 đặt IP cho cổng Ethernet tích hợp của XBM rồi ghi xuống PLC (Write). Cắm cáp LAN PC ↔ PLC (hoặc qua switch). Đặt IP card mạng PC cùng dải, cùng Subnet với PLC.
2. `ping <IP PLC>` từ Command Prompt phải thông.
3. Chạy phần mềm:
   ```powershell
   $env:PATH = "C:\Users\TV AMG 2026\AppData\Local\Microsoft\dotnet;" + $env:PATH
   dotnet run --project src/IndustrialVision.App
   ```
4. Tab **PLC LS CONTROL & MANUAL TEST** → nhập **PLC IP**, **Port** (2004) → **PING** (badge xanh) → **SAVE IP** (tùy chọn, ghi vào `Config/plc.json` bản chạy) → **CONNECT PLC**.
   - Địa chỉ tín hiệu (Ready/Trigger/…) trong `plc.json` còn trống nên khi kết nối chỉ mở TCP, chưa ghi bit nào xuống PLC.
5. **Chỉ ĐỌC trước**: nhập một địa chỉ biết chắc an toàn (tên biến `%...` đầy đủ) → **Read Word / Read Bit**.
6. Chỉ **GHI** vào vùng M/D **chưa dùng**. Tuyệt đối không ghi vào ngõ ra vật lý (`%PX…`) đang nối cơ cấu chấp hành.
7. Nếu lỗi `[PLC_NAK] ... errorCode=0x...` → chụp lại thông báo/log (`Logs/`) để xác định nguyên nhân (sai tên biến, vùng nhớ không tồn tại, …).

> [!NOTE]
> BCC của header, CPU-info và độ dài mã lỗi NAK được cài theo tài liệu giao thức và **chưa được kiểm chứng trên phần cứng thật**. Công cụ [`tools/XgtSelfCheck`](file:///d:/ATS/CameraBasler-LightControl-PLC/tools/XgtSelfCheck/Program.cs) chạy driver với một *fake server* tự viết chỉ để kiểm tra tính tự nhất quán của code, không thay thế test trên PLC thật.

---

## 7. CÁC BƯỚC CẤU HÌNH KHI NHẬN THÔNG TIN PHẦN CỨNG THẬT

Khi kỹ sư lập trình PLC bàn giao bảng địa chỉ (Address Map), bạn chỉ cần điền vào `plc.json`. **Không cần sửa code**: driver được chọn theo trường `Protocol`.

### Cập nhật file `plc.json`
Đường dẫn file: `src/IndustrialVision.App/Config/plc.json`
```json
{
  "PLC": {
    "Model": "XGB XBM-DN32HP",
    "Protocol": "XGT_DEDICATED",
    "IpAddress": "<IP thực tế của PLC>",
    "Port": 2004,
    "Addresses": {
      "Ready": "<tên biến XGT do kỹ sư PLC cung cấp>",
      "Trigger": "<...>",
      "Busy": "<...>",
      "CaptureComplete": "<...>",
      "OK": "<...>",
      "NG": "<...>",
      "Error": "<...>",
      "Reset": "<...>",
      "Result": "<...>"
    },
    "Heartbeat": { "Enabled": true, "Address": "<...>", "IntervalMs": 1000, "Mode": "Toggle" }
  }
}
```
* Giá trị `NEEDS_*` hoặc để trống = tín hiệu **chưa cấu hình**: [`PlcAddressMap`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.Core/Models/PlcAddressMap.cs) tự đổi thành chuỗi rỗng và mọi service sẽ bỏ qua tín hiệu đó (không ghi chuỗi placeholder xuống PLC).
* Giao thức khác (Modbus TCP, …) cần viết thêm một class implement `ILsPlcDriver` và thêm điều kiện chọn trong [`ServiceRegistration.cs`](file:///d:/ATS/CameraBasler-LightControl-PLC/src/IndustrialVision.App/ServiceRegistration.cs). Toàn bộ ViewModel, UI, Handshake, Monitor giữ nguyên.
