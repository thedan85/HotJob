# REVIEW CODE - QUẢN LÝ CÔNG TY

**Ngày review:** 2026-10-12  
**Phạm vi:** 4 chức năng quản lý công ty (2.4.1 → 2.4.4)

---

## 1. TỔNG QUAN HIỆN TRẠNG

### ✅ Đã triển khai:
- **2.4.1. Duyệt nhà tuyển dụng** (Partial)
  - Service: `CompanyService.ReviewEmployerAsync()`
  - UI: `CompanyMembers.razor`

### ❌ Chưa triển khai:
- **2.4.2. Duyệt tin tuyển dụng**
- **2.4.3. Xóa nhà tuyển dụng**
- **2.4.4. Xem đánh giá công ty**

---

## 2. REVIEW CHI TIẾT CHỨC NĂNG 2.4.1 (Duyệt nhà tuyển dụng)

### 2.1. Models (✅ TốT)

**File:** `Models/Users/Employer.cs`

**Điểm mạnh:**
- ✅ Có enum `MembershipStatus` với 3 trạng thái: Pending, Active, Rejected
- ✅ Quan hệ với `ApplicationUser` và `CompanyProfile` đã đúng
- ✅ Có trường `IsOwner` để phân biệt chủ công ty
- ✅ Có `JoinedAt` để tracking thời gian

**Vấn đề và đề xuất cải thiện:**

❌ **Thiếu trường quan trọng:**
```csharp
// Thiếu: Lý do từ chối (khi MembershipStatus = Rejected)
public string? RejectionReason { get; set; }

// Thiếu: Ai duyệt (admin/owner)
public string? ReviewedByUserId { get; set; }

// Thiếu: Thời gian duyệt
public DateTime? ReviewedAt { get; set; }
```

❌ **Validation chưa đầy đủ:**
```csharp
// Nên thêm:
[MaxLength(1000)]
public string? RejectionReason { get; set; }
```

---

### 2.2. Service Layer

**File:** `Services/CompanyService.cs`

#### ✅ Điểm mạnh:
1. Logic kiểm tra quyền owner đúng
2. Kiểm tra trạng thái Pending trước khi duyệt
3. Cập nhật `JoinedAt` khi approve

#### ❌ Vấn đề nghiêm trọng:

**1. Thiếu xử lý transaction:**
```csharp
// HIỆN TẠI - Không an toàn
public async Task<bool> ReviewEmployerAsync(int employerId, string actingUserId, bool approve)
{
    // Nếu SaveChangesAsync() fail → không rollback được
    employer.MembershipStatus = approve ? MembershipStatus.Active : MembershipStatus.Rejected;
    await _db.SaveChangesAsync(); // ❌ Không có transaction
    return true;
}

// NÊN SỬA:
public async Task<bool> ReviewEmployerAsync(int employerId, string actingUserId, bool approve, string? rejectionReason = null)
{
    using var transaction = await _db.Database.BeginTransactionAsync();
    try 
    {
        // Logic xử lý
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
    catch 
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

**2. Thiếu validation đầu vào:**
```csharp
// ❌ Không validate rejectionReason khi reject
if (!approve && string.IsNullOrWhiteSpace(rejectionReason))
    throw new ArgumentException("Rejection reason is required when rejecting");
```

**3. Không trả về thông tin chi tiết:**
```csharp
// HIỆN TẠI: return true/false - không rõ lý do fail
return false;

// NÊN: return ServiceResult với message chi tiết
return ServiceResult.Fail("Employer not found");
return ServiceResult.Fail("You are not authorized");
return ServiceResult.Fail("Employer is not in pending status");
```

**4. Thiếu logging:**
```csharp
// Nên thêm logging cho các hành động quan trọng
_logger.LogInformation("Owner {OwnerId} approved employer {EmployerId}", actingUserId, employerId);
```

**5. Thiếu notification:**
```csharp
// Sau khi duyệt, nên gửi notification cho employer
await _notificationService.NotifyEmployerReviewResult(employer, approve, rejectionReason);
```

**6. Query không tối ưu:**
```csharp
// HIỆN TẠI: 2 queries riêng biệt
var employer = await _db.Employers.Include(e => e.Company).FirstOrDefaultAsync(e => e.Id == employerId);
var owner = await _db.Employers.FirstOrDefaultAsync(...);

// NÊN: 1 query với join
var result = await (from emp in _db.Employers.Include(e => e.Company)
                    join own in _db.Employers on emp.CompanyProfileId equals own.CompanyProfileId
                    where emp.Id == employerId && own.UserId == actingUserId && own.IsOwner
                    select new { Employer = emp, Owner = own }).FirstOrDefaultAsync();
```

---

### 2.3. UI Layer

**File:** `Components/Pages/CompanyMembers.razor`

#### ✅ Điểm mạnh:
1. Hiển thị danh sách pending employers
2. Có nút Duyệt/Từ chối
3. Load lại sau khi review

#### ❌ Vấn đề:

**1. Thiếu xử lý lỗi:**
```csharp
// HIỆN TẠI
private async Task Review(int employerId, bool approve)
{
    var ok = await CompanyService.ReviewEmployerAsync(employerId, userId, approve);
    if (ok) await LoadPending();
    // ❌ Không thông báo nếu ok = false
}

// NÊN
private async Task Review(int employerId, bool approve)
{
    try 
    {
        var result = await CompanyService.ReviewEmployerAsync(employerId, userId, approve);
        if (result.Success)
        {
            _toastService.ShowSuccess("Đã duyệt thành công");
            await LoadPending();
        }
        else
        {
            _toastService.ShowError(result.Message);
        }
    }
    catch (Exception ex)
    {
        _toastService.ShowError($"Lỗi: {ex.Message}");
    }
}
```

**2. Thiếu confirmation dialog:**
```csharp
// Khi từ chối, nên có popup nhập lý do từ chối
<button @onclick="() => ShowRejectDialog(e.Id)">Từ chối</button>
```

**3. Thiếu phân quyền:**
```csharp
// Không kiểm tra role trước khi hiển thị trang
@attribute [Authorize(Roles = "Employer")] // ❌ Thiếu

// Và cần kiểm tra IsOwner
if (!currentEmployer.IsOwner) 
{
    <p>Bạn không có quyền truy cập trang này.</p>
    return;
}
```

**4. UI/UX chưa tốt:**
- ❌ Không có filter/search
- ❌ Không có pagination
- ❌ Không hiển thị ngày đăng ký
- ❌ Không có trạng thái loading khi đang review

---

## 3. THIẾT KẾ CHỨC NĂNG CÒN THIẾU

### 3.1. Chức năng 2.4.2: Duyệt tin tuyển dụng

**Models cần thêm:**
```csharp
// JobPosting.cs - Thêm fields
public enum JobPostingStatus 
{
    Draft,          // Nhà tuyển dụng đang soạn
    PendingReview,  // Chờ admin duyệt
    Published,      // Đã duyệt, hiển thị công khai
    Rejected,       // Bị từ chối
    Closed,         // Đã đóng
    Expired         // Hết hạn
}

public string? RejectionReason { get; set; }
public string? ReviewedByAdminId { get; set; }
public ApplicationUser? ReviewedBy { get; set; }
public DateTime? ReviewedAt { get; set; }
```

**Service methods:**
- `GetPendingJobPostingsAsync()` - Lấy danh sách tin chờ duyệt
- `ReviewJobPostingAsync(jobId, adminId, approve, reason)` - Duyệt/từ chối

### 3.2. Chức năng 2.4.3: Xóa nhà tuyển dụng

**Lưu ý:** Nên sử dụng soft delete thay vì hard delete

**Models cần thêm:**
```csharp
// Employer.cs
public bool IsDeleted { get; set; }
public DateTime? DeletedAt { get; set; }
public string? DeletedByUserId { get; set; }
```

**Service methods:**
- `DeleteEmployerAsync(employerId, adminId, reason)` - Soft delete
- `RestoreEmployerAsync(employerId, adminId)` - Khôi phục
- `PermanentDeleteEmployerAsync(employerId)` - Hard delete (admin only)

### 3.3. Chức năng 2.4.4: Xem đánh giá công ty

**Models:** Đã có `Review` model - OK ✅

**Service methods cần thêm:**
- `GetCompanyReviewsAsync(companyId, page, pageSize)` - Lấy danh sách reviews
- `GetCompanyReviewStatisticsAsync(companyId)` - Thống kê đánh giá (avg rating, count by stars)
- `DeleteReviewAsync(reviewId, adminId, reason)` - Xóa review vi phạm

---

## 4. CẤU TRÚC CODE ĐỀ XUẤT

### 4.1. Service Result Pattern

Tạo class `ServiceResult<T>` để trả về kết quả rõ ràng:

```csharp
public class ServiceResult<T>
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public T? Data { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    public static ServiceResult<T> Ok(T data, string message = "Success")
        => new() { Success = true, Data = data, Message = message };
    
    public static ServiceResult<T> Fail(string message, Dictionary<string, string[]>? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}
```

### 4.2. Repository Pattern

Hiện tại đang inject DbContext trực tiếp vào Service - chưa tốt cho testing.

**Đề xuất:**
```csharp
public interface IEmployerRepository
{
    Task<Employer?> GetByIdAsync(int id, bool includeCompany = false);
    Task<List<Employer>> GetPendingByCompanyAsync(int companyId);
    Task<Employer?> GetOwnerByUserIdAsync(string userId);
    Task UpdateAsync(Employer employer);
}
```

---

## 5. SECURITY CONCERNS

### 5.1. Authorization

❌ **Thiếu kiểm tra role/permission:**
- CompanyMembers.razor không có `[Authorize]`
- Không validate IsOwner trước khi cho phép duyệt

✅ **Nên thêm:**
```csharp
[Authorize(Roles = "Employer")]
public class CompanyService 
{
    // Hoặc dùng Policy-based authorization
    [Authorize(Policy = "MustBeCompanyOwner")]
}
```

### 5.2. Input Validation

❌ **Thiếu validate:**
- Không check employerId > 0
- Không check userId không null/empty
- Không validate rejectionReason khi reject

### 5.3. SQL Injection

✅ EF Core đã protect - OK

### 5.4. Mass Assignment

✅ Không có DTO nên cần cẩn thận khi bind từ client - nên tạo DTOs

---

## 6. TESTING GAPS

❌ **Hiện tại:** Không có unit test nào

✅ **Cần test:**
1. Service layer:
   - Happy paths
   - Authorization failures
   - Validation failures
   - Edge cases (employer not found, not pending, etc.)

2. Repository layer (nếu có):
   - CRUD operations
   - Query filters

3. Integration tests:
   - End-to-end flows
   - Database transactions

---

## 7. PERFORMANCE CONCERNS

### 7.1. N+1 Query Problem

✅ Đã dùng `.Include()` - OK

### 7.2. Pagination

❌ `GetPendingEmployersForOwnerAsync()` không có pagination - có thể bị performance issue nếu có nhiều pending

---

## 8. TÓM TẮT & KHUYẾN NGHỊ

### 8.1. Critical Issues (Cần fix ngay)

1. ❌ **Thiếu transaction handling**
2. ❌ **Thiếu authorization attributes**
3. ❌ **Thiếu error handling và user feedback**
4. ❌ **3/4 chức năng chưa triển khai**

### 8.2. High Priority

1. ❌ Thiếu notification sau khi duyệt
2. ❌ Thiếu logging
3. ❌ Thiếu validation đầy đủ
4. ❌ Không có unit tests

### 8.3. Medium Priority

1. ❌ Thiếu pagination
2. ❌ Thiếu DTOs
3. ❌ Chưa áp dụng Repository pattern
4. ❌ UI/UX chưa hoàn thiện

### 8.4. Refactoring Plan

**Bước 1:** Hoàn thiện Models (thêm fields tracking)  
**Bước 2:** Refactor Service (thêm transaction, validation, ServiceResult)  
**Bước 3:** Triển khai 3 chức năng còn thiếu  
**Bước 4:** Viết đầy đủ Unit Tests  
**Bước 5:** Cải thiện UI/UX  
**Bước 6:** Performance optimization  

---

## 9. CHECKLIST HOÀN THIỆN

### Chức năng 2.4.1 - Duyệt nhà tuyển dụng
- [ ] Thêm fields tracking vào Employer model
- [ ] Refactor service với transaction
- [ ] Thêm validation đầy đủ
- [ ] Thêm notification
- [ ] Thêm logging
- [ ] Thêm authorization
- [ ] Cải thiện UI (confirmation, error handling)
- [ ] Viết unit tests

### Chức năng 2.4.2 - Duyệt tin tuyển dụng
- [ ] Thêm fields vào JobPosting model
- [ ] Tạo service methods
- [ ] Tạo UI page
- [ ] Viết unit tests

### Chức năng 2.4.3 - Xóa nhà tuyển dụng
- [ ] Implement soft delete
- [ ] Tạo service methods
- [ ] Tạo UI
- [ ] Viết unit tests

### Chức năng 2.4.4 - Xem đánh giá công ty
- [ ] Tạo service methods (đã có model)
- [ ] Tạo UI page
- [ ] Thêm statistics/aggregation
- [ ] Viết unit tests

---

**Kết luận:** Code hiện tại có foundation tốt nhưng cần hoàn thiện đáng kể về logic, security, error handling và testing trước khi đưa vào production.
