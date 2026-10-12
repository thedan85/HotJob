# HotJob — Phân việc 5 người  mỗi người frontend + backend 

Sử dụng code Blazor pages + service/EF . Dùng chung `ApplicationDbContext`, entities trong `hotjob/Models/`, roles `Admin/Candidate/Employer`. Không ai sửa DbContext/entities khi chưa thống nhất nhóm.

## M1 — Nền tảng: Auth + Layout + Admin
- Frontend: layout, nav theo role, trang Login/Register (chỉ Candidate), nút Become Employer, trang Admin (duyệt Company, ban/unban user, xem ModelTrainingLogs).
- Backend: Identity wiring (đã có trong Program.cs), `IdentitySeeder`, authorize policies, endpoint Become Employer (tạo `Employers` + gán role, không chờ Admin), CRUD admin cho `CompanyProfiles` (Approve/Reject + RejectionReason), khóa `ApplicationUser.IsActive`.
- Nhận: xong đầu tiên, các cụm khác chờ auth để gắn `[Authorize]`.
- Xong khi: register ra Candidate, Become Employer lên Employer ngay, Admin duyệt Company được, user bị ban không login được.

## M2 — Candidate: Resume/CV
- Frontend: trang quản lý Resumes (list/tạo/sửa/xóa, set primary), form Education + WorkExperience + ResumeSkills, upload file CV.
- Backend: CRUD `Resumes/Educations/WorkExperiences/ResumeSkills`, upload file vào wwwroot/uploads, rule chỉ sửa CV của mình (filter CandidateId).
- Xong khi: 1 candidate tạo N CV, upload file xem lại được, chỉ thấy CV của mình.

## M3 — Job: đăng tin + tìm việc + SavedJobs + Reviews
- Frontend: Employer — form đăng/sửa/đóng Job (chọn categories + skills + ImportanceWeight); Candidate — list/search/filter/sort job, nút Save, nút Apply (hiện % MatchScore), form Review công ty.
- Backend: CRUD `JobPostings/JobCategoryMappings/JobSkills`, search/filter/paging, `SavedJobs` (chống trùng PK), `Reviews` (unique Company+Candidate), rule chỉ Employer Active của Company Approved được đăng, Close → mass Rejected apps dở.
- Xong khi: đăng job hiện lên list, filter chạy, save/unsave được, review 1 lần duy nhất.

## M4 — ATS: Application + Interview + Notification
- Frontend: Candidate — list applications + history timeline + nút Withdrawn; Employer — hàng đợi duyệt đơn, nút chuyển status, form tạo lịch Interview; chuông Notification.
- Backend: Apply (check Published + ExpiryDate, copy snapshot file, unique Job+Resume), state machine + `ApplicationStatusHistory` mỗi lần chuyển, CRUD `Interviews`, bắn `Notifications` theo event (đơn mới → Employer; đổi status/lịch PV → Candidate; duyệt company → Employer).
- Phụ thuộc: cần M2 (Resume) + M3 (Job) xong mới test end-to-end được.
- Xong khi: apply → employer thấy → chuyển status có history → candidate nhận noti → đặt lịch PV được → rút đơn được (trừ Offered).

## M5 — ML.NET MatchScore + tích hợp
- Frontend: badge % phù hợp trên card Job, trang "Gợi ý cho bạn", trang Admin xem TrainingLogs + nút Retrain.
- Backend: service tính `MatchScores` từ ResumeSkills ↔ JobSkills (chạy khi xem Job / Resume-Job đổi), trang train lưu `ModelTrainingLogs` (RecordCount, Accuracy, ModelFilePath, TrainedBy).
- Phụ thuộc: cần M2 + M3 có dữ liệu thật mới train/test được.
- Xong khi: mở job thấy % hợp lý (đổi skill đổi điểm), retrain lưu log xem được.

## Thứ tự + milestones
1. Tuần 1: M1 xong auth → M2 + M3 song song.
2. Tuần 2: M4 nối M2+M3 thành luồng apply hoàn chỉnh.
3. Tuần 3: M5 gắn điểm lên UI M3, demo + fix.
