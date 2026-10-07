# Industrial Vision System - Camera Hikrobot, Light Controller & PLC LS

Hệ thống điều khiển thị giác công nghiệp (C# .NET 8 WPF MVVM) tích hợp:
- **Hikrobot Camera** (GigE / USB3 qua MVS SDK, giống `TestLight_Cam`)
- **Rsee Light Controller** (PW-D-24W20-8TE qua Ethernet LAN)
- **LS PLC** (XGB Series - XBM-DN32HP qua FEnet / XGT Dedicated TCP)
- **Module OCR / AI**

## Cấu trúc Solution
- `IndustrialVision.Core`: Interfaces, Models, Configurations, Enums, Exceptions.
- `IndustrialVision.Infrastructure`: Configuration Loader & Validation, Logging.
- `IndustrialVision.Camera`: Hikrobot MVS Camera Service & Mock.
- `IndustrialVision.Light`: Rsee Light Controller driver (ASCII protocol qua LAN) & Mock.
- `IndustrialVision.Plc`: LS XGT Dedicated protocol driver, Handshake & Trigger monitor.
- `IndustrialVision.Ocr`: OCR integration layer & Mock.
- `IndustrialVision.Workflow`: Machine State Machine & Cycle orchestration.
- `IndustrialVision.App`: WPF MVVM HMI Application.
- `tools/XgtSelfCheck`: Tool kiểm tra khung truyền giao thức XGT Dedicated.

## Yêu cầu môi trường
- .NET 8.0 SDK
- Windows 10/11 x64
- Hikrobot MVS x64 runtime/driver (cùng bộ SDK đang dùng cho `TestLight_Cam`). DLL .NET đã nằm trong `src/IndustrialVision.Camera/Libs`.

## Chạy ứng dụng
```powershell
dotnet run --project src/IndustrialVision.App
```

## Kết nối camera
- `Config/appsettings.json`: đặt `System.SimulationMode = false` để dùng camera Hikrobot thật; `true` dùng camera giả lập.
- Mở tab **CAMERA (HIKROBOT)** → **DISCOVER** → chọn camera theo model/serial/IP → **CONNECT**.
- **LIVE** xem liên tục, **STOP LIVE** khôi phục trigger trong `camera.json`, **CAPTURE** chụp một ảnh (Software trigger mặc định). Chụp kiểm tra sẽ dừng Live để luồng xem trước không lấy mất ảnh trigger.
- Sửa **Exposure (µs)** / **Gain (dB)** rồi nhấn **APPLY**; **SAVE SETTINGS** lưu serial/IP và thông số vào `Config/camera.json` cạnh file chạy.
- `camera.json` hỗ trợ `TriggerMode` (`On`/`Off`), `TriggerSource` (`Software` hoặc line do camera hỗ trợ), `PixelFormat`, `Width`, `Height`, `FrameRate`, `TimeoutMs`. Để trống `PixelFormat` và đặt kích thước/frame rate bằng 0 để dùng mặc định của camera.
- Có thể cấu hình serial/IP trước khi chạy. Khi có nhiều camera mà chưa chọn thiết bị, dịch vụ yêu cầu chọn camera để tránh kết nối nhầm.
