# Đánh giá độ phủ chức năng HotJob

- Ngày lập: 2026-11-23.
- Cập nhật sau khi đối chiếu lại mã nguồn: 2026-11-25.
- Cập nhật sau merge `phuong-admin` vào `dev`: 2026-11-26.
- Revision được kiểm tra: `fe2d208b3f708501636786e0d023c533de5d2ec4` (merge commit; working tree sạch).

## Phạm vi và cách đọc

- Đối chiếu các mục của audit cũ với service, controller, trang Blazor, model/EF và test hiện có. Bổ sung các phần bị bỏ sót: quản trị ML, recommended jobs, job alerts, candidate discovery và billing.
- `docs/idea.md`, `docs/task-assignment.md` và `docs/specs/*.md` là nguồn tham chiếu ý định, **không phải bằng chứng đã triển khai**. Khi tài liệu khác code, ghi rõ sai khác; không mặc định mọi mục back-office trong audit cũ đều là yêu cầu bắt buộc của scope gốc.
- **CÓ:** có triển khai cho phạm vi mô tả trong hàng; không có nghĩa đã nghiệm thu E2E hoặc production-ready.
- **MỘT PHẦN:** có backend nhưng thiếu UI, thiếu một phần luồng, hoặc còn giới hạn làm thay đổi kết quả sử dụng.
- **KHÔNG:** chưa tìm thấy triển khai trong các luồng liên quan đã kiểm tra.
- **CHƯA KIỂM CHỨNG:** mức xác nhận runtime, không đồng nghĩa thiếu tính năng. Browser E2E, OAuth Google thật, MySQL migration và VNPay sandbox đều chưa được chạy trong lần rà soát này.
- Đường dẫn mã nguồn bên dưới tính từ repository root. Lần cập nhật 2026-11-26 chỉ điều chỉnh các kết luận lỗi thời sau merge, không sửa product code.

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
| MỘT PHẦN | Liệt kê công ty theo filter Pending/Approved/Rejected | Có danh sách và paging; không có filter trạng thái. `AdminService.GetCompaniesAsync` không nhận tham số status. |
| KHÔNG | Xem chi tiết công ty | Trang admin chỉ có bảng vài cột; không có trang chi tiết. |
| CÓ | Duyệt công ty | `AdminCompanies.razor`, `AdminService.UpdateCompanyStatusAsync`; lưu `VerifiedAt`, `VerifiedByAdminId`. Không tự gửi notification. |
| CÓ | Từ chối công ty + lý do | Lý do bắt buộc khi chọn Rejected, lưu `RejectionReason`; UI cho duyệt lại hai chiều. |
| MỘT PHẦN | Thu hồi/tạm ngưng công ty đã duyệt | Reject công ty Approved chặn ATS/job/billing đã có guard; không có trạng thái Suspended riêng. Public list/detail và apply không kiểm tra company Approved nên job cũ vẫn có thể được xem/nộp; xem mục V. |
| KHÔNG | Thông báo kết quả duyệt công ty cho owner/employer | `UpdateCompanyStatusAsync` chỉ cập nhật công ty rồi save. `AdminCompanies.razor` không có bước tạo notification bổ sung, dù `docs/idea.md` và `docs/specs/admin.md` mô tả có. |
| CÓ | Đăng nhập admin | Role Admin được seed; tài khoản admin lấy từ cấu hình. |
| MỘT PHẦN | Danh sách người dùng, tìm theo tên/email/vai trò | Có danh sách và paging; không có ô tìm kiếm hoặc filter vai trò. |
| KHÔNG | Xem chi tiết người dùng (Profile, CV, applications) | Không có trang hoặc service admin cho user detail. |
| CÓ | Khóa/Mở khóa tài khoản | `ToggleUserActiveAsync` lật `IsActive`; login/refresh/khôi phục session kiểm tra cờ này. Không revoke ngay access token đã cấp; service/UI chưa có chặn admin tự khóa mình. |
| KHÔNG | Admin gán/đổi role người dùng | Có hiển thị roles; không có thao tác quản trị role trong `IAdminService` hoặc trang Accounts. Role được gán qua seed/register/become-employer. |
| KHÔNG | Reset password | Không có chức năng admin reset mật khẩu. `ChangePassword` chỉ dành cho chính user. |
| CÓ | Liệt kê tất cả tin tuyển dụng | Có paging. |
| KHÔNG | Filter tin theo công ty/trạng thái | Không có tham số filter trong `GetJobsAsync` và UI. |
| CÓ | Hủy bỏ tin vi phạm | `DeleteJobAsync` hard delete, không phải ẩn/đóng có lý do. EF cascade xóa cả applications/history/interviews liên quan; không có moderation log. |
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
| MỘT PHẦN | Nhật ký thao tác admin | Có `AdminAuditLog`, `AdminAuditService`, trang `AdminAuditLogs.razor` và migration `AddAdminAuditLog`; ghi thao tác quản trị kèm actor, lọc theo action/type/email/thời gian. Chưa production-ready: `Detail` là text tự do, không lọc secret. Các thao tác admin đã kiểm tra ghi audit trong cùng transaction với thao tác gốc. Chi tiết: `docs/admin/admin-audit-log-phase-0.md`. |
| KHÔNG | Gửi thông báo cho người dùng (tất cả/nhóm) | Không có broadcast admin; notification hiện sinh từ application/interview, tin nhắn employer và job alert. |
| CÓ | Xem lịch sử huấn luyện ML | `AdminModel.razor`, `GetTrainingLogsAsync`: số bản ghi, accuracy, người train, thời gian, model path và cờ active. |
| CÓ | Retrain ML thủ công | `AdminModel.razor` gọi `MatchScoreService.TrainAsync`. Thiếu dữ liệu trả kết quả chưa train, không tạo log giả; model train không quyết định điểm hiển thị. |

### Bằng chứng chính

- `hotjob/Services/Admin/AdminService.cs`
- `hotjob/Services/Admin/IAdminService.cs`
- `hotjob/DTOs/Admin/AdminDtos.cs`
- `hotjob/Components/Pages/Admin/AdminAccounts.razor`
- `hotjob/Components/Pages/Admin/AdminCompanies.razor`
- `hotjob/Components/Pages/Admin/AdminJobs.razor`
- `hotjob/Components/Pages/Admin/AdminDashboard.razor`
- `hotjob/Controllers/AdminSkillsController.cs`
- `hotjob/Components/Pages/Admin/AdminModel.razor`
- `hotjob/Services/ML/MatchScoreService.cs`
- `hotjob/Services/Authentication/AuthService.cs`
- `hotjob/Data/ApplicationDbContext.cs`
- Test trong `hotjob/Tests/hotjob.Tests/AdminTests/`: `AdminListPagingTests.cs`, `AdminTrainingLogTests.cs`, `AdminSkillServiceTests.cs`, `AdminSkillsControllerTests.cs`, `AdminAuditServiceTests.cs`, `AdminAuditHooksTests.cs`, `AdminModerationTests.cs`.

---

## II. Chức năng Candidate

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Đăng nhập/đăng xuất, refresh và khôi phục session | JWT + HttpOnly refresh cookie; refresh xoay token, dùng lại token đã revoke thì revoke các refresh session; logout thu hồi refresh session. |
| CÓ | Đăng nhập Google | Có controller/service và nút đăng nhập; chỉ bật provider khi có ClientId/ClientSecret. Chưa kiểm chứng OAuth thật. |
| CÓ | Đăng ký tài khoản Candidate | UI `/register` cố định Candidate. **API còn cho đăng ký Employer trực tiếp**, khác quy tắc “chỉ đăng ký Candidate” trong tài liệu ý tưởng. |
| CÓ | Cập nhật thông tin cá nhân | `AccountService.UpdateProfileAsync`, `PUT /api/account/profile`; không có endpoint PATCH. Có headline/summary, tùy chọn việc làm, avatar và social links. |
| CÓ | Đổi mật khẩu của chính mình | Có `POST /api/account/change-password`, yêu cầu mật khẩu cũ và thu hồi refresh session. Form `ChangePasswordSection` được render trong `Profile.razor`; sau đổi mật khẩu UI đăng xuất và dọn cookie. Không phải forgot-password/reset qua email. |
| KHÔNG | Quên mật khẩu/reset qua email | Link “Forgot password?” ở `LoginPage.razor` chỉ trỏ về `/`; chưa có luồng gửi email/token và đặt mật khẩu mới. |
| CÓ | Tạo CV thủ công và chọn CV primary | Education, Skills, Experience; tối đa 5 resume/candidate, CV đầu tiên tự là primary. |
| CÓ | Upload file CV | PDF/DOC/DOCX, tối đa 5 MiB, kiểm tra extension/header, lưu bằng tên sinh tự động. Đây là lưu file, không phải CV parsing. |
| KHÔNG | Upload CV và tự bóc tách Education, Skills, Experience | Không có parser hoặc bước cho duyệt dữ liệu trích xuất. User nhập dữ liệu cấu trúc thủ công; resume yêu cầu ít nhất một skill. |
| CÓ | Cập nhật CV | Sửa dữ liệu cấu trúc và upload file mới. File cũ được giữ nếu còn application trỏ tới `SnapshotFileUrl`, nếu không thì xóa. |
| MỘT PHẦN | Gỡ CV | Xóa resume được nếu chưa từng dùng để apply. Resume đã apply bị chặn xóa. Không có thao tác chỉ gỡ file đính kèm. |
| CÓ | Tìm việc theo keyword, city, skill, job type | Có thêm filter category và salary. |
| CÓ | Xem công việc, chi tiết công việc | `JobDetail.razor`. |
| CÓ | Nộp CV | Apply với resume của mình + cover letter; lưu `SnapshotJson`, file URL và summary. Chặn trùng theo **candidate + job**, kể cả đổi resume hoặc đã withdraw; không chỉ theo job + resume như tài liệu cũ. |
| CÓ | Rút CV | Chỉ khi application chưa Offered/Rejected/Withdrawn. |
| CÓ | Theo dõi đơn, timeline và lịch phỏng vấn | `MyApplications.razor`, `CandidateService.GetMyApplicationsAsync`; tìm/lọc phía UI. Service tải toàn bộ đơn của candidate, không paging phía DB. |
| CÓ | Lưu công việc | Saved jobs. |
| CÓ | Tìm công ty theo keyword, industry, location | `Companies.razor`, `CompanyService.ListAsync`. |
| CÓ | Xem công ty, chi tiết công ty | `CompanyDetail.razor`. |
| CÓ | Đánh giá công ty 1-5 sao | Mỗi candidate một review/công ty. |
| CÓ | Trở thành nhà tuyển dụng | Tạo công ty mới hoặc join công ty có sẵn. |
| CÓ | Gia nhập công ty | Membership Pending chờ owner duyệt. |
| CÓ | Thành lập công ty | Company Pending chờ admin duyệt; owner Active ngay. |
| CÓ | Bật/tắt cho employer tìm thấy hồ sơ | `Profile.razor`, `CandidateSearchService.SetDiscoverableAsync`; discovery chỉ trả candidate có `IsDiscoverable`. Không phải public profile cho khách. |
| KHÔNG | Thông báo real time | Chưa có cơ chế push/polling notification. Blazor Interactive Server dùng kết nối realtime cho UI nhưng không tự đồng bộ notification mới giữa các user. Badge nạp khi khôi phục/auth state thay đổi, không đăng ký sự kiện navigation để refresh. |
| CÓ | Đọc thông báo và đánh dấu đã đọc | DB + `/notifications`, đọc từng mục/tất cả, phân trang; feed và badge chung loại trừ `JobAlert`. |
| CÓ | Nhận thông báo kết quả ứng tuyển | `ApplicantService.UpdateStatusAsync` tạo notification khi status thực sự đổi. **Đóng job mass-reject chưa tạo notification** cho các đơn bị đổi gián tiếp. |
| CÓ | Nhận thông báo phỏng vấn | Tạo, cập nhật và hủy lịch đều ghi notification; cập nhật trạng thái Completed cũng đi qua nhánh update. Chưa realtime/email/push. |
| CÓ | Gợi ý việc theo resume | `/recommended-jobs`, `GetJobsMatchingResumeAsync`: chọn resume, xếp theo số skill khớp, điểm đã lưu rồi thời gian đăng. Không phải ranking bằng dự đoán FastTree. |
| CÓ | Nhận job alerts miễn phí | Khi publish, xét điểm cao nhất trong các resume của candidate active, đạt ngưỡng `JobAlerts:MinMatchPercent` (mặc định 60) thì tạo một alert/candidate/job. Có `/job-alerts`, paging và đánh dấu tất cả đã đọc. Không phải email subscription; sinh alert là best-effort. |
| CÓ | Thanh toán/gia hạn Premium 30 ngày | VNPay; seed Premium Monthly 99.000 VND/30 ngày. Backend lấy giá/thời hạn từ plan; trang Premium đang ghi cứng giá và 30 ngày. Chưa xác nhận thanh toán sandbox trong lần audit này. |
| CÓ | Xem điểm CV tương thích | Basic score miễn phí mọi lần; advanced breakdown cần Premium. Không triển khai cơ chế “chấm free lần đầu, các lần sau trả phí”. Chi tiết thuật toán ở mục IV. |
| CÓ | Đẩy hồ sơ ứng viên | Candidate boosted được xếp trước trong employer candidate search. |
| MỘT PHẦN | User Profile gán nhãn Premium | Badge Premium có trên card employer candidate search; `/premium` có trạng thái gói. `/profile` chưa có badge và chưa có trang public candidate profile riêng. |

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

Cập nhật 2026-11-25: đã xác nhận các bổ sung về snapshot dữ liệu CV, actor trong history và notification sửa/hủy lịch. Tuy nhiên, **không kết luận toàn bộ employer đã hoàn thiện**: cần phân biệt snapshot dữ liệu với mở file CV, và tạo notification DB với một luồng nhận/đọc hoàn chỉnh.

| Trạng thái | Chức năng | Ghi chú |
|---|---|---|
| CÓ | Tạo công ty mới (Owner) | Company Pending; owner Active. |
| CÓ | Join company có sẵn | Membership Pending chờ owner duyệt. |
| CÓ | Công ty Pending chưa được đăng job | `JobService` yêu cầu company Approved. |
| CÓ | Member Pending chưa được dùng ATS | `EmployerGuard` yêu cầu membership Active và company Approved. |
| CÓ | Xem hồ sơ công ty | Thông tin, trạng thái công ty, thành viên, độ hoàn thiện hồ sơ. Company workspace chỉ yêu cầu membership Active, nên owner có thể xem/sửa khi công ty còn Pending hoặc Rejected. |
| CÓ | Sửa hồ sơ công ty | Owner cập nhật website, ngành, địa điểm, quy mô, mô tả. |
| CÓ | Quản lý thành viên | Owner xem danh sách và duyệt/từ chối **member Pending**; không có chuyển owner, gỡ member Active hoặc chuyển công ty. |
| CÓ | Đăng tin theo gói | Quota tính Draft + Published; chặn thêm/mở lại job khi hết slot. Đóng/hết hạn giải phóng slot; gói hết hạn quay về Free khi đọc entitlement. |
| CÓ | Sửa tin | Cập nhật nội dung, category, skill. |
| CÓ | Xóa/Đóng tin | Draft xóa hẳn; non-Draft đóng và mass-reject đơn đang mở, ghi history “Job closed”. Không sinh notification cho thao tác mass-reject này. |
| CÓ | Danh sách tin | Filter trạng thái, search, paging, applicant count. |
| CÓ | Xem chi tiết tin | Đầy đủ thông tin, category, skill, trạng thái. |
| CÓ | Kiểm tra dữ liệu | Chặn lương sai, hạn nộp quá khứ, skill/category không tồn tại. |
| CÓ | Phân quyền ATS/job | Employer Active + company Approved, dữ liệu theo company của caller. **Không áp dụng nguyên xi cho mọi service**: company workspace có guard riêng, candidate discovery được phép tìm xuyên công ty trên hồ sơ opt-in. |
| CÓ | Hàng đợi ứng viên | Filter job/status, search tên/email, paging, match filter/sort. Match lấy từ `MatchScores` đã lưu của resume hiện tại, không tự chấm khi HR mở danh sách và không phải điểm snapshot lúc apply. |
| CÓ | Xem snapshot dữ liệu CV trong chi tiết đơn | Cover letter, history; `Application.SnapshotJson` lưu title, Education, Work Experience, Skills, summary, file URL lúc apply. JSON thiếu/không hợp lệ thì `ApplicantMapping` dùng CV hiện tại và UI ghi “current CV”; không backfill lịch sử. |
| MỘT PHẦN | Mở file CV snapshot đã upload | File URL đã lưu và được giữ khi thay file. Nhưng `CvFileStorage` trả `/uploads/cv/...`; `Interviews.razor.SafeUrl` chỉ nhận HTTP(S) tuyệt đối nên không render nút “Open resume snapshot” cho URL upload này. Snapshot dữ liệu vẫn xem được. |
| CÓ | Chuyển trạng thái tuần tự | Pending → Viewed → Shortlisted → Interviewing → Offered; Reject từ trạng thái mở. |
| CÓ | Ghi lịch sử | Lưu actor, thời gian, ghi chú. Employer UI hiện email người thay đổi, fallback username/user id; không có actor thì hiện “System”. |
| CÓ | Nhắn tin ứng viên | Gửi notification một chiều theo application; không phải chat/hộp thoại hai chiều. |
| CÓ | Đặt lịch phỏng vấn nhiều vòng | Nhiều interview/application, có type, ngày giờ, location/link, notes; không có số vòng riêng. Không đặt mới cho đơn terminal; Pending/Viewed vẫn đặt được nhưng không tự đổi status. |
| CÓ | Tự nâng trạng thái | Shortlisted + tạo lịch → Interviewing. |
| CÓ | Sửa/Hủy/Hoàn thành lịch | UI có edit, cancel, mark completed. Create/update notification được cắt tối đa 2000 ký tự. Service update/cancel chưa ép state machine lịch như UI (UI chỉ hiện các nút khi Scheduled). |
| CÓ | Thông báo lịch phỏng vấn | Gửi Notification cho candidate khi tạo, sửa (reschedule) và hủy lịch. |
| CÓ | Job hết hạn tự động | `JobExpiryService` chạy lúc startup rồi mỗi 15 phút, chuyển Published quá hạn sang Expired, giữ đơn cho HR. Public query cũng loại job quá hạn, không đợi sweep. |
| CÓ | Gửi thông báo sự kiện cho candidate | Đổi status trực tiếp, tạo/sửa/hủy lịch và tin nhắn có notification DB; không có realtime. Đóng job mass-reject là ngoại lệ chưa thông báo. |
| MỘT PHẦN | Nhận/đọc thông báo có đơn ứng tuyển mới | Apply tạo notification cho mọi employer membership Active của công ty. Chưa có inbox/badge riêng cho employer-only: `/notifications` và badge chỉ hiện cho role Candidate. User đồng thời Candidate + Employer có thể đọc qua feed đó. |
| CÓ | Tìm ứng viên bên ngoài hàng đợi ứng tuyển | `/employer/candidates`: keyword theo position/summary/skill, filter skill/min years, badge Premium/Boosted, boost trước rồi mới nhất. Chỉ hồ sơ discoverable; card không trả email, phone hoặc file CV. Paging thực hiện sau khi tổng hợp trong bộ nhớ. |
| CÓ | Mua/gia hạn plan, mua featured job | Owner Active của công ty Approved được checkout; member thường chỉ xem entitlement. Featured áp dụng Draft/Published, không phải quyền admin cấp miễn phí. Xem mục IV. |

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

1. **Duyệt công ty chưa gửi notification.** Ý tưởng/spec mô tả có, nhưng `AdminService.UpdateCompanyStatusAsync` không ghi notification. Dấu `VerifiedAt/VerifiedByAdminId` không thay thế thông báo cho employer.
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

2. **Admin back-office theo checklist đã thống nhất**
   - User detail, reset password.
   - Company filter/detail/suspend.
   - Job filter và quản lý featured.
   - Category CRUD, Skills UI, quản trị role nếu thuộc scope.
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
   - Phân biệt weighted score với model inference; xác nhận có thực sự yêu cầu “free lần đầu” hay basic free không giới hạn.
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
