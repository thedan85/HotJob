using hotjob.Data;
using hotjob.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Repositories.User;

public sealed class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public Task<ApplicationUser?> GetByIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public Task<ApplicationUser?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    public async Task<IReadOnlyList<ApplicationUser>> SearchAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ApplicationUser>();
        }

        pageNumber = Math.Max(pageNumber, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var term = query.Trim();

        return await dbContext.Users
            .AsNoTracking()
            .Where(user =>
                (user.UserName != null && EF.Functions.Like(user.UserName, $"%{term}%")) ||
                (user.Email != null && EF.Functions.Like(user.Email, $"%{term}%")))
            .OrderBy(user => user.UserName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);
    }

    public async Task SetActiveStatusAsync(
        string userId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(user => user.IsActive, isActive),
                cancellationToken);

        if (affectedRows == 0)
        {
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");
        }
    }
}