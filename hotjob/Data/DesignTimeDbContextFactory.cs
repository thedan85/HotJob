using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace hotjob.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can scaffold migrations without
/// running Program.cs (which connects to MySQL for seeding).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(
                "Server=localhost;Database=hotjob;User=root;Password=;",
                new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        return new ApplicationDbContext(options);
    }
}
