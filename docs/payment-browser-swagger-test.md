# Payment — Browser and Swagger Manual Test

> **Scope:** VNPay sandbox checkout for Candidate Premium, Employer plans and Featured jobs.
> **Surfaces:** Blazor UI in a browser, Swagger UI/API endpoints.
> **Last updated:** 2026-10-28
> **Status:** Checklist. This document describes the tests; it does not claim they were all executed.

---

## 1. Preconditions

### 1.1 Use a scratch database

The app runs migrations and seeders on startup. Do not point it at the shared local `hotjob` database.

```bash
mysql -u root -e "DROP DATABASE IF EXISTS hotjob_verify_payment; CREATE DATABASE hotjob_verify_payment CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
```

Recreate the scratch database before a full pass, or Premium/plan state from an earlier run will invalidate the “not subscribed” and “current plan” assertions.

Change only the connection string in `HotJob/hotjob/.env.local`:

```env
ConnectionStrings__DefaultConnection=Server=localhost;Database=hotjob_verify_payment;User=root;Password=;
VnPay__TmnCode=<sandbox TmnCode>
VnPay__HashSecret=<sandbox HashSecret>
VnPay__ReturnUrl=http://localhost:5168/api/vnpay/return
VnPay__IpnUrl=
```

`.env.local` is loaded after environment variables and overrides them. Restart the app after editing it.

`VnPay__IpnUrl` is not read by the current code. The IPN URL must be registered in VNPay Merchant Admin when testing server-to-server callbacks.

### 1.2 Start the app

```bash
cd /home/dan/Projects/dotnet/HotJob/hotjob
dotnet run
```

- App: `http://localhost:5168`
- Swagger: `http://localhost:5168/swagger`
- Swagger is only enabled in Development.

### 1.3 Demo accounts

| Role | Email | Password |
|---|---|---|
| Candidate | `candidate@example.com` | `Candidate123!` |
| Employer (owner) | `employer@example.com` | `Employer123!` |

If `.env.local` overrides `Seed__Demo__*`, use those values.

### 1.4 Test amounts

| Purchase | Amount |
|---|---:|
| Candidate Premium Monthly | 99,000 VND |
| Employer Starter | 500,000 VND |
| Employer Growth | 1,500,000 VND |
| Featured job, 7 days | 200,000 VND |

### 1.5 VNPay sandbox cards

| Scenario | Card number | Name | Date | OTP |
|---|---|---|---|---|
| Success | `9704198526191432198` | `NGUYEN VAN A` | Issue date `07/15` | `123456` |
| Insufficient balance | `9704195798459170488` | `NGUYEN VAN A` | Issue date `07/15` | `123456` |
| Card not activated | `9704192181368742` | `NGUYEN VAN A` | Issue date `07/15` | `123456` |
| Card locked | `9704193370791314` | `NGUYEN VAN A` | Issue date `07/15` | `123456` |
| Card expired | `9704194841945513` | `NGUYEN VAN A` | Issue date `07/15` | `123456` |

Official list: <https://sandbox.vnpayment.vn/apis/vnpay-demo/>

Use only sandbox cards. Do not enter real card details.

---

## 2. Browser tests

### B1. Candidate Premium — successful payment

1. Sign in as `candidate@example.com`.
2. Open `/premium`.
3. Confirm the status is `Not subscribed`.
4. Click `Subscribe to Premium — 99.000đ`.
5. Confirm the browser leaves the app and opens the VNPay sandbox payment page.
6. Pay with the success card and OTP `123456`.
7. Confirm the browser returns to:

   ```text
   /payment/result?status=success&purpose=CandidatePremium&amount=99000&txnRef=...
   ```

8. Confirm the result page says `Payment successful`.
9. Return to `/premium`.

Expected:

- Status is `Active`.
- Premium-until date is approximately 30 days in the future.
- Boost-until date is set.
- The Premium badge/benefits are available.
- `Payments` contains one row with `Purpose = 2`, `Amount = 99000`, `Status = 1`, `VnpResponseCode = 00`, and `PaidAt` set.

SQL:

```bash
mysql -u root hotjob_verify_payment -e "SELECT Id, VnpTxnRef, Purpose, Amount, Status, VnpResponseCode, PaidAt FROM Payments ORDER BY Id DESC LIMIT 5;"
```

### B2. Employer plan — successful payment

1. Sign in as `employer@example.com`.
2. Open `/employer/billing`.
3. Confirm the current plan and active-job quota.
4. Choose `Starter` (500,000 VND) or `Growth` (1,500,000 VND).
5. Pay with the success card.
6. Confirm the result page says `Payment successful`.
7. Return to `/employer/billing`.

Expected:

- The current plan is the purchased plan.
- The job quota matches the plan: Starter `5`, Growth `20`.
- The expiry date is set.
- `Payments` contains `Purpose = 0`, the correct `Amount`, `Status = 1`, and `VnpResponseCode = 00`.

### B3. Featured job — successful payment

1. Sign in as the employer.
2. Open `/employer/jobs`.
3. Choose a published job and click `Feature`.
4. Pay 200,000 VND with the success card.
5. Confirm the result page says `Payment successful`.
6. Return to `/employer/jobs`.

Expected:

- The job shows `Featured until ...`.
- The job appears before non-featured jobs where featured ordering is applied.
- `Payments` contains `Purpose = 1`, `Amount = 200000`, `Status = 1`, and `VnpResponseCode = 00`.

### B4. Failed payment — insufficient balance

1. Start a Candidate Premium or Employer plan checkout.
2. Pay with `9704195798459170488`.
3. Complete the VNPay flow.

Expected under the current implementation:

- The browser returns to `/payment/result?status=processed...`.
- The result page says `Payment processed` / `This payment was already handled`.
- This is a UX mismatch: a signed but declined callback is recorded as failed, yet the return controller maps `RspCode 00` with `Granted = false` to `processed`.
- No Premium/plan/featured entitlement is granted.
- `Payments` contains `Status = 2` and a non-`00` `VnpResponseCode`.
- Reopening `/premium` or `/employer/billing` shows no new entitlement.

Do not treat `Payment processed` as success. Check the `Payments` row and the entitlement state.

### B5. Replay the return URL

1. Complete a successful payment.
2. Copy the full VNPay return URL from the browser or DevTools.
3. Open it again in a new tab.

Expected:

- The result page says `Payment processed` / `This payment was already handled`.
- No second entitlement is granted.
- The payment remains `Paid`.
- The candidate/company expiry is not extended twice.

### B6. Return URL without a signature

Open:

```text
http://localhost:5168/api/vnpay/return
```

Expected:

- The app redirects to `/payment/result?status=failed`.
- The result page says `Payment not completed`.
- No entitlement is granted.

This checks that an unsigned callback is not trusted.

---

## 3. Swagger tests

### S1. Login and Authorize

1. Open `http://localhost:5168/swagger`.
2. Expand `POST /api/auth/login`.
3. Use:

   ```json
   {
     "email": "candidate@example.com",
     "password": "Candidate123!"
   }
   ```

4. Copy `accessToken` from the response.
5. Click `Authorize` and enter:

   ```text
   Bearer <accessToken>
   ```

6. Call `GET /api/account/profile` to verify the bearer token works.

Expected:

- Login returns `200` with `accessToken`, `userId`, `email` and `roles`.
- `/api/account/profile` returns `200` for the authenticated candidate.
- Repeat with `employer@example.com` / `Employer123!` and call `GET /api/employer/company`. It should return `200`; the candidate token should return `403`.

`GET /api/auth/me` restores a session from the HttpOnly refresh cookie, not from the bearer token. Do not use it to test the `Authorize` bearer header. The VNPay endpoints themselves are anonymous; authorization is not required for them.

### S2. IPN without a signature

Call:

```text
GET /api/vnpay/ipn
```

Expected:

```http
HTTP/1.1 200 OK
Content-Type: application/json
```

```json
{"rspCode":"97","message":"Invalid signature"}
```

This verifies that an unsigned IPN is rejected. It should not create or modify a payment.

### S3. Return without a signature

Call:

```text
GET /api/vnpay/return
```

Expected:

```http
HTTP/1.1 302 Found
Location: /payment/result?status=failed
```

Swagger UI may follow the redirect and render HTML. Use the browser Network tab or:

```bash
curl -i "http://localhost:5168/api/vnpay/return"
```

No entitlement should be granted.

### S4. Replay a real signed callback through IPN

1. Complete a successful payment in the browser.
2. Copy the complete return URL, including every `vnp_*` query parameter and `vnp_SecureHash`.
3. Change only the path from `/api/vnpay/return` to `/api/vnpay/ipn`.
4. Call it in the browser or curl. Swagger UI cannot add arbitrary `vnp_*` query parameters to this endpoint.

Expected:

```json
{"rspCode":"02","message":"Transaction already confirmed"}
```

Reason: the browser return already processed the payment, so the IPN replay must not grant anything twice. The signature is valid because the signed payload does not include the URL path.

To test a *first-delivery* IPN, use the VNPay SIT tool to trigger the IPN independently, or use a public tunnel and a controlled payment where the return handler does not win the race. The IPN URL must be HTTPS and publicly reachable; `localhost` cannot receive VNPay IPN calls.

### S5. Verify payment rows after Swagger tests

```bash
mysql -u root hotjob_verify_payment -e "SELECT Id, VnpTxnRef, Purpose, Amount, Status, VnpResponseCode, PaidAt FROM Payments ORDER BY Id DESC LIMIT 10;"
```

Reference values:

| Enum | Value |
|---|---:|
| `PaymentPurpose.EmployerPlan` | 0 |
| `PaymentPurpose.EmployerFeatured` | 1 |
| `PaymentPurpose.CandidatePremium` | 2 |
| `PaymentStatus.Pending` | 0 |
| `PaymentStatus.Paid` | 1 |
| `PaymentStatus.Failed` | 2 |
| `PaymentStatus.Expired` | 3 |

Expected callback response codes:

| Case | `RspCode` | Message |
|---|---|---|
| First valid success | `00` | `Confirm Success` |
| Successful payment already processed | `02` | `Transaction already confirmed` |
| Unknown transaction | `01` | `Transaction not found` |
| Amount mismatch | `04` | `Invalid amount` |
| Invalid signature | `97` | `Invalid signature` |

`00` on a declined or expired payment stops VNPay retries but does not grant an entitlement.

---

## 4. Automated regression checks

These tests do not call the real VNPay gateway, but they cover URL construction, signature validation, amount checks, entitlement grants and replay behavior.

```bash
cd /home/dan/Projects/dotnet
dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q --filter "FullyQualifiedName~BillingTests"
```

Full suite:

```bash
dotnet test HotJob/hotjob/Tests/hotjob.Tests -nologo -v q
```

---

## 5. Cleanup

Restore the original connection string in `.env.local`, then drop the scratch database:

```bash
mysql -u root -e "DROP DATABASE hotjob_verify_payment;"
```

Never run that command against `hotjob`.

---

## 6. Known constraints

- VNPay sandbox only accepts the published test cards.
- IPN requires a public HTTPS URL. Use a tunnel and register the URL in VNPay Merchant Admin; `VnPay__IpnUrl` alone does not register it.
- Both Return and IPN process the same idempotent callback. Whichever arrives first can grant the entitlement; the other returns `02`.
- A payment link expires after 15 minutes (`Billing:PaymentWindowMinutes`).
- A signed declined/expired callback currently redirects to `status=processed`, not `status=failed`. The payment row and entitlement are still correct; the result-page label is misleading.
- Swagger cannot generate a valid signed VNPay callback. Use a real payment return query, the VNPay SIT tool, or the automated tests for signed-payload cases.
