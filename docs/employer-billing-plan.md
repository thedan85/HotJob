# HotJob — Kế hoạch triển khai tính tiền cho Employer

> **Ngày:** 2026-10-26
> **Trạng thái:** Chờ duyệt trước khi code
> **Phạm vi:** Đủ để demo trước hội đồng
> **Tài liệu tham khảo:** [`monetization-strategy.md`](../../monetization-strategy.md)

---

## 1. Mục tiêu

Áp dụng mô hình kiếm tiền từ nhà tuyển dụng vào hệ thống thật:

- Công ty có gói đăng tin và hạn mức cụ thể.
- Hết hạn mức thì không đăng thêm tin được.
- Công ty trả tiền qua **VNPay sandbox** để nâng gói hoặc mua tin nổi bật.
- Quản trị viên xem được **doanh thu theo tháng**.

Kết quả cuối cùng phải chứng minh được 3 điều khi bảo vệ:

1. Có chỗ lưu gói và hạn mức.
2. Hệ thống thật sự chặn khi hết hạn mức.
3. Người trả tiền thấy được lợi ích (đăng thêm tin, tin nổi bật hiện trước).

---

## 2. Phạm vi

### Làm trong đợt này

| Nhóm | Nội dung |
|---|---|
| Gói | 3 gói: Free, Starter, Growth |
| Hạn mức | Đếm số tin đang hoạt động, chặn đăng khi hết lượt |
| Thanh toán | VNPay sandbox cho mua gói và mua tin nổi bật |
| Tin nổi bật | Mua 7 ngày, hiện trước và có nhãn |
| Lịch sử | Lưu từng giao dịch để đối soát |
| Báo cáo | Doanh thu theo tháng cho Admin |
| Admin | Gán gói thủ công cho công ty |

### Không làm trong đợt này

- Cổng thanh toán khác (MoMo, Stripe).
- Hoá đơn VAT, hoàn tiền, tự động gia hạn.
- Đẩy hồ sơ ứng viên, bán lượt xem hồ sơ.
- Trang công ty nâng cao, quảng cáo đối tác.
- Trang Premium của Candidate (giữ nguyên).

---

## 3. Cách làm tổng quan

Chọn **phương án B+**: trạng thái gói lưu trực tiếp trên công ty, thêm một sổ giao dịch để báo cáo doanh thu.

Lý do:

- Ít bảng hơn phương án đầy đủ (không cần bảng `Entitlements`).
- Vẫn có lịch sử thanh toán — điều kiện bắt buộc để báo cáo doanh thu.
- Đủ đơn giản để làm kịp, nhưng không thiếu dữ liệu khi bảo vệ.

Thành phần chính:

| Thành phần | Trách nhiệm |
|---|---|
| `IEntitlementService` | Xác định gói hiện hành và số lượt còn lại của công ty |
| `IVnPayService` | Tạo URL thanh toán và kiểm tra chữ ký VNPay |
| `IBillingService` | Tạo giao dịch, xử lý kết quả, cấp gói / bật nổi bật |
| `IBillingAdminService` | Gán gói thủ công và tổng hợp doanh thu |
| `VnPayController` | Nhận IPN từ VNPay (server gọi server) |

---

## 4. Thay đổi dữ liệu

### Bảng mới: `Plans`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | int | Khoá chính |
| `Code` | string(20) | `FREE`, `STARTER`, `GROWTH` — unique |
| `Name` | string(100) | Tên hiển thị |
| `Price` | decimal(18,0) | Giá VND |
| `JobQuota` | int | Số tin được giữ cùng lúc |
| `DurationDays` | int | 0 = vĩnh viễn |
| `IsActive` | bool | Cho phép mua hay không |
| `SortOrder` | int | Thứ tự hiển thị |

Dữ liệu seed:

| Code | Giá | Hạn mức | Thời hạn |
|---|---|---|---|
| FREE | 0đ | 1 tin | Vĩnh viễn |
| STARTER | 500.000đ | 5 tin | 30 ngày |
| GROWTH | 1.500.000đ | 20 tin | 30 ngày |

### Bảng mới: `Payments`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | int | Khoá chính |
| `CompanyProfileId` | int | Công ty trả tiền |
| `CreatedByEmployerId` | int? | Người bấm mua |
| `Purpose` | enum | `Plan` hoặc `Featured` |
| `PlanId` | int? | Khi mua gói |
| `JobPostingId` | int? | Khi mua tin nổi bật |
| `Amount` | decimal(18,0) | Số tiền VND |
| `Status` | enum | `Pending`, `Paid`, `Failed`, `Expired` |
| `CreatedAt` | datetime | Thời điểm tạo |
| `PaidAt` | datetime? | Thời điểm thanh toán thành công |
| `VnpTxnRef` | string(100) | Mã giao dịch gửi VNPay — unique |
| `VnpTransactionNo` | string(50)? | Mã giao dịch VNPay trả về |
| `VnpResponseCode` | string(10)? | Mã phản hồi |
| `VnpBankCode` | string(20)? | Ngân hàng |
| `VnpPayDate` | string(14)? | Thời điểm VNPay ghi nhận |
| `Note` | string(500)? | Ghi chú |

### Cột thêm vào bảng có sẵn

| Bảng | Cột | Ghi chú |
|---|---|---|
| `CompanyProfiles` | `PlanId` (int?) | Gói hiện tại; null = Free |
| `CompanyProfiles` | `PlanStartedAt` (datetime?) | Bắt đầu gói |
| `CompanyProfiles` | `PlanExpiresAt` (datetime?) | Hết hạn; null = vĩnh viễn |
| `JobPostings` | `FeaturedUntil` (datetime?) | Hạn tin nổi bật |

### Seed

- Thêm 3 gói.
- Gán gói Free cho tất cả công ty đang có.
- Không đụng dữ liệu job/application hiện tại.

---

## 5. Quy tắc nghiệp vụ

### 5.1. Tin đang hoạt động

Chỉ tính **Draft + Published**.

- `Closed` và `Expired` không tính.
- Đóng hoặc xoá tin sẽ giải phóng một lượt.
- Sửa tin không tính thêm lượt.

### 5.2. Gói hiện hành

- Nếu `PlanId` null → dùng gói Free.
- Nếu `PlanExpiresAt` nhỏ hơn thời điểm hiện tại → coi như Free.
- Không xoá dữ liệu gói cũ; chỉ đánh giá lại khi dùng.

### 5.3. Hạn mức

- `Còn lại = JobQuota - số tin đang hoạt động`.
- Nếu còn lại ≤ 0: chặn tạo tin.
- Thông báo: "Bạn đã dùng hết X/Y tin của gói. Nâng cấp để đăng thêm."
- Trang quản lý tin hiện thanh tiến độ và nút nâng cấp.

### 5.4. Mua gói

- Chỉ Owner được mua.
- Mua gói mới thay gói hiện tại:
  - `PlanId = gói mua`
  - `PlanStartedAt = hiện tại`
  - `PlanExpiresAt = hiện tại + DurationDays`
- Gói Free có thời hạn vĩnh viễn.

### 5.5. Tin nổi bật

- Giá: **200.000đ / 7 ngày**.
- Chỉ mua cho tin `Published`, chưa hết hạn và thuộc công ty mình.
- Cộng dồn: `FeaturedUntil = max(hiện tại, hạn cũ) + 7 ngày`.
- Trang tìm việc sắp xếp: tin còn nổi bật trước, sau đó theo ngày đăng.
- Hiện nhãn "Nổi bật" ở trang công khai và trang quản lý tin.

### 5.6. Phân quyền

- Employer phải có membership Active và công ty Approved (đã có sẵn).
- Chỉ thấy dữ liệu công ty mình.
- Owner: mua gói, mua nổi bật, gán gói.
- Member: chỉ xem.

---

## 6. Luồng thanh toán VNPay

### 6.1. Luồng tổng quát

1. Owner bấm "Mua" trên trang billing.
2. Hệ thống tạo `Payment` trạng thái **Pending** và sinh `VnpTxnRef` duy nhất.
3. Hệ thống tạo URL thanh toán VNPay và chuyển trình duyệt sang cổng sandbox.
4. Người dùng thanh toán bằng thẻ test.
5. VNPay gọi về hai nơi:
   - **ReturnUrl**: trình duyệt quay về trang kết quả.
   - **IPN**: server VNPay gọi server HotJob.
6. Hệ thống kiểm tra chữ ký, số tiền, mã giao dịch.
7. Nếu hợp lệ và thành công:
   - `Payment = Paid`, ghi `PaidAt`.
   - Mua gói → cập nhật gói cho công ty.
   - Mua nổi bật → cập nhật `FeaturedUntil`.
8. Hiển thị kết quả cho người dùng.

### 6.2. Tham số gửi VNPay

| Tham số | Giá trị |
|---|---|
| `vnp_Version` | `2.1.0` |
| `vnp_Command` | `pay` |
| `vnp_TmnCode` | Lấy từ cấu hình |
| `vnp_Amount` | Số tiền × 100 |
| `vnp_CurrCode` | `VND` |
| `vnp_TxnRef` | Mã giao dịch nội bộ |
| `vnp_OrderInfo` | Nội dung không dấu |
| `vnp_OrderType` | `other` |
| `vnp_Locale` | `vn` |
| `vnp_ReturnUrl` | URL trang kết quả |
| `vnp_IpAddr` | IP người dùng |
| `vnp_CreateDate` | `yyyyMMddHHmmss` theo GMT+7 |
| `vnp_ExpireDate` | Tạo + 15 phút |
| `vnp_SecureHash` | HMAC-SHA512 trên chuỗi tham số đã sắp xếp |

### 6.3. Kiểm tra khi VNPay gọi về

Thứ tự bắt buộc:

1. Kiểm tra chữ ký trước khi làm bất cứ việc gì.
2. Tìm `Payment` theo `VnpTxnRef`.
3. So số tiền VNPay trả về với số tiền đã lưu.
4. Nếu `Payment` đã `Paid` → coi như đã xử lý, không cấp gói lần hai.
5. Chỉ cấp gói khi `vnp_ResponseCode = 00` và `vnp_TransactionStatus = 00`.

Mã phản hồi IPN:

| Mã | Ý nghĩa |
|---|---|
| `00` | Đã cập nhật thành công |
| `01` | Không tìm thấy giao dịch |
| `02` | Giao dịch đã xác nhận trước đó |
| `04` | Sai số tiền |
| `97` | Sai chữ ký |
| `99` | Lỗi khác |

### 6.4. Cấu hình

| Khoá | Ý nghĩa |
|---|---|
| `VnPay:TmnCode` | Mã merchant sandbox |
| `VnPay:HashSecret` | Khoá bí mật |
| `VnPay:BaseUrl` | `https://sandbox.vnpayment.vn/paymentv2/vpcpay.html` |
| `VnPay:ReturnUrl` | Trang kết quả của HotJob |
| `VnPay:IpnUrl` | Địa chỉ IPN công khai |
| `VnPay:Version` | `2.1.0` |
| `VnPay:Locale` | `vn` |
| `Billing:FeaturedPrice` | `200000` |
| `Billing:FeaturedDays` | `7` |

`TmnCode` và `HashSecret` đặt bằng user-secrets hoặc biến môi trường, **không commit**.

### 6.5. Lưu ý khi chạy local

- ReturnUrl dùng `localhost` được vì trình duyệt tự chuyển về.
- IPN cần URL công khai có HTTPS. Khi demo local có thể dùng cloudflared/ngrok, hoặc cho phép ReturnUrl xử lý kết quả để không bị kẹt.
- Nếu xử lý ở ReturnUrl, vẫn phải kiểm tra chữ ký và tính idempotent giống IPN.
- Khi chạy thật, chỉ tin IPN.

---

## 7. Giao diện

### 7.1. Trang billing của Employer — `/employer/billing`

- Thẻ gói hiện tại: tên gói, ngày hết hạn, số tin đã dùng / hạn mức, số còn lại.
- Thanh tiến độ hạn mức.
- 3 thẻ gói: giá, số tin, thời hạn, nút "Mua".
- Mục tin nổi bật: chọn tin Published, hiện giá 200.000đ / 7 ngày, nút mua.
- Bảng lịch sử giao dịch: thời gian, loại, số tiền, trạng thái.
- Thông báo kết quả sau khi thanh toán.

### 7.2. Trang kết quả — `/employer/billing/vnpay-return`

- Đọc tham số VNPay trả về.
- Kiểm tra chữ ký và cập nhật giao dịch.
- Hiện: thành công / thất bại / sai chữ ký.
- Có nút quay lại trang billing.

### 7.3. Trang quản lý tin — `/employer/jobs`

- Banner hạn mức: "Đã dùng 1/1 tin".
- Nút "Nâng cấp" khi sắp hết hoặc hết lượt.
- Khi tạo tin bị chặn: hiện thông báo rõ và link nâng cấp.
- Nhãn "Nổi bật" + ngày hết hạn trên tin đã mua.

### 7.4. Trang tìm việc công khai — `/it-jobs`

- Tin còn nổi bật xếp trước.
- Nhãn "Nổi bật" trên thẻ tin.

### 7.5. Trang Admin — `/admin/billing`

- Chỉ role Admin.
- Bảng doanh thu theo tháng: tháng, tổng, doanh thu gói, doanh thu nổi bật.
- Tổng 12 tháng gần nhất.
- Danh sách công ty: gói hiện tại, hạn mức, ngày hết hạn.
- Form gán gói thủ công cho một công ty.
- Danh sách giao dịch gần đây.

### 7.6. Điều hướng

- Menu Employer: thêm "Billing".
- Menu Admin: thêm "Billing".

---

## 8. Báo cáo doanh thu

Chỉ tính giao dịch `Paid`.

| Chỉ số | Cách tính |
|---|---|
| Doanh thu tháng | Tổng `Amount` theo tháng của `PaidAt` |
| Doanh thu gói | Tổng giao dịch `Purpose = Plan` |
| Doanh thu nổi bật | Tổng giao dịch `Purpose = Featured` |
| Tổng 12 tháng | Cộng dồn 12 tháng gần nhất |

Có thể dùng số liệu này để vẽ biểu đồ cột trong báo cáo.

---

## 9. Các bước triển khai

### Bước 1 — Dữ liệu và seed

**Việc làm:**

- Thêm model `Plan`, `Payment`.
- Thêm cột vào `CompanyProfile` và `JobPosting`.
- Cấu hình DbContext: unique `Plan.Code`, unique `Payment.VnpTxnRef`, precision tiền.
- Tạo migration.
- Seed 3 gói và gán Free cho công ty hiện có.

**Xong khi:** chạy migration thành công, database có 3 gói, công ty cũ dùng Free.

### Bước 2 — Hạn mức

**Việc làm:**

- Viết `IEntitlementService` và implementation.
- Hàm lấy gói hiện hành, đếm tin hoạt động, tính lượt còn lại.
- Gọi kiểm tra trong `JobService.CreateAsync`.
- Trả lỗi rõ ràng khi hết lượt.

**Xong khi:** gói Free chỉ tạo được 1 tin; đóng tin thì tạo được tin mới; gói hết hạn quay về Free.

### Bước 3 — VNPay

**Việc làm:**

- Viết `IVnPayService`.
- Hàm tạo URL thanh toán.
- Hàm kiểm tra chữ ký.
- Đọc cấu hình từ appsettings/user-secrets.
- Viết unit test cho tạo và kiểm tra chữ ký.

**Xong khi:** URL tạo ra mở được cổng sandbox; sửa một tham số thì kiểm tra chữ ký thất bại.

### Bước 4 — Xử lý giao dịch

**Việc làm:**

- Viết `IBillingService`:
  - Tạo checkout mua gói.
  - Tạo checkout mua nổi bật.
  - Xử lý kết quả trả về.
- Chống xử lý trùng.
- Kiểm tra số tiền.
- Cấp gói hoặc bật nổi bật khi thành công.
- Thêm endpoint IPN công khai.
- Viết unit test: thành công, trùng, sai tiền, sai chữ ký.

**Xong khi:** thanh toán sandbox thành công thì gói được cấp đúng một lần.

### Bước 5 — Giao diện Employer

**Việc làm:**

- Trang `/employer/billing`.
- Trang kết quả `/employer/billing/vnpay-return`.
- Banner hạn mức và nút nâng cấp ở `/employer/jobs`.
- Thêm mục Billing vào menu Employer.

**Xong khi:** Owner mua được gói từ UI và thấy hạn mức thay đổi ngay.

### Bước 6 — Tin nổi bật

**Việc làm:**

- Mua nổi bật từ trang billing.
- Sắp xếp tin nổi bật trước ở repository công khai.
- Thêm `FeaturedUntil` vào DTO danh sách.
- Nhãn nổi bật ở trang công khai và trang Employer.

**Xong khi:** mua nổi bật xong, tin nhảy lên đầu danh sách và có nhãn.

### Bước 7 — Admin

**Việc làm:**

- Trang `/admin/billing`.
- Tổng hợp doanh thu theo tháng.
- Gán gói thủ công.
- Danh sách giao dịch.
- Thêm mục Billing vào menu Admin.

**Xong khi:** Admin thấy đúng doanh thu của giao dịch vừa test.

### Bước 8 — Kiểm thử và demo

**Việc làm:**

- Chạy toàn bộ unit test.
- Chạy kịch bản demo end-to-end.
- Cập nhật tài liệu báo cáo.
- Ghi lại ảnh chụp màn hình cho báo cáo.

**Xong khi:** kịch bản demo ở mục 11 chạy trơn không lỗi.

---

## 10. Kiểm thử

### Unit test

| Nhóm | Trường hợp |
|---|---|
| Hạn mức | Free còn 1 lượt |
| Hạn mức | Đã có 1 tin Draft thì không tạo thêm |
| Hạn mức | Đóng tin thì tạo được tin mới |
| Hạn mức | Gói hết hạn quay về Free |
| Hạn mức | Closed/Expired không bị đếm |
| VNPay | Tạo URL có đủ tham số |
| VNPay | Chữ ký hợp lệ |
| VNPay | Sửa tham số thì chữ ký sai |
| Thanh toán | Thành công thì cấp gói |
| Thanh toán | Gọi lại lần hai không cấp gói lần hai |
| Thanh toán | Sai số tiền thì từ chối |
| Nổi bật | Cộng dồn ngày đúng |
| Nổi bật | Tin nổi bật xếp trước tin thường |

### Kiểm thử thủ công

- Mua gói bằng thẻ test VNPay.
- Huỷ thanh toán giữa chừng.
- Quay lại trang billing sau khi thanh toán.
- Đăng tin khi hết hạn mức.
- Mua nổi bật cho tin Draft (phải bị chặn).
- Member thường bấm mua (phải bị chặn).

---

## 11. Kịch bản demo

Chuẩn bị:

- Tài khoản Employer seed: `employer@example.com` / `Employer123!`.
- Tài khoản Admin seed: `admin1@example.com` / `Admin123!`.
- Thẻ test VNPay sandbox:
  - Ngân hàng: NCB
  - Số thẻ: `9704198526191432198`
  - Tên: `NGUYEN VAN A`
  - Ngày phát hành: `07/15`
  - OTP: `123456`

Các bước:

1. Đăng nhập Employer, mở Billing.
2. Hệ thống hiện gói Free, đã dùng 1/1 tin.
3. Thử đăng tin thứ hai → bị chặn, hiện nút nâng cấp.
4. Mua gói Starter → chuyển sang VNPay sandbox → thanh toán bằng thẻ test.
5. Quay về HotJob → gói Starter, hạn mức 5 tin.
6. Đăng tin thứ hai → thành công.
7. Mua tin nổi bật cho tin vừa đăng.
8. Mở trang tìm việc → tin nằm đầu danh sách, có nhãn "Nổi bật".
9. Đăng nhập Admin → mở Billing → thấy doanh thu tháng này gồm 500.000đ gói và 200.000đ nổi bật.
10. Kết luận: có gói, có chặn, có lợi ích nhìn thấy, có doanh thu.

---

## 12. Rủi ro và lưu ý

| Rủi ro | Cách xử lý |
|---|---|
| VNPay gọi IPN không tới máy local | Dùng cloudflared/ngrok, hoặc cho ReturnUrl xử lý khi demo |
| Cấp gói hai lần | Kiểm tra trạng thái `Paid` trước khi cấp; khoá theo `VnpTxnRef` |
| Sai số tiền | Luôn tính tiền ở server, không nhận từ client |
| Lộ khoá bí mật | Dùng user-secrets/biến môi trường, không commit |
| Sai múi giờ | `vnp_CreateDate` dùng GMT+7 |
| Công ty cũ có nhiều hơn hạn mức Free | Không xoá tin cũ; chỉ chặn tạo tin mới |
| Gói hết hạn giữa chừng | Đánh giá lại mỗi lần dùng, không cần job nền |
| Migration lỗi trên MySQL | Chạy migration trên database sạch trước, sau đó mới áp dụng |

---

## 13. Việc cần bạn chuẩn bị

1. `vnp_TmnCode` và `vnp_HashSecret` của sandbox.
2. Xác nhận giá 3 gói và giá tin nổi bật (đang dùng số trong tài liệu monetization).
3. Nếu muốn demo IPN thật: một URL công khai (cloudflared/ngrok) để khai báo với VNPay.
4. Xác nhận có cần trang Admin hay chỉ cần báo cáo bằng dữ liệu seed.

---

## 14. Ngoài phạm vi

- Không sửa luồng Candidate.
- Không thêm cổng thanh toán khác.
- Không làm hoá đơn/hoàn tiền.
- Không thay đổi quy trình ATS hiện có.
