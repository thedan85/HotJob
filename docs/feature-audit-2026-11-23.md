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
- CV snapshot lúc apply chưa freeze toàn bộ nội dung CV.
- Sửa/hủy lịch phỏng vấn chưa gửi thông báo cho candidate.

Đã chạy:

```powershell
dotnet test ..\hotjob.slnx --nologo -v q
```

Kết quả: **443/443 test pass**.

Lưu ý: chưa chạy kiểm thử E2E trên trình duyệt. Kết luận dựa trên đọc code, cấu trúc dữ liệu và test hiện có.

---

## I. Chức năng Admin

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| MỘT PHẦN | Liệt kê công ty theo filter Pending/Approved/Rejected | Có danh sách và paging; không có filter trạng thái. `AdminService.GetCompaniesAsync` không nhận tham số status. |
| KHÔNG | Xem chi tiết công ty | Trang admin chỉ có bảng vài cột; không có trang chi tiết. |
| CÓ | Duyệt công ty | `AdminCompanies.razor`, `AdminService.UpdateCompanyStatusAsync`. |
| CÓ | Từ chối công ty + lý do | Lý do bắt buộc khi chọn Rejected. |
| MỘT PHẦN | Thu hồi/tạm ngưng công ty đã duyệt | Reject công ty Approved hoạt động như thu hồi; không có trạng thái tạm ngưng riêng. Job đã publish vẫn hiển thị. |
| CÓ | Đăng nhập admin | Role Admin được seed; tài khoản admin lấy từ cấu hình. |
| MỘT PHẦN | Danh sách người dùng, tìm theo tên/email/vai trò | Có danh sách và paging; không có ô tìm kiếm hoặc filter vai trò. |
| KHÔNG | Xem chi tiết người dùng (Profile, CV, applications) | Không có trang hoặc service admin cho user detail. |
| CÓ | Khóa/Mở khóa tài khoản | `AdminService.ToggleUserActiveAsync`. |
| KHÔNG | Reset password | Không có chức năng admin reset mật khẩu. `ChangePassword` chỉ dành cho chính user. |
| CÓ | Liệt kê tất cả tin tuyển dụng | Có paging. |
| KHÔNG | Filter tin theo công ty/trạng thái | Không có tham số filter trong `GetJobsAsync` và UI. |
| CÓ | Hủy bỏ tin vi phạm | Hard delete job. |
| KHÔNG | Cấp/gia hạn/thu hồi tin nổi bật | Admin không có chức năng. Employer có thể tự mua featured qua VNPay. |
| MỘT PHẦN | Thống kê ứng tuyển theo tin, công ty, tháng | Chỉ có `ApplicationCount` theo từng job; không có thống kê theo công ty hoặc theo tháng. |
| KHÔNG | Xem hồ sơ ứng tuyển khi có tranh chấp | Không có màn hình admin xem application detail. |
| KHÔNG | Xóa/Ẩn danh theo yêu cầu ứng viên | Không có chức năng xóa dữ liệu hoặc anonymize. |
| KHÔNG | Quản lý Category của Job | Category chỉ được seed và đọc; không có CRUD admin. |
| MỘT PHẦN | Quản lý Skills | Có CRUD API `AdminSkillsController` và test; không có trang admin UI. |
| KHÔNG | Quản lý gói dịch vụ (giá, số tin, thời hạn) | Plan chỉ seed sẵn; không có CRUD. |
| KHÔNG | Gán gói/sửa hạn mức cho công ty hoặc ứng viên | Entitlement chỉ đọc; không có thao tác admin. |
| KHÔNG | Danh sách thanh toán, đánh dấu đã trả/hoàn tiền | Payment do VNPay callback tự cập nhật; admin không có màn hình quản lý. |
| KHÔNG | Báo cáo doanh thu theo tháng | Không có query hoặc UI. |
| MỘT PHẦN | Dashboard | Có users, companies, jobs. Thiếu số hồ sơ ứng tuyển và doanh thu. |
| KHÔNG | Nhật ký thao tác admin | Không có entity/table audit log. |
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
| MỘT PHẦN | Xem chi tiết đơn | Có cover letter, CV hiện tại, lịch sử trạng thái. Snapshot chỉ lưu file URL + summary; không freeze toàn bộ Education/Experience/Skills. |
| CÓ | Chuyển trạng thái tuần tự | Pending → Viewed → Shortlisted → Interviewing → Offered; Reject từ trạng thái mở. |
| CÓ | Ghi lịch sử | Lưu actor, thời gian, ghi chú. UI chỉ hiện trạng thái/thời gian/ghi chú, không hiện tên actor. |
| CÓ | Nhắn tin ứng viên | Notification trực tiếp. |
| CÓ | Đặt lịch phỏng vấn nhiều vòng | Cho phép tạo nhiều interview record; không có trường số vòng riêng. |
| CÓ | Tự nâng trạng thái | Shortlisted + tạo lịch → Interviewing. |
| CÓ | Sửa/Hủy lịch | Có cập nhật và cancel interview. |
| MỘT PHẦN | Thông báo lịch phỏng vấn | Chỉ gửi khi tạo lịch. Sửa/hủy lịch không gửi notification. |
| CÓ | Job hết hạn tự động | Background service chuyển Published quá hạn sang Expired. Applications vẫn giữ cho HR xử lý. |
| CÓ | Thông báo sự kiện | Đổi trạng thái, tạo lịch, nhắn tin đều tạo Notification cho candidate. |

### Bằng chứng chính

- `hotjob/Services/Employer/CompanyService.cs`
- `hotjob/Services/Employer/EmployerGuard.cs`
- `hotjob/Services/Employer/ApplicantService.cs`
- `hotjob/Services/Employer/InterviewService.cs`
- `hotjob/Services/Employer/NotificationService.cs`
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

4. **CV snapshot đầy đủ**
   - Freeze cả Education, Experience, Skills lúc apply.
   - Không chỉ lưu file URL + summary.

5. **Thông báo khi sửa/hủy lịch phỏng vấn**
   - Gửi notification cho candidate khi reschedule hoặc cancel.

---

## V. Ghi chú kiểm chứng

- Đã chạy test tự động: 443 test pass.
- Chưa chạy E2E trình duyệt.
- Chưa kiểm tra hành vi thanh toán với VNPay sandbox trong lần audit này.
