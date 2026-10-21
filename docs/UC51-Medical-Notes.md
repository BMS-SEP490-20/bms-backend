# UC51 — Add Medical Notes (I3-010 / I3-011)

## Phạm vi và cách dùng

Đăng nhập tài khoản Doctor, chọn **Ghi Chú Y Khoa** trong menu web hoặc nút cùng tên trong chi tiết bệnh án. Trang `/doctor/medical-notes?recordId=<ID hoặc mã EMR>` tải bệnh án thật, cho nhập ghi chú tối đa 5.000 ký tự và xem lịch sử phân trang.

Phạm vi triển khai: bác sĩ phụ trách (`MedicalRecord.DoctorId` bằng account ID trong JWT) được đọc/thêm ghi chú. Admin, Manager, bệnh nhân và các vai trò khác không được gọi API UC51. Tài liệu hiện có không đặc tả trường riêng cho UC51; phiên bản này dùng nội dung văn bản tự do, gắn bắt buộc với một bệnh án có sẵn.

Ghi chú là phần bổ sung bất biến: không thay thế nội dung chẩn đoán/kê đơn, không sửa hoặc xóa ghi chú cũ. Có thể bổ sung vào bệnh án cũ mà không mở lại quyền sửa bệnh án sau 24 giờ. Khi cần đính chính, thêm ghi chú mới.

## Dữ liệu và API

Tái sử dụng `IMedicalRecordService` và dữ liệu bệnh án/bệnh nhân/bác sĩ đã có. Các trường prescriptionNotes, treatmentPlan trong EMR không được dùng làm lịch sử ghi chú vì không lưu được từng lần ghi và tác giả. Collection mới `medical_notes` chỉ chứa các phần bổ sung; MongoDB tạo collection khi ghi lần đầu. Không seed hoặc tạo bản sao bệnh nhân/bác sĩ; không dùng fallback demo.

- `GET /api/medical-notes?recordId=...&page=1&pageSize=20`: bệnh án và lịch sử. PageSize tối đa 50.
- `POST /api/medical-notes`: `{ "recordId": "...", "content": "...", "submissionId": "UUID" }`.
- Giữ alias `/api/MedicalNotes` cho tên endpoint cũ. Placeholder cũ chưa cung cấp dữ liệu; đầu vào nay dùng recordId thay vì patientId.
- Server tự lấy patientId từ EMR, authorId/name từ phiên đăng nhập và thời gian UTC. Response chỉ trả DTO.
- UUID được lưu làm `_id`, giúp retry cùng nội dung không tạo trùng. Tái dùng UUID với nội dung/bệnh án/tác giả khác trả 409.
- Index compound `(recordId ASC, createdAt DESC, _id DESC)` phục vụ lịch sử; không thay đổi schema hoặc index collection cũ.
- Repository/service mới đăng ký tại Infrastructure/DependencyInjection.cs và Application/DependencyInjection.cs.

## Kiểm tra

```powershell
dotnet build src/BMS.Api/BMS.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/BMS.UC51.Checks --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Trong repository web: `npm ci`, sau đó `npm run build`.

Bộ checks là chương trình kiểm tra hành vi không cần thư viện test mới: validate, ownership, retry, pagination và HTTP 401/403/400/404/409/201/200 qua controller thật với dependencies giả lập. Test auth handler chỉ tồn tại trong project checks; không được đăng ký trong ứng dụng. Không ghi dữ liệu bệnh nhân hoặc database thật.

Cần kiểm tra tích hợp trên môi trường phát triển với MongoDB thật: chọn EMR thuộc bác sĩ đăng nhập, lưu, tải lại trang để xác nhận persistence, kiểm tra lịch sử và thử tài khoản khác. Build/checks dùng fake repository không xác nhận kết nối hoặc quyền ghi MongoDB của môi trường triển khai.

Không thay đổi mobile, không commit/push tự động, không cập nhật trạng thái backlog thành hoàn thành chỉ dựa trên build.
