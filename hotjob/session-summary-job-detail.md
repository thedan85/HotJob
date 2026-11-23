# Tóm tắt session: Job Detail + Phân việc nhóm HotJob

> Ngôn ngữ: tiếng Việt. Tổng hợp toàn bộ câu trả lời trong session này.

## 1. Vấn đề bạn báo

Trang "show job detail" (`/jobs/{id}`) luôn hiện 2 nút:

- `Sign in to apply`
- `Create account to save`

Dù bạn **đã đăng nhập**.

Câu hỏi của bạn:
> "last time i build you said somes of the feature arent exist yet so you just build that, can i ask which feature is missing..."

## 2. Root cause đã xác minh

Trong `Components/Pages/JobDetail.razor:102-105`, cụm auth box đang **viết cứng**, không kiểm tra đăng nhập:

```razor
<div class="job-detail-auth">
    <a class="jobs-search-button" href="/login">Sign in to apply</a>
    <a class="jobs-clear" href="/register">Create account to save</a>
</div>
```

Hệ quả: đã đăng nhập vẫn thấy link `/login`, `/register`. Đây là bug UI, không phải backend.

Pattern đúng đã có trong codebase, nhưng `JobDetail.razor` không dùng:

- `Components/Layout/NavMenu.razor:36-52` dùng `<AuthorizeView>` với 2 nhánh `Authorized` / `NotAuthorized`.
- `Components/Routes.razor:3-14`, `Components/Pages/Employer/Company.razor:11-171`, `Components/Pages/Employer/Jobs.razor:11-170` cũng dùng `AuthorizeView`.
- Auth state: `Services/Authentication/HotJobAuthStateProvider.cs:17-61` + `AuthSession.IsAuthenticated` (`HotJobAuthStateProvider.cs:10`).

## 3. Lần build trước chỉ làm phần public

Lần trước chỉ xong phần xem công khai, chưa có apply/save thật:

- `Components/Pages/Jobs.razor:1-106` — trang `/it-jobs`.
- `Components/Pages/JobDetail.razor:1-108` — trang `/jobs/{id}`.
- `Services/Job/JobService.cs:277-318` — `BrowsePublishedAsync` + `GetPublishedAsync`.
- `Services/Job/IJobService.cs:50-62`.
- `Repositories/Job/JobPostingRepository.cs:25-136` + `Repositories/Job/IJobPostingRepository.cs:13-33`.
- `DTOs/Job/JobDtos.cs:65-111` — `JobDetailResponse`, `PublicJobListItemResponse`.
- Test: `Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs:51-100`.

## 4. Những backend còn thiếu cho người đã đăng nhập

### 4.1 Apply — chưa có backend candidate

- Có model `Models/Application/Application.cs:19-54` (cần `JobPostingId`, `ResumeId`, `CandidateId`).
- Có phía employer: `Services/Employer/ApplicantService.cs`, `Controllers/EmployerApplicantsController.cs:16-113`.
- Nhưng **không có**:
  - `ApplyAsync` cho candidate.
  - Controller candidate (`Controllers/` hiện chỉ có `Auth`, `Account`, `Companies`, `Employer*`).
  - DTO apply request.
  - API `POST /applications`.
- `Services/Job/IJobService.cs:60-62` chỉ có `GetPublishedAsync`.

### 4.2 Save — chưa có backend

- Có model `Models/SavedJob.cs:1-19`.
- Có DbSet + mapping `Data/ApplicationDbContext.cs:32,99-101`.
- Có quan hệ `Models/Users/Candidate.cs:66`, `Models/Job/JobPosting.cs:73`.
- Nhưng **không có** service / controller / ApiClient nào cho save / unsave / check đã lưu. `grep SavedJob` chỉ ra model + Db + migrations.

### 4.3 DTO thiếu field cho UI đã đăng nhập

`DTOs/Job/JobDtos.cs:65-81` — `JobDetailResponse` hiện không có:

- `IsSaved`
- `HasApplied`
- `CanApply`

Nên UI không thể hiện `Apply` / `Save` / `Đã lưu` / `Đã ứng tuyển`.

### 4.4 CV để apply vẫn là fake

Muốn apply cần `ResumeId` thật từ `Models/Resume/Resume.cs`, nhưng:

- `Components/Pages/Profile.razor:127-132` danh sách CV đang viết cứng 3 item giả.

## 5. Phân công nhóm bạn đưa

- Người 1: quản lí công việc, nhận ứng viên.
- Bạn : quản lí tài khoản ứng viên, xem công việc.
- Người 3: quản lý tài khoản NTD, trở thành NTD, xem công ty, thông báo real time.
- Người 4: quản lý công ty, quản lí tài khoản.
- Người 5: quản lý công ty (Admin), quản lý tin tuyển dụng, quản lý công việc (Admin).

Kết luận:

- **2 nút kia không phải phần của bạn.** Chúng thuộc luồng apply/save của ứng viên, dính tới `Application` mà người 1 (`nhận ứng viên`) đang giữ.
- Phần của bạn là: `quản lí tài khoản ứng viên` (đã xong) + `xem công việc` public (chỉ xem, không gồm apply/save thật).

## 6. File thuộc phần của bạn

### 6.1 Quản lí tài khoản ứng viên

- `Controllers/AccountController.cs:14-64` — `GET/PUT profile`, `POST change-password`, `POST register`.
- `Services/Account/AccountService.cs:114-246` — `GetProfileAsync`, `UpdateProfileAsync`, `ChangePasswordAsync`.
- `Services/Account/AccountService.cs:54-68` — tạo `Candidate` khi register.
- `Services/Account/IAccountService.cs:12-24`.
- `DTOs/Authentication/AccountDtos.cs:45-129` — `CandidateProfileDto`, `UpdateProfileRequest`, `ChangePasswordRequest`.
- `Components/Pages/Profile.razor:1-207`.
- `Services/Api/AccountApiClient.cs:15-31`.
- Test:
  - `Tests/hotjob.Tests/ProfileTests/ProfileServiceTests.cs`
  - `Tests/hotjob.Tests/ProfileTests/ProfileControllerTests.cs`
  - `Tests/hotjob.Tests/RegisterTests/RegisterCandidateTests.cs`
  - `Tests/hotjob.Tests/RegisterTests/RegisterValidationTests.cs`
  - `Tests/hotjob.Tests/RegisterTests/AccountControllerTests.cs`

### 6.2 Xem công việc (public)

- `Components/Pages/Jobs.razor:1-106`.
- `Components/Pages/JobDetail.razor:1-108`.
- `Services/Job/JobService.cs:277-318`.
- `Services/Job/IJobService.cs:50-62`.
- `Repositories/Job/JobPostingRepository.cs:25-136`.
- `Repositories/Job/IJobPostingRepository.cs:13-33`.
- `DTOs/Job/JobDtos.cs:65-111`.
- `wwwroot/css/JobDetail.css:91-102` — style `.job-detail-auth`.
- Test: `Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs:51-100`.

## 7. Việc bạn CẦN làm

1. **Sửa `Components/Pages/JobDetail.razor:102-105`:**
   - Bọc 2 nút guest bằng `<AuthorizeView>` theo mẫu `Components/Layout/NavMenu.razor:36-52`.
   - Đã đăng nhập thì ẩn link `/login`, `/register`.
   - Chưa cần nút Apply/Save thật nếu muốn giữ đúng ranh giới — chỉ cần không hiện sai.

2. **Nối CV thật trong `Components/Pages/Profile.razor:127-132`:**
   - Bỏ 3 CV giả.
   - Nối với `Models/Resume/Resume.cs` thật vì sau này apply cần `ResumeId`.

3. **Nếu muốn chuẩn bị cho người 1 (optional, không bắt buộc):**
   - Thêm `IsSaved`, `HasApplied`, `CanApply` vào `DTOs/Job/JobDtos.cs:65-81` để sau này UI dùng, nhưng không tự làm API apply/save.

## 8. Việc KHÔNG phải của bạn (đừng đụng để khỏi chồng việc)

- Nút `Apply` thật + `ApplyAsync` + `POST /applications` → của người 1.
- Nút `Save` thật + API save/unsave + dùng bảng `Models/SavedJob.cs:1-19` → cũng thuộc luồng ứng viên, để người 1 làm.
- `Quản lý tài khoản NTD`, `Trở thành NTD`, `Xem công ty`, `Thông báo real time` → người 3.
- `Quản lý công ty`, `quản lí tài khoản` chung → người 4.
- Admin (`quản lý công ty`, `tin tuyển dụng`, `công việc`) → người 5.

Ranh giới ngắn gọn: **bạn chỉ lo xem đúng + ẩn nút khi đã đăng nhập. Bấm nút để nộp/lưu là việc người 1.**

## 9. Tiến độ phần của bạn (backend + frontend đều có)

### 9.1 Quản lí tài khoản ứng viên — ~90% xong

Backend đã xong:
- Đăng ký candidate: `Services/Account/AccountService.cs:20-112` (tạo `Candidate` ở `:54-68`, gán role ở `:63-68`).
- Xem/sửa profile: `AccountService.cs:114-219`, interface `Services/Account/IAccountService.cs:12-24`.
- Đổi mật khẩu: `AccountService.cs:221-246` (sai pass cũ → `Unauthorized`, đúng → revoke session).
- API: `Controllers/AccountController.cs:14-64` (`GET/PUT profile`, `POST change-password`, `POST register`).
- DTO: `DTOs/Authentication/AccountDtos.cs:45-129` (`CandidateProfileDto`, `UpdateProfileRequest`, `ChangePasswordRequest`).

Frontend đã xong:
- `Components/Pages/Profile.razor:1-207` (xem/sửa profile, đổi qua `Services/Api/AccountApiClient.cs:15-31`).
- `Components/Pages/RegisterPage/RegisterPage.razor:1-165` (form candidate, gọi `AccountApi.RegisterAsync` ở `:129`).
- `Components/Pages/LoginPage.razor:1-120` (gọi `hotjobAuth.login` ở `:90`, `AuthStateProvider.SignIn` ở `:93`).

Test đã có:
- `Tests/hotjob.Tests/ProfileTests/ProfileServiceTests.cs:12-156` (7 test: get, update trim, conflict email, clear position, sai pass, revoke session, prefs round-trip).
- `Tests/hotjob.Tests/ProfileTests/ProfileControllerTests.cs:13-57` (3 test: conflict 409, NoContent, 401 khi thiếu claim).
- `Tests/hotjob.Tests/RegisterTests/RegisterCandidateTests.cs:21-104` (5 test: happy path, trim, trùng email, trùng khác hoa/thường, không duplicate retry).

Còn thiếu ~10%:
- `Profile.razor:127-132` CV còn 3 item giả viết cứng, chưa nối `Models/Resume/Resume.cs` thật → apply sau này cần `ResumeId` sẽ kẹt.

### 9.2 Xem công việc — ~80% xong

Backend đã xong:
- `Services/Job/JobService.cs:277-299` `BrowsePublishedAsync` + `:301-318` `GetPublishedAsync`.
- `Repositories/Job/JobPostingRepository.cs:25-136` (`GetPublishedByIdAsync`, `BrowsePublishedAsync` lọc published + chưa hết hạn).
- `DTOs/Job/JobDtos.cs:65-111`.

Frontend đã xong:
- `Components/Pages/Jobs.razor:1-106` (search keyword/city/skill/type, pager).
- `Components/Pages/JobDetail.razor:1-108` (detail, skills, categories, salary).

Test đã có:
- `Tests/hotjob.Tests/JobTests/PublicJobDetailTests.cs:51-100` (4 test: published OK, draft 404, hết hạn 404, id missing 404).

Còn thiếu ~20%:
- `JobDetail.razor:102-105` chưa bọc `<AuthorizeView>`, đã đăng nhập vẫn hiện nút guest. Sửa theo `NavMenu.razor:36-52`.
- `JobDtos.cs:65-81` chưa có `IsSaved/HasApplied/CanApply` (optional, để người 1 dùng sau).

**Tổng phần của bạn: ~85%. Có cả backend lẫn frontend. Còn 2 việc nhỏ: ẩn nút khi đã đăng nhập + nối CV thật.**

## 10. Đối chiếu SQL vs entity (chỉ phần của bạn)

File SQL: `project.sql` (ngoài folder code, ngang hàng `hotjob/`). Nguồn DDL ghi ở `project.sql:2` là generate từ `hotjob/Models` + `hotjob/Data/ApplicationDbContext`.

Kết quả: **khớp 100%, không thiếu thuộc tính nào.**

- **`Candidates`** — SQL `project.sql:143-161` đủ 14 cột (`Id, UserId, FullName, DateOfBirth, Gender, Address, AvatarUrl, GithubUrl, LinkedInUrl, Position, Summary, JobSeekingStatus, PreferredWorkModel, PreferredLocation`). Khớp `Models/Users/Candidate.cs:17-54`. Unique `UserId` khớp `Data/ApplicationDbContext.cs:48`. Enum `Gender` khớp comment SQL `project.sql:7`.
- **`AspNetUsers`** — SQL `project.sql:68-89` có `Id, CreatedAt, IsActive` + chuẩn Identity. Khớp `Models/Users/ApplicationUser.cs:8-10`.
- **`JobPostings`** — SQL `project.sql:316-334` đủ 12 cột (`Id, CompanyProfileId, CreatedByEmployerId, Title(200), Description(10000), SalaryMin/Max decimal(18,2), ExperienceRequired, JobType, ExpiryDate, Status, CreatedAt`). Khớp `Models/Job/JobPosting.cs:28-55` + precision ở `Data/ApplicationDbContext.cs:73-74`. Enum `Status/JobType` khớp comment SQL `project.sql:11-12`.
- **`JobSkills` / `JobCategoryMappings` / `Skills` / `JobCategories`** — SQL `project.sql:242-256,336-353` khớp `Models/Skill.cs`, `Models/Job/JobCategory.cs`; khóa chính kép + `ImportanceWeight default 1.0` khớp `Data/ApplicationDbContext.cs:76-82`.
- **`Resumes`** — SQL `project.sql:262-273` đủ `Id, CandidateId, Title(200), Summary(2000), FileUrl(2048), IsPrimary, CreatedAt`. Khớp `Models/Resume/Resume.cs:8-23`. Đây là bảng cần nối ở `Profile.razor:127-132` thay cho 3 CV giả.

Lưu ý: `project.sql` là snapshot DDL thủ công, nguồn thật là migration EF (`Migrations/`). Nếu ai sửa entity mà quên regenerate `project.sql` thì 2 bên sẽ lệch. Hiện tại phần của bạn chưa lệch gì.

## 11. Sửa navbar /login + /register cho giống các trang khác

Nguyên nhân: `/login` (`Components/Pages/LoginPage.razor:2`) và `/register` (`Components/Pages/RegisterPage/RegisterPage.razor:2`) ép `@layout RegisterLayout`, còn các trang khác dùng `DefaultLayout MainLayout` (`Components/Routes.razor:3`).

Khác biệt đã xác minh:
- Navbar: `RegisterLayout.razor:4-20` (link chết `Jobs/Companies/Salary Report/For Employers` đều `href="/"`, nút `Sign In` tĩnh, class `register-header`) vs `NavMenu.razor:8-55` (`Home/Companies/Explore jobs`, `AuthorizeView` hiện avatar + `Sign out` khi đã đăng nhập, class `top-row/nav-inner`, có menu mobile).
- Footer: `RegisterLayout.razor:26-49` có footer riêng, `MainLayout.razor:1-13` không có footer.
- CSS: `RegisterLayout.css` (`register-shell/header/content/footer`, nền `#f7f7f7`) vs `MainLayout.css` (`page/content-shell`, padding `48px 0 72px`) + `NavMenu.css`.
- Form: `LoginPage.css:1-11` (`.login-card` có card bo góc, `min-height 620px`) và `RegisterPage.css:3-16` (`.register-card` full-bleed `min-height 100vh`, `margin 0`) được viết cho `.register-content` (`RegisterLayout.css:81-92`, `padding 0`, `align-items stretch`). Sang `MainLayout` (`.content` padding `48px 0 72px`, `min-height calc(100vh - 72px)`) form vẫn render được nhưng sẽ bị co vào `content-shell 1340px` + padding, không còn full-bleed, và `.register-card min-height 100vh` có thể gây cuộn thừa.

Đã sửa theo lựa chọn "Dùng chung MainLayout":
- `Components/Pages/LoginPage.razor:2`: `@layout RegisterLayout` → `@layout MainLayout`.
- `Components/Pages/RegisterPage/RegisterPage.razor:2`: `@layout RegisterLayout` → `@layout MainLayout`.
- `RegisterLayout.razor` + `RegisterLayout.css` giữ nguyên (không xóa) vì `App.razor:13` vẫn load CSS, không ảnh hưởng.

Verify: `dotnet build` pass Razor — `0 Error(s)` (chỉ fail copy `bin/...hotjob.dll` vì app `hotjob (26992)` đang chạy khóa file, không liên quan code). Muốn build sạch thì tắt app đang chạy rồi build lại. Chưa chạy test suite vì build bin bị khóa.

## 12. Dự đoán phạm vi người 4 (quản lý công ty + quản lí tài khoản)

### 12.1 File chắc chắn phải đụng — quản lý công ty (employer tự quản công ty mình)

- `Models/Users/CompanyProfile.cs:23-70` + `Models/Users/Employer.cs:13-43` — entity gốc.
- `Data/ApplicationDbContext.cs:50-58` — map quan hệ Employer-Company.
- `DTOs/Employer/CompanyDtos.cs:7-54` — `CompanyDetailResponse`, `CompanyMemberDto`, `UpdateCompanyRequest`.
- `Services/Employer/ICompanyService.cs:8-42` + `Services/Employer/CompanyService.cs:16-114` — `GetPageAsync`, `UpdateAsync`, `ApproveMemberAsync`/`RejectMemberAsync`.
- `Controllers/EmployerCompanyController.cs:11-79` — `api/employer/company`.
- `Components/Pages/Employer/Company.razor:1-278` + `wwwroot/css/EmployerCompany.css` — UI 4 tab team/profile/compliance/audit.
- `Services/Api/CompanyApiClient.cs:14-29` — client gọi API trên.
- `Tests/hotjob.Tests/CompanyTests/EmployerCompanyControllerTests.cs`, `CompanyServiceTests.cs` — test kèm theo.
- `Program.cs:182` — DI `Services.Employer.ICompanyService`.

### 12.2 File có thể phải đụng — tùy `quản lí tài khoản` hiểu theo nghĩa nào

- Nếu là quản lý member trong công ty (duyệt Pending → Active/Rejected): vẫn là cụm trên, trọng tâm `Services/Employer/CompanyService.cs:85-114` `TransitionMemberAsync`. Không cần file mới.
- Nếu là quản lý tài khoản chung (list/block user kiểu admin): hiện **chưa có code** — không có `AdminController`, không có API list/block user. Người 4 sẽ phải **tạo mới** `Controllers/AdminAccountsController.cs` + `Services/Admin/*` + DTO mới, và chỉ đọc `Models/Users/ApplicationUser.cs:8-10` (`IsActive`), `Data/IdentitySeeder.cs:8-12` (`AppRoles`). Đừng sửa trực tiếp `Services/Account/AccountService.cs:20-246` vì đó là file chung của bạn (candidate) + người 3 (employer) — dễ xung đột.

### 12.3 File KHÔNG liên quan — đừng đụng

- Tuyển dụng/việc làm: `Services/Job/*`, `Repositories/Job/*`, `Controllers/EmployerJobsController.cs`, `Components/Pages/Jobs.razor`, `JobDetail.razor`, `Employer/Jobs.razor` — của người 1, người 5, và bạn (xem).
- Ứng viên/ATS/phỏng vấn: `Models/Application/*`, `Services/Employer/ApplicantService.cs`, `InterviewService.cs`, `NotificationService.cs`, `Controllers/EmployerApplicantsController.cs`, `EmployerInterviewsController.cs` — của người 1 + người 3 (realtime).
- Auth core: `Services/Authentication/*`, `Controllers/AuthController.cs`, `LoginPage.razor`, `RegisterPage.razor` — của bạn + người 3.
- CV/Save/Review/ML: `Models/Resume/*`, `Models/SavedJob.cs`, `Models/Review.cs`, `Models/ML/*` — không thuộc công ty/tài khoản.
- Xem công ty công khai: `Components/Pages/Companies.razor`, `Services/Company/*`, `Controllers/CompaniesController.cs`, `DTOs/Company/*`, `Repositories/Company/*` — khả năng của người 3 (`Xem công ty`), người 4 chỉ đọc ké, không sửa.
- `Migrations/*`, `project.sql` — sinh tự động, không sửa tay.

### 12.4 Ranh giới dễ chồng — cần chốt với nhóm

- `CompanyProfile.Status`, `VerifiedByAdminId`, `RejectionReason` (`CompanyProfile.cs:48-57`) là của người 5 (Admin duyệt). Người 4 chỉ được sửa `Website/Industry/Location/Scale/Description` (`DTOs/Employer/CompanyDtos.cs:39-54`).
- `AccountService.cs:331-400` `AddEmployerProfileAsync` (trở thành NTD) là của người 3, không phải người 4.
- `Profile.razor`, `AccountController.cs:14-64` là của bạn — người 4 làm `quản lí tài khoản` admin thì tách service mới, đừng sửa vào đây.
