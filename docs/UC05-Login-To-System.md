# UC05 — Log In to System

## 1. Tiêu chuẩn áp dụng & Cơ sở lý thuyết
- **OWASP ASVS 5.0 (Application Security Verification Standard):**
  - **V2: Authentication Verification Standard:** Yêu cầu về độ phức tạp mật khẩu, băm mật khẩu an toàn (không dùng plain text / MD5 / SHA-1), chống user enumeration / timing attacks.
  - **V3: Session Management Standard:** Quản lý vòng đời JWT token (thời hạn ngắn cho access token), refresh token rotation (RTR), thu hồi phiên và vô hiệu hóa sau đăng xuất.
  - **V4: Access Control Standard:** Thực thi nguyên tắc Zero Trust / Default Deny tại tầng API; phân quyền chặt chẽ theo RBAC (Role-Based Access Control) và PBAC (Permission-Based Access Control).
- **ISO/IEC 27001:2022:**
  - **Control 5.15 & 5.18:** Kiểm soát truy cập và phân định quyền truy cập tối thiểu (Least Privilege).
  - **Control 5.16 & 5.17:** Quản lý danh tính và bảo vệ thông tin xác thực người dùng.
  - **Control 8.5:** Cơ chế xác thực an toàn, chống tấn công dò đoán mật khẩu tự động.
  - **Control 8.15:** Logging & Audit trail ghi vết các sự kiện đăng nhập, cảnh báo xâm nhập.

---

## 2. Ma trận Rủi ro & Giải pháp triển khai trong BMS

| Rủi ro bảo mật | Mối nguy hại | Giải pháp triển khai cụ thể trong BMS | Vị trí code / Kỹ thuật áp dụng |
| :--- | :--- | :--- | :--- |
| **Lộ mật khẩu plain text** | Đánh cắp CSDL dẫn đến lộ mật khẩu hàng loạt | Tuyệt đối không lưu plain text. Sử dụng **BCrypt** hashing với salt ngẫu nhiên và cost factor tiêu chuẩn. | `BCryptPasswordHasher.cs`, `AuthService.cs` |
| **Dò mật khẩu liên tục (Brute Force)** | Tấn công từ điển, brute force tài khoản | 1. Rate Limiting trên endpoint `api/auth/login`<br>2. Khóa tạm thời 15 phút sau 5 lần nhập sai liên tiếp (`BR-IAM-03`)<br>3. Chống Timing Attack bằng `VerifyDummy()` khi tài khoản không tồn tại | `[EnableRateLimiting("login")]`, `AuthService.cs` (LockoutEnd, FailedLoginCount) |
| **Liệt kê tài khoản (User Enumeration)** | Kẻ tấn công phân biệt được username tồn tại hay không | Trả về thông báo lỗi chung chung (Generic error): *"Tên đăng nhập hoặc mật khẩu không chính xác. Vui lòng thử lại!"* (HTTP 401). Không trả về "User not found". | `AuthController.cs`, `AuthService.cs` |
| **Truy cập trái quyền (Privilege Escalation)** | Người dùng gọi API của role khác (VD: Bệnh nhân gọi API Admin) | Triển khai **Default-Deny fallback policy** tại tầng API. Mỗi endpoint được bảo vệ bởi `[Authorize(Roles = "...")]` hoặc `[HasPermission(...)]`. Client chỉ có vai trò hiển thị UI. | `ServiceCollectionExtensions.cs`, `PermissionAuthorization.cs` |
| **Token bị giả mạo / Đánh cắp** | Sửa đổi payload role trong JWT hoặc dùng token sau khi thu hồi | 1. Backend xác thực chữ ký số HMAC-SHA256, Issuer, Audience, Expiration.<br>2. Thu hồi Refresh Token khi Logout; cơ chế Refresh Token Rotation (RTR) phát hiện token reuse. | `JwtBearerOptions`, `RefreshTokenRepository.cs`, `AuthService.LogoutAsync` |
| **Thiếu dấu vết vi phạm (Lack of Audit Trail)** | Không xác định được nguồn gốc tấn công hoặc xâm nhập | Ghi nhận toàn bộ sự kiện `LOGIN_SUCCESS` và `LOGIN_FAILED` kèm `UserId`, `Timestamp`, `IpAddress`, `UserRole`, `Action` vào bảng/collection `audit_logs`. | `_auditLogRepository.AddAsync` |

---

## 3. Phân quyền RBAC trong BMS (Role-Based Access Control)

| Vai trò (Role) | Mã Role | Quyền truy cập & Nghiệp vụ dự kiến |
| :--- | :---: | :--- |
| **Patient** | Role 9 | Xem thông tin cá nhân, lịch sử khám bệnh, tra cứu kết quả xét nghiệm của chính mình, đặt lịch khám trực tuyến. |
| **Receptionist** | Role 6 | Tiếp nhận bệnh nhân, tạo/cập nhật hồ sơ bệnh nhân hành chính, cấp số thứ tự Smart Queue, điều phối phòng khám. |
| **Doctor** | Role 2 | Tiếp nhận khám lâm sàng, tra cứu tiền sử bệnh án EMR, chỉ định xét nghiệm (ICD-10), xem kết quả Lab, kết luận tư vấn, ghi chú y khoa (UC51). |
| **Lab Technician** | Role 3 | Tiếp nhận mẫu xét nghiệm, quét barcode ống máu, chạy máy phân tích, cập nhật trạng thái mẫu, nhập kết quả chỉ số xét nghiệm, chạy IQC. |
| **Manager / Lab Head** | Role 4 | Thẩm định chuyên môn kết quả xét nghiệm, ký số duyệt báo cáo, xem báo cáo tổng hợp, quản trị chất lượng phòng xét nghiệm. |
| **Administrator** | Role 1 | Quản trị cấu hình hệ thống, quản lý tài khoản/phân quyền nhân viên, cấu hình tham số bảo mật, kiểm tra nhật ký kiểm toán (Audit Trail). |

> **Nguyên tắc cốt lõi về phân quyền:**
> Frontend (React) ẩn/hiện nút bấm, menu chỉ nhằm mục đích tối ưu hóa trải nghiệm người dùng (UX). **Toàn bộ quyết định kiểm soát truy cập và bảo mật đều phải được thẩm định độc lập tại Backend thông qua JWT Claims và Middleware Authorization.**

---

## 4. Kịch bản kiểm thử bảo mật (Security Test Cases)

| Mã Test Case | Tên kịch bản | Điều kiện đầu vào | Kết quả mong đợi |
| :--- | :--- | :--- | :--- |
| **SEC-LOGIN-01** | Đăng nhập hợp lệ | Gửi `POST /api/auth/login` với username/password chính xác | HTTP 200 OK; Trả về Access Token, Refresh Token, User Profile (Role, FullName); Ghi Audit Log `LOGIN_SUCCESS`. |
| **SEC-LOGIN-02** | Thử mật khẩu sai liên tiếp (Brute Force) | Gửi `POST /api/auth/login` với mật khẩu sai liên tục 5 lần | 4 lần đầu trả HTTP 401 Unauthorized (thông báo lỗi chung); Đến lần thứ 5, tài khoản bị khóa trong 15 phút, trả HTTP 423 Locked kèm `RetryAfterMinutes: 15`. |
| **SEC-LOGIN-03** | Sử dụng JWT hết hạn | Gửi request đến protected API (VD: `GET /api/users/profile`) với Access Token đã quá hạn | Backend từ chối với HTTP 401 Unauthorized (`IDX10223: Lifetime validation failed`). |
| **SEC-LOGIN-04** | Vi phạm phân quyền vai trò (RBAC) | Đăng nhập tài khoản `Patient`, lấy JWT rồi gửi request gọi API quản trị (VD: `GET /api/v1/admin/users`) | Backend từ chối với HTTP 403 Forbidden; Chặn đứng hành vi leo thang đặc quyền. |
| **SEC-LOGIN-05** | Giả mạo / Sửa đổi JWT (Tampering) | Thay đổi payload của JWT (sửa claim `role` từ `Patient` thành `Administrator`), giữ nguyên signature cũ | Backend xác thực chữ ký HMAC-SHA256 thất bại, trả về HTTP 401 Unauthorized. |
| **SEC-LOGIN-06** | Tái sử dụng Refresh Token sau khi đăng xuất | Đăng nhập lấy cặp Token -> Gọi `POST /api/auth/logout` -> Dùng Refresh Token vừa đăng xuất gọi `POST /api/auth/refresh-token` | Backend phát hiện token đã bị thu hồi (Revoked), từ chối cấp token mới và trả về HTTP 401 Unauthorized. |
