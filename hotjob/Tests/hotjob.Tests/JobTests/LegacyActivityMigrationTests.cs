using hotjob.Models.Users;
using hotjob.Models.Job;
using hotjob.Repositories.Job;
using hotjob.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace hotjob.Tests.JobTests;

public sealed class LegacyActivityMigrationTests
{
    [Theory]
    [InlineData(typeof(CompanyProfile))]
    [InlineData(typeof(Employer))]
    public async Task Activity_DefaultIsTrue_AndExplicitFalseIsNotDatabaseGenerated(Type entityType)
    {
        await using var fx = await TestFixture.CreateAsync();
        var property = fx.Db.Model.FindEntityType(entityType)!.FindProperty("IsActive")!;

        Assert.Equal(true, property.GetDefaultValue());
        // Explicit false must be sent to the database rather than replaced by its true default.
        Assert.Equal(true, property.Sentinel);
    }

    [Theory]
    [InlineData("CompanyProfiles", "")]
    [InlineData("Employers", ", DeactivationReason TEXT NULL")]
    public async Task Backfill_RestoresOnlyLegacyRows_AndIsIdempotent(string table, string extraColumn)
    {
        await using var fx = await TestFixture.CreateAsync();
        var migration = FindRepairMigration(fx);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"""
            CREATE TABLE {table} (
                Id INTEGER PRIMARY KEY, IsActive INTEGER NOT NULL DEFAULT 0,
                DeactivatedAt TEXT NULL, DeactivatedByAdminId TEXT NULL {extraColumn});
            INSERT INTO {table} (Id, IsActive, DeactivatedAt, DeactivatedByAdminId)
            VALUES (1, 0, NULL, NULL), (2, 0, '2026-11-26', NULL),
                   (3, 0, NULL, 'admin'), (4, 0, '2026-11-26', 'admin'),
                   (5, 1, NULL, NULL);
            """);
        if (table == "Employers")
        {
            await ExecuteAsync(connection, """
                INSERT INTO Employers (Id, IsActive, DeactivationReason)
                VALUES (6, 0, 'Disabled by admin');
                """);
        }

        var sql = migration.UpOperations.OfType<SqlOperation>()
            .Single(op => op.Sql.Contains($"UPDATE `{table}`", StringComparison.Ordinal)).Sql;
        await ExecuteAsync(connection, sql);
        await ExecuteAsync(connection, sql);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id, IsActive FROM {table} ORDER BY Id";
        await using var reader = await command.ExecuteReaderAsync();
        var states = new List<bool>();
        while (await reader.ReadAsync()) states.Add(reader.GetBoolean(1));
        Assert.Equal(table == "Employers"
            ? new[] { true, false, false, false, true, false }
            : new[] { true, false, false, false, true }, states);
    }

    [Fact]
    public async Task Migration_ChangesBothDefaults_AndRollbackDoesNotDeactivateRepairedRows()
    {
        await using var fx = await TestFixture.CreateAsync();
        var migration = FindRepairMigration(fx);
        var alterations = migration.UpOperations.OfType<AlterColumnOperation>().ToList();

        Assert.Equal(2, alterations.Count);
        Assert.All(alterations, column =>
        {
            Assert.Equal("IsActive", column.Name);
            Assert.Equal(true, column.DefaultValue);
            Assert.Equal(false, column.OldColumn.DefaultValue);
        });
        Assert.Equal(new[] { "CompanyProfiles", "Employers" },
            alterations.Select(column => column.Table).OrderBy(table => table));
        Assert.Equal(2, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, operation =>
            Assert.Equal(false, Assert.IsType<AlterColumnOperation>(operation).DefaultValue));
    }

    [Fact]
    public async Task ExplicitDeactivation_IsPersistedDespiteTrueDatabaseDefault()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync();
        var company = new CompanyProfile { CompanyName = "Inactive company", IsActive = false };
        var employer = new Employer { Company = company, UserId = user.Id, FullName = "Inactive employer", IsActive = false };
        fx.Db.Employers.Add(employer);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        Assert.False((await fx.Db.CompanyProfiles.SingleAsync(c => c.Id == company.Id)).IsActive);
        Assert.False((await fx.Db.Employers.SingleAsync(e => e.Id == employer.Id)).IsActive);
    }

    [Fact]
    public async Task DatabaseDefaults_CreateActiveCompaniesAndEmployers_WhenFlagsAreOmitted()
    {
        await using var fx = await TestFixture.CreateAsync();
        var user = await fx.SeedUserAsync();
        await fx.Db.Database.ExecuteSqlRawAsync("""
            INSERT INTO CompanyProfiles (CompanyName, Status) VALUES ('Default activity', 1)
            """);
        var company = await fx.Db.CompanyProfiles.SingleAsync();
        await fx.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Employers (UserId, CompanyProfileId, FullName, IsOwner, MembershipStatus, JoinedAt)
            VALUES ({user.Id}, {company.Id}, {"Default employer"}, 0, 1, CURRENT_TIMESTAMP)
            """);

        Assert.True(company.IsActive);
        Assert.True((await fx.Db.Employers.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Backfill_RestoresPublicJobs_ButPreservesDeactivationAndApprovalGates()
    {
        await using var fx = await TestFixture.CreateAsync();
        var legacyCompany = new CompanyProfile
        {
            CompanyName = "Legacy approved", Status = CompanyStatus.Approved, IsActive = false
        };
        var deactivatedCompany = new CompanyProfile
        {
            CompanyName = "Admin deactivated", Status = CompanyStatus.Approved,
            IsActive = false, DeactivatedAt = DateTime.UtcNow
        };
        var pendingCompany = new CompanyProfile
        {
            CompanyName = "Pending approval", Status = CompanyStatus.Pending, IsActive = false
        };
        foreach (var company in new[] { legacyCompany, deactivatedCompany, pendingCompany })
        {
            fx.Db.JobPostings.Add(new JobPosting
            {
                Company = company, Title = company.CompanyName, Description = "Published job",
                Status = JobStatus.Published, ExpiryDate = DateTime.UtcNow.AddDays(7)
            });
        }
        await fx.Db.SaveChangesAsync();
        var repository = new JobPostingRepository(fx.Db);
        var before = await repository.BrowsePublishedAsync(null, null, null, null, null, null, null, null, 1, 20);
        Assert.Equal(0, before.TotalCount);

        foreach (var operation in FindRepairMigration(fx).UpOperations.OfType<SqlOperation>())
            await fx.Db.Database.ExecuteSqlRawAsync(operation.Sql);
        fx.Db.ChangeTracker.Clear();

        var after = await repository.BrowsePublishedAsync(null, null, null, null, null, null, null, null, 1, 20);
        Assert.Equal(1, after.TotalCount);
        Assert.Equal("Legacy approved", Assert.Single(after.Items).Title);
        Assert.Equal(CompanyStatus.Pending,
            (await fx.Db.CompanyProfiles.SingleAsync(c => c.Id == pendingCompany.Id)).Status);
    }

    private static Migration FindRepairMigration(TestFixture fx)
    {
        var assembly = fx.Db.GetService<IMigrationsAssembly>();
        var entry = assembly.Migrations.SingleOrDefault(pair =>
            pair.Key.EndsWith("_RestoreLegacyCompanyAndEmployerActivity", StringComparison.Ordinal));
        Assert.NotNull(entry.Value);
        return assembly.CreateMigration(entry.Value, fx.Db.Database.ProviderName!);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
