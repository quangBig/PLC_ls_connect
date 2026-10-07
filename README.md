# Industrial Vision System - Camera Basler, Light Controller & PLC LS

Hệ thống điều khiển thị giác công nghiệp (C# .NET 8 WPF MVVM) tích hợp:
- **Basler Camera** (GigE / USB3 qua pylon .NET SDK)
- **Rsee Light Controller** (PW-D-24W20-8TE qua Ethernet LAN)
- **LS PLC** (XGB Series - XBM-DN32HP qua FEnet / XGT Dedicated TCP)
- **Module OCR / AI**

## Cấu trúc Solution
- `IndustrialVision.Core`: Interfaces, Models, Configurations, Enums, Exceptions.
- `IndustrialVision.Infrastructure`: Configuration Loader & Validation, Logging.
- `IndustrialVision.Camera`: Basler pylon Camera Service & Mock.
- `IndustrialVision.Light`: Rsee Light Controller driver (ASCII protocol qua LAN) & Mock.
- `IndustrialVision.Plc`: LS XGT Dedicated protocol driver, Handshake & Trigger monitor.
- `IndustrialVision.Ocr`: OCR integration layer & Mock.
- `IndustrialVision.Workflow`: Machine State Machine & Cycle orchestration.
- `IndustrialVision.App`: WPF MVVM HMI Application.
- `tools/XgtSelfCheck`: Tool kiểm tra khung truyền giao thức XGT Dedicated.

## Yêu cầu môi trường
- .NET 8.0 SDK
- Windows 10/11 x64
- Project dùng NuGet [Basler.Pylon.NET8.x64 11.2.1.755](https://www.nuget.org/packages/Basler.Pylon.NET8.x64/11.2.1.755); `dotnet build` / `dotnet run` tự restore thư viện .NET x64.
- Máy chạy camera thật cần cài **Basler pylon 11 Runtime x64**, kèm driver GigE hoặc USB3 theo camera. **pylon Software Suite 25.10 / 25.11** cung cấp native SDK 11.2.x tương thích với NuGet trên; xem [release notes 25.10](https://docs.baslerweb.com/pylon-software-suite-25-10-release-notes) và [25.11](https://docs.baslerweb.com/pylon-software-suite-25-11-release-notes). NuGet chỉ cung cấp thư viện để build; không thay thế runtime và driver. Không thay bằng runtime pylon 12 khi vẫn dùng binding này. Xem [hướng dẫn cài pylon cho Windows](https://docs.baslerweb.com/software-installation-%28windows%29).
- Nếu triển khai bằng cách copy DLL, lấy các native DLL từ `<thư mục cài pylon>\Runtime\x64` của phiên bản tương thích và đặt cạnh file chạy; vẫn cần cài driver camera. Danh sách DLL phụ thuộc giao tiếp: [pylon Deployment Guide](https://docs.baslerweb.com/pylonapi/pylon-deployment-guide). Không dùng DLL x86 hoặc trộn DLL từ các phiên bản khác nhau.

## Chạy ứng dụng
```powershell
dotnet run --project src/IndustrialVision.App
```

## Kết nối camera
- `Config/appsettings.json`: đặt `System.SimulationMode = false` để dùng camera Basler thật; `true` dùng camera giả lập.
- Kiểm tra camera trong **pylon Viewer**, đóng kết nối camera trong Viewer, rồi mở tab **CAMERA (BASLER)** → **DISCOVER** → chọn camera theo model/serial/IP → **CONNECT**.
- **LIVE** xem liên tục, **STOP LIVE** khôi phục trigger trong `camera.json`, **CAPTURE** chụp một ảnh (Software trigger mặc định). Chụp kiểm tra sẽ dừng Live để luồng xem trước không lấy mất ảnh trigger.
- Sửa **Exposure (µs)** / **Gain** rồi nhấn **APPLY**; **SAVE SETTINGS** lưu serial/IP và thông số vào `Config/camera.json` cạnh file chạy. Gain dùng đơn vị của tham số camera: `Gain` / `GainAbs` thường là dB; model cũ chỉ hỗ trợ `GainRaw` dùng giá trị raw nguyên theo camera, không tự quy đổi từ dB.
- Khi kết nối model cũ dùng `GainRaw`, giá trị Gain mặc định 0 được đổi thành mức raw nhỏ nhất của camera và cập nhật lại trên giao diện.
- `camera.json` hỗ trợ `TriggerMode` (`On`/`Off`), `TriggerSource` (`Software` hoặc line do camera hỗ trợ), `PixelFormat`, `Width`, `Height`, `FrameRate`, `TimeoutMs`. Để trống `PixelFormat` và đặt kích thước/frame rate bằng 0 để dùng mặc định của camera.
- Có thể cấu hình serial/IP trước khi chạy. Khi có nhiều camera mà chưa chọn thiết bị, dịch vụ yêu cầu chọn camera để tránh kết nối nhầm.
- Nếu không dò thấy camera: kiểm tra pylon runtime/driver, nguồn và cáp; camera GigE cần cùng subnet với card mạng máy tính. Nếu báo thiếu DLL hoặc không tải được SDK, kiểm tra runtime pylon x64 tương thích và đường dẫn native DLL theo hướng dẫn triển khai ở trên.
