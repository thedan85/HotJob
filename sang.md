

3. Đơn vị sử dụng:
    - Chủ nền tảng 

4. Đối tượng sử dụng:
    - Admin
    - Employer
    - Candidate

5. Thương mại:
    - Gói đăng tin của Employer. Free giới hạn số tin tuyển dụng, thu phí khi đăng nhiều. 
    - Thu tiền hàng tháng để Verify -> Được recommend cho ứng viên -> Đẩy job posting lên đầu
    - Mở bán gói tính điểm CV bằng AI
    - Thêm tích xanh verify

    - Gói premium cho ứng viên:
        1. Xem điểm CV cá nhân bằng AI
        2. Làm CV tự động
        3. Nhận thông báo job mới phù hợp với bản thân nhanh nhất có thể
        4. Thêm badge chứng nhận premium user












































































        1. Đổi tên bảng: được hoàn toàn.
    Thêm trong OnModelCreating sau base.OnModelCreating(modelBuilder):
        modelBuilder.Entity<ApplicationUser>().ToTable("Users");
        modelBuilder.Entity<IdentityRole>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
        modelBuilder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");

2. Bỏ bớt thuộc tính: chỉ bỏ được một phần.
    - Thuộc tính tự thêm (CreatedAt, IsActive): xóa khỏi class + migration là xong.
    - Thuộc tính kế thừa từ IdentityUser: không xóa trong class được, phải dùng Fluent API:
        modelBuilder.Entity<ApplicationUser>().Ignore(u => u.PhoneNumber);