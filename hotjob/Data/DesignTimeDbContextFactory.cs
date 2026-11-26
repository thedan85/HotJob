using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace hotjob.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can scaffold migrations without
/// running Program.cs (which connects to MySQL for seeding).
/// The connection string comes from the same sources as the app
/// (appsettings.json, appsettings.{Environment}.json, user secrets, environment variables
/// such as <c>ConnectionStrings__DefaultConnection</c>); nothing is hard-coded here.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        // dotnet ef usually runs from the project folder; fall back to the build output,
        // where the Web SDK copies appsettings*.json.
        var basePath = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
            ? Directory.GetCurrentDirectory()
            : AppContext.BaseDirectory;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets(typeof(DesignTimeDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing ConnectionStrings:DefaultConnection for design-time tools. Set it in appsettings, " +
                "user secrets, or the ConnectionStrings__DefaultConnection environment variable.");
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        return new ApplicationDbContext(options);
    }
}
