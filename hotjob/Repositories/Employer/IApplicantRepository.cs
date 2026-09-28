using hotjob.Models;
using hotjob.Models.Application;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore.Storage;

namespace hotjob.Repositories.Employer;

public interface IApplicantRepository
{
    Task<global::hotjob.Models.Users.Employer?> GetEmployerByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Application> Items, int TotalCount)> ListOwnedAsync(
        int companyProfileId,
        int? jobPostingId,
        ApplicationStatus? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Application?> GetOwnedDetailAsync(
        int companyProfileId,
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<Application?> GetOwnedForUpdateAsync(
        int companyProfileId,
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<bool> IsApplicationOwnedAsync(
        int companyProfileId,
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Interview>> ListInterviewsAsync(
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<Interview?> GetOwnedInterviewForUpdateAsync(
        int companyProfileId,
        int interviewId,
        CancellationToken cancellationToken = default);

    void AddHistory(ApplicationStatusHistory history);

    void AddNotification(Notification notification);

    void AddInterview(Interview interview);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
