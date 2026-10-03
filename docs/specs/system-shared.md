# System + shared — cái giữ mọi thứ lại với nhau

Job board + ATS. Không có social feed, không duyệt từng job, MatchScore hiện trước nút Apply chứ không phải sau.

## 1. Hệ thống tự làm gì

- Gác mọi request: trạng thái login, role, MembershipStatus Active, Company Approved, phạm vi Company của mình. Id lạ trả về not found.
- Giữ session: login cấp access token + refresh cookie. Mỗi lần refresh xoay token mới và nối token cũ với token mới. Dùng lại token đã revoke thì kill hết session. Logout kill hết session.
- Ẩn job Expired khỏi Candidate nhưng giữ đơn đang mở cho HR. Job Closed thì mass-reject đơn mở.
- Tính lại MatchScore khi xem job có chọn Resume, hoặc khi skill đổi ở bên nào.
- Chỉ gửi Notification trong app, không email hay push:
  - đơn mới tới mọi Active Employer của Company
  - đổi status tới Candidate
  - lịch interview tới Candidate
  - tin nhắn tay của Employer tới Candidate
  - Company Approved hay Rejected tới các Employer của Company
- List nào cũng phân trang phía server. Đọc không tracking. Ghi đụng nhiều bảng thì bọc transaction.

## 2. Luồng dữ liệu xuyên actor

**Một vòng tuyển trọn vẹn:** Candidate register, Employer tạo Company, Admin approve Company, Employer đăng job, Candidate xem match percent rồi Apply với CV sẽ được freeze, Employer duyệt từng bước, Interview tự nâng Shortlisted lên Interviewing, Offer khép vòng, nút withdraw biến mất.

Ví dụ: Lan register, HR lập Acme, admin duyệt, HR đăng "Junior .NET Dev", Lan thấy 87% rồi nộp, HR chuyển Pending → Viewed → Shortlisted, đặt vòng 1 nên Lan thành Interviewing, rồi Offer. Mỗi bước Lan nhận Notification và history dài thêm một row.

**State machine:**

- Application: Pending → Viewed → Shortlisted → Interviewing → Offered, đi từng bước một. Reject từ trạng thái mở bất kỳ. Withdrawn chỉ Candidate được đặt, trừ khi đã Offered. Ba trạng thái cuối là terminal.
- Job: Draft → Published → Closed (đóng tay, mass-reject) hoặc Expired (tự động, giữ đơn).
- Company: Pending → Approved hoặc Rejected. Member: Pending → Active hoặc Rejected.
- Interview: Scheduled → Completed hoặc Cancelled.

**Bản đồ dữ liệu:** 7 bảng Identity cộng Candidate, Employer, CompanyProfile, Resume, Education, WorkExperience, Skill, ResumeSkill, JobCategory, JobPosting, JobCategoryMapping, JobSkill, Application, ApplicationStatusHistory, Interview, Review, SavedJob, Notification, MatchScore, ModelTrainingLog, RefreshToken. Quy tắc chính: một user một Candidate row, một user một Employer row, tên Company unique, tên Skill và Category unique, một Candidate một job một đơn, một Company một Candidate một review, một Candidate một job một save.
