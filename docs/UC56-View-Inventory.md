# UC56 — View Inventory (I3-025 Backend / I3-026 Web)

## Phạm vi

UC56 chỉ đọc danh sách, cảnh báo và chi tiết kho hóa chất/sinh phẩm. Không thêm/sửa/xóa vật tư, nhập/xuất kho hoặc tự động trừ tồn. Mobile đã được loại khỏi phạm vi theo yêu cầu cập nhật của người dùng.

API cho Admin/Administrator, Manager, WarehouseStaff, LabHead và các tên vai trò KTV đang được sử dụng (LabTech, LabTechnician, LaboratoryUser, LabUser). Các vai trò Patient, Doctor, Receptionist, Nurse, Cashier và người chưa đăng nhập bị từ chối. Không dùng permission handler có cơ chế cho phép preview ẩn danh cho API này.

Web: đăng nhập nhân viên kho sẽ chuyển đến `/inventory`; các vai trò được phép khác mở từ menu **Kho Hóa Chất & Sinh Phẩm**. Trang có tìm kiếm debounce 300ms, cảnh báo, khoảng hạn dùng, xóa bộ lọc, tổng số kết quả, phân trang và modal chi tiết.

## Tái sử dụng database

Đã đối chiếu MongoDB cục bộ `BMS_Database`, có sẵn `reagents` (4 bản ghi tại lúc khảo sát) và `inventory_transactions` (3 bản ghi). Không seed hoặc sao chép dữ liệu thật sang collection mới.

- `reagents`: `_id` chuỗi, reagent_code, reagent_name, manufacturer, lot_number, quantity_in_stock, unit, min_threshold, expiry_date. Trường status cũ không được dùng để xác định cảnh báo vì có thể không còn đúng theo ngày hiện tại.
- `inventory_transactions`: reagent_code, transaction_type, quantity, unit_price, department, timestamp. Có performed_by trong DB nhưng không trả thông tin tài khoản nội bộ cho UI.
- Giao dịch hiện không có số lô: modal phải ghi rõ lịch sử là theo mã hóa chất và chỉ hiển thị 20 giao dịch mới nhất cùng tổng số giao dịch. Không suy diễn ngày nhập, FIFO hay lịch sử riêng từng lô.
- Index mới không unique: `(expiry_date, reagent_code, _id)`, `(reagent_code, lot_number)` và `(reagent_code, timestamp DESC, _id DESC)` của transaction. Không đổi dữ liệu/số lượng/giá trị status có sẵn.

## Hợp đồng và cảnh báo

- `GET /api/inventory` (alias `/api/reagents`): search, status, expiryFrom, expiryTo, page, pageSize. Tìm mã/tên/lô/hãng bằng literal text, không cho regex của người gọi chạy trực tiếp. Search tối đa 100 ký tự; page 1–10.000; pageSize 1–100.
- `GET /api/inventory/{id}`: chi tiết theo ID lô, trả 404 nếu không tồn tại.
- Status filter: LOW_STOCK, EXPIRED, EXPIRING_SOON, AVAILABLE, UNKNOWN_EXPIRY.
- Ngày cảnh báo tính theo ngày phòng khám UTC+7. Hạn dùng là ngày lịch được lưu trong expiry_date, còn hiệu lực đến hết ngày đó. EXPIRED: trước hôm nay; EXPIRING_SOON: từ hôm nay đến dưới 30 ngày; AVAILABLE: còn ít nhất 30 ngày (chỉ trạng thái hạn dùng, không khẳng định còn hàng); UNKNOWN_EXPIRY: chưa có ngày.
- Tồn thấp khi quantity_in_stock <= min_threshold (gồm hết hàng). Cờ tồn thấp độc lập với trạng thái hạn dùng. UI giữ nguyên số lượng và đơn vị, không cộng lẫn các đơn vị.
- Khoảng hạn dùng bao gồm cả ngày bắt đầu/kết thúc; chưa có ngày không thuộc các khoảng lọc ngày. Các bộ đếm là của toàn bộ tập kết quả đã lọc, không chỉ trang hiện tại.
- Thứ tự theo expiry_date, reagent_code, _id; đây là thứ tự xem hạn dùng, không phải chỉ định xuất kho FIFO.
- Không có fallback demo khi MongoDB/API lỗi. Các câu truy vấn danh sách/count riêng có thể quan sát thay đổi kho đồng thời, không phải snapshot giao dịch.

## Cấu trúc

- Core: `Entities/Reagent.cs`, `Interfaces/IInventoryRepository.cs`.
- Application: `DTOs/Inventory/InventoryDtos.cs`, `Interfaces/IInventoryService.cs`, `Services/InventoryService.cs`.
- Infrastructure: `Repositories/InventoryRepository.cs`.
- API: thay placeholder `Controllers/Inventory/ReagentsController.cs`.
- Service/repository đăng ký trong DI tương ứng; TimeProvider tái sử dụng đăng ký hệ thống hiện có.

## Kiểm tra

```powershell
dotnet build src/BMS.Api/BMS.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/BMS.UC56.Checks -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Bộ checks cần MongoDB cục bộ ở 127.0.0.1:27017. Tạo fixture ở database tên `BMS_UC56_Checks_<UUID>` rồi dọn chính database tạm đó; không thay đổi kho thật. Kiểm tra mốc ngày, tồn thấp bằng ngưỡng, tìm kiếm literal, lọc, phân trang, lịch sử mới nhất, HTTP auth/validation và phương thức chỉ đọc. Có bước đọc schema thật `BMS_Database` và tạo các index nói trên để phát hiện lỗi mapping.

Web dùng `npm run build`; manifest hiện không có lint/test script. Cần kiểm tra giao diện trên trình duyệt, quyền tài khoản thực và dữ liệu triển khai trước merge. Không khẳng định build thay thế kiểm thử giao diện.
