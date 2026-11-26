# Tính năng cần có — HotJob

## I. Các chức năng cho admin

### Quản lý duyệt công ty

1. Liệt kê công ty theo filter (Pending/Approved/Rejected)
2. Xem chi tiết công ty
3. Duyệt công ty
4. Từ chối công ty + Lý do
5. Thu hồi/tạm ngưng công ty đã được duyệt

### Quản lý người dùng

1. Đăng nhập admin
2. Danh sách người dùng, tìm theo tên/email/vai trò
3. Xem chi tiết người dùng (Profile, CV, applications)
4. Khóa/Mở khóa tài khoản
5. Reset password

### Quản lý tin tuyển dụng

1. Liệt kê tất cả tin, filter theo công ty/trạng thái
2. Hủy bỏ tin vi phạm
3. Cấp/gia hạn/thu hồi tin nổi bật

### Xem hồ sơ ứng tuyển

1. Thống kê ứng tuyển theo tin, công ty, tháng
2. Xem hồ sơ khi có tranh chấp
3. Xóa/Ẩn danh theo yêu cầu khi ứng viên yêu cầu xóa dữ liệu

### Dữ liệu danh mục (Category)

1. Quản lý Category của Job
2. Quản lý Skills

### Kinh doanh

1. Quản lý gói dịch vụ (giá, số tin, thời hạn)
2. Gán gói / sửa hạn mức cho công ty hoặc ứng viên
3. Danh sách thanh toán, đánh dấu đã trả / hoàn tiền
4. Báo cáo doanh thu theo tháng

### Tổng quan & an toàn

1. Dashboard: số người dùng, công ty chờ duyệt, tin đang đăng, số hồ sơ, doanh thu
2. Nhật ký thao tác admin (ai, làm gì, lúc nào)
3. Gửi thông báo cho người dùng (tất cả / theo nhóm)

## II. Các chức năng cho người dùng xin việc (candidate)

### Quản lý tài khoản cá nhân

1. Đăng nhập / đăng xuất tài khoản
2. Đăng ký tài khoản
3. Cập nhật thông tin cá nhân

### Quản lý CV cá nhân

1. Tạo CV: Điền Educations, Skills, Experiences cho CV
2. Upload CV: Đọc và lọc ra Educations, Skills, Experiences và candidate duyệt, sửa trên đó
3. Cập nhật CV: Upload CV mới hoặc cập nhật lại các thuộc tính
4. Gỡ CV

### Tìm việc

1. Tìm việc theo filter (keyword, city, skill, job type)
2. Xem công việc, chi tiết công việc
3. Nộp, rút CV
4. Lưu công việc

### Tìm công ty

1. Tìm công ty theo filter (keyword, industry, location)
2. Xem công ty, chi tiết công ty
3. Đánh giá công ty theo thang 1-5 sao

### Trở thành nhà tuyển dụng

1. Trở thành nhà tuyển dụng
2. Gia nhập một công ty
3. Thành lập một công ty

### Thông báo real time

1. Nhận thông báo kết quả
2. Nhận thông báo phỏng vấn

### Mua gói premium

1. Thanh toán gói premium (1 tháng)
2. Xem điểm CV cá nhân được chấm điểm tương thích (Chấm free lần đầu)
3. Đẩy hồ sơ ứng viên (Hồ sơ đưa lên đầu khi employer chọn xem hồ sơ)
4. User Profile được gán nhãn premium

## III. Các chức năng cho nhà tuyển dụng (employer)

### Tài khoản & công ty

1. Trở thành Employer: Tạo công ty mới (Làm Owner) hoặc join company có sẵn (Chờ owner duyệt)
2. Chờ duyệt: Công ty pending chưa được đăng job; member pending chưa được sử dụng ATS
3. Xem hồ sơ công ty: Xem thông tin, trạng thái xác minh, thành viên và mức hoàn thiện hồ sơ
4. Sửa hồ sơ công ty: Owner cập nhật website, ngành, địa điểm, quy mô, mô tả
5. Quản lý thành viên: Owner duyệt/từ chối người xin join, xem danh sách member

### Quản lý tin tuyển dụng

1. Đăng tin (Tạo job: tiêu đề, mô tả, lương, kinh nghiệm, loại hình, hạn nộp, category, skill + trọng số); số lần đăng tin dựa trên gói mua
2. Sửa tin: Cập nhật nội dung job, category và skill
3. Xóa / Đóng tin: Draft thì xóa hẳn; Published thì đóng và tự từ chối các đơn đang mở
4. Danh sách tin: Xem job của công ty mình, lọc theo trạng thái, tìm kiếm, phân trang, kèm số ứng viên
5. Xem chi tiết tin: Xem đầy đủ thông tin, category, skill và trạng thái job
6. Kiểm tra dữ liệu: Chặn lương sai, hạn nộp quá khứ, skill/category không tồn tại
7. Phân quyền: Chỉ Employer Active thuộc công ty Approved; chỉ thấy dữ liệu công ty mình

### ATS - Duyệt ứng viên

1. Hàng đợi ứng viên: Lọc đơn theo job, trạng thái; tìm theo tên hoặc email
2. Xem chi tiết đơn: Xem cover letter, CV snapshot lúc nộp, CV hiện tại và lịch sử trạng thái
3. Chuyển trạng thái: Tiến tuần tự Pending → Viewed → Shortlisted → Interviewing → Offered; được Reject từ trạng thái mở
4. Ghi lịch sử: Mỗi lần đổi trạng thái lưu người đổi, thời gian, ghi chú
5. Nhắn tin ứng viên: Gửi thông báo trực tiếp cho ứng viên

### Phỏng vấn

1. Đặt lịch phỏng vấn: Tạo nhiều vòng: ngày giờ, địa điểm/link, hình thức, ghi chú
2. Tự nâng trạng thái: Đặt lịch cho đơn Shortlisted thì tự chuyển sang Interviewing
3. Sửa / Hủy lịch: Dời lịch hoặc hủy buổi phỏng vấn
4. Thông báo lịch: Gửi thông báo lịch phỏng vấn cho ứng viên

### Tự động

1. Job hết hạn: Tự ẩn khỏi Candidate, đơn đang mở vẫn giữ cho HR xử lý
2. Thông báo sự kiện: Đổi trạng thái, đặt lịch, nhắn tin đều gửi Notification cho Candidate
