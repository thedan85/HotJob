# Đánh giá độ phủ chức năng HotJob

- Ngày lập: 2026-11-23.
- Cập nhật sau khi đối chiếu lại mã nguồn: 2026-11-25.
- Cập nhật sau merge `phuong-admin` vào `dev`: 2026-11-26.
- Revision được kiểm tra: `fe2d208b3f708501636786e0d023c533de5d2ec4` (merge commit; working tree sạch).

## Phạm vi và cách đọc

- Checklist nguồn: `docs/features-needed.md` (I Admin, II Candidate, III Employer). Các bảng I–III đi theo thứ tự checklist này; tính năng có trong code nhưng ngoài checklist ghi chú riêng, không tính vào độ phủ.
- Đối chiếu từng mục với service, controller, trang Blazor, model/EF và test hiện có. Mục IV là bằng chứng bổ trợ cho match score/ML/billing.
- `docs/idea.md`, `docs/task-assignment.md` và `docs/specs/*.md` là nguồn tham chiếu ý định, **không phải bằng chứng đã triển khai**. Khi tài liệu khác code, ghi rõ sai khác; không mặc định mọi mục back-office trong audit cũ đều là yêu cầu bắt buộc của scope gốc.
- **CÓ:** có triển khai cho phạm vi mô tả trong hàng; không có nghĩa đã nghiệm thu E2E hoặc production-ready.
- **MỘT PHẦN:** có backend nhưng thiếu UI, thiếu một phần luồng, hoặc còn giới hạn làm thay đổi kết quả sử dụng.
- **KHÔNG:** chưa tìm thấy triển khai trong các luồng liên quan đã kiểm tra.
- **CHƯA KIỂM CHỨNG:** mức xác nhận runtime, không đồng nghĩa thiếu tính năng. Browser E2E, OAuth Google thật, MySQL migration và VNPay sandbox đều chưa được chạy trong lần rà soát này.
- Đường dẫn mã nguồn bên dưới tính từ repository root. Lần cập nhật 2026-11-26 điều chỉnh kết luận sau merge và chuẩn hóa bảng I–III theo `docs/features-needed.md`; không sửa product code.

## Kết luận

Dự án **chưa phủ đủ các mục được liệt kê trong audit này**. Luồng Job Board + ATS chính đã có backend và UI, nhưng không nên kết luận “toàn bộ employer đã hoàn thiện” hoặc “614 test pass nghĩa là mọi luồng đều chạy đúng trên trình duyệt”.

Phần lõi đã hoàn thiện tương đối tốt:

- Candidate: tài khoản, CV thủ công, upload file, tìm/lưu việc, apply/withdraw, theo dõi đơn, review công ty, recommended jobs, job alerts, Premium.
- Employer: công ty, duyệt thành viên, đăng tin theo quota, tìm ứng viên đã bật discoverable, ATS và lịch phỏng vấn.
- ATS: snapshot dữ liệu CV khi apply, lịch sử có actor, thông báo cho candidate khi đổi trạng thái/tạo/sửa/hủy lịch và nhắn tin.
- Billing: checkout/callback VNPay cho employer plan, featured job và candidate Premium; có test tự động, chưa xác nhận với cổng thanh toán thật trong lần audit này.
- ML: có train FastTree và training log; **điểm hiển thị hiện dùng `weighted-rule`, không dùng dự đoán của model**.

Các khoảng trống lớn:

- Admin back-office còn thiếu nhiều chức năng quản trị.
- Upload CV chưa bóc tách Education/Skills/Experience.
- Thông báo chưa tự cập nhật realtime; thiếu notification khi admin duyệt/từ chối công ty và thiếu inbox riêng cho employer-only.
- File CV upload có URL tương đối nhưng nút mở snapshot của employer chỉ nhận URL HTTP(S) tuyệt đối; dữ liệu snapshot đã có nhưng luồng mở file còn thiếu.
- Quy tắc công ty bị Rejected chưa nhất quán giữa public jobs, save/recommend và ATS.

Kiểm chứng sau merge ngày 2026-11-26: **614 passed, 0 failed, 0 skipped**. Có cảnh báo dependency và compiler; chi tiết, lịch sử kết quả và giới hạn kiểm chứng ở mục VII.

---

## I. Chức năng Admin

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Liệt kê công ty theo filter Pending/Approved/Rejected | `ICompanyAdminService.ListAsync` nhận `CompanyStatus?` và `search`; `AdminCompanies.razor` có select trạng thái + ô tìm kiếm, Pending lên đầu. |
| MỘT PHẦN | Xem chi tiết công ty | Có `ICompanyAdminService.GetDetailAsync` qua `CompanyAdminController` (`CompanyDetailDto`); UI `AdminCompanies.razor` vẫn chỉ là bảng. |
| CÓ | Duyệt công ty | `AdminCompanies.razor` gọi `ICompanyAdminService.ApproveAsync`; lưu `VerifiedAt`, `VerifiedByAdminId`, ghi audit. |
| CÓ | Từ chối công ty + lý do | `RejectAsync` yêu cầu lý do, lưu `RejectionReason`, ghi audit; UI cho duyệt lại hai chiều. |
| MỘT PHẦN | Thu hồi/tạm ngưng công ty đã duyệt | Có `DeactivateAsync`/`ReactivateAsync` và Reject công ty Approved; chưa có trạng thái Suspended riêng. Public list/detail và apply không kiểm tra company Approved; xem mục V. |
| CÓ | Đăng nhập admin | Role Admin được seed; tài khoản admin lấy từ cấu hình. |
| MỘT PHẦN | Danh sách người dùng, tìm theo tên/email/vai trò | Có paging; `IAdminService.GetUsersAsync` không nhận search/filter role, UI chưa có ô tìm kiếm. |
| KHÔNG | Xem chi tiết người dùng (Profile, CV, applications) | Chưa có trang hoặc service admin cho user detail. |
| CÓ | Khóa/Mở khóa tài khoản | `ToggleUserActiveAsync` lật `IsActive`, revoke refresh token khi khóa, chặn self-lock và khóa admin khác. Access token đã cấp không bị revoke ngay. |
| KHÔNG | Reset password | Chưa có admin reset mật khẩu; `ChangePassword` chỉ dành cho chính user. |
| MỘT PHẦN | Liệt kê tất cả tin, filter theo công ty/trạng thái | `IJobAdminService.ListAsync` nhận `JobStatus?` và `search` (title/company); UI có select trạng thái + tìm kiếm. Chưa filter riêng theo company ID. |
| CÓ | Hủy bỏ tin vi phạm | `DeleteJobAsync` hard delete trong transaction và ghi audit `JobDelete`; EF cascade xóa applications/history/interviews liên quan. |
| KHÔNG | Cấp/gia hạn/thu hồi tin nổi bật | Admin không có chức năng; employer tự mua featured qua VNPay. |
| KHÔNG | Thống kê ứng tuyển theo tin, công ty, tháng | Chỉ có `ApplicationCount` theo từng job. |
| KHÔNG | Xem hồ sơ khi có tranh chấp | Chưa có màn hình admin xem application detail. |
| KHÔNG | Xóa/Ẩn danh theo yêu cầu ứng viên | Chưa có chức năng xóa hoặc anonymize dữ liệu ứng viên. |
| KHÔNG | Quản lý Category của Job | Category chỉ được seed và đọc; không có CRUD admin. |
| MỘT PHẦN | Quản lý Skills | Có CRUD API `AdminSkillsController` và test; chưa có trang admin UI. |
| KHÔNG | Quản lý gói dịch vụ (giá, số tin, thời hạn) | Plan chỉ seed sẵn; không có CRUD. |
| KHÔNG | Gán gói/sửa hạn mức cho công ty hoặc ứng viên | Entitlement chỉ đọc; không có thao tác admin. |
| KHÔNG | Danh sách thanh toán, đánh dấu đã trả/hoàn tiền | VNPay callback tự cập nhật; không có màn hình admin. |
| KHÔNG | Báo cáo doanh thu theo tháng | Không có query hoặc UI. |
| MỘT PHẦN | Dashboard: số người dùng, công ty chờ duyệt, tin đang đăng, số hồ sơ, doanh thu | Có users/companies/jobs; thiếu số hồ sơ và doanh thu. |
| MỘT PHẦN | Nhật ký thao tác admin (ai, làm gì, lúc nào) | `AdminAuditLog`, `AdminAuditService`, `AdminAuditLogs.razor` + migration; actor/action/time và filter. `Detail` là text tự do, chưa lọc secret. Chi tiết: `docs/admin/admin-audit-log-phase-0.md`. |
| KHÔNG | Gửi thông báo cho người dùng (tất cả / theo nhóm) | Không có broadcast admin; notification hiện sinh từ application/interview, tin nhắn employer và job alert. |

Ngoài checklist `features-needed.md`: code còn có duyệt/từ chối job (`AdminJobs.razor`), duyệt/thu hồi membership employer (`AdminEmployers.razor`) và kiểm duyệt review/report (`AdminReviews.razor`); các tính năng này không tính vào độ phủ bảng trên.

### Bằng chứng chính

- `hotjob/Services/Admin/AdminService.cs`
- `hotjob/Services/Admin/IAdminService.cs`
- `hotjob/Services/Admin/CompanyAdminService.cs`, `EmployerAdminService.cs`, `JobAdminService.cs`, `ReviewAdminService.cs`
- `hotjob/Controllers/Admin/CompanyAdminController.cs`, `EmployerAdminController.cs`, `JobAdminController.cs`, `ReviewAdminController.cs`
- `hotjob/DTOs/Admin/AdminDtos.cs`
- `hotjob/Components/Pages/Admin/AdminAccounts.razor`
- `hotjob/Components/Pages/Admin/AdminCompanies.razor`
- `hotjob/Components/Pages/Admin/AdminEmployers.razor`, `AdminJobs.razor`, `AdminReviews.razor`, `AdminAuditLogs.razor`
- `hotjob/Components/Pages/Admin/AdminDashboard.razor`
- `hotjob/Controllers/AdminSkillsController.cs`
- `hotjob/Components/Pages/Admin/AdminModel.razor`
- `hotjob/Services/ML/MatchScoreService.cs`
- `hotjob/Services/Authentication/AuthService.cs`
- `hotjob/Data/ApplicationDbContext.cs`
- Test trong `hotjob/Tests/hotjob.Tests/AdminTests/`: `AdminListPagingTests.cs`, `AdminTrainingLogTests.cs`, `AdminSkillServiceTests.cs`, `AdminSkillsControllerTests.cs`, `AdminAuthorizationTests.cs`, `AdminAuditServiceTests.cs`, `AdminAuditHooksTests.cs`, `AdminModerationTests.cs`.

---

## II. Chức năng Candidate

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Đăng nhập / đăng xuất tài khoản | JWT + HttpOnly refresh cookie; refresh xoay token, dùng lại token đã revoke thì revoke các session; logout thu hồi refresh session. |
| CÓ | Đăng ký tài khoản | UI `/register` cố định Candidate. API còn cho đăng ký Employer trực tiếp. |
| CÓ | Cập nhật thông tin cá nhân | `AccountService.UpdateProfileAsync`, `PUT /api/account/profile`; headline/summary, avatar, social links. |
| CÓ | Tạo CV: Education, Skills, Experience | Tối đa 5 resume/candidate; CV đầu tiên tự là primary. |
| KHÔNG | Upload CV: đọc/lọc Education, Skills, Experience rồi candidate duyệt/sửa | Chỉ lưu file PDF/DOC/DOCX ≤5 MiB, kiểm tra extension/header; không có parser hay bước duyệt dữ liệu trích xuất. |
| CÓ | Cập nhật CV: upload mới hoặc sửa thuộc tính | File cũ giữ nếu còn application trỏ tới `SnapshotFileUrl`, nếu không thì xóa. |
| MỘT PHẦN | Gỡ CV | Resume chưa từng apply mới xóa được; không có thao tác gỡ riêng file đính kèm. |
| CÓ | Tìm việc theo filter (keyword, city, skill, job type) | Có thêm filter category và salary. |
| CÓ | Xem công việc, chi tiết công việc | `Jobs.razor`, `JobDetail.razor`. |
| CÓ | Nộp, rút CV | Apply bằng resume + cover letter, lưu `SnapshotJson`; chặn trùng theo candidate + job. Rút CV khi đơn chưa ở trạng thái terminal. |
| CÓ | Lưu công việc | Saved jobs. |
| CÓ | Tìm công ty theo filter (keyword, industry, location) | `Companies.razor`, `CompanyService.ListAsync`. |
| CÓ | Xem công ty, chi tiết công ty | `CompanyDetail.razor`. |
| CÓ | Đánh giá công ty 1-5 sao | Mỗi candidate một review/công ty. |
| CÓ | Trở thành nhà tuyển dụng | Tạo công ty mới hoặc join công ty có sẵn. |
| CÓ | Gia nhập công ty | Membership Pending chờ owner duyệt. |
| CÓ | Thành lập công ty | Company Pending chờ admin duyệt; owner Active ngay. |
| KHÔNG | Thông báo real time | Không có push/polling; badge không tự refresh khi có notification mới giữa các user. |
| CÓ | Nhận thông báo kết quả | `ApplicantService.UpdateStatusAsync` ghi notification khi status đổi. Đóng job mass-reject là ngoại lệ. |
| CÓ | Nhận thông báo phỏng vấn | Tạo, sửa, hủy lịch và mark completed đều ghi notification; chưa realtime/email/push. |
| CÓ | Thanh toán gói premium (1 tháng) | VNPay; seed Premium Monthly 99.000 VND/30 ngày; backend lấy giá/thời hạn từ plan. Chưa xác nhận sandbox. |
| MỘT PHẦN | Xem điểm CV tương thích (chấm free lần đầu) | Basic score free mọi lần; advanced breakdown cần Premium. Chưa có cơ chế “free lần đầu, sau đó trả phí”. |
| CÓ | Đẩy hồ sơ ứng viên | Candidate boosted được xếp trước trong employer candidate search. |
| MỘT PHẦN | User Profile gán nhãn premium | Badge trên card employer candidate search và `/premium`; `/profile` chưa có badge. |

Ngoài checklist `features-needed.md`: code còn có đăng nhập Google, đổi mật khẩu, forgot-password, theo dõi đơn/timeline, bật tìm hồ sơ (discoverable), recommended jobs và job alerts; các tính năng này không tính vào độ phủ bảng trên.

### Bằng chứng chính

- `hotjob/Services/Candidate/ResumeService.cs`
- `hotjob/Services/Candidate/ApplicationService.cs`
- `hotjob/Services/Candidate/CandidateService.cs`
- `hotjob/Services/Candidate/NotificationFeed.cs`
- `hotjob/Services/Account/AccountService.cs`
- `hotjob/Services/Company/CompanyService.cs`
- `hotjob/Controllers/AccountController.cs`
- `hotjob/Controllers/AuthController.cs`
- `hotjob/Services/Authentication/AuthService.cs`
- `hotjob/Services/Candidate/CvFileStorage.cs`
- `hotjob/Services/Employer/CandidateSearchService.cs`
- `hotjob/Services/JobAlerts/JobAlertService.cs`
- `hotjob/Services/JobAlerts/JobAlertsOptions.cs`
- `hotjob/Services/Job/JobService.cs`
- `hotjob/Components/Layout/NavMenu.razor`
- `hotjob/Components/Pages/Profile.razor`
- `hotjob/Components/Pages/LoginPage.razor`
- `hotjob/Components/Pages/RegisterPage/RegisterPage.razor`
- `hotjob/Components/Pages/JobDetail.razor`
- `hotjob/Components/Pages/Jobs.razor`
- `hotjob/Components/Pages/Companies.razor`
- `hotjob/Components/Pages/CompanyDetail.razor`
- `hotjob/Components/Pages/Notifications.razor`
- `hotjob/Components/Pages/Premium.razor`
- `hotjob/Components/Pages/MyApplications.razor`
- `hotjob/Components/Pages/RecommendedJobs.razor`
- `hotjob/Components/Pages/JobAlerts.razor`
- Test: `hotjob/Tests/hotjob.Tests/CandidateTests/`, `hotjob/Tests/hotjob.Tests/ProfileTests/`, `hotjob/Tests/hotjob.Tests/JobAlertTests/`, `hotjob/Tests/hotjob.Tests/JobTests/`.

---

## III. Chức năng Employer

Cập nhật 2026-11-25: đã xác nhận các bổ sung về snapshot dữ liệu CV, actor trong history và notification sửa/hủy lịch.

Cập nhật 2026-11-26 sau merge `phuong-admin`: các khoảng trống employer dưới đây vẫn còn; merge chỉ bổ sung admin moderation/audit. **Không kết luận toàn bộ employer đã hoàn thiện**: cần phân biệt snapshot dữ liệu với mở file CV, và tạo notification DB với một luồng nhận/đọc hoàn chỉnh.

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Trở thành Employer: tạo công ty mới (Owner) hoặc join company có sẵn | Company Pending; membership Pending chờ owner duyệt. |
| CÓ | Chờ duyệt: công ty Pending chưa đăng job; member Pending chưa dùng ATS | `JobService` và `EmployerGuard` yêu cầu company Approved / membership Active. |
| CÓ | Xem hồ sơ công ty | Thông tin, trạng thái, thành viên, mức hoàn thiện; workspace chỉ cần membership Active. |
| CÓ | Sửa hồ sơ công ty | Owner cập nhật website, ngành, địa điểm, quy mô, mô tả. |
| CÓ | Quản lý thành viên | Owner duyệt/từ chối member Pending, xem danh sách; chưa chuyển owner/gỡ member Active. |
| CÓ | Đăng tin (tiêu đề, mô tả, lương, kinh nghiệm, loại hình, hạn nộp, category, skill + trọng số); quota theo gói | `ImportanceWeight`/`MinYears` đi cùng skill; quota tính Draft + Published, company phải Approved. |
| CÓ | Sửa tin | Cập nhật nội dung, category, skill. |
| CÓ | Xóa / Đóng tin | Draft xóa hẳn; non-Draft đóng + mass-reject đơn mở, ghi history “Job closed”. Không sinh notification cho mass-reject. |
| CÓ | Danh sách tin: job công ty mình, lọc trạng thái, tìm kiếm, phân trang, kèm số ứng viên | `Employer/Jobs.razor`, `IJobService.ListAsync`. |
| CÓ | Xem chi tiết tin | Đầy đủ nội dung, category, skill, trạng thái. |
| CÓ | Kiểm tra dữ liệu | Chặn lương sai, hạn nộp quá khứ, skill/category không tồn tại. |
| CÓ | Phân quyền: Employer Active thuộc công ty Approved, chỉ thấy dữ liệu công ty mình | `EmployerGuard`; scope theo `CompanyProfileId` của caller. |
| CÓ | Hàng đợi ứng viên: lọc job/trạng thái, tìm tên/email | Có thêm paging, match filter/sort từ `MatchScores` đã lưu của resume hiện tại. |
| MỘT PHẦN | Xem chi tiết đơn: cover letter, CV snapshot lúc nộp, CV hiện tại và lịch sử trạng thái | `SnapshotJson` lưu dữ liệu + actor history. URL `/uploads/cv/...` bị `SafeUrl` loại nên chưa mở được file snapshot; JSON hỏng thì fallback CV hiện tại. |
| CÓ | Chuyển trạng thái: Pending → Viewed → Shortlisted → Interviewing → Offered; Reject từ trạng thái mở | Service ép forward-only transition. |
| CÓ | Ghi lịch sử: người đổi, thời gian, ghi chú | UI hiện email người đổi, fallback username/user id, không có actor thì “System”. |
| CÓ | Nhắn tin ứng viên | Notification một chiều theo application; không phải chat hai chiều. |
| CÓ | Đặt lịch phỏng vấn: ngày giờ, địa điểm/link, hình thức, ghi chú | Nhiều interview/application; chưa có số vòng riêng; không đặt mới cho đơn terminal. |
| CÓ | Tự nâng trạng thái: đặt lịch cho đơn Shortlisted thì chuyển Interviewing | Xem `InterviewService.CreateAsync`. |
| CÓ | Sửa / Hủy lịch | UI có edit/cancel/completed; service chưa ép state machine lịch như UI. |
| CÓ | Thông báo lịch cho ứng viên | Create/update/cancel gửi notification; content tối đa 2000 ký tự. |
| CÓ | Job hết hạn tự ẩn khỏi Candidate, đơn mở vẫn giữ cho HR | `JobExpiryService` lúc startup + mỗi 15 phút; public query cũng loại job quá hạn. |
| MỘT PHẦN | Thông báo sự kiện cho Candidate: đổi trạng thái, đặt lịch, nhắn tin | Đều ghi notification DB; đóng job mass-reject và realtime là ngoại lệ. |

Ngoài checklist `features-needed.md`: code còn có tìm ứng viên discoverable (`/employer/candidates`), mua/gia hạn plan và featured job qua VNPay; các tính năng này không tính vào độ phủ bảng trên.

### Bằng chứng chính

- `hotjob/Services/Employer/CompanyService.cs`
- `hotjob/Services/Employer/EmployerGuard.cs`
- `hotjob/Services/Employer/ApplicantService.cs`
- `hotjob/Services/Employer/InterviewService.cs`
- `hotjob/Services/Employer/NotificationService.cs`
- `hotjob/Services/Employer/ApplicantMapping.cs`
- `hotjob/Services/Candidate/ApplicationService.cs`
- `hotjob/Services/Candidate/ResumeService.cs`
- `hotjob/Services/Candidate/CvFileStorage.cs`
- `hotjob/Models/Application/ResumeSnapshot.cs`
- `hotjob/Migrations/20261123122108_AddApplicationSnapshotJson.cs`
- `hotjob/Services/Job/JobService.cs`
- `hotjob/Services/Job/JobExpiryService.cs`
- `hotjob/Components/Pages/Employer/Jobs.razor`
- `hotjob/Components/Pages/Employer/Interviews.razor`
- `hotjob/Components/Pages/Employer/Company.razor`
- `hotjob/Components/Pages/Employer/Candidates.razor`
- `hotjob/Components/Pages/Employer/Billing.razor`
- `hotjob/Services/Employer/CandidateSearchService.cs`
- `hotjob/Repositories/Employer/ApplicantRepository.cs`
- Test trong `hotjob/Tests/hotjob.Tests/EmployerTests/`: `EmployerAtsTests.cs`, `EmployerGuardTests.cs`, `CandidateSearchTests.cs`.
- Test trong `hotjob/Tests/hotjob.Tests/CandidateTests/`: `ApplicationServiceTests.cs`, `ResumeServiceTests.cs`.

---

## IV. Match score, ML và Billing

### 1. Match score và huấn luyện

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Chấm điểm CV–job trước khi apply | `MatchScoreService.GetAsync` dùng resume thuộc candidate, lưu theo cặp job/resume, hiển thị 0–100. Job không có skill thì trả unavailable, không tạo điểm giả. |
| CÓ | Weighted score có xét số năm mỗi skill | `MatchFeatureExtractor`: skill có `MinYears > 0` nhận credit `min(1, resumeYears / MinYears)`; nếu không thì chỉ xét có skill. Điểm = tỷ lệ trọng số đạt được × 100; tổng weight bằng 0 thì dùng tỷ lệ số skill khớp. |
| CÓ | Vô hiệu hóa điểm khi đổi dữ liệu đầu vào | Đổi bộ skill hoặc `YearsOfExperience` trên resume, hoặc đổi skill/weight/MinYears/experience trên job thì xóa điểm cũ; lần đọc chấm tiếp theo tính lại. Không phải background tự chấm lại toàn bộ ATS. |
| CÓ | Phân tích nâng cao cho Premium | Server-side Premium gate, matched/missing skills, số năm yêu cầu/thực tế, experience gap và gợi ý cải thiện. Basic score không bị giới hạn lần dùng. |
| CÓ | Train/evaluate/promote FastTree thủ công | Mặc định cần ít nhất 50 bản ghi hợp lệ và 5 mỗi outcome. Offered là positive; Rejected/Withdrawn là negative; loại auto-reject có note cuối “Job closed”. Hold-out phân tầng 20%; train thành công có log. Promote nếu accuracy không thấp hơn model active, chưa có log active hoặc file model hiện tại không tồn tại. |
| KHÔNG | Dùng xác suất FastTree để chấm điểm hiển thị | Code hiện luôn dùng `weighted-rule`, kể cả khi có model active. `docs/specs/ml-matchscore.md` còn mô tả đường FastTree inference cũ; không dùng tài liệu đó để khẳng định tính năng đang chạy. |
| CÓ | Seed/train dữ liệu tổng hợp phục vụ development | `POST /api/dev/match-model/seed-and-train`, yêu cầu role Admin + môi trường Development + `DevTools:SyntheticTrainingEnabled`. Không phải chức năng production hay bằng chứng accuracy trên dữ liệu thật. |

Nguồn:

- `hotjob/Services/ML/MatchScoreService.cs`
- `hotjob/Services/ML/MatchFeatureExtractor.cs`
- `hotjob/Services/ML/MatchScoreOptions.cs`
- `hotjob/Services/ML/MatchModelStore.cs`
- `hotjob/Services/ML/SyntheticMatchTrainingService.cs`
- `hotjob/Controllers/DevMatchModelController.cs`
- Test trong `hotjob/Tests/hotjob.Tests/MLTests/`: `MatchScoreServiceTests.cs`, `MatchFeatureExtractionTests.cs`, `MatchTrainingTests.cs`, `CandidatePremiumGateTests.cs`, `SyntheticMatchTrainingTests.cs`, `DevMatchModelControllerTests.cs`.

### 2. Thanh toán và entitlement

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Catalogue gói seed | Employer Free: 1 slot, không hết hạn; Starter: 5 slot/500.000 VND/30 ngày; Growth: 20 slot/1.500.000 VND/30 ngày. Candidate Premium Monthly: 99.000 VND/30 ngày. Đây là giá trị seed, không phải xác nhận dữ liệu DB đang triển khai. |
| CÓ | Checkout VNPay | Ba purpose: EmployerPlan, EmployerFeatured, CandidatePremium; giá lấy phía server. Chưa cấu hình VNPay thì trả lỗi, không giả lập thanh toán thành công. |
| CÓ | Xử lý IPN và browser return | `VnPayController` gọi cùng callback service; kiểm tra signature, mã giao dịch, số tiền, trạng thái gateway và thời hạn payment. Có chống grant lặp bằng trạng thái Pending và concurrency token. |
| CÓ | Gia hạn plan và thời gian featured/Premium | Cùng employer plan còn hiệu lực thì cộng hạn từ expiry hiện tại; đổi plan bắt đầu từ hiện tại. Featured còn hạn thì cộng tiếp. Candidate Premium cộng tiếp hạn đang còn; `BoostUntil` bằng `PremiumUntil`. |
| CÓ | Hiển thị trạng thái/usage | `/employer/billing` hiển thị quota/gói/hạn; `/premium` hiển thị Premium/boost; `/payment/result` hiển thị kết quả callback. Chưa có trang lịch sử giao dịch hoặc refund cho admin. |

Nguồn:

- `hotjob/Data/DatabaseSeeder.cs`
- `hotjob/Services/Billing/BillingService.cs`
- `hotjob/Services/Billing/VnPayService.cs`
- `hotjob/Services/Billing/EntitlementService.cs`
- `hotjob/Services/Billing/CandidatePremiumService.cs`
- `hotjob/Controllers/VnPayController.cs`
- `hotjob/Data/ApplicationDbContext.cs`
- `hotjob/Components/Pages/PaymentResult.razor`
- Test trong `hotjob/Tests/hotjob.Tests/BillingTests/`: `BillingServiceTests.cs`, `VnPayServiceTests.cs`, `EntitlementQuotaTests.cs`, `PaymentDataModelTests.cs`.

---

## V. Sai khác liên luồng cần lưu ý

1. **Duyệt công ty chưa gửi notification.** `CompanyAdminService.ApproveAsync`/`RejectAsync` chỉ cập nhật công ty rồi ghi audit; dấu `VerifiedAt/VerifiedByAdminId` không thay thế thông báo cho employer. Mục này nằm ngoài checklist `features-needed.md`.
2. **Công ty Rejected không đồng nghĩa job cũ bị ẩn.** `JobPostingRepository.BrowsePublishedAsync`/`GetPublishedByIdAsync` và `ApplicationService.ApplyAsync` không kiểm tra trạng thái công ty. Trong khi đó `SaveJobAsync`, recommendations, ATS/job và checkout employer yêu cầu Approved. Đây là khác biệt chính sách thực tế, không phải một trạng thái suspend hoàn chỉnh.
3. **Snapshot dữ liệu không đồng nghĩa UI mở được file.** JSON đã freeze dữ liệu và file cũ được giữ nếu application còn tham chiếu, nhưng URL tương đối của upload bị `Interviews.razor.SafeUrl` loại. Probe .NET `Uri.TryCreate(..., UriKind.Absolute)` + kiểm tra scheme đã xác nhận URL `/uploads/cv/1/example.pdf` bị loại, URL HTTPS được nhận; chưa chạy browser E2E.
4. **Đóng job có đổi trạng thái nhưng chưa thông báo.** `DeleteOrCloseAsync` thêm history cho mass-reject, không thêm notification như `ApplicantService.UpdateStatusAsync`. Admin hard delete còn xóa dữ liệu liên quan thay vì giữ lịch sử.
5. **Điểm ATS không được đóng băng theo CV.** CV snapshot là dữ liệu lúc apply; filter/sort ATS lấy `MatchScores` của resume hiện tại. Sau invalidation, điểm có thể chưa có cho tới khi một luồng chấm điểm đọc lại.
6. **Đăng ký chỉ Candidate là quy tắc UI, chưa phải quy tắc API.** `AccountService.RegisterAsync`/`AccountController.Register` hỗ trợ cả Candidate và Employer trực tiếp. Không tự suy ra employer đăng ký trực tiếp có cả hai profile/role.

Nguồn bổ sung cho đối chiếu liên luồng:

- `hotjob/Repositories/Job/JobPostingRepository.cs`
- `hotjob/Services/Job/JobService.cs`
- `hotjob/Services/Candidate/ApplicationService.cs`
- `hotjob/Data/ApplicationDbContext.cs`

---

## VI. Khoảng trống ưu tiên

Thứ tự dưới đây là đề xuất xử lý, không phải cam kết bổ sung scope hoặc kết luận mọi mục đều chặn demo.

1. **Khép kín luồng đang có**
   - Mở được file CV upload từ chi tiết ứng tuyển employer.
   - Notification kết quả duyệt công ty, inbox/badge employer-only, notification khi đóng job mass-reject.
   - Chốt chính sách đối với công ty Rejected và áp dụng nhất quán cho browse/detail/apply/save/recommend.
   - Rà soát cảnh báo dependency từ lần chạy test trước khi triển khai.

2. **Admin theo `docs/features-needed.md`**
   - User detail, reset password.
   - Company detail UI và trạng thái Suspended (filter/deactivate đã có).
   - Quản lý featured job (filter status/search đã có).
   - Category CRUD, Skills UI (quản trị role nằm ngoài checklist `features-needed.md`).
   - Plan/payment/revenue.
   - Audit log: chính sách lọc secret trong `Detail`.
   - Broadcast notification.
   - Xử lý tranh chấp ứng tuyển và xóa/ẩn danh dữ liệu theo yêu cầu.

3. **Upload CV parsing**
   - Bóc tách Education, Skills, Experience từ PDF/DOC/DOCX.
   - Cho candidate duyệt và sửa dữ liệu trước khi lưu.

4. **Realtime notifications**
   - Bổ sung push/event subscription hoặc polling cho notification, không chỉ dựa vào kết nối Blazor.
   - Đồng bộ badge và danh sách khi có dữ liệu mới/đánh dấu đã đọc.

5. **Các phần còn một phần hoặc cần chốt yêu cầu**
   - Gỡ file CV riêng, badge Premium trên profile.
   - Đáp ứng yêu cầu “chấm free lần đầu” trong `features-needed.md`; phân biệt weighted score với model inference.
   - Đồng bộ quy tắc đăng ký Candidate-only giữa UI/API nếu giữ yêu cầu này.

### Các khoảng trống cũ đã xác nhận có triển khai (2026-11-25)

- Snapshot dữ liệu Education, Experience, Skills, title, summary và file URL trong `Application.SnapshotJson`; **không bao gồm việc đã sửa nút mở file upload**.
- Actor trong lịch sử trạng thái ở employer detail.
- Notification khi sửa/hủy lịch phỏng vấn; create/update có giới hạn content 2000 ký tự.

---

## VII. Ghi chú kiểm chứng

### Lần chạy sau merge ngày 2026-11-26

Chạy từ repository root (`hotjob/`):

```bash
dotnet test hotjob.slnx --nologo -v q
```

Kết quả: **614 passed, 0 failed, 0 skipped, total 614**, exit code 0. Lệnh có build và chạy test project `hotjob/Tests/hotjob.Tests/hotjob.Tests.csproj` trên `net10.0`.

Cảnh báo xuất hiện trong output:

- `NU1902`: `MySqlConnector` 2.4.0, mức moderate — https://github.com/advisories/GHSA-473q-m89c-ghf8.
- `NU1903`: `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 trong test project, mức high — https://github.com/advisories/GHSA-2m69-gcr7-jv3q.
- Compiler: duplicate using (`CS0105`), primary-constructor parameters chưa dùng (`CS9113`), nullability (`CS8620`, `CS8604`). Vì vậy không gọi build/test này là “không có cảnh báo”.

### Phân biệt mức bằng chứng

| Hạng mục | Mức kiểm chứng |
|---|---|
| Service/controller, EF mapping, wiring và UI trong audit | Đã đọc/đối chiếu mã nguồn; test tự động hiện có chạy pass. Không có nghĩa mỗi hàng đều có test chuyên biệt. |
| Snapshot CV, actor history, notification sửa/hủy lịch | Có test regression trong `ApplicationServiceTests`, `ResumeServiceTests`, `EmployerAtsTests`; chưa nghiệm thu browser. |
| Guard URL file snapshot | Có probe .NET độc lập với URL tương đối/HTTPS; không phải browser test hay test tích hợp đầy đủ. |
| MySQL, migration snapshot và seed lúc startup | CHƯA KIỂM CHỨNG trên DB triển khai. Fixture chính dùng SQLite in-memory + `EnsureCreatedAsync`, không chạy chuỗi migration MySQL. |
| Browser E2E và realtime giữa hai tài khoản | CHƯA KIỂM CHỨNG; không khởi chạy ứng dụng/trình duyệt trong lần rà soát này. |
| Thanh toán VNPay sandbox và OAuth Google thật | CHƯA KIỂM CHỨNG; test service/controller không thay thế callback từ nhà cung cấp thật. |
| Security/performance/accessibility toàn diện | Ngoài phạm vi lần audit chức năng này; các cảnh báo nêu trên chưa được xử lý trong thay đổi tài liệu. |

### Lịch sử kết quả giữ lại từ audit trước

- Kiểm toán ban đầu ngày 2026-11-23 ghi nhận **443/443 test pass**.
- Bản cập nhật employer ngày 2026-11-25 ghi nhận **451/451 test pass**.
- Lần đối chiếu lại ngày 2026-11-25 chạy mới được **451/451**, không chỉ sao chép kết quả lịch sử.
- Sau merge `phuong-admin` vào `dev` ngày 2026-11-26: **614/614**, không phải kết quả lịch sử.

---

## VIII. Lịch sử cập nhật tài liệu

- **2026-11-23 — Bản ban đầu:** kiểm kê Admin/Candidate/Employer, ghi nhận thiếu back-office, CV parsing, realtime và các khoảng trống employer; lưu kết quả 443 test pass.
- **2026-11-25 — Bản cập nhật employer trước lần rà soát này:** đánh dấu đã bổ sung snapshot CV, actor history, notification sửa/hủy lịch; ghi nhận 451 test pass và từng kết luận toàn bộ employer hoàn thiện.
- **2026-11-25 — Đối chiếu lại toàn dự án:** thay kết luận hoàn thiện tuyệt đối bằng độ phủ có giới hạn; bổ sung ML/billing/alerts/discovery, thiếu notification duyệt công ty/inbox employer, lỗi guard URL snapshot và sai khác liên luồng. Làm rõ weighted-rule, Candidate-only UI so với API, tác động hard delete và mức kiểm chứng; chạy lại 451 test, ghi cả cảnh báo và giới hạn. Không sửa product code.
- **2026-11-26 — Cập nhật sau merge `phuong-admin`:** giữ report audit riêng tại `docs/admin/admin-audit-log-phase-0.md`; đánh dấu audit log và form đổi mật khẩu đã có, cập nhật kết quả test 614/614. Không sửa product code.
- **2026-11-26 — Đối chiếu Admin/Employer:** cập nhật các mục admin đã có (filter công ty/tin, duyệt công ty/job/employer, kiểm duyệt review, audit, self-lock guard); giữ nguyên các khoảng trống employer (mở file snapshot, mass-reject notification, inbox/badge employer-only). Không sửa product code.
- **2026-11-26 — Chuẩn hóa checklist:** bảng I–III đi theo `docs/features-needed.md`; tính năng ngoài checklist gom thành ghi chú riêng. Không sửa product code.
