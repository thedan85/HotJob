# Đánh giá độ phủ chức năng HotJob

Ngày: 2026-11-23

## Kết luận

Dự án **chưa có đủ** toàn bộ danh sách chức năng yêu cầu.

Phần lõi đã hoàn thiện tương đối tốt:

- Candidate: tài khoản, CV thủ công, tìm việc, apply/withdraw, saved jobs, company review, premium, match score.
- Employer: công ty, thành viên, đăng tin theo gói, ATS, lịch phỏng vấn, thông báo sự kiện.
- ATS: state machine, lịch sử trạng thái, job hết hạn tự động.
- Billing: VNPay cho employer plan, featured job, candidate premium.

Các khoảng trống lớn:

- Admin back-office còn thiếu nhiều chức năng quản trị.
- Upload CV chưa bóc tách Education/Skills/Experience.
- Thông báo chưa realtime.

Các khoảng trống employer từng ghi nhận ở bản này (CV snapshot và thông báo sửa/hủy lịch) đã hoàn thiện ngày 2026-11-25; xem mục III.

Đã chạy:

```powershell
dotnet test ..\hotjob.slnx --nologo -v q
```

Kết quả ban đầu: **443/443 test pass**. Sau khi bổ sung chức năng employer: **451/451 test pass** (2026-11-25).

Lưu ý: chưa chạy kiểm thử E2E trên trình duyệt. Kết luận dựa trên đọc code, cấu trúc dữ liệu và test hiện có.

---

## I. Chức năng Admin

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Liệt kê công ty theo filter Pending/Approved/Rejected | `CompanyAdminService.ListAsync` lọc status + tìm tên/ngành/địa điểm, Pending lên trước; `/admin/companies?status=Pending` mở từ dashboard. |
| CÓ | Xem chi tiết công ty | `/admin/companies/{id}` (`GetOverviewAsync`): hồ sơ, plan/quota, thành viên, job theo trạng thái, review + điểm TB, lịch sử duyệt; duyệt/từ chối/tạm ngưng ngay trên trang. |
| CÓ | Duyệt công ty | `AdminCompanies.razor`, `CompanyAdminService.ApproveAsync/RejectAsync`; ghi audit log và gửi notification `CompanyReview` cho employer Active (Reject kèm lý do). Employer có inbox `/notifications` + badge NavMenu (2026-11-26). |
| CÓ | Từ chối công ty + lý do | Lý do bắt buộc khi chọn Rejected. |
| CÓ | Thu hồi/tạm ngưng công ty đã duyệt | Reject hoặc Deactivate; `JobVisibility.OpenToCandidates` ẩn job của công ty không Approved hoặc bị deactivate khỏi browse/detail/save/apply. Deactivate/Reactivate cũng báo cho employer Active ("Company suspended" kèm lý do / "Company reactivated"). |
| CÓ | Đăng nhập admin | Role Admin được seed; tài khoản admin lấy từ cấu hình. |
| CÓ | Danh sách người dùng, tìm theo tên/email/vai trò | `/admin/accounts`: tab vai trò, tìm email/họ tên, lọc trạng thái, giữ filter trên URL (`GetUsersAsync(AdminUserQuery)`). |
| CÓ | Xem chi tiết người dùng (Profile, CV, applications) | `/admin/accounts/{id}`: tài khoản, roles, hồ sơ candidate (CV, Premium, 20 đơn gần nhất), membership employer, lịch sử thao tác admin. |
| CÓ | Khóa/Mở khóa tài khoản | `AdminService.ToggleUserActiveAsync`: thu hồi refresh token, chặn tự khóa và khóa tài khoản Admin, có audit log. |
| CÓ | Reset password | `AdminUserService.ResetPasswordAsync`: mật khẩu tạm ngẫu nhiên hiển thị một lần, thu hồi refresh token, audit không chứa mật khẩu; không áp dụng cho chính mình và admin khác. |
| CÓ | Liệt kê tất cả tin tuyển dụng | Có paging. |
| CÓ | Filter tin theo công ty/trạng thái | `/admin/jobs`: trạng thái, tìm kiếm, `?companyId=`, nổi bật, đã gỡ (`ListAsync(AdminJobQuery)`; API nhận cùng tham số). |
| CÓ | Hủy bỏ tin vi phạm | `JobAdminService.RemoveAsync` (2026-11-26): Closed + lý do/người gỡ/thời gian, giữ application/history, reject application đang mở và thông báo candidate + employer; employer không mở lại được. Hard delete chỉ cho tin chưa có application. |
| KHÔNG | Cấp/gia hạn/thu hồi tin nổi bật | Admin không có chức năng. Employer có thể tự mua featured qua VNPay. |
| CÓ | Thống kê ứng tuyển theo tin, công ty, tháng | `/admin/reports`: đơn theo tháng, top 10 công ty và top 10 tin theo số đơn. |
| KHÔNG | Xem hồ sơ ứng tuyển khi có tranh chấp | Không có màn hình admin xem application detail. |
| KHÔNG | Xóa/Ẩn danh theo yêu cầu ứng viên | Không có chức năng xóa dữ liệu hoặc anonymize. |
| KHÔNG | Quản lý Category của Job | Category chỉ được seed và đọc; không có CRUD admin. |
| MỘT PHẦN | Quản lý Skills | Có CRUD API `AdminSkillsController` và test; không có trang admin UI. |
| KHÔNG | Quản lý gói dịch vụ (giá, số tin, thời hạn) | Plan chỉ seed sẵn; không có CRUD. |
| KHÔNG | Gán gói/sửa hạn mức cho công ty hoặc ứng viên | Entitlement chỉ đọc; không có thao tác admin. |
| KHÔNG | Danh sách thanh toán, đánh dấu đã trả/hoàn tiền | Payment do VNPay callback tự cập nhật; admin không có màn hình quản lý. |
| CÓ | Báo cáo doanh thu theo tháng | `/admin/reports`: doanh thu theo tháng và theo `PaymentPurpose` (chỉ Paid, theo `PaidAt`). |
| CÓ | Dashboard | Thêm tổng đơn, đơn theo trạng thái, đơn 30 ngày, doanh thu và số payment Paid tháng này; link tới công ty Pending và Reports. |
| CÓ | Nhật ký thao tác admin | `AdminAuditLog` + `/admin/audit-logs`, ghi cùng transaction với thao tác admin. |
| KHÔNG | Gửi thông báo cho người dùng (tất cả/nhóm) | Chỉ có notification theo sự kiện application/interview. |

### Bằng chứng chính

- `hotjob/Services/Admin/AdminService.cs`
- `hotjob/Services/Admin/IAdminService.cs`
- `hotjob/DTOs/Admin/AdminDtos.cs`
- `hotjob/Components/Pages/Admin/AdminAccounts.razor`
- `hotjob/Components/Pages/Admin/AdminCompanies.razor`
- `hotjob/Components/Pages/Admin/AdminJobs.razor`
- `hotjob/Components/Pages/Admin/AdminDashboard.razor`
- `hotjob/Controllers/AdminSkillsController.cs`

---

## II. Chức năng Candidate

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Đăng nhập/đăng xuất | JWT + refresh cookie. |
| CÓ | Đăng ký tài khoản | Register Candidate. |
| CÓ | Cập nhật thông tin cá nhân | `PATCH/PUT` profile qua `AccountService`. |
| CÓ | Tạo CV thủ công | Education, Skills, Experience. |
| KHÔNG | Upload CV và đọc/lọc Education, Skills, Experience | Chỉ lưu file PDF/DOC/DOCX; không có parser bóc tách nội dung. User phải tự thêm skill. |
| CÓ | Cập nhật CV | Sửa thuộc tính; upload file mới thay file cũ. |
| MỘT PHẦN | Gỡ CV | Xóa resume được nếu chưa từng dùng để apply. Resume đã apply bị chặn xóa. Không có thao tác chỉ gỡ file đính kèm. |
| CÓ | Tìm việc theo keyword, city, skill, job type | Có thêm filter category và salary. |
| CÓ | Xem công việc, chi tiết công việc | `JobDetail.razor`. |
| CÓ | Nộp CV | Apply với resume + cover letter; tạo snapshot file + summary. |
| CÓ | Rút CV | Chỉ khi application chưa Offered/Rejected/Withdrawn. |
| CÓ | Lưu công việc | Saved jobs. |
| CÓ | Tìm công ty theo keyword, industry, location | `Companies.razor`, `CompanyService.ListAsync`. |
| CÓ | Xem công ty, chi tiết công ty | `CompanyDetail.razor`. |
| CÓ | Đánh giá công ty 1-5 sao | Mỗi candidate một review/công ty. |
| CÓ | Trở thành nhà tuyển dụng | Tạo công ty mới hoặc join công ty có sẵn. |
| CÓ | Gia nhập công ty | Membership Pending chờ owner duyệt. |
| CÓ | Thành lập công ty | Company Pending chờ admin duyệt; owner Active ngay. |
| KHÔNG | Thông báo real time | Không có SignalR, Hub, polling. Badge chỉ refresh khi navigation/auth state đổi. |
| CÓ | Nhận thông báo kết quả ứng tuyển | Notification DB + trang Notifications. |
| CÓ | Nhận thông báo phỏng vấn | Gửi khi employer tạo lịch. |
| CÓ | Thanh toán gói Premium 1 tháng | VNPay; plan Premium Monthly 30 ngày. |
| CÓ | Xem điểm CV tương thích | Basic score miễn phí; advanced breakdown cần Premium. Không đúng cơ chế “chấm free lần đầu” vì basic score free mọi lần. |
| CÓ | Đẩy hồ sơ ứng viên | Candidate boosted được xếp trước trong employer candidate search. |
| MỘT PHẦN | User Profile gán nhãn Premium | Badge Premium chỉ có trên card candidate search. Không có badge trên profile riêng hoặc public profile. |

### Bằng chứng chính

- `hotjob/Services/Candidate/ResumeService.cs`
- `hotjob/Services/Candidate/ApplicationService.cs`
- `hotjob/Services/Candidate/CandidateService.cs`
- `hotjob/Services/Candidate/NotificationFeed.cs`
- `hotjob/Components/Pages/Profile.razor`
- `hotjob/Components/Pages/JobDetail.razor`
- `hotjob/Components/Pages/Jobs.razor`
- `hotjob/Components/Pages/Companies.razor`
- `hotjob/Components/Pages/CompanyDetail.razor`
- `hotjob/Components/Pages/Notifications.razor`
- `hotjob/Components/Pages/Premium.razor`

---

## III. Chức năng Employer

Cập nhật 2026-11-25: toàn bộ chức năng employer đã hoàn thiện. Ba mục từng ở mức MỘT PHẦN trong bản kiểm toán 2026-11-23 đã được bổ sung: freeze CV đầy đủ lúc apply, hiển thị actor trong lịch sử trạng thái, và gửi thông báo khi sửa/hủy lịch phỏng vấn.

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Tạo công ty mới (Owner) | Company Pending; owner Active. |
| CÓ | Join company có sẵn | Membership Pending chờ owner duyệt. |
| CÓ | Công ty Pending chưa được đăng job | `JobService` yêu cầu company Approved. |
| CÓ | Member Pending chưa được dùng ATS | `EmployerGuard` yêu cầu membership Active và company Approved. |
| CÓ | Xem hồ sơ công ty | Thông tin, trạng thái xác minh, thành viên, độ hoàn thiện hồ sơ. |
| CÓ | Sửa hồ sơ công ty | Owner cập nhật website, ngành, địa điểm, quy mô, mô tả. |
| CÓ | Quản lý thành viên | Owner duyệt/từ chối member, xem danh sách. |
| CÓ | Đăng tin theo gói | Quota theo plan; chặn khi hết slot. |
| CÓ | Sửa tin | Cập nhật nội dung, category, skill. |
| CÓ | Xóa/Đóng tin | Draft xóa hẳn; Published đóng và mass-reject đơn đang mở. |
| CÓ | Danh sách tin | Filter trạng thái, search, paging, applicant count. |
| CÓ | Xem chi tiết tin | Đầy đủ thông tin, category, skill, trạng thái. |
| CÓ | Kiểm tra dữ liệu | Chặn lương sai, hạn nộp quá khứ, skill/category không tồn tại. |
| CÓ | Phân quyền | Chỉ Employer Active thuộc company Approved; chỉ thấy dữ liệu công ty mình. |
| CÓ | Hàng đợi ứng viên | Filter job/status, search tên/email; có thêm match filter/sort. |
| CÓ | Xem chi tiết đơn | Cover letter, lịch sử trạng thái và CV đóng băng tại thời điểm apply. `Application.SnapshotJson` lưu đầy đủ Education, Work Experience, Skills, summary và file URL. Đơn cũ không có snapshot thì dùng CV hiện tại và ghi nhãn "current CV". |
| CÓ | Chuyển trạng thái tuần tự | Pending → Viewed → Shortlisted → Interviewing → Offered; Reject từ trạng thái mở. |
| CÓ | Ghi lịch sử | Lưu actor, thời gian, ghi chú. UI hiện trạng thái, thời gian, ghi chú và email người thay đổi (hoặc "System"). |
| CÓ | Nhắn tin ứng viên | Notification trực tiếp. |
| CÓ | Đặt lịch phỏng vấn nhiều vòng | Cho phép tạo nhiều interview record; không có trường số vòng riêng. |
| CÓ | Tự nâng trạng thái | Shortlisted + tạo lịch → Interviewing. |
| CÓ | Sửa/Hủy lịch | Có cập nhật và cancel interview; nội dung thông báo được giới hạn 2000 ký tự. |
| CÓ | Thông báo lịch phỏng vấn | Gửi Notification cho candidate khi tạo, sửa (reschedule) và hủy lịch. |
| CÓ | Job hết hạn tự động | Background service chuyển Published quá hạn sang Expired. Applications vẫn giữ cho HR xử lý. |
| CÓ | Thông báo sự kiện | Đổi trạng thái, tạo/sửa/hủy lịch, nhắn tin đều tạo Notification cho candidate. |

### Bằng chứng chính

- `hotjob/Services/Employer/CompanyService.cs`
- `hotjob/Services/Employer/EmployerGuard.cs`
- `hotjob/Services/Employer/ApplicantService.cs`
- `hotjob/Services/Employer/InterviewService.cs`
- `hotjob/Services/Employer/NotificationService.cs`
- `hotjob/Services/Employer/ApplicantMapping.cs`
- `hotjob/Services/Candidate/ApplicationService.cs`
- `hotjob/Models/Application/ResumeSnapshot.cs`
- `hotjob/Services/Job/JobService.cs`
- `hotjob/Services/Job/JobExpiryService.cs`
- `hotjob/Components/Pages/Employer/Jobs.razor`
- `hotjob/Components/Pages/Employer/Interviews.razor`
- `hotjob/Components/Pages/Employer/Company.razor`

---

## IV. Khoảng trống ưu tiên

1. **Admin back-office**
   - User detail, reset password.
   - Company filter/detail/suspend.
   - Job filter và quản lý featured.
   - Plan/payment/revenue.
   - Audit log.
   - Broadcast notification.

2. **Upload CV parsing**
   - Bóc tách Education, Skills, Experience từ PDF/DOC/DOCX.
   - Cho candidate duyệt và sửa dữ liệu trước khi lưu.

3. **Realtime notifications**
   - SignalR hoặc polling.
   - Cập nhật badge và danh sách ngay khi employer đổi trạng thái/gửi tin.

4. **CV snapshot đầy đủ — ĐÃ XONG (2026-11-25)**
   - Freeze Education, Experience, Skills, summary và file URL trong `Application.SnapshotJson`.

5. **Thông báo khi sửa/hủy lịch phỏng vấn — ĐÃ XONG (2026-11-25)**
   - Gửi notification cho candidate khi reschedule hoặc cancel.

---

## V. Ghi chú kiểm chứng

- Đã chạy test tự động: 443 test pass lúc kiểm toán; 451 test pass sau khi hoàn thiện employer (2026-11-25).
- Chưa chạy E2E trình duyệt.
- Chưa kiểm tra hành vi thanh toán với VNPay sandbox trong lần audit này.
- 2026-11-26 (nhánh `phuong-admin`, admin giai đoạn 0): 599 test pass. Chỉ cập nhật các dòng mục I liên quan giai đoạn 0; các dòng khác chưa đối chiếu lại với nhánh này.
- 2026-11-26 (nhánh `phuong-admin`, admin giai đoạn 1): tìm/lọc tài khoản, trang chi tiết user, reset mật khẩu, trang chi tiết công ty, lọc tin, báo cáo + dashboard mở rộng; 627 test pass.
