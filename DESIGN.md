# HotJob — DESIGN.md (itviec.com clone spec)

Benchmark: https://itviec.com/. Approach A: page-by-page clone, mapped to HotJob Models (`hotjob/Models/`, `hotjob/Data/`).
Blazor Server (.NET 10), EF Core + Identity, MySQL.

Non-goals: no social feed/likes/connections, no per-job admin approval, ML runs BEFORE Apply (not after).

## 1. Global shell

Header (mirror itviec nav):
- Logo → `/`. `IT Jobs` mega-menu: by Skill (`Skill.Name`), by Title (`JobPosting.Title` index), by Company (`CompanyProfile.CompanyName`), by City (`CompanyProfile.Location`).
- `IT Companies` → Best IT Companies + Company Reviews. `Blog` → Salary Report stub. `For Employers` → `/employer`.
- Icons: inbox + notifications (`[Authorize]`), `Sign In` → Identity, EN/VI toggle (vi default; `ponytail:` full resx later).

Footer: About (Home/About/Contact/All Jobs/FAQ), Terms (Privacy/Operating/Complaints/Terms), contact block, tax-code line.

Blazor: `MainLayout` + `NavMenu` + `JobCard`/`CompanyCard` shared components.

## 2. Homepage (`/`)

Sections (itviec order):
1. Hero search: keyword + city filter, trending skills chips (Java, ReactJS, .NET, Tester, PHP… from `Skills`).
2. Campaign banner slot (static content, no model).
3. Feature shortcuts: Passive Job Search (ML suggestions), IT CV Templates (`Resume`), Story Hub (static), Company Reviews, Salary Report.
4. `Top Employers`: cards with logo (`LogoUrl`), skills (`JobSkills`), location, open-job count → `/companies/{id}`.
5. `Featured articles`: static/blog stub.

Query: `JobPostings` where `Status == Published && (ExpiryDate == null || > Now)`, include Company + JobSkills.

## 3. Jobs

Routes:
- `/it-jobs` + `/it-jobs/{skill}` + `/it-jobs/{company}` + `/it-jobs/{city}`: filter by keyword, city, skill, `SalaryMin/Max`, `JobType`.
- `/jobs/{id}` job detail: title, company, description, skills + `ImportanceWeight`, salary, expiry, category mappings.

Job card fields: `Title`, `Company.CompanyName`, skill names, salary range, `Location` (from Company), `ExpiryDate` badge.
HotJob-only addition (not on itviec): `MatchPercentage` from `MatchScore` shown BEFORE Apply button.
Actions: `Apply` (candidate, snapshot freeze to `Application`) / `Save` (`SavedJob`, PK CandidateId+JobPostingId).

## 4. Company

- `/companies/{id}`: logo, `CompanyName`, `Industry`, `Scale`, `Location`, `Description`, open jobs list.
- Reviews: 1 review per (Company, Candidate) — unique index enforced; `RatingScore` 1–5, `ReviewText`.
- `/companies/best`: Best IT Companies list (ordered by avg rating — computed query, no new model).

Entities: `CompanyProfile` (+ `VerifiedBy` FK), `Review`, `JobPosting`.

## 5. Candidate (`[Authorize(Roles=Candidate)]`)

- `/resumes`: multi-resume CRUD, upload `FileUrl`, `IsPrimary` flag, education/experience/skills entries.
- Apply flow: pick resume → freeze `SnapshotFileUrl` + `SnapshotSummary` on `Application` → unique (JobPostingId, CandidateId).
- `/my-applications`: ATS status timeline (`CurrentStatus` + `ApplicationStatusHistory`: who/when/notes), Withdraw (blocked when Offered).
- `/saved-jobs`: bookmarked list, un-save.
- Notifications: new application (to employer), status change, interview invite, company approval.

## 6. Employer (`[Authorize(Roles=Employer)]`) + Admin (`[Authorize(Roles=Admin)]`)

Employer:
- `/employer/jobs`: CRUD job (category via `JobCategoryMapping`, skills via `JobSkill` + weights); Close → mass-reject open applications (Notes="Job closed").
- `/employer/applications`: ATS board Pending → Viewed → Shortlisted → Interviewing → Offered/Rejected; every transition inserts `ApplicationStatusHistory`.
- `/employer/interviews`: N interviews per application (`InterviewDate`, `LocationOrLink`, `InterviewType`, `Notes`), notify candidate.

Admin:
- `/admin/companies`: Approve/Reject + `RejectionReason`, `VerifiedAt`, `VerifiedByAdminId`.
- `/admin/users`: lock/unlock (`IsActive`).
- `/admin/ml-logs`: `ModelTrainingLog` list (RecordCount, AccuracyScore, ModelFilePath, TrainedBy, TrainedAt).

## 7. Shared components

`JobCard`, `CompanyCard`, `SkillChip`, `SearchBar`, `StatusTimeline`, `RatingStars`, `Pagination`. Forms use DataAnnotations validation from Models.

## 9. UI system (itviec tokens, verbatim)

Source: `application.css` (:root + Style Guide Figma link in comment).

### 9.1 Colors

```css
--i-red: #ED1B2F;          /* primary brand */
--i-dark-red: #C82222;     /* primary hover */
--i-white-red: #FFF5F5;    /* secondary hover bg */
--i-light-red: #FFDFDF;    /* selected chip bg, search focus ring */
--i-black: #121212;        /* body text, header/footer gradient base (#54151C → #121212) */
--i-rich-grey: #414042;    /* secondary text */
--i-dark-grey: #A6A6A6;    /* muted, disabled */
--i-silver-grey: #DEDEDE;  /* borders */
--i-light-grey: #F7F7F7;   /* tertiary hover, code bg */
--i-hyperlink: #0E2EED;
--i-success-color: #0AB305; --i-light-success-color: #EAF9E9;
--i-info-color: #085BDD;    --i-light-info-color: #EAF0FA;
--i-error-color: #F60D00;   --i-light-error-color: #FEEEED;
--i-warning-color: #FF9119; --i-light-warning-color: #FFF4E9;
```

Header nav: `linear-gradient(269.85deg, #54151C 0%, #121212 54.89%)`. Footer: `linear-gradient(177.12deg, #121212 48.81%, #54151C 98.26%)`.

### 9.2 Typography

Font: `"Lexend", sans-serif` (Google Fonts, weights 400–900, Vietnamese subset). Body 16px, line-height 1.5, color `#121212`.
Scale: h1 28/700, h2 22/700, h3 18/700, h4 16/600, `.lg-text` 36/700, `.small-text` 14, `.tiny-text` 12, list marker red (`ul>li::marker #ED1B2F`).
`hotjob/wwwroot/app.css` hiện dùng Helvetica + `#1b6ec2` — thay bằng Lexend + tokens trên.

### 9.3 Buttons (`.ibtn`)

Primary `#ED1B2F` → hover `#C82222`; secondary outline red → hover bg `#FFF5F5`; tertiary white/grey; disabled grey `#A6A6A6`. Sizes sm/md/lg/xs, `border-radius: 4px`. Blazor: `HjButton` (Variant Primary/Secondary/Tertiary, Size Sm/Md/Lg).

### 9.4 Skill tags (`.itag`)

Pill `border-radius: 20px`; light variant border `#DEDEDE` → selected red text/border + `#FFDFDF` bg. Blazor: `SkillChip` (Selected state).

### 9.5 Job cards

Default transparent border; hover `border #DEDEDE + box-shadow 0 4px 24px #0000001F`; selected: red `::before` bar + `border #ED1B2F`; `super-hot` border `#FFDFDF`; logo 48px. Blazor: `JobCard` (Selected/SuperHot flags).

### 9.6 Search

Height var `--search-input-height: 56px` (48px compact); keyword input square-left, red focus ring `0 0 2px 2px #FFDFDF`; desktop search button 240px. Blazor: `SearchBar`.

### 9.7 Layout

`.icontainer` max 1340px; `-sm` 884px; `-xs` 656px; search form 1220px; header full-bleed max 1860px. Radius: 4px buttons/cards, 20px pills, 50% dots/avatars.

## 10. Model ↔ page map

| Page | Models |
|---|---|
| Homepage, jobs | `JobPosting`, `JobCategory`, `JobSkill`, `Skill`, `CompanyProfile` |
| Job detail + match | + `MatchScore`, `SavedJob`, `Application` |
| Company + reviews | `CompanyProfile`, `Review`, `JobPosting` |
| Resumes | `Resume`, `Education`, `WorkExperience`, `ResumeSkill` |
| ATS | `Application`, `ApplicationStatusHistory`, `Interview` |
| Notify | `Notification` |
| Admin ML | `ModelTrainingLog` |
