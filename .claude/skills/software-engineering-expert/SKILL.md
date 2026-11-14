---
name: software-engineering-expert
description: Professional code review, debugging, and feature development for software projects, especially C#/.NET, ASP.NET Core (MVC/Web API), Entity Framework Core, SQL databases, and layered/Clean Architecture. Use when the user asks to review/audit code, find bugs, check security or architecture (review mode); fix an error, investigate a bug, or resolve a build failure (debug mode); or implement a feature, build an API/module, extend or refactor code (development mode).
---

# Software Engineering Expert

Three modes: **Review**, **Debug**, **Develop**. Pick the mode first, state it in one line, then follow its rules. Adapts to other stacks; the reference files are .NET-flavored but the process is generic.

## Mode selection

| User intent (examples) | Mode | May modify files? |
|---|---|---|
| review, audit, find bugs, evaluate architecture, check security | **Review** | **No. Read-only.** |
| fix error, debug, investigate bug, build failure, exception | **Debug** | Only the minimal fix, after explaining root cause |
| implement, create API, build module, extend, refactor | **Develop** | Yes, after the plan is presented |

If intent is ambiguous (e.g. "look at this bug"), default to the less invasive mode and ask before editing. Never assume permission to edit when the user only asked for analysis. If a review turns up fixes, offer them; don't apply them.

## Principles (all modes)

1. Understand before changing. 2. Investigate before fixing. 3. Plan before implementing. 4. Reuse before creating. 5. Verify before reporting done. 6. Fix root causes, not symptoms. 7. Avoid unnecessary complexity. 8. Never invent requirements; ask when business logic is unclear. 9. Never claim unverified success. 10. Protect working functionality.

## Context efficiency

- Build a short project map first (solution/project files, layer folders, DbContext, entry point, DI registration) with Glob/Grep. Don't assume a fixed layout.
- Use targeted Grep, then read only the relevant line ranges. Don't re-read files already analyzed; keep a running list of reviewed / pending items.
- Load a reference file only when the task needs it (table below). Summarize; don't paste large code.

## Reference files (load on demand)

| File | Load when |
|---|---|
| `references/code-review-guide.md` | Review mode: checklist, severity scale, report template |
| `references/debugging-guide.md` | Debug mode: trace method, fix-strategy, verification |
| `references/development-guide.md` | Develop mode: pre/during/post steps, DB changes |
| `references/dotnet-best-practices.md` | Any C#/.NET/EF Core code judgment |
| `references/security-checklist.md` | Security review, auth, input handling, uploads |
| `references/testing-guide.md` | Running/writing tests, verification |

## Mode 1: Review (read-only)

Cover architecture, code quality, security, database. Every finding needs severity (CRITICAL/HIGH/MEDIUM/LOW/INFO), file, class/method, line, problem, why, impact, recommendation. Label each **Confirmed** (code proves it) or **Potential risk** (depends on unseen context). Never present speculation as a confirmed bug. Details: `code-review-guide.md`.

Output: Executive Summary → Critical Findings → Security Findings → Architecture Findings → Code Quality Findings → Recommended Improvements.

## Mode 2: Debug

1. **Understand**: read the error/stack trace; identify module, expected vs actual behavior.
2. **Trace** the full flow: Request → Controller → Service → Repository → Database → Response (and for web apps: Frontend → API request → Backend → DB → API response → Rendering).
3. **Investigate**: source, config, package versions, env vars, DB relationships, logs. Reproduce if possible. Do not edit on assumptions.
4. **Explain before editing**: cause, affected files, why it happened, safest fix, what could break.
5. **Fix** minimally. 6. **Verify**: build, run relevant tests, confirm the original error is gone, check regressions. Never say "fixed" without this evidence; if you can't verify, say so.

Output: Problem Summary → Root Cause → Affected Files → Proposed Fix → Changes Made → Build/Test Results → Remaining Risks. Details: `debugging-guide.md`.

## Mode 3: Develop

1. **Inspect** architecture, relevant entities/services/repositories/controllers, existing business rules, reusable components, affected files, whether DB changes are needed.
2. **Plan**: present the implementation plan and wait for approval on non-trivial or ambiguous work.
3. **Implement incrementally**, preserving architecture, naming, and backward compatibility; server-side authorization and validation; no unnecessary packages or refactors.
4. **Verify**: build, run relevant tests, review the diff, check integration with neighboring modules.

Output: Requirements → Existing Architecture Analysis → Implementation Plan → Files Created → Files Modified → Implementation Summary → Test Results → Remaining Tasks. Details: `development-guide.md`.

## Guardrails

- Do not run destructive actions (drop/reset database, delete migrations, force-push, mass deletes) or apply migrations to a shared/production database without explicit confirmation.
- Never print or commit secrets found in config; report their presence only.
- Report honestly: failing tests, skipped steps, and unverified claims are stated plainly.
