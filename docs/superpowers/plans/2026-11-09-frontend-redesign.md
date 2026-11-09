# Frontend Redesign — Quiet Signal

Scope: markup, CSS, motion JS only. No backend, API, DTO, service, or migration changes.

Direction: 70% modern minimalist SaaS, 20% editorial, 10% soft UI.

## Checklist

- [x] Design tokens, typography, reset in `app.css`
- [x] Theme layer (`css/Theme.css`) loaded last for shared primitives
- [x] Fonts + motion script in `App.razor`
- [x] Motion module `js/motion.js` (IntersectionObserver, MutationObserver, reduced motion)
- [x] Nav + layout + footer
- [x] Home
- [x] Jobs
- [x] Job detail
- [x] Companies + company detail
- [x] Auth pages (login, register, become employer)
- [x] Candidate pages (profile, applications, alerts, premium, payment result)
- [x] Employer pages (jobs, candidates, interviews, company, billing)
- [x] Admin pages
- [x] State pages + reconnect modal
- [x] Build + tests (417 passed)
- [ ] Browser smoke at 375/768/1024/1440 — blocked: local MySQL rejects `root` with empty password; app cannot start.
