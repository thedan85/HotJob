# HotJob — Mô tả dự án

## 1. Dự án là gì?
HotJob là **Job Board + ATS (Applicant Tracking System)** kiểu TopCV, viết bằng **Blazor Server (.NET 10)**.
Không phải mạng xã hội kiểu LinkedIn. Chỉ làm 1 việc: **kết nối ứng viên với nhà tuyển dụng** — đăng job, nộp CV, theo dõi trạng thái, hẹn phỏng vấn, gợi ý việc phù hợp bằng ML.NET.

## 2. Ai dùng? (3 vai trò)
- **Candidate (ứng viên):** tạo nhiều CV (học vấn, kinh nghiệm, kỹ năng), tìm/lưu job, xem % phù hợp do ML chấm trước khi nộp, nộp đơn, theo dõi trạng thái, rút đơn, đánh giá công ty 1 lần.
- **Employer (HR):** tạo công ty mới (chờ Admin duyệt) hoặc xin join công ty có sẵn (chờ Owner duyệt). Đăng job (chọn ngành + kỹ năng + trọng số), duyệt CV theo quy trình Pending → Viewed → Shortlisted → Interviewing → Offered/Rejected, lên lịch phỏng vấn, đóng job.
- **Admin:** duyệt công ty (Approve/Reject + lý do), khóa/mở tài khoản, xem log train model ML.

Đăng ký chỉ Candidate. Candidate bấm Become Employer để thêm vai Employer (1 user giữ cả 2 profile). Admin seed sẵn.

## 3. Có gì? (tính năng chính)
1. **Auth Identity:** đăng nhập/đăng ký, phân quyền 3 roles, 7 bảng AspNet*.
2. **Resume/CV:** nhiều CV/người, upload file, freeze bản snapshot lúc nộp (HR luôn thấy đúng bản lúc apply).
3. **Job Posting:** đăng thẳng nếu công ty đã Approved, filter theo category/skill/lương, hạn nộp (hết hạn tự ẩn, chủ động đóng thì mass-reject đơn dở).
4. **ATS:** audit mọi lần đổi trạng thái (ai, lúc nào, ghi chú), chống nộp trùng, candidate rút đơn trừ khi đã Offered.
5. **Interview:** N vòng/phỏng vấn mỗi đơn, báo lịch qua Notification.
6. **ML.NET MatchScore:** chấm % hợp Resume↔Job (0–100) để gợi ý trước nút Apply + giúp HR lọc đơn. Log mỗi lần train vào ModelTrainingLogs.
7. **Tiện ích:** SavedJobs, Reviews công ty, Notifications theo event (có đơn mới, đổi status, có lịch PV, duyệt công ty).

## 4. Không có gì (đừng code nhầm)
Không có Posts/Feed/Like/Connection kiểu mạng xã hội. Không có Admin duyệt từng Job. Không chấm điểm sau khi nộp — ML chạy **trước** nút Apply.

## 5. Kỹ thuật
- Stack: Blazor Server Interactive, EF Core + Identity, MySQL(localdb), ML.NET .
- DB: 27 bảng (7 Identity + 20 domain). Code ở `hotjob/Models/` (21 files), `hotjob/Data/ApplicationDbContext.cs` (FluentAPI: unique, cascade, composite key).
- Docs: `docs/idea.md` (flow chi tiết theo actor), `docs/database-design.md` (bảng + FK).
- Chạy: `dotnet build` trong `source/hotjob`. Connection string `Default` trong appsettings.json.

## 6. Chia việc gợi ý
- Auth + layout + phân quyền trang.
- Candidate: CRUD Resume + Apply + SavedJobs + Reviews.
- Employer: Company join/duyệt + CRUD Job + duyệt Application + Interview.
- Admin: duyệt Company + quản lý Users + xem TrainingLogs.
- ML: tính MatchScore + trang gợi ý + retrain.
