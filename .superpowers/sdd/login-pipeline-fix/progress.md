# SDD ledger — plan: docs/login-pipeline-fix.md

Pre-flight: no shared interfaces (fix list, sequential errors).
Ruling: no git/commit available in env (git not on PATH) — tasks recorded without commits; verification is build+test output.
Ruling: Errors 3/4/5 cookie flow uses browser fetch (wwwroot/js/auth.js) instead of server-side HttpClient — server-side HttpClient cannot deliver Set-Cookie to the browser; documented in Error 8 as the implementation decision.
Ruling: Error 8 HttpClient registration skipped (no callers after browser-fetch decision).
Ruling: Sign out does API logout + in-memory SignOut without forceLoad — avoids reload flash; cookie already cleared by API.
Task 1: complete — Errors 1+2 already fixed in codebase (SeedOptions.Admins/Demo present, 6-arg IdentitySeeder matches Program.cs); build 0 errors. Ruling: EmployerGuardTests seeded Candidate UserId="cand" (FK violation, test bug, pre-existing) fixed to SeedUserAsync - 4/4 green; suite 100/100.
Task 2: complete — Error 7: AuthResponse.DisplayName + GetDisplayNameAsync(Employers->Candidates); DisplayNameTests 3, controller assert RED->GREEN, 32/32.
Task 3a: complete — IAuthService.GetSessionAsync (read-only session restore) + GET /api/auth/me; GetSessionTests 6 RED->GREEN (compile fail first), controller Me tests 2; filter 21/21. Ruling: GetSession is read-only (no revoke-all on revoked token) to avoid stale-tab races killing live sessions; reuse detection stays in RefreshAsync.
