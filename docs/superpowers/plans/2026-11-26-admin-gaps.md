# Kế hoạch hoàn thiện Admin back-office

- Ngày lập: 2026-11-26. Revision đối chiếu: `da6c99c` (sau audit chỉ thêm form đổi mật khẩu candidate, không đụng admin).
- Nguồn yêu cầu: `docs/feature-audit-2026-11-23.md` (mục I, V, VI.2), `docs/specs/admin.md`, `sang.md` (mục 3 – Admin, mục 4 – thương mại).
- Đã đối chiếu lại code: `Services/Admin/AdminService.cs`, `IAdminService.cs`, `DTOs/Admin/AdminDtos.cs`, 5 trang `Components/Pages/Admin/*.razor`, `AdminSkillsController.cs`, model Billing/Job/Notification, `ApplicationDbContext`. **Kết luận của audit về Admin vẫn đúng 100% tại HEAD.**

## 1. Đối chiếu yêu cầu mới với hiện trạng

`sang.md` đánh dấu "ok" cho cả 5 mục Admin, nhưng code hiện chỉ đáp ứng ở mức tối thiểu:

| Yêu cầu (`sang.md`) | Hiện có | Thiếu so với audit/spec |
|---|---|---|
| Quản lý tài khoản ứng viên | Bảng chung mọi user + paging, khóa/mở khóa | Không tách theo vai trò, không tìm kiếm/filter, không xem chi tiết (profile, CV, đơn), không reset mật khẩu, **admin có thể tự khóa mình** (trái `specs/admin.md`) |
| Quản lý tài khoản nhà tuyển dụng | Như trên | Không xem công ty/membership của employer, không filter role Employer |
| Quản lý công ty | List + paging, approve/reject có lý do, lưu verifier | Không filter Pending/Approved/Rejected, không trang chi tiết, **không gửi notification kết quả duyệt** (spec yêu cầu), chính sách Rejected chưa nhất quán |
| Quản lý tin tuyển dụng | List + paging, xóa | Không filter công ty/trạng thái, **hard delete cascade xóa cả application/history/interview**, không lý do, không quản lý featured |
| Xem báo cáo thống kê | Dashboard đếm users/companies/jobs | Không có số đơn ứng tuyển, doanh thu, thống kê theo tháng/công ty |
| (Mục 4) Gói/thanh toán | Không có gì phía admin | Không CRUD plan, không danh sách payment, không báo cáo doanh thu, không cấp gói/featured thủ công |
| Khác (audit VI.2) | Skills chỉ có API; ML log/retrain đã có | Không UI Skills, không CRUD Category, không audit log, không broadcast, không xem đơn khi tranh chấp, không ẩn danh dữ liệu |

## 2. Quyết định cần chốt trước khi làm

Đã chốt ngày 2026-11-26: cả 4 mục theo đề xuất (mục 1 chọn **ẩn job**).

1. **Công ty bị Rejected thì job cũ có còn hiện không?** `specs/admin.md` nói *vẫn hiện*, audit ghi nhận save/recommend/ATS lại yêu cầu Approved → không nhất quán.
   *Đề xuất:* ẩn job khỏi browse/detail/apply khi công ty không Approved (thu hồi phải có hiệu lực), cập nhật lại `specs/admin.md`. Nếu giữ như spec thì chỉ cần nới save/recommend cho khớp.
2. **Gỡ tin vi phạm:** *Đề xuất:* chuyển sang `Closed` + lưu lý do/người gỡ, giữ toàn bộ application/history; bỏ hard delete trên UI admin.
3. **Reset mật khẩu:** chưa có hạ tầng email. *Đề xuất:* admin đặt mật khẩu tạm (sinh ngẫu nhiên, hiển thị một lần), thu hồi mọi refresh session của user.
4. **Quản trị role, hoàn tiền, ẩn danh dữ liệu:** *Đề xuất:* để ngoài scope đợt này (Giai đoạn 4 – tùy chọn). Hoàn tiền VNPay cần API refund chưa tích hợp.

## 3. Ràng buộc chung

- Giữ kiến trúc hiện có: trang Blazor gọi `IAdminService` qua `CircuitDbGate`, bọc `<AuthorizeView Roles="Admin">`; lỗi nghiệp vụ trả `ServiceResult.Fail`, không throw.
- Migration sinh bằng `dotnet ef migrations add`, không sửa tay migration/snapshot. Query phải dịch được trên SQLite (test) và MySQL/Pomelo.
- Dùng `TimeProvider` cho giá trị "now" mới. DTO/API chỉ thêm trường, không xóa.
- Mỗi thao tác ghi của admin phải ghi audit log (sau Task A0.1).
- Test: xUnit, mỗi test một SQLite fixture mới, đặt trong `Tests/hotjob.Tests/AdminTests/`. Cuối mỗi giai đoạn chạy `dotnet test .\hotjob.slnx`.
- Có thể tách `AdminService` (550 dòng) thành các service nhỏ theo nhóm (`AdminUserService`, `AdminCompanyService`, `AdminJobService`, `AdminBillingService`, `AdminReportService`) khi thêm chức năng, giữ `IAdminService` cũ chạy được để không phá test.

---

## Giai đoạn 0 — Nền tảng và khép kín lỗi (ưu tiên cao, rủi ro thấp) — ✅ Xong 2026-11-26 trên nhánh `phuong-admin`

> Nhánh `phuong-admin` đã có sẵn A0.1 (`AdminAuditService` + `/admin/audit-logs`, ghi trong transaction), A0.2 (chặn tự khóa, không khóa được admin khác, thu hồi refresh token, `ActiveAccountTokenValidation`) và A0.5 (`JobVisibility.OpenToCandidates` ẩn job của công ty không Approved hoặc bị deactivate). Đợt này chỉ bổ sung phần còn thiếu, dùng lại hạ tầng của nhánh:
> - **A0.3:** `CompanyAdminService.ApproveAsync/RejectAsync` gửi notification `CompanyReview` cho employer Active (helper `CompanyMemberNotifier`); `/notifications` + badge NavMenu mở cho role Employer.
> - **A0.4:** `JobAdminService.RemoveAsync` + `POST /api/admin/jobs/{id}/remove` + nút Remove trên `/admin/jobs`; cột mới `JobPostings.ModeratedAt/ModeratedByAdminId/ModerationReason` (migration `AddJobModeration`); `AdminService.DeleteJobAsync` trả Conflict khi job có application; employer không mở lại được job đã gỡ; `JobClosure` dùng chung cho employer đóng job (giờ có notification cho candidate) và admin gỡ tin; ML bỏ cả hai loại auto-reject.
> - Test: `AdminTests/AdminNotificationAndRemovalTests.cs`, thêm case `Remove` trong `AdminAuthorizationTests.cs`. Toàn bộ 599 test pass.
> - Bổ sung sau: Deactivate/Reactivate công ty cũng gửi notification `CompanyReview` ("Company suspended" kèm lý do, "Company reactivated").

### A0.1 Nhật ký thao tác admin (M)
- Entity mới `AdminAuditLog`: `Id`, `AdminUserId` (FK SetNull), `Action` (string ≤ 60, ví dụ `User.Lock`, `Company.Reject`, `Job.Remove`), `TargetType`, `TargetId`, `Details` (≤ 2000, JSON ngắn: lý do, giá trị cũ/mới), `CreatedAt`. Index `(CreatedAt)`, `(TargetType, TargetId)`.
- Service `IAdminAuditLogger.LogAsync(...)` thêm entry vào cùng `SaveChanges` với thao tác chính (không lưu riêng).
- Trang `/admin/audit`: filter theo action, admin, khoảng ngày; paging.
- Gắn vào các thao tác hiện có: toggle active, update company status, delete job, CRUD skill, retrain.
- Test: mỗi mutation tạo đúng một log; mutation thất bại không tạo log.

### A0.2 Chặn admin tự khóa mình (S)
- `ToggleUserActiveAsync(userId, actingAdminId)`: từ chối nếu `userId == actingAdminId`; từ chối khóa admin active cuối cùng.
- Khi khóa: thu hồi refresh session (`AuthService.LogoutAsync(null, userId)`) để chặn ngay ở lần refresh.
- UI `AdminAccounts.razor`: ẩn nút khóa ở dòng của chính mình. Truyền `actingAdminId` từ principal.
- Test: tự khóa → Validation; khóa admin cuối → Validation; khóa user thường → refresh token cũ bị từ chối.

### A0.3 Notification kết quả duyệt công ty + inbox employer (M)
- Trong `UpdateCompanyStatusAsync`: khi status thực sự đổi, tạo `Notification` (Kind mới `CompanyReview`) cho mọi Employer membership Active của công ty; nội dung Rejected kèm lý do (cắt 2000 ký tự).
- Mở `/notifications` và badge `NavMenu.razor` (dòng ~136 đang chỉ cho `Candidate`) cho role `Employer`; feed vẫn loại `JobAlert`.
- Test: Approve/Reject tạo notification cho đúng owner + member Active, không gửi member Pending; set cùng status không tạo trùng; user chỉ có role Employer đọc được feed.

### A0.4 Gỡ tin vi phạm thay cho hard delete (M)
- Thêm vào `JobPosting`: `ModeratedAt`, `ModeratedByAdminId` (FK SetNull), `ModerationReason` (≤ 500). Migration.
- `AdminService.RemoveJobAsync(jobId, reason, adminId)`: bắt buộc lý do; chuyển `Closed`, mass-reject đơn đang mở theo đúng logic đóng job của employer (tái sử dụng, không copy), ghi history "Removed by admin", gửi notification cho candidate bị ảnh hưởng và employer của công ty.
- Employer không được mở lại job đã bị admin gỡ (`JobService` kiểm tra `ModeratedAt != null`). Hiển thị lý do trong `/employer/jobs`.
- Giữ `DeleteJobAsync` chỉ cho job Draft không có application, hoặc bỏ khỏi UI.
- Đồng thời sửa audit V.4: đóng job phía employer cũng gửi notification mass-reject (cùng helper).
- Test: application/history/interview còn nguyên sau khi gỡ; employer reopen → fail; thiếu lý do → Validation; candidate nhận notification.

### A0.5 Áp dụng chính sách công ty không Approved (S — theo quyết định #1)
- `JobPostingRepository.BrowsePublishedAsync`, `GetPublishedByIdAsync`, `ApplicationService.ApplyAsync`, job alerts: thêm điều kiện `Company.Status == Approved`.
- Cập nhật `docs/specs/admin.md` cho khớp.
- Test: công ty bị Reject → job biến khỏi browse/detail, apply trả lỗi; Approve lại → hiện lại.

---

## Giai đoạn 1 — Đáp ứng đầy đủ 5 mục Admin trong `sang.md`

### A1.1 Tìm kiếm/lọc tài khoản theo vai trò (S)
- `GetUsersAsync(AdminUserQuery query)`: `Search` (email/họ tên), `Role` (Candidate/Employer/Admin), `IsActive`, `Page`, `PageSize`. Giữ overload cũ.
- UI: tab **Ứng viên / Nhà tuyển dụng / Admin / Tất cả**, ô tìm kiếm, filter trạng thái; giữ query trên URL (`?role=&q=&page=`).
- Test: filter từng role, search không phân biệt hoa thường, kết hợp filter + paging.

### A1.2 Trang chi tiết người dùng `/admin/accounts/{userId}` (M)
- `GetUserDetailAsync` trả: thông tin tài khoản, roles, trạng thái, ngày tạo; nếu Candidate: profile, danh sách resume (tiêu đề, primary, có file), số đơn + 20 đơn gần nhất (job, công ty, status, ngày), trạng thái Premium/discoverable; nếu Employer: công ty, membership status, vai trò owner, số job đã tạo.
- Hành động trên trang: khóa/mở khóa, reset mật khẩu (A1.3), xem audit log của user.
- Test: user Candidate-only, Employer-only, cả hai, user không tồn tại → NotFound.

### A1.3 Admin reset mật khẩu (S — theo quyết định #3)
- `ResetPasswordAsync(userId, adminId)`: sinh mật khẩu tạm thỏa policy Identity, `RemovePasswordAsync` + `AddPasswordAsync` (hoặc token reset), thu hồi refresh session, ghi audit log (không ghi mật khẩu). Trả mật khẩu tạm để hiển thị một lần.
- Không cho reset tài khoản admin khác (hoặc chỉ cho chính mình qua luồng đổi mật khẩu).
- Test: mật khẩu cũ fail, mật khẩu tạm login được, refresh token cũ bị từ chối, log không chứa mật khẩu.

### A1.4 Công ty: filter + trang chi tiết (M)
- `GetCompaniesAsync(AdminCompanyQuery)`: `Status`, `Search` (tên/ngành/địa điểm). Mặc định hiển thị Pending trước.
- `/admin/companies/{id}`: hồ sơ đầy đủ, lịch sử duyệt (từ audit log), lý do reject, danh sách thành viên + trạng thái, job theo trạng thái, plan/entitlement hiện tại, số review + điểm trung bình. Approve/Reject ngay trên trang.
- Dashboard: badge số công ty Pending dẫn tới danh sách đã lọc.
- Test: filter từng status, detail trả đúng members/jobs, NotFound.

### A1.5 Tin tuyển dụng: filter + xem chi tiết (S)
- `GetJobsAsync(AdminJobQuery)`: `Status`, `CompanyId`, `Search` (tiêu đề), `IsFeatured`, `IsModerated`.
- UI: filter, link sang trang job public (nếu Published) và trang công ty admin; nút "Gỡ tin" mở hộp nhập lý do (A0.4).
- Test: từng filter và kết hợp.

### A1.6 Báo cáo thống kê (M)
- Mở rộng `AdminDashboardDto` (thêm trường, không xóa): tổng đơn, đơn theo status, đơn 30 ngày gần nhất, doanh thu tháng hiện tại, số payment Paid.
- Trang `/admin/reports` với khoảng thời gian (mặc định 12 tháng):
  - Đơn ứng tuyển theo tháng; top 10 công ty và top 10 tin theo số đơn.
  - User mới theo tháng theo vai trò; job mới theo tháng.
  - Doanh thu theo tháng theo `PaymentPurpose` (chỉ `Status == Paid`, theo `PaidAt`).
- Group-by dùng `Year`/`Month` của cột `DateTime` (dịch được trên SQLite và MySQL); tháng không có dữ liệu được lấp 0 trong service.
- Hiển thị bảng + biểu đồ cột đơn giản (CSS/SVG, không thêm thư viện nếu không cần).
- Test: dữ liệu rải nhiều tháng → đúng số liệu; payment Pending/Failed không tính doanh thu; tháng trống trả 0.

---

## Giai đoạn 2 — Back-office thương mại (`sang.md` mục 4)

### A2.1 Danh sách thanh toán `/admin/payments` (S)
- Filter: status, purpose, khoảng ngày, tìm theo `VnpTxnRef`/email người trả. Hiển thị plan/job/công ty/candidate liên quan, mã phản hồi VNPay, `Note`.
- Chỉ đọc. Hoàn tiền ngoài scope (quyết định #4).

### A2.2 Quản lý gói dịch vụ `/admin/plans` (M)
- Sửa `Name`, `Price`, `JobQuota`, `DurationDays`, `IsActive`, `SortOrder`; `Code` và `Audience` bất biến; không xóa plan đã có payment (chỉ ngưng `IsActive`); không ngưng gói `FREE`.
- Payment đang Pending giữ số tiền đã chốt (đã lấy giá phía server khi checkout — xác nhận lại).
- Sửa `Premium.razor` đang ghi cứng 99.000 VND/30 ngày → đọc từ plan.
- Test: validation giá/quota/thời hạn, chặn sửa Code, chặn ngưng FREE, checkout dùng giá mới.

### A2.3 Cấp gói / featured / Premium thủ công (M)
- Từ trang chi tiết công ty: cấp/gia hạn plan; từ trang job: cấp/gia hạn/thu hồi `FeaturedUntil`; từ trang user: cấp Premium.
- Ghi một `Payment` với `Amount = 0`, `Status = Paid`, `Note = "Manual admin grant: <lý do>"` (đúng ý của comment trong `PaymentStatus.Paid`), dùng lại logic grant của `BillingService` để quy tắc cộng hạn giống hệt VNPay. Báo cáo doanh thu tự động không tính (Amount 0).
- Test: grant cộng hạn đúng như mua; thu hồi featured đặt `FeaturedUntil = now`; có audit log.

---

## Giai đoạn 3 — Danh mục và vận hành

### A3.1 Trang quản lý Skills `/admin/skills` (S)
- Dùng service skill đã có (CRUD + test sẵn); chỉ làm UI: search, filter category, tạo/sửa/xóa, hiện lỗi khi skill đang được dùng.

### A3.2 CRUD Category `/admin/categories` (S)
- Thêm/sửa tên, mô tả; tên duy nhất. **Xóa phải chặn khi còn job mapping** — hiện `JobCategoryMapping` cascade khi xóa category, nên guard ở service.
- Test: trùng tên, xóa category đang dùng → Conflict, xóa category trống → OK.

### A3.3 Gửi thông báo hàng loạt (M)
- Form: đối tượng (Tất cả / Ứng viên / Nhà tuyển dụng / một công ty), tiêu đề ≤ 200, nội dung ≤ 2000, Kind `Broadcast`; xem trước số người nhận trước khi gửi.
- Chèn theo lô (ví dụ 500 bản ghi/lần), chỉ user active. Ghi audit log với số người nhận.
- Test: đúng tập người nhận theo nhóm, user bị khóa không nhận, giới hạn độ dài.

---

## Giai đoạn 4 — Tùy chọn (chốt scope trước, quyết định #4)

- **A4.1 Xem hồ sơ ứng tuyển khi tranh chấp:** trang admin read-only `/admin/applications/{id}` dùng `ApplicantMapping` (snapshot + history + interview). Liên kết từ user detail và company detail.
- **A4.2 Ẩn danh dữ liệu ứng viên theo yêu cầu:** khóa tài khoản, thay tên/email/phone, xóa file CV, ghi đè thông tin cá nhân trong `SnapshotJson`, giữ bản ghi application để thống kê. Cần thiết kế riêng.
- **A4.3 Quản trị role:** chỉ cân nhắc cấp/thu hồi role Admin; Candidate/Employer giữ theo luồng register/become-employer.
- **A4.4 Hoàn tiền:** cần tích hợp API refund VNPay; ngoài scope.

---

## 4. Thứ tự đề xuất và ước lượng

| Đợt | Task | Ước lượng | Ghi chú |
|---|---|---|---|
| 1 | A0.1 → A0.2 → A0.3 → A0.4 → A0.5 | ~3–4 ngày | A0.1 làm trước để mọi thao tác sau đều có log. A0.4 và A0.1 cần migration — gộp chung một migration nếu làm cùng đợt. |
| 2 | A1.1 → A1.2 → A1.3 → A1.4 → A1.5 → A1.6 | ~4–5 ngày | Hoàn thành đợt này là đáp ứng đúng nghĩa 5 mục Admin của `sang.md`. |
| 3 | A2.1 → A2.2 → A2.3 | ~2–3 ngày | A2.3 phụ thuộc A1.2, A1.4. |
| 4 | A3.1 → A3.2 → A3.3 | ~2 ngày | Độc lập, có thể làm song song đợt 3. |
| 5 | Giai đoạn 4 | Chưa ước lượng | Chỉ làm khi đã chốt scope. |

Ký hiệu: S ≈ ≤ nửa ngày, M ≈ 1 ngày (gồm test).

## 5. Tiêu chí hoàn thành mỗi đợt

- `dotnet test .\hotjob.slnx` pass toàn bộ, không thêm cảnh báo compiler mới.
- Migration áp dụng được trên MySQL local (`dotnet ef database update`), không chỉ SQLite.
- Kiểm tra thủ công trên trình duyệt bằng tài khoản admin seed: mỗi trang mới mở được, filter giữ trên URL, thao tác ghi có thông báo kết quả và xuất hiện trong `/admin/audit`.
- Cập nhật mục I của `docs/feature-audit-2026-11-23.md` (đổi trạng thái hàng tương ứng) và ghi kết quả vào `docs/admin/task-<n>-result.md` như các task trước.
