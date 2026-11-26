# Admin — làm gì và luồng dữ liệu

Chỉ seed lúc startup, không tự register. Không cần Candidate hay Employer profile.

## 1. Admin làm được gì

- Tồn tại nhờ seed roles + tài khoản admin cấu hình sẵn. Email đã có thì bỏ qua. Thiếu cấu hình thì warn và chỉ seed roles.
- Approve hoặc Reject Company. Reject bắt buộc có RejectionReason. Quyết định nào cũng đóng dấu verifier và thời gian, ghi audit log, rồi gửi Notification (Kind `CompanyReview`) cho các Employer membership Active, chưa bị deactivate, của Company. Member Pending không nhận.
- Duyệt lại Company theo cả hai chiều. Deactivate/Reactivate Company vi phạm; employer Active được báo ("Company suspended" kèm lý do nếu có, "Company reactivated").
- Xem và tìm user kèm role, status, Company. Lock và unlock account.
- Duyệt (Approve/Reject) tin tuyển dụng đang chờ. Gỡ tin vi phạm (Remove) kèm lý do. Xóa cứng chỉ cho tin chưa có application.
- Xem nhật ký thao tác admin tại `/admin/audit-logs`.
- Xem ModelTrainingLog. Bấm retrain, mỗi lần chạy ghi một log row.

## 2. Luồng dữ liệu chảy thế nào

**Duyệt Company:**
- Pending: Company mới tạo, chưa đăng job được.
- Approve: chuyển sang Approved, mở đăng job. Employer Active nhận notification "Company approved".
- Reject: chuyển sang Rejected + RejectionReason. Từ lúc này chặn việc mới: không join thêm, không đăng/sửa job. Employer Active nhận notification "Company rejected" kèm lý do.
- Job của Company không Approved hoặc đã bị deactivate bị ẩn khỏi Candidate (`JobVisibility.OpenToCandidates`): không hiện ở browse/detail/recommend, không save, không apply được. Job và application vẫn giữ trong DB, nên Approve/Reactivate lại là job hiện lại.

Ví dụ:
1. Thứ Hai Acme được Approved, HR đăng job "Junior .NET" và được duyệt.
2. Thứ Ba admin Rejected Acme vì "No business license". HR của Acme thấy lý do trong `/notifications`.
3. Kết quả: người mới không join Acme được, HR không đăng job mới được, job "Junior .NET" biến khỏi trang tìm việc và không apply được.
4. Thứ Năm Acme bổ sung giấy phép, admin Approve → "Junior .NET" hiện lại.

**Inbox Employer:** Employer (kể cả tài khoản chỉ có role Employer) dùng chung trang `/notifications` và badge chưa đọc trên NavMenu với Candidate. Feed vẫn loại `JobAlert`.

**Lock user:** Lock lật IsActive thành false và thu hồi các refresh token đang sống. Từ đó login và refresh đều bị từ chối. User đã có Application thì lock thay vì xóa cứng vì Application restrict delete. Admin không tự lock mình và không lock được tài khoản Admin khác.

Ví dụ: spammer bị lock lúc trưa. Login và refresh fail ngay.

**Gỡ tin (Remove):** Admin nhập lý do (bắt buộc, ≤ 500 ký tự). Job chuyển `Closed`, lưu `ModeratedAt`, `ModeratedByAdminId`, `ModerationReason`. Application đang mở (Pending/Viewed/Shortlisted/Interviewing) chuyển Rejected với history note `Job removed by admin`, candidate nhận notification "Application closed". Application Offered/Rejected/Withdrawn giữ nguyên. Employer Active nhận notification (Kind `JobModeration`) kèm lý do, thấy lý do trên trang My jobs, và không sửa hay mở lại job đó được. Gỡ lần hai trả Conflict. API: `POST /api/admin/jobs/{id}/remove`.

**Xóa cứng job:** chỉ cho job chưa có application nào. Có application thì trả Conflict và phải dùng Remove, vì xóa sẽ cascade mất application/history/interview.

**Employer đóng job:** application đang mở chuyển Rejected với history `Job closed`, và candidate nhận notification "Application closed".

**ML:** Rejection do đóng job (`Job closed`) hoặc admin gỡ tin (`Job removed by admin`) không dùng làm mẫu âm khi train.

**Audit log:** Mỗi thao tác ghi của admin (company, employer, job approve/reject/remove/delete, review, lock/unlock, skill) ghi một dòng `AdminAuditLog` trong cùng transaction. Thao tác thất bại không ghi.

**Retrain:** Chạy tay trên cặp ResumeSkill và JobSkill hiện tại, ghi RecordCount, AccuracyScore, ModelFilePath, trainer, timestamp. Không có lịch tự chạy. Bảng MatchScore đối với admin chỉ đọc.
