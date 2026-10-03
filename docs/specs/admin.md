# Admin — làm gì và luồng dữ liệu

Chỉ seed lúc startup, không tự register. Không cần Candidate hay Employer profile.

## 1. Admin làm được gì

- Tồn tại nhờ seed roles + tài khoản admin cấu hình sẵn. Email đã có thì bỏ qua. Thiếu cấu hình thì warn và chỉ seed roles.
- Approve hoặc Reject Company đang Pending. Reject bắt buộc có RejectionReason. Quyết định nào cũng đóng dấu verifier và thời gian, rồi gửi Notification cho các Employer của Company.
- Duyệt lại Company theo cả hai chiều.
- Xem và tìm user kèm role, status, Company. Lock và unlock account.
- Xem ModelTrainingLog. Bấm retrain, mỗi lần chạy ghi một log row.

## 2. Luồng dữ liệu chảy thế nào

**Duyệt Company:**
- Pending: Company mới tạo, chưa đăng job được.
- Approve: chuyển sang Approved, mở đăng job.
- Reject: chuyển sang Rejected + RejectionReason. Từ lúc này chặn việc mới: không join thêm, không đăng/sửa job.
- Việc cũ giữ nguyên: job đã Published trước khi Reject vẫn hiện cho Candidate, vì filter hiện tại chỉ check JobPosting.Status + ExpiryDate, không check Company.Status. Muốn gỡ phải close tay hoặc chờ hết hạn.

Ví dụ:
1. Thứ Hai Acme được Approved, HR đăng job "Junior .NET".
2. Thứ Ba admin Rejected Acme vì "No business license".
3. Kết quả: người mới không join Acme được, HR không đăng job mới được, nhưng job "Junior .NET" hôm thứ Hai vẫn hiện.

**Lock user:** Lock lật IsActive thành false. Từ đó login và refresh token đều bị từ chối. Access token đã cấp vẫn sống tới hết hạn. User đã có Application thì lock thay vì xóa cứng vì Application restrict delete. Admin không tự lock mình.

Ví dụ: spammer bị lock lúc trưa. Token buổi sáng xài tới 1 giờ chiều mới hết, nhưng login và refresh fail ngay.

**Retrain:** Chạy tay trên cặp ResumeSkill và JobSkill hiện tại, ghi RecordCount, AccuracyScore, ModelFilePath, trainer, timestamp. Không có lịch tự chạy. Bảng MatchScore đối với admin chỉ đọc.
