# HotJob — Kế hoạch triển khai tính tiền chung (Premium & Payment)

> **Ngày:** 2026-10-26  
> **Trạng thái:** Đang triển khai theo giai đoạn (đã duyệt, không cần chờ thêm)  
> **Phạm vi:** Tất cả 8 giai đoạn GĐ1–GĐ8 (thứ tự: 1 → 2 → 3 → 4 → 5 → 8 → 6 → 7)  
> **Nguồn thân chính:** `docs/premium-payment-plan.md` (tài liệu này), cùng `docs/employer-billing-plan.md` (chi tiết Employer), `docs/specs/{candidate,employer,admin,system-shared,ml-matchscore}.md` và `.superpowers/sdd/premium-payment/contract.md` (quyết định ràng buộc D1–D16; khi hợp đồng và kế hoạch mâu thuẫn, hợp đồng thắng).  
> **Ngôn ngữ mã và giao diện:** Tiếng Anh (giống UI hiện có), tài liệu bằng Tiếng Việt.

---

## 1. Mục tiêu

Xây một hệ thống thu tiền dùng chung cho hai nhóm người dùng:

| Nhóm | Trả tiền cho | Ví dụ |
|---|---|---|
| Employer (nhà tuyển dụng) | Gói đăng tin, tin nổi bật | 500.000đ/tháng |
| Candidate (ứng viên) | Gói Premium | 99.000đ/tháng |

Cả hai đều thanh toán qua **VNPay sandbox** và ghi chung vào một sổ giao dịch (`Payments`) để báo cáo doanh thu.

Kết quả cần chứng minh được khi bảo vệ:

1. Có bảng gói và hạn mức.
2. Hệ thống thật sự chặn khi hết quyền lợi (quota, Premium, tin nổi bật).
3. Người trả tiền thấy được lợi ích rõ ràng.
4. Admin xem được doanh thu theo tháng, tách theo nguồn thu (Employer / Candidate).

---

## 2. Hiện trạng

| Hạng mục | Trạng thái |
|---|---|
| Trang `/premium` | Chỉ là trang giới thiệu, nút "Coming soon" — cần viết lại tại GĐ5 để hứa đúng những gì đã làm |
| Thanh toán | Chưa có |
| Bảng gói `Plans` | Chưa có (đang bổ sung tại GĐ1) |
| Hạn mức đăng tin | Chưa có |
| Lịch sử giao dịch `Payments` | Chưa có |
| Báo cáo doanh thu | Chưa có |
| CV + Apply của Candidate | Chưa có (mới có model) |
| MatchScore | Chưa có (mới có model spec) |
| ML.NET | Chưa cài package, chưa có code train |
| Job alerts | Chưa có |
| Tìm ứng viên + đẩy hồ sơ | Chưa có |

---

## 3. Nguyên tắc thiết kế

1. **Một cổng thanh toán:** chỉ VNPay, dùng chung cho mọi giao dịch.
2. **Một sổ giao dịch:** bảng `Payments` duy nhất, phân biệt bằng `Purpose` (`EmployerPlan`, `EmployerFeatured`, `CandidatePremium`).
3. **Một bảng gói:** bảng `Plans` dùng chung, phân biệt bằng `Audience` (`Employer`, `Candidate`).
4. **Tiền tính ở server:** không nhận số tiền từ client; giá lấy từ `Plan.Price` hoặc cấu hình `Billing:`.
5. **Cấp quyền một lần:** chống xử lý trùng bằng `VnpTxnRef` + Claim `Pending → Paid` nguyên tử trong cùng transaction với entitlement grant.
6. **Trạng thái gói lưu trên người mua:** công ty lưu trên `CompanyProfile` (`PlanId`/`PlanStartedAt`/`PlanExpiresAt`), ứng viên lưu trên `Candidate` (`PremiumPlanId`/`PremiumStartedAt`/`PremiumUntil`/`BoostUntil`). Không cần bảng entitlement riêng.
7. **Múi giờ:** timestamp lưu UTC; doanh thu tháng bucket theo giờ Việt Nam (UTC+7) của `PaidAt`.

---

## 4. Kiến trúc tổng thể

```
Người dùng
   │
   ├── Employer ──► mua gói / mua tin nổi bật
   │
   └── Candidate ─► mua Premium
                     │
                     ▼
              BillingService
                     │
   ┌─────────────────┼─────────────────┐
   ▼                 ▼                 ▼
  Plans           Payments        VnPayService
                                               │
                                               ▼
                                         VNPay sandbox
                                               │
                                       ReturnUrl + IPN
                                               │
                                               ▼
                                         Cấp gói / Premium
```

| Thành phần | Trách nhiệm |
|---|---|
| `Plans` | Danh mục gói của cả Employer và Candidate |
| `Payments` | Sổ giao dịch dùng chung |
| `IEntitlementService` | Quyền lợi của công ty (hạn mức, gói hiện hành) |
| `ICandidatePremiumService` | Quyền lợi Premium của ứng viên |
| `IVnPayService` | Tạo URL thanh toán và kiểm tra chữ ký VNPay |
| `IBillingService` | Tạo giao dịch, xử lý kết quả, cấp quyền, idempotent |
| `IBillingAdminService` | Gán gói thủ công, tổng hợp doanh thu |
| `VnPayController` | `GET api/vnpay/ipn` (server-server, JSON) + `GET api/vnpay/return` (trình duyệt, redirect kết quả) |

---

## 5. Dữ liệu dùng chung

### 5.1. Bảng `Plans`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | int | Khoá chính |
| `Audience` | int enum (`PlanAudience`) | `Employer` hoặc `Candidate` |
| `Code` | varchar(30) | `FREE`, `STARTER`, `GROWTH`, `PREMIUM_MONTHLY` — unique |
| `Name` | varchar(100) | Tên hiển thị |
| `Price` | decimal(18,0) | Giá VND (không phần thập phân) |
| `JobQuota` | int | Số tin giữ cùng lúc (chỉ Employer) |
| `DurationDays` | int | Số ngày một lần mua; 0 = vĩnh viễn |
| `IsActive` | bool | Cho phép mua hay không |
| `SortOrder` | int | Thứ tự hiển thị |

### 5.2. Bảng `Payments`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | int | Khoá chính |
| `PayerUserId` | varchar(255) | Tài khoản Identity trả tiền (hoặc admin cho grant thủ công) |
| `CompanyProfileId` | int? | Công ty trả tiền (Employer) |
| `CandidateId` | int? | Ứng viên trả tiền (CandidatePremium) |
| `Purpose` | int enum | `EmployerPlan`, `EmployerFeatured`, `CandidatePremium` |
| `PlanId` | int? | Gói mua (khi mua gói) |
| `JobPostingId` | int? | Tin nổi bật (khi mua featured) |
| `Amount` | decimal(18,0) | Số tiền VND, tính ở server |
| `Status` | int enum | `Pending`, `Paid`, `Failed`, `Expired` |
| `CreatedAt` | datetime(6) | Thời điểm tạo |
| `PaidAt` | datetime(6)? | Thời điểm thanh toán thành công |
| `VnpTxnRef` | varchar(64) | Mã giao dịch gửi VNPay — unique |
| `VnpTransactionNo` | varchar(50)? | Mã giao dịch VNPay trả về |
| `VnpResponseCode` | varchar(10)? | Mã phản hồi VNPay |
| `VnpBankCode` | varchar(20)? | Ngân hàng |
| `VnpPayDate` | varchar(14)? | Thời điểm VNPay ghi nhận (`yyyyMMddHHmmss`) |
| `Note` | varchar(500)? | Ghi chú (admin grant ghi tên admin) |

Mọi khóa ngoại là `Restrict`/`SetNull` — xóa công ty/tin/người dùng **không** làm mất lịch sử thanh toán.

### 5.3. Cột thêm vào bảng có sẵn

| Bảng | Cột | Ghi chú |
|---|---|---|
| `CompanyProfiles` | `PlanId` (int?) | Gói hiện tại; null = Free |
| `CompanyProfiles` | `PlanStartedAt` (datetime?) | Ngày bắt đầu gói |
| `CompanyProfiles` | `PlanExpiresAt` (datetime?) | Hết hạn; null = vĩnh viễn |
| `Candidates` | `PremiumPlanId` (int?) | Gói Premium lần mua cuối |
| `Candidates` | `PremiumStartedAt` (datetime?) | Khi kỳ Premium không ngắt bắt đầu |
| `Candidates` | `PremiumUntil` (datetime?) | Premium cònactive nếu > now |
| `Candidates` | `BoostUntil` (datetime?) | Hồ sơ được đẩy lên trước trong tìm ứng viên |
| `JobPostings` | `FeaturedUntil` (datetime?) | Hạn tin nổi bật |

---

## 6. Danh mục gói (contract D3)

### 6.1. Gói Employer

| Code | Giá | Hạn mức | Thời hạn | `Audience` |
|---|---|---|---|---|
| `FREE` | 0đ | 1 tin | Vĩnh viễn | Employer |
| `STARTER` | 500.000đ | 5 tin | 30 ngày | Employer |
| `GROWTH` | 1.500.000đ | 20 tin | 30 ngày | Employer |

Dịch vụ đi kèm:

| Dịch vụ | Giá | Thời hạn |
|---|---|---|
| Tin nổi bật | 200.000đ | 7 ngày (từ `Billing:FeaturedPrice` / `Billing:FeaturedDays`) |

### 6.2. Gói Candidate Premium

| Code | Giá | Thời hạn | `JobQuota` | `Audience` |
|---|---|---|---|---|
| `PREMIUM_MONTHLY` | 99.000đ | 30 ngày | 0 | Candidate |

Quyền lợi theo bản tóm tắt (chính xác với code đã triển khai / đang triển khai):

| Quyền lợi | Free | Premium |
|---|---|---|
| Xem điểm phù hợp cơ bản | Có | Có |
| Điểm phù hợp nâng cao + gợi ý cải thiện CV | Không | Có (server-side gate) |
| Tạo CV (CV builder) | Có miễn phí | Không bán (theo khuyến nghị monetization) |
| Thông báo việc làm phù hợp (job alerts) | Không | Có |
| Huy hiệu Premium trên hồ sơ | Không | Có |
| Đẩy hồ sơ lên trước trong tìm ứng viên của Employer | Không | Có (`BoostUntil`) |

> **Lưu ý quan trọng:** tài liệu monetization khuyên **không bán tính năng tạo CV** (các bên khác đã làm miễn phí). Do đó CV builder để Free, và Premium chỉ bán điểm phù hợp nâng cao + job alerts + huy hiệu/đẩy hồ sơ. Trang `/premium` phải được viết lại tại GĐ5 để hứa đúng những tính năng này (loại bỏ các claim "AI CV score" / "Automatic CV builder" chưa có).

---

## 7. Luồng thanh toán (contract D4, D5, D14)

### 7.1. Luồng chung

1. Người dùng bấm "Mua".
2. Server xác định giá từ bảng gói hoặc cấu hình (không tin client).
3. Tạo `Payment` trạng thái `Pending` kèm `VnpTxnRef` duy nhất (≤ 34 ký tự).
4. Tạo URL VNPay và chuyển trình duyệt sang sandbox.
5. Người dùng thanh toán.
6. VNPay gọi về hai nơi: `ReturnUrl` (trình duyệt) và `IPN` (server-server).
7. Server kiểm tra chữ ký trước hết, sau đó số tiền, mã giao dịch; chỉ cấp khi `vnp_ResponseCode = 00` **và** `vnp_TransactionStatus = 00`.
8. Nếu thành công: `Payment = Paid`, ghi `PaidAt`, cấp quyền theo `Purpose`.
9. Hiển thị kết quả cho người dùng (chỉ thông tin phi nhạy cảm: trạng thái, số tiền, mục đích, mã giao dịch).

### 7.2. Tham số gửi VNPay

| Tham số | Giá trị |
|---|---|
| `vnp_Version` | `2.1.0` |
| `vnp_Command` | `pay` |
| `vnp_TmnCode` | Từ cấu hình `VnPay:TmnCode` |
| `vnp_Amount` | Số tiền × 100 (integer VND) |
| `vnp_CurrCode` | `VND` |
| `vnp_TxnRef` | Mã giao dịch nội bộ (`VnpTxnRef`) |
| `vnp_OrderInfo` | Nội dung ASCII không dấu |
| `vnp_OrderType` | `other` |
| `vnp_Locale` | `vn` |
| `vnp_ReturnUrl` | Trang kết quả của HotJob |
| `vnp_IpAddr` | IP người dùng (fallback `127.0.0.1`) |
| `vnp_CreateDate` | `yyyyMMddHHmmss` theo GMT+7 |
| `vnp_ExpireDate` | Tạo + 15 phút |
| `vnp_SecureHash` | HMAC-SHA512 trên chuỗi tham số đã sắp xếp |

### 7.3. Kiểm tra khi VNPay gọi về (thứ tự bắt buộc)

1. Kiểm tra chữ ký trước khi làm bất cứ việc gì (`CryptographicOperations.FixedTimeEquals`, không đổi trạng thái trước khi chữ ký hợp lệ).
2. Tìm `Payment` theo `VnpTxnRef`.
3. So số tiền VNPay trả về (× 100) với số tiền đã lưu.
4. Nếu `Payment` đã `Paid` → coi như đã xử lý, không cấp lần hai (IPN mã `02`).
5. Chỉ cấp quyền khi `vnp_ResponseCode = 00` và `vnp_TransactionStatus = 00`.

| Mã IPN | Ý nghĩa |
|---|---|
| `00` | Đã cập nhật thành công |
| `01` | Không tìm thấy giao dịch |
| `02` | Giao dịch đã xác nhận trước đó |
| `04` | Sai số tiền |
| `97` | Sai chữ ký |
| `99` | Lỗi khác |

- `IPN` và `ReturnUrl` đều đi qua cùng hàm idempotent `IBillingService.ProcessVnPayCallbackAsync`.
- Endpoint `return` **không yêu cầu auth** (trình duyệt mới quay về từ VNPay); an toàn vì chữ ký + số tiền + tra `Payment` đã chặn.
- Sai chữ ký/số tiền/không tìm thấy → **không** thay đổi trạng thái. Thất bại/hủy → `Failed` (không bao giờ giảm hạng một `Payment` đã `Paid`).
- `Pending` cũ hơn 15 phút → đánh `Expired` khi gặp, không có job nền.

### 7.4. Cấp quyền theo `Purpose` (contract D6)

| Purpose | Khi thành công |
|---|---|
| `EmployerPlan` | Cập nhật gói cho `CompanyProfile`: mua gói khác thay ngay (`PlanStartedAt = now`, `PlanExpiresAt = now + DurationDays`); mua cùng gói đang active gia hạn (`PlanExpiresAt = max(now, hiện tại) + DurationDays`). FREE không mua được. |
| `EmployerFeatured` | `FeaturedUntil = max(now, FeaturedUntil) + 7 ngày` cho tin Published, chưa hết hạn, thuộc công ty mình. |
| `CandidatePremium` | `PremiumUntil = max(now, PremiumUntil) + 30 ngày`; `PremiumStartedAt` đặt khi chưa active; `BoostUntil = PremiumUntil` mỗi lần grant. |

### 7.5. Hạn mức và phân quyền (contract D7, D8)

- Tin đang hoạt động = Draft + Published. `Closed`/`Expired` không tính. Đóng/xóa tin giải phóng một lượt. Sửa tin không tính thêm lượt.
- Gói hiện hành: `PlanId` null → Free; `PlanExpiresAt < now` → Free. Không xóa dữ liệu cũ; chỉ đánh giá lại khi dùng.
- `Còn lại = JobQuota − số tin đang hoạt động`; nếu ≤ 0 thì chặn tạo tin. Thông báo: "You have used all {used}/{quota} job slots on your plan. Upgrade to post more." (mã lỗi riêng để trang hiển thị nút Upgrade).
- Chỉ `Owner` (membership Active, công ty Approved) được mua gói/nổi bật/gán gói; member thường chỉ xem.
- Quota được thực thi ở `JobService.CreateAsync` **và** `UpdateAsync` khi tin chuyển từ Closed/Expired sang Draft/Published (re-open không bypass quota). Công ty cũ vượt quota (legacy) được grandfather: không xóa tin cũ, chỉ chặn tạo/mở lại mới.

### 7.6. Gán gói thủ công (contract D8)

Admin gán gói theo cách viết một `Payment` kiểm toán với `Amount = 0`, `Status = Paid`, `VnpTxnRef = "MANUAL-" + guid`, `Note` ghi tên admin — nên không bao giờ tính vào doanh thu.

---

## 8. Cấu hình (contract D15)

| Khoá | Ý nghĩa | Giá trị mặc định (không secret) |
|---|---|---|
| `VnPay:TmnCode` | Mã merchant sandbox | (trống — không commit) |
| `VnPay:HashSecret` | Khoá bí mật | (trống — không commit) |
| `VnPay:BaseUrl` | URL cổng VNPay | `https://sandbox.vnpayment.vn/paymentv2/vpcpay.html` |
| `VnPay:ReturnUrl` | Trang kết quả | `http://localhost:5168/api/vnpay/return` |
| `VnPay:IpnUrl` | IPN công khai | `http://localhost:5168/api/vnpay/ipn` |
| `VnPay:Version` | Phiên bản VNPay | `2.1.0` |
| `VnPay:Locale` | Ngôn ngữ | `vn` |
| `Billing:FeaturedPrice` | Giá tin nổi bật | `200000` |
| `Billing:FeaturedDays` | Hạn tin nổi bật | `7` |

`appsettings.json` chứa các giá trị không secret với `TmnCode`/`HashSecret` **trống**. Ứng dụng phải boot được nếu không có chúng; khi đó checkout báo lỗi thân thiện "Online payment is not configured."  
`hotjob/.env.example` ghi lại `VnPay__TmnCode`, `VnPay__HashSecret`, `VnPay__ReturnUrl`, `VnPay__IpnUrl` (giá trị dấu chấm, không ghi secret thật).

---

## 9. Lộ trình triển khai (contract D1)

Thứ tự thực hiện: **GĐ1 → GĐ2 → GĐ3 → GĐ4 → GĐ5 → GĐ8 → GĐ6 → GĐ7** (GĐ6, GĐ7 có thể tách ra làm mở rộng sau MVP).

| Giai đoạn | Nội dung | Phụ thuộc | Kích thước |
|---|---|---|---|
| GĐ1 | Payment core dùng chung | Không | M |
| GĐ2 | Employer billing | GĐ1 | M |
| GĐ3 | CV + Apply cho Candidate | Không | L |
| GĐ4 | MatchScore bằng ML.NET | GĐ3 | M |
| GĐ5 | Candidate Premium | GĐ1, GĐ3, GĐ4 | M |
| GĐ8 | Admin revenue + kiểm thử cuối | GĐ2, GĐ5 | S |
| GĐ6 | Job alerts | GĐ3, GĐ4 | M |
| GĐ7 | Tìm ứng viên + đẩy hồ sơ | GĐ3, GĐ4 | L |

MVP đủ demo: GĐ1 → 2 → 3 → 4 → 5.  
Mở rộng: GĐ6 → 7.

---

## 10. Chi tiết từng giai đoạn

### GĐ1 — Payment core

**Việc làm:**
- Tạo bảng `Plans`, `Payments` (migration `AddPaymentCore`).
- Thêm cột gói cho `CompanyProfile`, Premium cho `Candidate`, `FeaturedUntil` cho `JobPosting`.
- Viết `IVnPayService`/`VnPayService`: tạo URL thanh toán, kiểm tra chữ ký (HMAC-SHA512),GMT+7, amount × 100, known-answer vector test.
- Viết `IBillingService`/`BillingService`: tạo giao dịch, xử lý callback idempotent, cấp quyền cho 3 mục đích, hết hạn lazy `Pending`.
- Thêm endpoint IPN công khai (`api/vnpay/ipn`, JSON) + return (`api/vnpay/return`, redirect).
- Cấu hình `VnPay:*` và `Billing:*`; user-secrets / biến môi trường cho `TmnCode`/`HashSecret` (không commit).
- Seed 4 gói idempotent theo `Code`, back-fill `FREE` cho công ty cũ.

**Xong khi:** một giao dịch test đi hết luồng sandbox và được ghi `Paid` đúng một lần; migration áp dụng sạch trên MySQL scratch; ứng dụng boot được mà không cần secret.

### GĐ2 — Employer billing

Làm theo `docs/employer-billing-plan.md`, dùng lại Payment core của GĐ1.

**Bổ sung so với plan cũ:** `Payments.Purpose` dùng `EmployerPlan` và `EmployerFeatured`; thêm `PayerUserId`; gói `PREMIUM_MONTHLY` đã có từ GĐ1.

**Xong khi:** chặn đăng tin khi hết hạn mức; mua gói qua VNPay; tin nổi bật lên đầu và có nhãn; menu Employer/Admin có mục Billing.

### GĐ3 — CV + Apply cho Candidate

**Việc làm:**
- Trang quản lý CV: tạo, sửa, xóa, chọn CV chính, upload file.
- Học vấn, kinh nghiệm, kỹ năng.
- Nộp đơn kèm cover letter; snapshot CV lúc nộp.
- Theo dõi đơn, rút đơn.

**Xong khi:** ứng viên tạo CV và nộp được đơn; nhà tuyển dụng thấy snapshot.

### GĐ4 — MatchScore bằng ML.NET

Bám theo `docs/specs/ml-matchscore.md`.

**4.1. Trích xuất feature** (4 features cho mỗi cặp Resume + JobPosting):
- `MatchedWeightRatio`: tổng `ImportanceWeight` skill chung / tổng weight job yêu cầu (job có trọng số bằng 0 → dùng count ratio).
- `MatchedCountRatio`: số skill chung / số skill job yêu cầu.
- `ExperienceGap`: tổng `YearsOfExperience` của Resume − `ExperienceRequired` của job (theo đúng kế hoạch).
- `HasAllRequired`: 1 nếu đủ skill bắt buộc, ngược lại 0.

Job không có kỹ năng yêu cầu → không có điểm (UI ẩn badge và có dòng giải thích).

**4.2. Cold start:** chưa đủ application đã kết thúc (< ~50) → không train, tính trực tiếp `MatchPercentage = MatchedWeightRatio × 100`, `AlgorithmUsed = "weighted-rule"`.

**4.3. Train model:** package `Microsoft.ML` + `Microsoft.ML.FastTree`; mỗi application đã kết thúc là một dòng (label 1 = `Offered`, label 0 = `Rejected`/`Withdrawn`, trừ application bị từ chối do đóng công ty); FastTree binary classification, split 80/20 có seed cố định, đánh giá `AccuracyScore` trên hold-out; lưu file `App_Data/models/matchscore.zip`; ghi `ModelTrainingLogs`. Chỉ train khi có ≥ 50 application đã kết thúc **và** mỗi lớp ≥ 5; nếu không đủ → báo "không đủ dữ liệu", giữ weighted-rule. Mô hình mới chỉ được nâng cấp nếu accuracy ≥ mô hình hiện tại; ngược lại giữ mô hình cũ và vẫn ghi log lần chạy.

**4.4. Chấm điểm:** chạy model lấy xác suất 0–1 × 100; upsert `MatchScores` theo `(JobPostingId, ResumeId)`; `AlgorithmUsed = "FastTreeBinary"`; `CalculatedAt`. Tính lại khi skill Resume đổi, skill/trọng số job đổi, hoặc model được train lại. Điểm cơ bản (%) hiển thị cho mọi người; bản phân tích nâng cao (skill khớp/thiếu theo trọng số, khoảng cách kinh nghiệm, gợi ý cải thiện) chỉ dành Premium và **thực thi ở server** (GĐ5).

**Xong khi:** 2 CV khác nhau cho 2 điểm khác nhau trên cùng một job; train sinh một dòng `ModelTrainingLogs`; sau train điểm dùng `FastTreeBinary`; mất file model thì quay về weighted-rule mà không ném exception.

### GĐ5 — Candidate Premium

**Việc làm:**
- Thêm gói `PREMIUM_MONTHLY` (đã có từ GĐ1).
- Viết lại `/premium`: hiển thị gói thật và nút mua; hứa đúng tính năng đã có (loại bỏ "AI CV score"/"Automatic CV builder").
- Thanh toán VNPay (`CandidatePremium`), cộng 30 ngày vào `PremiumUntil`.
- `ICandidatePremiumService.IsPremiumAsync` ⇔ `PremiumUntil > now`; hết hạn tự động (kiểm tra khi dùng).
- Huy hiệu Premium trên hồ sơ.
- Gate quyền lợi Premium ở server (điểm nâng cao, job alerts, đẩy hồ sơ).
- Trang quản lý Premium: ngày hết hạn, gia hạn.

**Xong khi:** mua Premium xong quyền lợi mở ngay; hết hạn thì tự khóa lại; Free không dùng được quyền lợi Premium.

### GĐ8 — Admin revenue (thực hiện ngay sau GĐ5, trước GĐ6/GĐ7)

**Việc làm:**
- Trang `/admin/billing` (chỉ role Admin).
- Tổng hợp doanh thu theo tháng (bucket theo giờ Việt Nam UTC+7 của `PaidAt`), tách Employer (`EmployerPlan` + `EmployerFeatured`) và Candidate (`CandidatePremium`).
- Gán gói thủ công cho công ty (viết `Payment` kiểm toán `Amount = 0`, `MANUAL-...`, không tính doanh thu).
- Danh sách giao dịch gần đây.
- Thêm mục Billing vào menu Admin.

**Xong khi:** số liệu doanh thu khớp với các giao dịch test.

### GĐ6 — Job alerts

**Việc làm:**
- Khi một tin trở thành Published (tạo Published, Draft→Published, hoặc mở lại), tạo `Notification` cho mỗi ứng viên Premium có resume tốt nhất đạt ≥ `JobAlerts:MinMatchPercent` (mặc định 60%).
- Một lần duy nhất cho mỗi (ứng viên, tin) — thêm `Kind` và `RelatedJobPostingId` vào `Notification` để loại trùng/lọc.
- Chỉ trong ứng dụng (in-app); email để sau.
- Trang `/job-alerts` (Premium) + liên kết trong nav; ứng viên Free không nhận và thấy trang upsell.

**Xong khi:** đăng tin mới → ứng viên Premium nhận thông báo, ứng viên Free không.

### GĐ7 — Tìm ứng viên + đẩy hồ sơ

**Việc làm:**
- `Candidate.IsDiscoverable` (mặc định false) bật/tắt trên trang Profile.
- Trang Employer `/employer/candidates` (chỉ thành viên Active của công ty Approved): lọc theo kỹ năng/số năm kinh nghiệm.
- Sắp xếp: `BoostUntil > now` trước, sau đó theo ngày mới; nhãn "Nổi bật"; huy hiệu Premium.
- Chỉ hiển thị thông tin công khai: tên, vị trí, tóm tắt, kỹ năng+số năm — **không** email/số điện thoại/ngày sinh/địa chỉ.

**Xong khi:** Employer thấy hồ sơ Premium trước hồ sơ thường.

---

## 11. Báo cáo doanh thu

Chỉ tính giao dịch `Status = Paid` (grant thủ công `MANUAL-` với `Amount = 0` không tính).

| Chỉ số | Cách tính |
|---|---|
| Tổng doanh thu tháng | Tổng `Amount` theo tháng của `PaidAt` (giờ Việt Nam UTC+7) |
| Doanh thu Employer | `Purpose` là `EmployerPlan` + `EmployerFeatured` |
| Doanh thu Candidate | `Purpose` là `CandidatePremium` |
| Doanh thu theo gói | Nhóm theo `PlanId` |
| 12 tháng gần nhất | Cộng dồn theo tháng |

Dùng để vẽ biểu đồ cột hoặc bảng trong báo cáo.

---

## 12. Kiểm thử

| Nhóm | Trường hợp |
|---|---|
| Data model | Seed gói idempotent, không ghi đè giá đã sửa; ràng buộc unique `Code` / `VnpTxnRef` |
| VNPay | URL đủ tham số; chữ ký hợp lệ; sửa tham số → chữ ký sai; so sánh chữ ký không phân biệt hoa/thường; thiếu secret → lỗi "not configured" |
| Thanh toán | Thành công → cấp gói cho 3 mục đích với ngày đúng (fake `TimeProvider`) |
| Thanh toán | Gọi lại lần hai (truyền tuần tự và đồng thời) không cấp quyền lần hai |
| Thanh toán | Sai số tiền (04) không thay đổi trạng thái; sai chữ ký (97) / không tìm thấy txn (01) không đổi gì |
| Thanh toán | Mã phản hồi thất bại → `Failed`; callback `Failed` sau đó không làm giảm hạng `Paid` |
| Thanh toán | `responseCode=00` nhưng `transactionStatus` khác `00` → không cấp |
| Hạn mức | Free chỉ tạo 1 tin; đóng tin thì tạo được tin mới; gói hết hạn quay về Free; Closed/Expired không đếm |
| Hạn mức | Mua gói khác thay ngay; mua cùng gói đang active gia hạn (`max(now, expiry) + duration`) |
| Tin nổi bật | Cộng dồn ngày đúng; tin nổi bật xếp trước tin thường |
| Phân quyền | Không-owner / công ty khác / tin Draft / FREE / gói không active bị chặn |
| ML.NET | Chưa đủ dữ liệu → weighted-rule; đủ dữ liệu → train sinh `ModelTrainingLogs`; accuracy lưu đúng; sau train dùng `FastTreeBinary`; mất file model → weighted-rule |
| Premium | Mua Premium mở quyền ngay; hết hạn tự khóa; Free không dùng quyền Premium |
| Doanh thu | Tổng tháng khớp giao dịch |

Quy tắc viết test: một file mỗi 영역 dưới `Tests/hotjob.Tests/<Area>Tests/`; dùng `TestFixture` (SQLite in-memory, `EnsureCreated`); mỗi quy tắc nghiệp vụ có test đánh comportement thật (không test khô khan / không assertion); hành vi phụ thời gian dùng `TimeProvider` giả.

---

## 13. Kịch bản demo

### Phần Employer
1. Đăng nhập Employer, mở Billing.
2. Gói Free đã dùng 1/1 tin.
3. Thử đăng tin thứ hai → bị chặn, hiện nút nâng cấp.
4. Mua gói Starter qua VNPay sandbox (thẻ test NCB).
5. Quay về HotJob → gói Starter, hạn mức 5 tin.
6. Đăng tin thứ hai → thành công.
7. Mua tin nổi bật cho tin vừa đăng → tin lên đầu danh sách, nhãn "Nổi bật".

### Phần Candidate
8. Đăng nhập Candidate, mở một job và đổi giữa 2 CV → thấy % phù hợp khác nhau.
9. Mở `/premium`.
10. Mua Premium qua VNPay → mở khóa điểm nâng cao + huy hiệu ngay.
11. Giả lập hết hạn → quyền lợi bị khóa lại.

### Phần ML.NET
12. Admin mở trang Model.
13. Chưa đủ dữ liệu → hệ thống hiện `weighted-rule`.
14. Đủ dữ liệu: bấm train → sinh `ModelTrainingLogs`, điểm mới dùng `FastTreeBinary`.

### Phần Admin
15. Mở `/admin/billing`.
16. Thấy doanh thu tháng: gói Employer + tin nổi bật + Premium.
17. Kết luận: một cổng thanh toán, hai nguồn thu, có báo cáo và có ML.

Thẻ test VNPay sandbox: Ngân hàng NCB, thẻ `9704198526191432198`, tên `NGUYEN VAN A`, ngày phát hành `07/15`, OTP `123456`.

---

## 14. Rủi ro và cách xử lý

| Rủi ro | Cách xử lý |
|---|---|
| Làm Premium trước khi có CV/MatchScore | Làm GĐ3, GĐ4 trước GĐ5 |
| IPN không tới máy local | Dùng cloudflared/ngrok, hoặc để ReturnUrl xử lý khi demo |
| Cấp quyền hai lần | Claim `Pending → Paid` nguyên tử + kiểm tra `Paid` trước khi cấp; duy nhất theo `VnpTxnRef` |
| Bán CV builder trái khuyến nghị monetization | Để CV builder miễn phí; Premium không bán CV builder |
| Nhiều nguồn thu khó báo cáo | Một bảng `Payments` + `Purpose` |
| Gói hết hạn giữa chừng | Kiểm tra khi dùng, không cần job nền |
| Lộ khoá bí mật | user-secrets / biến môi trường, không commit |
| Dữ liệu train quá ít | Dùng weighted-rule và ghi rõ trong báo cáo |
| File model không ghi được | Thư mục `App_Data/models` trong `.gitignore`, kiểm tra quyền ghi |
| Mô hình mới kém hơn mô hình cũ | Giữ mô hình cũ nếu accuracy thấp hơn; vẫn ghi log lần chạy |
| Công ty cũ vượt quota Free | Không xóa tin cũ; chỉ chặn tạo/mở lại mới |

---

## 15. Việc đã chốt (không cần hỏi lại)

1. Premium bán điểm phù hợp nâng cao + job alerts + huy hiệu/đẩy hồ sơ; **không bán CV builder** (theo khuyến nghị monetization).
2. Giá Premium: 99.000đ/tháng (gói `PREMIUM_MONTHLY`, 30 ngày).
3. Thực hiện đầy đủ 8 giai đoạn theo thứ tự 1 → 2 → 3 → 4 → 5 → 8 → 6 → 7.
4. Cần trang Admin `/admin/billing` (doanh thu + gán gói thủ công).
5. Demo IPN: ReturnUrl làm fallback khi không có URL công khai.
6. Train ML thật trong demo khi đủ dữ liệu; ngược lại chứng minh cold start + một lần train.
7. Dùng dữ liệu seed để train nếu có; nếu không có đủ, chấp nhận weighted-rule và ghi rõ trong báo cáo.

---

## 16. Ngoài phạm vi

- Cổng thanh toán khác (MoMo, Stripe, …).
- Hoá đơn VAT, hoàn tiền, tự động gia hạn.
- Quảng cáo đối tác.
- Trang công ty nâng cao.
- Bán lượt xem hồ sơ.
