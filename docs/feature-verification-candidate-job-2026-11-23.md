# Kiểm chứng 2 chức năng: Quản lý tài khoản ứng viên & Xem công việc

Ngày: 2026-11-23

## Kết luận

Cả hai chức năng **đều hoàn chỉnh**, **không cần sửa thêm**. Có thể accept.

Đây là tác vụ kiểm chứng/đánh giá mức độ hoàn chỉnh, không phải thiết kế — nên không tạo spec/design mới. Phạm vi đối chiếu: yêu cầu trong `docs/idea.md`, `docs/project-description.md`, `docs/task-assignment.md` ↔ code ↔ test ↔ chạy thật trên app đang chạy.

---

## I. Quản lý tài khoản ứng viên — ĐẠT

| Yêu cầu | Trạng thái | Bằng chứng |
|---|---|---|
| Đăng ký Candidate | CÓ | `Services/Account/AccountService.cs` → `RegisterAsync`: transaction, validate, chống trùng email, gán role `Candidate`, rollback khi lỗi. Endpoint `POST api/account/register`. |
| Đăng nhập / logout / refresh | CÓ | `Controllers/AuthController.cs`, `Services/Authentication/AuthService.cs` — JWT + refresh cookie HttpOnly. |
| Xem profile | CÓ | `GET api/account/profile` → `AccountService.GetProfileAsync`. |
| Cập nhật profile | CÓ | `PUT api/account/profile` → `UpdateProfileAsync`: fullName, email (đổi email → `EmailConfirmed = false`), dateOfBirth, gender, address, avatarUrl, githubUrl, linkedInUrl, position, summary, jobSeekingStatus, preferredWorkModel, preferredLocation. |
| Đổi mật khẩu | CÓ | `ChangePasswordAsync`: kiểm tra mật khẩu cũ, logout các session sau khi đổi. |
| Quản lý CV | CÓ | `Components/Pages/Profile.razor`: tạo/build/upload (PDF/DOC/DOCX, ≤5MB), sửa, xóa có xác nhận, set primary, tối đa 5 CV; kỹ năng + số năm kinh nghiệm do user tự khai. |
| Nâng cấp lên Employer | CÓ | `UpgradeToEmployerAsync`: tạo công ty mới (Owner, Active) hoặc join công ty có sẵn (Pending), trong 1 transaction. |
| Phân quyền dữ liệu | CÓ | Mọi query lọc theo `userId` của caller; `SafeUrl` chỉ cho avatar http/https. |

### Kiểm chứng live (app đang chạy tại `http://localhost:5168`)

```
POST /api/auth/login  {email: candidate@example.com, password: Candidate123!}
→ 200, roles = Candidate, email = candidate@example.com

GET /api/account/profile  Authorization: Bearer <token>
→ 200, { userId: 7dec9845-..., email: candidate@example.com,
         emailConfirmed: true, hasCandidateProfile: true,
         hasEmployerProfile: false, candidate.fullName: "Demo Candidate",
         candidate.address: "Ho Chi Minh City", candidate.isDiscoverable: true }

GET /api/account/profile  (không có token)
→ 401
```

---

## II. Xem công việc — ĐẠT

| Yêu cầu | Trạng thái | Bằng chứng |
|---|---|---|
| Danh sách + filter | CÓ | `Components/Pages/Jobs.razor` + `JobService.BrowsePublishedAsync` + `JobPostingRepository.BrowsePublishedAsync`: keyword, city, skill, job type, category, salaryMin, salaryMax. |
| Phân trang | CÓ | `PageSize = 10`, pager, `PageUrl()` giữ toàn bộ filter khi chuyển trang. |
| Chi tiết công việc | CÓ | `Components/Pages/JobDetail.razor` + `JobService.GetPublishedAsync`: title, công ty + địa điểm, lương, skills kèm trọng số/minYears, mô tả, categories, ngày đăng/hết hạn, số năm KN. |
| Chỉ hiện job hợp lệ | CÓ | Repository lọc `Status = Published` && (`ExpiryDate == null` || `> now`); Draft/Expired/missing id → `NotFound`. |
| Lương chỉ hiện khi đăng nhập | CÓ | `AuthorizeView` ở cả trang list và detail; guest thấy "Log in to view salary". |
| Match score | CÓ | Badge % trên card (chỉ khi đã đăng nhập Candidate); ở detail có basic score + advanced breakdown (Premium). |
| Lưu job / Apply | CÓ | Nút Save/Unsave và form apply (chọn resume + cover letter) trên trang chi tiết. |
| Ưu tiên job featured | CÓ | `OrderByDescending(job => job.FeaturedUntil != null && job.FeaturedUntil > now)`. |

### Kiểm chứng live

```
GET /it-jobs   → 200, title "Explore jobs"
  filter keyword/city/skill/categoryId/salaryMin: có
  10 job-card, có pager

GET /jobs/1    → 200, title "Senior .NET Developer | HotJob"
  có Skills, Description, Categories, "Log in to view salary", link Sign in

GET /profile   → 200 (guest), hiển thị "Please sign in to view your profile"
GET /register  → 200, "Create your HotJob account"
GET /login     → 200, "Sign in"
```

---

## III. Độ tin cậy

- **443/443 test pass**, tự chạy lại trong lần kiểm chứng này.
- App đang chạy nên lock file build (`hotjob.exe`, `hotjob.dll` bị process `hotjob (4428)` giữ). Để không tắt process của người dùng, mình chạy:

```powershell
dotnet test hotjob.slnx --nologo -v q `
  -p:BaseOutputPath=C:\Users\phulo\AppData\Local\Temp\opencode\hotjob-verify\ `
  -p:UseAppHost=false
```

Kết quả: `Passed! - Failed: 0, Passed: 443, Skipped: 0, Total: 443`.

- Test riêng cho 2 chức năng: `RegisterTests/`, `ProfileTests/`, `CandidateTests/`, `AuthTests/`, `PublicJobBrowseFilterTests`, `PublicJobDetailTests`, `SavedJobTests`, `JobSearchControllerTests`.
- Browser E2E **không chạy** do extension browse offline (`paired-offline`, reconnecting). Thay bằng HTTP smoke test + authenticated API test trên app thật — đều đạt. Không retry-loop trên trình duyệt bị chặn.

---

## IV. Ngoài phạm vi (không ảnh hưởng kết luận)

`docs/feature-audit-2026-11-23.md` vẫn ghi các khoảng trống ở: Admin back-office, CV parsing (bóc tách Education/Skills/Experience từ file upload), notification realtime, CV snapshot đầy đủ, thông báo khi sửa/hủy lịch phỏng vấn. **Không nằm trong** "quản lý tài khoản ứng viên" hay "xem công việc".

## V. Ghi chú

- Không chỉnh sửa file code nào trong lần kiểm chứng này.
- Working tree có sẵn các file liên quan đến admin audit đang sửa/chưa commit (`hotjob/Tests/hotjob.Tests/AdminTests/AdminListPagingTests.cs`, `AdminSkillServiceTests.cs`, `AdminTrainingLogTests.cs`, `Support/TestFixture.cs`, và 2 file mới `AdminAuditHooksTests.cs`, `AdminAuditServiceTests.cs`) — **không đụng vào**, để nguyên cho người dùng.
