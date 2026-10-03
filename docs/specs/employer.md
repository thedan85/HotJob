# Employer — làm gì và luồng dữ liệu

Một user có một Employer row gắn với một Company. Cùng user đó có thể giữ thêm Candidate profile.

## 1. Employer làm được gì

- Register bằng cách tạo Company mới (thành owner, Active ngay, Company chờ duyệt) hoặc join Company có sẵn (Pending chờ owner duyệt).
- Chờ trong lúc Company Pending. Chỉ Company Approved mới được đăng job.
- Đăng và sửa JobPosting kèm Category, Skill + ImportanceWeight, lương, ExpiryDate. Job Draft thì xóa hẳn. Job Published thì close.
- Xem job của Company mình kèm số lượng Application. Tìm kiếm, lọc.
- Duyệt đơn: lọc theo job và status, tìm theo tên hoặc email, xem Snapshot CV đông lạnh đặt cạnh Resume live.
- Chuyển Application tiến từng bước Pending → Viewed → Shortlisted → Interviewing → Offered, hoặc Reject từ trạng thái mở bất kỳ. Đặt trùng status thì coi như no-op. Không được đặt Withdrawn.
- Nhắn thủ công cho Candidate.
- Tạo nhiều Interview cho mỗi Application. Dời lịch, sửa, hủy. Application terminal thì không đặt thêm.
- Owner duyệt hoặc từ chối member xin join, sửa thông tin Company.

## 2. Luồng dữ liệu chảy thế nào

**Join hoặc tạo Company:** Employer đưa `Employer.CompanyProfileId` có sẵn (Join) hoặc `CompanyProfile.CompanyName` mới (Tạo), không bao giờ cả hai. `CompanyProfile.CompanyName` mới phải unique. Tạo mới cho ra `Employer{IsOwner=true, MembershipStatus=Active}` và `CompanyProfile{Status=Pending}`. Join cho ra `Employer{IsOwner=false, MembershipStatus=Pending}`. Mọi API Employer đòi `Employer.MembershipStatus=Active`, riêng API job đòi thêm `CompanyProfile.Status=Approved`. Join `CompanyProfile{Status=Rejected}` thì bị từ chối.

Ví dụ: HR lập "Acme Corp" xong Active nhưng chưa đăng được job cho tới khi admin duyệt. Một HR khác join Acme thì phải chờ owner duyệt mới thấy hộp ATS.

**Đăng job:** Job thuộc về Company, đóng dấu CreatedByEmployerId. Tác giả rời đi thì author null, job ở lại. Bộ skill và category thay nguyên cụm trong một lần. Lương min/max ngược nhau, ExpiryDate quá khứ, Skill hoặc Category không tồn tại, status ngoài Draft/Published đều bị từ chối.

**Close job:** Job Draft thì xóa hẳn row. Close job Published thì chuyển sang Closed và mass-reject mọi Application đang mở, mỗi đơn thêm một history row ghi "Job closed". Offered, Rejected, Withdrawn giữ nguyên. Khác với Expired: job hết hạn tự ẩn khỏi Candidate nhưng đơn đang mở giữ lại cho HR xử lý tiếp.

Ví dụ: Acme close job "Intern .NET". Mười đơn Pending thành Rejected kèm ghi chú "Job closed". Hai đơn đã Offered giữ nguyên.

**Duyệt ATS:** Mọi query lọc theo CompanyProfileId của mình, id lạ trả về not found. Mỗi bước tiến ghi history row và gửi Notification cho Candidate. Nhảy cóc bị từ chối. Withdraw là quyền của Candidate.

Ví dụ: HR chuyển Lan từ Pending sang Viewed kèm note "CV looks good". Lan nhận Notification. HR thử chuyển Viewed thẳng sang Offered thì bị từ chối.

**Interview:** Đặt lịch tạo Interview row kèm Notification cho Candidate ghi ngày giờ và địa điểm. Đặt lịch cho đơn Shortlisted thì tự nâng lên Interviewing. Đặt cho Pending hoặc Viewed thì chỉ thêm lịch, không đổi status. Đơn terminal thì từ chối lịch mới.

Ví dụ: Lan đang Shortlisted. HR đặt vòng 1. Status của Lan thành Interviewing kèm history "Interview scheduled."
