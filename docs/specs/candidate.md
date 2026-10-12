# Candidate — làm gì và luồng dữ liệu

Một user có một Candidate profile. Cùng user đó sau này có thể thêm Employer profile.

## 1. Candidate làm được gì

- Register, login, logout, refresh session. Account bị lock thì không login được.
- Sửa profile của mình (tên, ngày sinh, địa chỉ, avatar, link Github/LinkedIn).
- Tạo nhiều Resume. Mỗi Resume gồm Education, WorkExperience, Skill kèm số năm kinh nghiệm. Một Resume được đánh dấu primary. Upload file CV.
- Xem các JobPosting đang Published và chưa hết hạn. Lọc theo keyword, city, skill, category, lương, JobType.
- Xem MatchScore theo từng Resume trước khi nộp.
- Save job để nộp sau, unsave khi không cần.
- Apply bằng một Resume kèm cover letter. Theo dõi status timeline và Interview. Withdraw trừ khi đã Offered.
- Review mỗi Company một lần, chấm 1–5 sao.
- Đọc Notification của mình (đổi status, lịch interview).

## 2. Luồng dữ liệu chảy thế nào

**Register:** Candidate nhập email và tên. Hệ thống tạo login user + Candidate row + role Candidate trong một transaction. Email trùng thì từ chối.

**Resume:** Candidate chỉ sửa các row gắn với CandidateId của mình. Đặt một Resume làm primary thì các Resume khác tự bỏ primary. Resume đã dùng để Apply thì không xóa được vì Application restrict delete Resume. Xóa user sẽ cascade theo Resume, skill, SavedJob, Review.

**Xem job và match:** Candidate chỉ thấy job Published mà ExpiryDate còn hiệu lực. MatchScore tính từ ResumeSkill đối chiếu JobSkill, hiện trước nút Apply. Đổi skill bên nào cũng tính lại điểm.

Ví dụ: Lan mở job "Junior .NET Dev" với Resume "Backend CV" thấy 87%. Đổi sang Resume "Frontend CV" thấy 40%. Lan chọn bản backend để nộp.

**Apply:** Hệ thống kiểm tra job còn Published và chưa hết hạn,điều kiện là Resume thuộc về Candidate và Candidate chưa từng nộp job này. Đạt hết thì freeze bản CV lúc nộp vào Snapshot, tạo Application ở Pending kèm history row, rồi gửi Notification cho mọi Active Employer của Company đó.

Ví dụ: Lan nộp xong. Sau đó Lan sửa Resume gốc, HR vẫn thấy bản snapshot ngày nộp nằm cạnh Resume live.

**Theo dõi và withdraw:** Mỗi lần đổi status thêm một history row ghi ai đổi, lúc nào, ghi chú gì. Candidate chỉ được chuyển sang Withdrawn, và không được withdraw khi đã Offered. Offered, Rejected, Withdrawn là terminal.

Ví dụ: Lan đang Shortlisted rồi nhận Offer thì nút withdraw biến mất. Nếu bị Rejected thì dừng ở đó.

**Review:** Mỗi cặp Company + Candidate chỉ một row. Gửi lần hai bị từ chối. Bảng xếp hạng company lấy trung bình RatingScore 1-5, không thêm cột mới.
