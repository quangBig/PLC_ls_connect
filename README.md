# IndustrialVision — Basler Camera, Rsee Light và PLC LS

Ứng dụng C# .NET 8 WPF x64 điều khiển camera, đèn và giao tiếp PLC LS.

## Tài liệu luồng code

Đọc [Luồng code từng thiết bị và truyền thông PLC](docs/LUONG_CODE_THIET_BI_VA_PLC.md).
Tài liệu đối chiếu source ngày 07/10/2026, có bảng địa chỉ, sơ đồ, thứ tự handshake,
tên hàm và liên kết đến dòng code cho camera, lens, đèn, PLC và OCR.

## Build và chạy

Yêu cầu Windows x64, .NET 8 SDK; camera thật cần Basler pylon runtime 11 x64 và driver
GigE/USB3 phù hợp với binding Basler.Pylon.NET8.x64 11.2.1.755 trong project Camera.
LIVE nhận ảnh liên tục cũng sử dụng SDK/runtime này. Ứng dụng dùng đường dẫn DLL
trong môi trường Windows sau khi cài pylon và không tự sửa PATH. Máy người dùng đã
có runtime x64 và đường dẫn tương ứng trong PATH; khi chạy trên máy khác cần bảo
đảm các thành phần này được cài đúng.

Từ thư mục PLC_ls_connect:

```powershell
dotnet build IndustrialVision.sln -c Release -m:1 -nr:false
dotnet run --project src/IndustrialVision.App -c Release
```

Đóng desktop cũ trước khi build vào cùng output để tránh file bị khóa.
Ứng dụng đọc Config cạnh file chạy. SAVE SETTINGS trong UI ghi vào bản Config đó;
cấu hình source và cấu hình cạnh exe có thể khác nhau.

## Luồng đang có

PLC Trigger 0→1 → bật kênh đèn đã chọn → chụp → tắt đèn → lấy OK/NG → ghi Result →
Complete=1 → chờ PLC hạ Trigger → Complete=0 → Result=0.

| Tín hiệu | XG5000 | Địa chỉ XGT | Hướng |
|---|---|---|---|
| Trigger | D01040.0 | %DX16640 | C# đọc từ PLC |
| Complete | D02040.2 | %DX32642 | C# ghi về PLC |
| Result: 0=trống, 1=OK, 2=NG | D02050 | %DW2050 | C# ghi về PLC |
| Heartbeat riêng | M0030F | %MX495 | C# ghi và đọc lại |

## Trạng thái tích hợp

- Camera và PLC dùng driver thật khi SimulationMode=false; đèn Rsee dùng driver thật
  ở cả hai chế độ SimulationMode.
- `MockCameraService` chỉ tạo ảnh giả khi SimulationMode=true. Cấu hình hiện là false,
  nên LIVE/CAPTURE sử dụng `BaslerCameraService`. Driver thật có các phần quét thiết
  bị, mở kết nối, cài tham số, nhận LIVE, chụp một ảnh, đổi dữ liệu ảnh và giải phóng
  tài nguyên; xem mục 3.4 và 5 trong tài liệu luồng code để đọc từng phần.
- OCR hiện vẫn giả lập. TEST OK/NG kiểm tra chu trình/handshake bằng verdict đặt trước.
- AUTO hiện cần xác nhận/map Vision Ready: Addresses.Ready đang trống trong khi
  RequiresReadySignal=true. Cần xác nhận vai trò D02040.0 và điều kiện M01000 ở PLC.
- Thiết bị đèn người dùng đang dùng là 192.168.110.10:9000; source light.json còn
  192.168.1.100:5000. Kiểm tra cấu hình cạnh exe hoặc nhập/lưu IP/port đúng trên UI.
- Bộ test, tool CLI, bản build thử và khung Workflow chưa dùng đã được bỏ theo yêu cầu.
  Kiểm tra giao tiếp tay/heartbeat vẫn có trong tab PLC của ứng dụng.

Solution giữ 7 project: App, Core, Infrastructure, Camera, Light, Plc và Ocr.
Luồng điều phối thực tế nằm trong MainViewModel.ExecuteInspectionCycleAsync.

Sau khi dọn, solution đã được build Release thành công: **0 warnings, 0 errors**.
