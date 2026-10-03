# Industrial Vision System - Camera Basler, Light Controller & PLC LS

Hệ thống điều khiển thị giác công nghiệp (C# .NET 8 WPF MVVM) tích hợp:
- **Basler Camera** (GigE / USB3)
- **Rsee Light Controller** (PW-D-24W20-8TE qua Ethernet LAN)
- **LS PLC** (XGB Series - XBM-DN32HP qua FEnet / XGT Dedicated TCP)
- **Module OCR / AI**

## Cấu trúc Solution
- `IndustrialVision.Core`: Interfaces, Models, Configurations, Enums, Exceptions.
- `IndustrialVision.Infrastructure`: Configuration Loader & Validation, Logging.
- `IndustrialVision.Camera`: Camera Service abstraction & Mock.
- `IndustrialVision.Light`: Rsee Light Controller driver (ASCII protocol qua LAN) & Mock.
- `IndustrialVision.Plc`: LS XGT Dedicated protocol driver, Handshake & Trigger monitor.
- `IndustrialVision.Ocr`: OCR integration layer & Mock.
- `IndustrialVision.Workflow`: Machine State Machine & Cycle orchestration.
- `IndustrialVision.App`: WPF MVVM HMI Application.
- `tools/XgtSelfCheck`: Tool kiểm tra khung truyền giao thức XGT Dedicated.

## Yêu cầu môi trường
- .NET 8.0 SDK
- Windows 10/11 x64

## Chạy ứng dụng
```powershell
dotnet run --project src/IndustrialVision.App
```
