# CODE TRIỂN KHAI ĐẦY ĐỦ 4 CHỨC NĂNG QUẢN LÝ CÔNG TY

**Dự án:** HotJob - Quản lý công ty  
**Phiên bản:** 1.0  
**Ngày:** 2026-10-12

---

## 📋 MỤC LỤC

1. [Cải thiện Models](#1-cải-thiện-models)
2. [DTOs mới](#2-dtos-mới)
3. [Service Layer - Interface](#3-service-layer---interface)
4. [Service Layer - Implementation](#4-service-layer---implementation)
5. [Controller/API (nếu cần)](#5-controllerapi)
6. [UI Components (Blazor)](#6-ui-components-blazor)

---

## 1. CẢI THIỆN MODELS

### 1.1. Models/Users/Employer.cs (CẢI THIỆN)

**File:** `HotJob/hotjob/Models/Users/Employer.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Users;

/// <summary>Membership state of an employer inside a company.</summary>
public enum MembershipStatus
{
    Pending,
    Active,
    Rejected,
    Deleted  // THÊM MỚI: Soft delete
}

/// <summary>Employer profile linked one-to-one to an application user and to a company.</summary>
public class Employer
{
    public int Id { get; set; }

    public string UserId { get; set; } = default!;

    public int CompanyProfileId { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = default!;

    [MaxLength(100)]
    public string? Position { get; set; }

    public bool IsOwner { get; set; }

    public MembershipStatus MembershipStatus { get; set; } = MembershipStatus.Pending;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // ========== THÊM MỚI: Tracking fields ==========
    
    /// <summary>Lý do từ chối khi MembershipStatus = Rejected</summary>
    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    /// <summary>User ID của người duyệt (owner hoặc admin)</summary>
    public string? ReviewedByUserId { get; set; }

    /// <summary>Thời gian được duyệt/từ chối</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Đánh dấu đã xóa mềm</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Thời gian xóa</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>User ID của người xóa</summary>
    public string? DeletedByUserId { get; set; }

    /// <summary>Lý do xóa</summary>
    [MaxLength(1000)]
    public string? DeletionReason { get; set; }

    // ========== Navigation Properties ==========

    /// <summary>Identity user owning this profile.</summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary>Company the employer belongs to.</summary>
    public CompanyProfile Company { get; set; } = default!;

    /// <summary>Job postings created by this employer.</summary>
    public ICollection<Job.JobPosting> CreatedJobs { get; set; } = new List<Job.JobPosting>();

    /// <summary>User who reviewed this employer (approved/rejected).</summary>
    public ApplicationUser? ReviewedBy { get; set; }

    /// <summary>User who deleted this employer.</summary>
    public ApplicationUser? DeletedBy { get; set; }
}
```

### 1.2. Models/Job/JobPosting.cs (CẢI THIỆN)

**File:** `HotJob/hotjob/Models/Job/JobPosting.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace hotjob.Models.Job;

/// <summary>Lifecycle state of a job posting.</summary>
public enum JobStatus
{
    Draft,              // Nhà tuyển dụng đang soạn
    PendingReview,      // THÊM MỚI: Chờ admin duyệt
    Published,          // Đã duyệt, hiển thị công khai
    Rejected,           // THÊM MỚI: Bị từ chối
    Closed,             // Đã đóng
    Expired             // Hết hạn
}

/// <summary>Employment type of a job posting.</summary>
public enum JobType
{
    FullTime,
    PartTime,
    Contract,
    Internship,
    Freelance,
    Remote
}

/// <summary>Job posting published by a company.</summary>
public class JobPosting
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    public int? CreatedByEmployerId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = default!;

    [Required, MaxLength(10000)]
    public string Description { get; set; } = default!;

    [Range(0, double.MaxValue)]
    public decimal? SalaryMin { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? SalaryMax { get; set; }

    [Range(0, 50)]
    public int ExperienceRequired { get; set; }

    public JobType JobType { get; set; } = JobType.FullTime;

    public DateTime? ExpiryDate { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ========== THÊM MỚI: Tracking fields cho approval workflow ==========

    /// <summary>Thời gian submit để chờ duyệt</summary>
    public DateTime? SubmittedForReviewAt { get; set; }

    /// <summary>Lý do từ chối nếu Status = Rejected</summary>
    [MaxLength(2000)]
    public string? RejectionReason { get; set; }

    /// <summary>Admin ID người duyệt</summary>
    public string? ReviewedByAdminId { get; set; }

    /// <summary>Thời gian được duyệt/từ chối</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Thời gian publish (khi Status chuyển sang Published)</summary>
    public DateTime? PublishedAt { get; set; }

    // ========== Navigation Properties ==========

    /// <summary>Company that owns this posting.</summary>
    public Users.CompanyProfile Company { get; set; } = default!;

    /// <summary>Employer who created this posting. Null if the author left.</summary>
    public Users.Employer? CreatedBy { get; set; }

    /// <summary>Admin who reviewed this posting.</summary>
    public Users.ApplicationUser? ReviewedBy { get; set; }

    /// <summary>Required skills with importance weights.</summary>
    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();

    /// <summary>Category mappings of this posting.</summary>
    public ICollection<JobCategoryMapping> CategoryMappings { get; set; } = new List<JobCategoryMapping>();

    /// <summary>Applications submitted to this posting.</summary>
    public ICollection<Application.Application> Applications { get; set; } = new List<Application.Application>();

    /// <summary>Candidates that bookmarked this posting.</summary>
    public ICollection<SavedJob> SavedBy { get; set; } = new List<SavedJob>();

    /// <summary>ML match scores computed for this posting.</summary>
    public ICollection<ML.MatchScore> MatchScores { get; set; } = new List<ML.MatchScore>();
}
```

---

## 2. DTOs MỚI

### 2.1. DTOs/ServiceResult.cs

**File:** `HotJob/hotjob/DTOs/ServiceResult.cs`

```csharp
namespace hotjob.DTOs;

/// <summary>
/// Generic service result wrapper for consistent API responses
/// </summary>
public class ServiceResult<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    public static ServiceResult<T> Ok(T data, string message = "Success")
    {
        return new ServiceResult<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    public static ServiceResult<T> Ok(string message = "Success")
    {
        return new ServiceResult<T>
        {
            Success = true,
            Message = message
        };
    }

    public static ServiceResult<T> Fail(string message, Dictionary<string, string[]>? errors = null)
    {
        return new ServiceResult<T>
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }
}

public class ServiceResult : ServiceResult<object>
{
    public new static ServiceResult Ok(string message = "Success")
    {
        return new ServiceResult
        {
            Success = true,
            Message = message
        };
    }

    public new static ServiceResult Fail(string message, Dictionary<string, string[]>? errors = null)
    {
        return new ServiceResult
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }
}
```

### 2.2. DTOs/CompanyDTOs.cs

**File:** `HotJob/hotjob/DTOs/CompanyDTOs.cs`

```csharp
namespace hotjob.DTOs;

/// <summary>DTO for employer review request</summary>
public class ReviewEmployerRequest
{
    public int EmployerId { get; set; }
    public bool Approve { get; set; }
    public string? RejectionReason { get; set; }
}

/// <summary>DTO for job posting review request</summary>
public class ReviewJobPostingRequest
{
    public int JobPostingId { get; set; }
    public bool Approve { get; set; }
    public string? RejectionReason { get; set; }
}

/// <summary>DTO for employer deletion request</summary>
public class DeleteEmployerRequest
{
    public int EmployerId { get; set; }
    public string Reason { get; set; } = default!;
}

/// <summary>DTO for company review statistics</summary>
public class CompanyReviewStatistics
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = default!;
    public int TotalReviews { get; set; }
    public double AverageRating { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
    public List<ReviewDTO> RecentReviews { get; set; } = new();
}

/// <summary>DTO for review display</summary>
public class ReviewDTO
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = default!;
    public int CandidateId { get; set; }
    public string CandidateName { get; set; } = default!;
    public int RatingScore { get; set; }
    public string? ReviewText { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

## 3. SERVICE LAYER - INTERFACE

### 3.1. Services/ICompanyService.cs (CẢI THIỆN)

**File:** `HotJob/hotjob/Services/ICompanyService.cs`

```csharp
using hotjob.DTOs;
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Users;

namespace hotjob.Services;

public interface ICompanyService
{
    // ========== 2.4.1. Duyệt nhà tuyển dụng ==========
    
    /// <summary>Lấy danh sách nhà tuyển dụng pending của công ty (dành cho owner)</summary>
    Task<ServiceResult<List<Employer>>> GetPendingEmployersForOwnerAsync(string ownerUserId);

    /// <summary>Duyệt hoặc từ chối nhà tuyển dụng</summary>
    Task<ServiceResult> ReviewEmployerAsync(int employerId, string actingUserId, bool approve, string? rejectionReason = null);

    /// <summary>Lấy chi tiết employer theo ID</summary>
    Task<ServiceResult<Employer>> GetEmployerByIdAsync(int employerId);

    // ========== 2.4.2. Duyệt tin tuyển dụng ==========
    
    /// <summary>Lấy danh sách tin tuyển dụng chờ duyệt (dành cho admin)</summary>
    Task<ServiceResult<List<JobPosting>>> GetPendingJobPostingsAsync(int page = 1, int pageSize = 20);

    /// <summary>Duyệt hoặc từ chối tin tuyển dụng (admin only)</summary>
    Task<ServiceResult> ReviewJobPostingAsync(int jobPostingId, string adminUserId, bool approve, string? rejectionReason = null);

    /// <summary>Submit tin tuyển dụng để chờ admin duyệt</summary>
    Task<ServiceResult> SubmitJobPostingForReviewAsync(int jobPostingId, string employerUserId);

    // ========== 2.4.3. Xóa nhà tuyển dụng ==========
    
    /// <summary>Soft delete nhà tuyển dụng (owner hoặc admin)</summary>
    Task<ServiceResult> DeleteEmployerAsync(int employerId, string actingUserId, string reason);

    /// <summary>Khôi phục nhà tuyển dụng đã xóa</summary>
    Task<ServiceResult> RestoreEmployerAsync(int employerId, string actingUserId);

    /// <summary>Lấy danh sách nhà tuyển dụng đã xóa</summary>
    Task<ServiceResult<List<Employer>>> GetDeletedEmployersAsync(int companyId);

    // ========== 2.4.4. Xem đánh giá công ty ==========
    
    /// <summary>Lấy danh sách đánh giá của công ty</summary>
    Task<ServiceResult<List<ReviewDTO>>> GetCompanyReviewsAsync(int companyId, int page = 1, int pageSize = 10);

    /// <summary>Lấy thống kê đánh giá công ty</summary>
    Task<ServiceResult<CompanyReviewStatistics>> GetCompanyReviewStatisticsAsync(int companyId);

    /// <summary>Xóa review vi phạm (admin only)</summary>
    Task<ServiceResult> DeleteReviewAsync(int reviewId, string adminUserId, string reason);

    /// <summary>Lấy chi tiết review theo ID</summary>
    Task<ServiceResult<Review>> GetReviewByIdAsync(int reviewId);
}
```

---

## 4. SERVICE LAYER - IMPLEMENTATION

### 4.1. Services/CompanyService.cs (HOÀN CHỈNH)

**File:** `HotJob/hotjob/Services/CompanyService.cs`

```csharp
using hotjob.Data;
using hotjob.DTOs;
using hotjob.Models;
using hotjob.Models.Job;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace hotjob.Services;

public class CompanyService : ICompanyService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CompanyService> _logger;

    public CompanyService(ApplicationDbContext db, ILogger<CompanyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ========== 2.4.1. Duyệt nhà tuyển dụng ==========

    public async Task<ServiceResult<List<Employer>>> GetPendingEmployersForOwnerAsync(string ownerUserId)
    {
        try
        {
            var owner = await _db.Employers
                .FirstOrDefaultAsync(e => e.UserId == ownerUserId && e.IsOwner && !e.IsDeleted);

            if (owner == null)
                return ServiceResult<List<Employer>>.Fail("Bạn không phải là chủ công ty");

            var pendingEmployers = await _db.Employers
                .Where(e => e.CompanyProfileId == owner.CompanyProfileId 
                         && e.MembershipStatus == MembershipStatus.Pending
                         && !e.IsDeleted)
                .Include(e => e.User)
                .OrderBy(e => e.JoinedAt)
                .ToListAsync();

            return ServiceResult<List<Employer>>.Ok(pendingEmployers, $"Tìm thấy {pendingEmployers.Count} nhà tuyển dụng chờ duyệt");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending employers for owner {UserId}", ownerUserId);
            return ServiceResult<List<Employer>>.Fail("Lỗi khi lấy danh sách nhà tuyển dụng");
        }
    }

    public async Task<ServiceResult> ReviewEmployerAsync(
        int employerId, 
        string actingUserId, 
        bool approve, 
        string? rejectionReason = null)
    {
        // Validation
        if (!approve && string.IsNullOrWhiteSpace(rejectionReason))
            return ServiceResult.Fail("Vui lòng nhập lý do từ chối");

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Lấy employer cần duyệt
            var employer = await _db.Employers
                .Include(e => e.Company)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == employerId);

            if (employer == null)
                return ServiceResult.Fail("Không tìm thấy nhà tuyển dụng");

            if (employer.IsDeleted)
                return ServiceResult.Fail("Nhà tuyển dụng đã bị xóa");

            // Kiểm tra quyền: phải là owner của cùng công ty
            var owner = await _db.Employers
                .FirstOrDefaultAsync(e => e.UserId == actingUserId 
                                       && e.IsOwner 
                                       && e.CompanyProfileId == employer.CompanyProfileId
                                       && !e.IsDeleted);

            if (owner == null)
                return ServiceResult.Fail("Bạn không có quyền duyệt nhà tuyển dụng này");

            // Kiểm tra trạng thái
            if (employer.MembershipStatus != MembershipStatus.Pending)
                return ServiceResult.Fail($"Nhà tuyển dụng đã ở trạng thái {employer.MembershipStatus}, không thể duyệt");

            // Cập nhật trạng thái
            employer.MembershipStatus = approve ? MembershipStatus.Active : MembershipStatus.Rejected;
            employer.ReviewedByUserId = actingUserId;
            employer.ReviewedAt = DateTime.UtcNow;

            if (approve)
            {
                employer.JoinedAt = DateTime.UtcNow;
                employer.RejectionReason = null;
            }
            else
            {
                employer.RejectionReason = rejectionReason;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Logging
            _logger.LogInformation(
                "Owner {OwnerId} {Action} employer {EmployerId} ({EmployerName})",
                actingUserId,
                approve ? "approved" : "rejected",
                employerId,
                employer.FullName
            );

            // TODO: Gửi notification cho employer
            // await _notificationService.NotifyEmployerReviewResult(employer, approve, rejectionReason);

            return ServiceResult.Ok(approve 
                ? $"Đã duyệt nhà tuyển dụng {employer.FullName}" 
                : $"Đã từ chối nhà tuyển dụng {employer.FullName}");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error reviewing employer {EmployerId}", employerId);
            return ServiceResult.Fail("Lỗi khi duyệt nhà tuyển dụng");
        }
    }

    public async Task<ServiceResult<Employer>> GetEmployerByIdAsync(int employerId)
    {
        try
        {
            var employer = await _db.Employers
                .Include(e => e.User)
                .Include(e => e.Company)
                .Include(e => e.ReviewedBy)
                .Include(e => e.DeletedBy)
                .FirstOrDefaultAsync(e => e.Id == employerId);

            if (employer == null)
                return ServiceResult<Employer>.Fail("Không tìm thấy nhà tuyển dụng");

            return ServiceResult<Employer>.Ok(employer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employer {EmployerId}", employerId);
            return ServiceResult<Employer>.Fail("Lỗi khi lấy thông tin nhà tuyển dụng");
        }
    }

    // ========== 2.4.2. Duyệt tin tuyển dụng ==========

    public async Task<ServiceResult<List<JobPosting>>> GetPendingJobPostingsAsync(int page = 1, int pageSize = 20)
    {
        try
        {
            var skip = (page - 1) * pageSize;

            var jobPostings = await _db.JobPostings
                .Where(j => j.Status == JobStatus.PendingReview)
                .Include(j => j.Company)
                .Include(j => j.CreatedBy)
                .OrderBy(j => j.SubmittedForReviewAt)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            return ServiceResult<List<JobPosting>>.Ok(jobPostings, $"Tìm thấy {jobPostings.Count} tin tuyển dụng chờ duyệt");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending job postings");
            return ServiceResult<List<JobPosting>>.Fail("Lỗi khi lấy danh sách tin tuyển dụng chờ duyệt");
        }
    }

    public async Task<ServiceResult> ReviewJobPostingAsync(
        int jobPostingId, 
        string adminUserId, 
        bool approve, 
        string? rejectionReason = null)
    {
        // Validation
        if (!approve && string.IsNullOrWhiteSpace(rejectionReason))
            return ServiceResult.Fail("Vui lòng nhập lý do từ chối");

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Lấy job posting
            var jobPosting = await _db.JobPostings
                .Include(j => j.Company)
                .Include(j => j.CreatedBy)
                .FirstOrDefaultAsync(j => j.Id == jobPostingId);

            if (jobPosting == null)
                return ServiceResult.Fail("Không tìm thấy tin tuyển dụng");

            // Kiểm tra admin role (TODO: implement role check)
            var admin = await _db.Users.FindAsync(adminUserId);
            if (admin == null)
                return ServiceResult.Fail("Người dùng không tồn tại");

            // Kiểm tra trạng thái
            if (jobPosting.Status != JobStatus.PendingReview)
                return ServiceResult.Fail($"Tin tuyển dụng đã ở trạng thái {jobPosting.Status}, không thể duyệt");

            // Cập nhật trạng thái
            jobPosting.Status = approve ? JobStatus.Published : JobStatus.Rejected;
            jobPosting.ReviewedByAdminId = adminUserId;
            jobPosting.ReviewedAt = DateTime.UtcNow;

            if (approve)
            {
                jobPosting.PublishedAt = DateTime.UtcNow;
                jobPosting.RejectionReason = null;
            }
            else
            {
                jobPosting.RejectionReason = rejectionReason;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Logging
            _logger.LogInformation(
                "Admin {AdminId} {Action} job posting {JobId} ({JobTitle})",
                adminUserId,
                approve ? "approved" : "rejected",
                jobPostingId,
                jobPosting.Title
            );

            // TODO: Gửi notification cho employer
            // await _notificationService.NotifyJobPostingReviewResult(jobPosting, approve, rejectionReason);

            return ServiceResult.Ok(approve 
                ? $"Đã duyệt tin tuyển dụng \"{jobPosting.Title}\"" 
                : $"Đã từ chối tin tuyển dụng \"{jobPosting.Title}\"");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error reviewing job posting {JobPostingId}", jobPostingId);
            return ServiceResult.Fail("Lỗi khi duyệt tin tuyển dụng");
        }
    }

    public async Task<ServiceResult> SubmitJobPostingForReviewAsync(int jobPostingId, string employerUserId)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var jobPosting = await _db.JobPostings
                .Include(j => j.CreatedBy)
                .FirstOrDefaultAsync(j => j.Id == jobPostingId);

            if (jobPosting == null)
                return ServiceResult.Fail("Không tìm thấy tin tuyển dụng");

            // Kiểm tra quyền
            if (jobPosting.CreatedBy?.UserId != employerUserId)
                return ServiceResult.Fail("Bạn không có quyền submit tin này");

            // Kiểm tra trạng thái
            if (jobPosting.Status != JobStatus.Draft)
                return ServiceResult.Fail($"Chỉ có thể submit tin ở trạng thái Draft, hiện tại: {jobPosting.Status}");

            // Validation cơ bản
            if (string.IsNullOrWhiteSpace(jobPosting.Title) || string.IsNullOrWhiteSpace(jobPosting.Description))
                return ServiceResult.Fail("Vui lòng điền đầy đủ tiêu đề và mô tả công việc");

            // Chuyển trạng thái
            jobPosting.Status = JobStatus.PendingReview;
            jobPosting.SubmittedForReviewAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Employer {EmployerId} submitted job posting {JobId} for review", employerUserId, jobPostingId);

            return ServiceResult.Ok("Đã gửi tin tuyển dụng để chờ duyệt");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error submitting job posting {JobPostingId} for review", jobPostingId);
            return ServiceResult.Fail("Lỗi khi submit tin tuyển dụng");
        }
    }

    // ========== 2.4.3. Xóa nhà tuyển dụng ==========

    public async Task<ServiceResult> DeleteEmployerAsync(int employerId, string actingUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ServiceResult.Fail("Vui lòng nhập lý do xóa");

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var employer = await _db.Employers
                .Include(e => e.Company)
                .FirstOrDefaultAsync(e => e.Id == employerId);

            if (employer == null)
                return ServiceResult.Fail("Không tìm thấy nhà tuyển dụng");

            if (employer.IsDeleted)
                return ServiceResult.Fail("Nhà tuyển dụng đã bị xóa trước đó");

            if (employer.IsOwner)
                return ServiceResult.Fail("Không thể xóa chủ công ty");

            // Kiểm tra quyền: phải là owner hoặc admin
            var isOwner = await _db.Employers
                .AnyAsync(e => e.UserId == actingUserId 
                            && e.IsOwner 
                            && e.CompanyProfileId == employer.CompanyProfileId
                            && !e.IsDeleted);

            // TODO: Thêm check admin role
            var isAdmin = false; // await _userManager.IsInRoleAsync(actingUser, "Admin");

            if (!isOwner && !isAdmin)
                return ServiceResult.Fail("Bạn không có quyền xóa nhà tuyển dụng này");

            // Soft delete
            employer.IsDeleted = true;
            employer.DeletedAt = DateTime.UtcNow;
            employer.DeletedByUserId = actingUserId;
            employer.DeletionReason = reason;
            employer.MembershipStatus = MembershipStatus.Deleted;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogWarning(
                "User {ActingUserId} deleted employer {EmployerId} ({EmployerName}). Reason: {Reason}",
                actingUserId, employerId, employer.FullName, reason
            );

            // TODO: Gửi notification
            // await _notificationService.NotifyEmployerDeleted(employer, reason);

            return ServiceResult.Ok($"Đã xóa nhà tuyển dụng {employer.FullName}");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting employer {EmployerId}", employerId);
            return ServiceResult.Fail("Lỗi khi xóa nhà tuyển dụng");
        }
    }

    public async Task<ServiceResult> RestoreEmployerAsync(int employerId, string actingUserId)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var employer = await _db.Employers
                .Include(e => e.Company)
                .FirstOrDefaultAsync(e => e.Id == employerId);

            if (employer == null)
                return ServiceResult.Fail("Không tìm thấy nhà tuyển dụng");

            if (!employer.IsDeleted)
                return ServiceResult.Fail("Nhà tuyển dụng chưa bị xóa");

            // Kiểm tra quyền
            var isOwner = await _db.Employers
                .AnyAsync(e => e.UserId == actingUserId 
                            && e.IsOwner 
                            && e.CompanyProfileId == employer.CompanyProfileId
                            && !e.IsDeleted);

            if (!isOwner)
                return ServiceResult.Fail("Bạn không có quyền khôi phục nhà tuyển dụng này");

            // Restore
            employer.IsDeleted = false;
            employer.DeletedAt = null;
            employer.DeletedByUserId = null;
            employer.DeletionReason = null;
            employer.MembershipStatus = employer.ReviewedAt != null && employer.MembershipStatus == MembershipStatus.Deleted
                ? MembershipStatus.Active 
                : MembershipStatus.Pending;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("User {ActingUserId} restored employer {EmployerId}", actingUserId, employerId);

            return ServiceResult.Ok($"Đã khôi phục nhà tuyển dụng {employer.FullName}");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error restoring employer {EmployerId}", employerId);
            return ServiceResult.Fail("Lỗi khi khôi phục nhà tuyển dụng");
        }
    }

    public async Task<ServiceResult<List<Employer>>> GetDeletedEmployersAsync(int companyId)
    {
        try
        {
            var deletedEmployers = await _db.Employers
                .Where(e => e.CompanyProfileId == companyId && e.IsDeleted)
                .Include(e => e.User)
                .Include(e => e.DeletedBy)
                .OrderByDescending(e => e.DeletedAt)
                .ToListAsync();

            return ServiceResult<List<Employer>>.Ok(deletedEmployers, $"Tìm thấy {deletedEmployers.Count} nhà tuyển dụng đã xóa");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting deleted employers for company {CompanyId}", companyId);
            return ServiceResult<List<Employer>>.Fail("Lỗi khi lấy danh sách nhà tuyển dụng đã xóa");
        }
    }

    // ========== 2.4.4. Xem đánh giá công ty ==========

    public async Task<ServiceResult<List<ReviewDTO>>> GetCompanyReviewsAsync(int companyId, int page = 1, int pageSize = 10)
    {
        try
        {
            var skip = (page - 1) * pageSize;

            var reviews = await _db.Reviews
                .Where(r => r.CompanyProfileId == companyId)
                .Include(r => r.Company)
                .Include(r => r.Candidate)
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(r => new ReviewDTO
                {
                    Id = r.Id,
                    CompanyId = r.CompanyProfileId,
                    CompanyName = r.Company.CompanyName,
                    CandidateId = r.CandidateId,
                    CandidateName = r.Candidate.FullName,
                    RatingScore = r.RatingScore,
                    ReviewText = r.ReviewText,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            return ServiceResult<List<ReviewDTO>>.Ok(reviews, $"Tìm thấy {reviews.Count} đánh giá");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reviews for company {CompanyId}", companyId);
            return ServiceResult<List<ReviewDTO>>.Fail("Lỗi khi lấy danh sách đánh giá");
        }
    }

    public async Task<ServiceResult<CompanyReviewStatistics>> GetCompanyReviewStatisticsAsync(int companyId)
    {
        try
        {
            var company = await _db.CompanyProfiles.FindAsync(companyId);
            if (company == null)
                return ServiceResult<CompanyReviewStatistics>.Fail("Không tìm thấy công ty");

            var reviews = await _db.Reviews
                .Where(r => r.CompanyProfileId == companyId)
                .ToListAsync();

            var statistics = new CompanyReviewStatistics
            {
                CompanyId = companyId,
                CompanyName = company.CompanyName,
                TotalReviews = reviews.Count,
                AverageRating = reviews.Any() ? reviews.Average(r => r.RatingScore) : 0,
                RatingDistribution = reviews
                    .GroupBy(r => r.RatingScore)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RecentReviews = await _db.Reviews
                    .Where(r => r.CompanyProfileId == companyId)
                    .Include(r => r.Company)
                    .Include(r => r.Candidate)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(5)
                    .Select(r => new ReviewDTO
                    {
                        Id = r.Id,
                        CompanyId = r.CompanyProfileId,
                        CompanyName = r.Company.CompanyName,
                        CandidateId = r.CandidateId,
                        CandidateName = r.Candidate.FullName,
                        RatingScore = r.RatingScore,
                        ReviewText = r.ReviewText,
                        CreatedAt = r.CreatedAt
                    })
                    .ToListAsync()
            };

            return ServiceResult<CompanyReviewStatistics>.Ok(statistics, "Lấy thống kê đánh giá thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting review statistics for company {CompanyId}", companyId);
            return ServiceResult<CompanyReviewStatistics>.Fail("Lỗi khi lấy thống kê đánh giá");
        }
    }

    public async Task<ServiceResult> DeleteReviewAsync(int reviewId, string adminUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ServiceResult.Fail("Vui lòng nhập lý do xóa đánh giá");

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var review = await _db.Reviews
                .Include(r => r.Company)
                .Include(r => r.Candidate)
                .FirstOrDefaultAsync(r => r.Id == reviewId);

            if (review == null)
                return ServiceResult.Fail("Không tìm thấy đánh giá");

            // TODO: Kiểm tra admin role
            var admin = await _db.Users.FindAsync(adminUserId);
            if (admin == null)
                return ServiceResult.Fail("Người dùng không tồn tại");

            // Xóa review
            _db.Reviews.Remove(review);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogWarning(
                "Admin {AdminId} deleted review {ReviewId} from candidate {CandidateId} for company {CompanyId}. Reason: {Reason}",
                adminUserId, reviewId, review.CandidateId, review.CompanyProfileId, reason
            );

            // TODO: Gửi notification cho candidate
            // await _notificationService.NotifyReviewDeleted(review, reason);

            return ServiceResult.Ok("Đã xóa đánh giá");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error deleting review {ReviewId}", reviewId);
            return ServiceResult.Fail("Lỗi khi xóa đánh giá");
        }
    }

    public async Task<ServiceResult<Review>> GetReviewByIdAsync(int reviewId)
    {
        try
        {
            var review = await _db.Reviews
                .Include(r => r.Company)
                .Include(r => r.Candidate)
                .FirstOrDefaultAsync(r => r.Id == reviewId);

            if (review == null)
                return ServiceResult<Review>.Fail("Không tìm thấy đánh giá");

            return ServiceResult<Review>.Ok(review);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting review {ReviewId}", reviewId);
            return ServiceResult<Review>.Fail("Lỗi khi lấy thông tin đánh giá");
        }
    }
}
```

---

**(Tiếp tục trong comment tiếp theo do giới hạn độ dài...)**
