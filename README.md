# HotJob

**HotJob** is a job portal — a job board + ATS (Applicant Tracking System) in the style of TopCV. It connects candidates with employers and deliberately does **not** implement a social graph (no feeds, likes, or connections). One product, one job: post jobs, apply with a CV, track the pipeline, schedule interviews, and get ML-assisted match suggestions.

> Note: this README reflects the current `dev` branch feature set. Some of those features (e.g. skill management, VNPay billing, ML.NET scoring) land on `main` as `dev` is merged.

## Overview

HotJob has three roles:

- **Candidate** — sign up, create multiple CVs (education, experience, skills), search and save jobs, see an ML-predicted match percentage before applying, apply, track application status, withdraw (until Offered), review companies.
- **Employer (HR)** — create a new company (pending admin approval) or request to join an existing one (pending owner approval). Post jobs with categories and weighted skills, review applications through a structured ATS pipeline, schedule interviews, and close jobs.
- **Admin** — approve/reject companies with a reason, lock/unlock accounts, manage the skills taxonomy (CRUD API with restricted references), and view ML model training logs. Admin console pages cover the dashboard, accounts, companies, and jobs.

Registration is candidate-only. A candidate can tap **Become Employer** to add the employer role — one user can hold both profiles. Admin accounts are seeded at startup.

## Features

### For candidates

- **Resume/CV management** — multiple CVs per user, file upload, and an immutable snapshot frozen at apply time so HR always sees exactly what was submitted.
- **Job search & filtering** — by keyword, category, skill, salary range, and location; save jobs; view your applications; withdraw before an offer is made.
- **Skill-based search & matching** — structured skills taxonomy powers refined job search and a skill-match query per resume.
- **ML.NET MatchScore** — a 0–100 resume↔job match score shown *before* the Apply button, trained with FastTree and logged to `ModelTrainingLogs`.
- **Job alerts** — get notified about new jobs matching your profile (free tier).
- **Premium plans** — subscribe via VNPay for premium candidate features.
- **Sign in with Google** — alongside classic Identity email/password login and JWT access/refresh tokens.

### For employers

- **Company onboarding** — create a company (approved by an admin) or request to join an existing one (approved by the company owner).
- **Job posting** — requires an approved company; pick category and skills with weights; jobs auto-hide at their deadline, and manually closing a job mass-rejects open applications.
- **ATS pipeline** — applications flow `Pending → Viewed → Shortlisted → Interviewing → Offered / Rejected` with a full audit trail (who changed what, when, and why) and duplicate-application protection.
- **Interviews** — multiple rounds per application, with notifications sent to the candidate.
- **Employer console** — jobs, applicants, interviews, and company management pages.
- **Premium plans** — VNPay-billed featured-job placement and premium employer features.

### For admins

- **Company review** — approve or reject with a reason; manage account lock/unlock.
- **Skills management API** — read/create/update/delete skills with restricted references (skills in use by jobs or resumes can't be deleted), plus paged listing and validation.
- **ML training logs** — audit of model training runs.
- **Admin pages** — dashboard, accounts, companies, and jobs management.

### Platform

- **Notifications** — event-driven: new application, status change, interview scheduled, company approved.
- **Company reviews** — candidates review a company once.
- **VNPay billing** — premium/featured plans for employers and candidates; checkout is disabled gracefully when VNPay keys are absent (`Online payment is not configured.`).
- **REST API + Swagger** — MVC JSON controllers alongside the Blazor UI, with Swagger UI for exploration.

## Tech stack

| Layer | Technology |
|---|---|
| UI | ASP.NET Core Blazor (Interactive Server) |
| API | MVC JSON controllers (`Controllers/`) |
| Runtime | .NET 10 |
| Data access | EF Core 9 + Pomelo (MySQL); Fluent API for keys, cascades, unique & composite indexes |
| Auth | ASP.NET Identity (7 `AspNet*` tables) + JWT bearer, Google OAuth, refresh tokens |
| ML | ML.NET (FastTree) for MatchScore training/inference |
| Payments | VNPay (amounts × 100, HMAC-SHA512 over ascending URL-encoded `vnp_*` params) |
| Tests | xUnit (`hotjob/Tests/hotjob.Tests`) |
| Docs | Swagger / Swashbuckle; markdown specs in `docs/` |

## Architecture & layout

```
HotJob/
├── hotjob/
│   ├── Components/        # Blazor UI (App, Routes, Layout, Pages/)
│   │   └── Pages/         # Home, Jobs, JobDetail, Profile, Premium, PaymentResult,
│   │                      #   Employer/*, Admin/*, JobAlerts, MyApplications, ...
│   ├── Controllers/       # REST/JSON API (Auth, Account, Companies, Jobs, Job search,
│   │                      #   Employer*, Admin skills, Dev match model, VNPay)
│   ├── Services/          # Business logic by domain: Account, Admin, Authentication,
│   │                      #   Billing, Candidate, Company, Employer, Job, JobAlerts, ML
│   ├── Models/            # Domain entities (EF Core)
│   ├── Data/              # ApplicationDbContext (Fluent API), migrations, seeders
│   ├── Repositories/      # Data-access abstractions where used
│   ├── DTOs/              # API request/response contracts
│   ├── Middleware/        # Custom middleware
│   └── Tests/             # xUnit test project (excluded from app build)
├── docs/                  # Idea, specs, database design, plans (see below)
├── project.sql            # Schema + sample data dump
└── hotjob.slnx            # Solution
```

Conventions worth knowing:

- **ServiceResult pattern** — services return `ServiceResult<T>` with a `ServiceErrorType`; business validation is returned, not thrown.
- **TimeProvider** — the registered `TimeProvider` is used for "now" in business logic, never `DateTime.UtcNow`.
- **No HTTP loopback** — Blazor pages call services directly through DI instead of calling the JSON API over HTTP.
- **Portable EF queries** — queries are written to translate on both SQLite (tests) and MySQL/Pomelo.
- **Money** — VND stored as `decimal(18,0)`.
- **Secrets** — `Jwt__SecretKey`, `VnPay__*` and capability keys live in `.env.local` / user-secrets, never in `appsettings.json`; the app boots with empty VNPay keys (checkout then fails gracefully).
- **Seeding** — migrations and seeders (admins, demo employer/candidate/company) run automatically at startup.

## Documentation

- [`docs/idea.md`](docs/idea.md) — detailed flows per actor
- [`docs/project-description.md`](docs/project-description.md) — project description (Vietnamese)
- [`docs/database-design.md`](docs/database-design.md) — tables and foreign keys
- [`docs/specs/`](docs/specs/) — feature specs: [admin](docs/specs/admin.md), [candidate](docs/specs/candidate.md), [employer](docs/specs/employer.md), [ML match score](docs/specs/ml-matchscore.md), [system](docs/specs/system-shared.md)
- [`docs/superpowers/`](docs/superpowers/) — design docs and implementation plans
- [`docs/task-assignment.md`](docs/task-assignment.md) — team task breakdown

## Testing

```bash
dotnet test hotjob/Tests/hotjob.Tests -nologo -v q
```

The test suite covers auth, billing, registration, employer/candidate flows, company admin management, job alerts, ML, and seeding.
