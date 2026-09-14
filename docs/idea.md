# HotJob — Idea (ASP.NET Identity + ATS Job Board)

ATS = Applicant Tracking System: hệ theo dõi tuyển dụng Job → Apply → Viewed → Shortlisted → Interview → Offer/Reject.
Tài khoản: chỉ đăng ký `Candidate`. `Candidate` bấm Become Employer để thêm vai `Employer` (1 user giữ cả 2 profile). `Admin` seed sẵn, không cho đăng ký.

## 1. Candidate flow (6 bước)

1. **Đăng ký / đăng nhập** — Register role Candidate (Identity). Rule: 1 AspNetUsers ↔ 1 Candidates. Bảng: `AspNetUsers`, `Candidates`.
2. **Quản lý Resume** — Tạo nhiều CV, 1 CV primary. Mỗi Resume có Educations, WorkExperiences, ResumeSkills(YearsOfExperience). Rule: chỉ sửa CV của mình. Bảng: `Resumes`, `Educations`, `WorkExperiences`, `ResumeSkills`, `Skills`.
3. **Tìm việc + ML gợi ý** — Filter theo JobCategories, JobSkills. ML.NET tính `MatchScores.MatchPercentage` (0–100%) Resume ↔ JobPosting, hiển thị trước nút Apply để ứng viên cân nhắc. Bảng: `JobPostings`, `JobCategoryMappings`, `JobSkills`, `MatchScores`.
4. **Lưu tin (SavedJobs)** — Lưu job để apply sau. Rule: PK(CandidateId, JobPostingId), không trùng. Bảng: `SavedJobs`.
5. **Nộp đơn (Apply)** — Chọn Resume → Application. Rule: Job phải Published + ExpiryDate > Now; copy file CV lúc nộp vào `SnapshotFileUrl` + `SnapshotSummary` (freeze, HR xem bản lúc nộp); unique(JobPostingId, ResumeId) chống nộp trùng. Bảng: `Applications`. Event: Apply mới → Notification cho các Employer của Company.
6. **Theo dõi + rút đơn + review** — Xem trạng thái Application + History. Rút đơn (Withdrawn) bất cứ lúc nào trừ khi đã Offered. Review công ty 1–5 sao, 1 Candidate 1 Company 1 lần duy nhất. Bảng: `Applications`, `ApplicationStatusHistory`, `Reviews`.

## 2. Employer flow (7 bước)

1. **Become Employer / vào Company** — 1 nút duy nhất: form gồm FullName + Position + radio Create/Join Company (tạo mới: tên/web/logo/location...; join: chọn Company có sẵn). Không tách nút riêng vì `Employer.CompanyProfileId` required — tách ra tạo state lửng. Tạo Company mới → thành IsOwner + Membership Active ngay (chỉ Company Pending). Join → Membership Pending, chờ Owner duyệt. Submit 1 transaction: tạo `Employers` + gán role Employer (giữ cả 2 vai). Bảng: `Employers`, `CompanyProfiles`.
2. **Chờ duyệt** — Tạo mới (Owner): `Employer Active` ngay, chỉ Company Pending chờ Admin duyệt (Approved mới được đăng Job; Rejected kèm RejectionReason). Join: chờ Owner duyệt Active mới được thao tác. Bảng: `CompanyProfiles`, `Employers`.
3. **Đăng Job** — Company Approved được đăng thẳng (không cần Admin duyệt từng Job). Chọn JobCategories (n-n qua Mappings), JobSkills + ImportanceWeight. Rule: chỉ Employer Active của Company; Owner thêm quyền duyệt member + sửa Company. Bảng: `JobPostings`, `JobCategoryMappings`, `JobSkills`.
4. **Duyệt Application** — Xem Applications của Job thuộc Company mình + CV snapshot. Chuyển trạng thái theo state machine: Pending → Viewed → Shortlisted → Interviewing → Offered/Rejected. Mỗi lần chuyển insert `ApplicationStatusHistory` (ai, lúc nào, ghi chú). Rule: Employer Active chỉ Job của Company mình (Owner = Member về ATS); unique chống nộp trùng đã chặn ở bước Apply. Bảng: `Applications`, `ApplicationStatusHistory`. Event: đổi status → Notification cho Candidate.
5. **Lọc Application (rule HR + điểm ML)** — Sắp xếp/lọc theo skill khớp + `MatchScores.MatchPercentage` có sẵn. Bảng: `Applications`, `MatchScores`, `JobSkills`, `ResumeSkills`.
6. **Phỏng vấn (Interview)** — Sau Shortlisted, tạo N vòng: InterviewDate, LocationOrLink, InterviewType, Notes, Status (Scheduled/Completed/Cancelled). Bảng: `Interviews`. Event: Interview scheduled → Notification cho Candidate.
7. **Đóng Job** — Chủ động Close → mass chuyển apps đang dở (Pending/Viewed/Shortlisted/Interviewing) sang Rejected, Notes="Job closed". Bảng: `JobPostings`, `Applications`, `ApplicationStatusHistory`.

## 3. Admin flow (3 việc)

1. **Duyệt Company** — Xem CompanyProfiles Pending → Approve (VerifiedAt, VerifiedByAdminId) / Reject (+ RejectionReason). Bảng: `CompanyProfiles`. Event: kết quả duyệt → Notification cho Owner/Employer.
2. **Quản lý Users** — Xem/khóa (IsActive) tài khoản, gán role. Bảng: `AspNetUsers`, `Candidates`, `Employers`.
3. **Giám sát ML** — Xem `ModelTrainingLogs` (RecordCount, AccuracyScore, ModelFilePath, TrainedBy). Bấm retrain thủ công khi đủ dữ liệu mới. Bảng: `ModelTrainingLogs`.

## 4. System-auto (không cần người bấm)

- **Job hết hạn:** ExpiryDate qua → Status Expired, ẩn khỏi list Candidate, apps dở giữ nguyên cho HR xử tiếp (khác Close chủ động mass Rejected).
- **Tính MatchScore:** khi xem Job hoặc Resume/Job đổi → tính lại % Resume ↔ Job.
- **Notifications:** Apply mới → Employer; đổi status → Candidate; Interview scheduled → Candidate; Company Approved/Rejected → Employer.

## 5. State diagrams

ATS: `Pending → Viewed → Shortlisted → Interviewing → Offered / Rejected`, nhánh `Withdrawn` (Candidate tự rút, trừ khi Offered).
Job: `Draft → Published → Closed (chủ động, mass Rejected apps dở) / Expired (tự động, giữ apps)`.
Company: `Pending → Approved / Rejected`. Member: `Pending → Active / Rejected`.
