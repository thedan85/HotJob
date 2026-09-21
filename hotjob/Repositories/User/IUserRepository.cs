using hotjob.Models.Users;

namespace hotjob.Repositories.User;

public interface IUserRepository
{
    Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationUser>> SearchAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken = default);

    Task SetActiveStatusAsync(
        string userId,
        bool isActive,
        CancellationToken cancellationToken = default);
}