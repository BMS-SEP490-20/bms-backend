# UC56 — Manage Inventory (I3-025 Backend / I3-026 Web)

## Phạm vi và sử dụng

Backend + Web hỗ trợ View, Create, Update và Delete trên `/inventory`. Mobile đã được bỏ khỏi phạm vi. Mỗi repository giữ commit View cũ và có thêm đúng ba commit riêng cho Create, Update, Delete.

Quyền xem: Admin/Administrator, Manager, WarehouseStaff, LabHead và các tên vai trò KTV hiện có (LabTech, LabTechnician, LaboratoryUser, LabUser). Quyền tạo/sửa/xóa: Admin/Administrator, Manager, WarehouseStaff. Backend kiểm tra độc lập với menu và route web; không dùng cơ chế preview ẩn danh của permission handler chung.

Sau đăng nhập, WarehouseStaff vào trang kho. Dùng **Thêm lô hóa chất** để tạo. Mở chi tiết lô để **Chỉnh sửa lô** hoặc **Xóa lô**. Biểu mẫu giữ lỗi validation và nội dung nhập nếu server từ chối; khi 409, đóng biểu mẫu và mở lại chi tiết để lấy phiên bản mới nhất.

## Dữ liệu tái sử dụng

- Dùng `reagents` với các field có sẵn: `_id` chuỗi, reagent_code, reagent_name, manufacturer, lot_number, quantity_in_stock, unit, min_threshold, expiry_date.
- Dùng `inventory_transactions` cho lịch sử giao dịch cũ theo reagent_code, tối đa 20 giao dịch mới nhất và tổng count. Collection này không lưu số lô, nên giao diện ghi rõ lịch sử theo mã hóa chất, không suy diễn FIFO/ngày nhập từng lô.
- Mỗi lô bổ sung `revision`, `changes`, `is_deleted`, `deleted_at`. Lô cũ không có các field này vẫn đọc được: revision 0, chưa xóa, nhật ký rỗng. Không backfill hoặc thay dữ liệu cũ.
- Mỗi phần tử `changes` có action, actor ID/name do server lấy từ JWT, thời gian UTC, lý do, snapshot trước/sau (tên, hãng, tồn, ngưỡng, hạn dùng). DTO nhật ký công khai tên tác giả/lý do/thời gian/số lượng, không trả account ID nội bộ.
- Nhật ký được nhúng cùng document: insert hoặc update stock/revision/audit là một thao tác nguyên tử, không phụ thuộc transaction nhiều collection. Không ghi stock adjustment vào `inventory_transactions` rồi tạo nguy cơ lệch giữa hai collection; nhật ký quản lý mới và giao dịch cũ có hai mục riêng trên UI.
- Index không unique: `(expiry_date, reagent_code, _id)`, `(reagent_code, lot_number)` và `(reagent_code, timestamp DESC, _id DESC)` của transaction. Không migration phá hủy dữ liệu.

## Hợp đồng API

- `GET /api/inventory` (alias `/api/reagents`): search, status, expiryFrom, expiryTo, page, pageSize. Search literal theo mã/tên/lô/hãng, tối đa 100 ký tự; page 1–10.000, pageSize 1–100. Có summary của toàn bộ kết quả lọc.
- `GET /api/inventory/{id}`: chi tiết lô đang hoạt động, revision, nhật ký quản lý và lịch sử giao dịch cũ.
- `POST /api/inventory`: code, lotNumber, unit, name, manufacturer, quantity, minimumStock, expiryDate, reason. Trả 201 và Location.
- `PUT /api/inventory/{id}`: name, manufacturer, quantity, minimumStock, expiryDate, reason, expectedRevision. Trả lô cập nhật với revision tăng 1.
- `DELETE /api/inventory/{id}` với JSON body `{ expectedRevision, reason }`: xóa mềm lô rỗng, trả 204.
- 400 cho validation; 401 chưa đăng nhập/không có identity; 403 sai quyền; 404 lô thiếu/đã xóa; 409 cho trùng identity, phiên bản cũ, hoặc xóa lô còn tồn.

## Quy tắc quản lý

- Mã hóa chất và số lô chuẩn hóa chữ hoa. ID mới là SHA-256 của cặp code/lot chuẩn hóa: unique `_id` sẵn có chặn concurrent create; trước insert cũng kiểm tra trùng với các ID legacy. Không ép unique index mới có thể làm lỗi dữ liệu lịch sử.
- Code, lotNumber và unit không đổi sau tạo để giữ liên kết lịch sử và ý nghĩa số lượng. Các giá trị này không có trong DTO Update.
- Tên/hãng/hạn dùng bắt buộc cho thao tác ghi. Quantity/minimumStock từ 0 đến 1.000.000.000, tối đa ba chữ số thập phân. Reason sau trim từ 5 đến 500 ký tự. Có thể ghi nhận lô đã hết hạn để phản ánh thực tế kho, nhưng UI vẫn cảnh báo không sử dụng cho xét nghiệm.
- Update là kiểm kê/điều chỉnh giá trị tồn, không phải quy trình nhập/xuất kho đầy đủ. Ghi snapshot trước/sau và lý do; không tạo chức năng cấp phát hoặc giả định đã trừ hóa chất cho máy xét nghiệm.
- Update/Delete dùng expectedRevision trong filter tại thời điểm ghi. Query cũng yêu cầu lô chưa xóa; Delete kiểm tra lại tồn = 0 trong cùng atomic update. Các writer khác sửa lô trong tương lai phải cùng tuân thủ revision.
- Chỉ xóa khi tồn bằng 0, có lý do, đúng phiên bản. Không xóa vật lý; dữ liệu/audit được giữ. Lô đã xóa không có trong danh sách, summary hoặc GET chi tiết hoạt động; code/lot đó vẫn được giữ chỗ, không tạo lại để thay thế lịch sử.
- Ngày cảnh báo theo phòng khám UTC+7. Hạn dùng còn hiệu lực hết ngày lịch đó; EXPIRED trước hôm nay; EXPIRING_SOON từ hôm nay đến dưới 30 ngày; AVAILABLE còn ít nhất 30 ngày (chỉ trạng thái hạn dùng); UNKNOWN_EXPIRY chưa có ngày. Tồn thấp khi quantity <= min_threshold, độc lập với hạn dùng.
- Khoảng lọc hạn dùng bao gồm hai đầu; thứ tự expiry_date/code/id là thứ tự xem, không phải quy trình xuất FIFO. Query rows/count riêng không phải snapshot nhiều document.

## Kiểm thử và tích hợp

```powershell
dotnet build src/BMS.Api/BMS.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/BMS.UC56.Checks --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Bộ checks cần MongoDB ở 127.0.0.1:27017, tạo fixture riêng `BMS_UC56_Checks_<UUID>` rồi dọn đúng database đó. Kho thật chỉ được đọc để kiểm tra mapping; không tạo/sửa/xóa stock thật. Có 17 nhóm kiểm tra bao gồm lọc/ngưỡng/phân trang, HTTP CRUD/role/validation, concurrent create/update/delete, audit, legacy revision thiếu, xóa mềm và không hồi sinh lô đã xóa.

Web dùng `npm run build`; manifest không có lint/test script. Chưa xác minh giao diện trực quan qua trình duyệt. Trước merge cần thử với tài khoản kho và KTV: tạo lô → sửa tồn về 0 kèm lý do → mở lại chi tiết → xóa → xác nhận không còn trong danh sách; thử phiên bản cũ và tài khoản chỉ xem. Reviewer: mrhoquockhanh; assignee: CE191059PhuongGiaLac. Người dùng merge thủ công.
