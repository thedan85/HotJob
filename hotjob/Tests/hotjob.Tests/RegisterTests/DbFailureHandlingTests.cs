using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Services.Common;
using hotjob.Tests.Support;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace hotjob.Tests;

public sealed class DbExceptionClassifierTests
{
    /// <summary>MySqlException has no public constructor taking an error code; build one reflectively.</summary>
    private static MySqlException MySqlError(MySqlErrorCode code, string message)
    {
        var ctor = typeof(MySqlException)
            .GetConstructors(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType == typeof(MySqlErrorCode))
            .OrderBy(c => c.GetParameters().Length)
            .First(c => c.GetParameters().Skip(1).All(p => p.ParameterType == typeof(string)));
        var args = ctor.GetParameters()
            .Select((p, i) => i == 0 ? (object)code : p.Name!.Contains("message", StringComparison.OrdinalIgnoreCase) ? message : null!)
            .ToArray();
        return (MySqlException)ctor.Invoke(args);
    }

    [Fact]
    public async Task RealSqliteDuplicate_IsUniqueViolation()
    {
        await using var fx = await TestFixture.CreateAsync();
        fx.Db.CompanyProfiles.Add(new CompanyProfile { CompanyName = "Same" });
        await fx.Db.SaveChangesAsync();
        fx.Db.CompanyProfiles.Add(new CompanyProfile { CompanyName = "Same" });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => fx.Db.SaveChangesAsync());

        Assert.True(DbExceptionClassifier.IsUniqueViolation(exception));
    }

    [Fact]
    public void MySqlDuplicateKeyEntry_IsUniqueViolation()
    {
        var mySql = MySqlError(MySqlErrorCode.DuplicateKeyEntry, "Duplicate entry 'x' for key 'IX_Name'");

        Assert.True(DbExceptionClassifier.IsUniqueViolation(new DbUpdateException("save", mySql)));
    }

    [Fact]
    public void OtherMySqlError_IsNotUniqueViolation_EvenIfMessageMentionsDuplicateOrUnique()
    {
        // The old implementation matched on message text and would call this a unique violation.
        var mySql = MySqlError(MySqlErrorCode.NoReferencedRow2, "Cannot add row: unique Duplicate wording");

        Assert.False(DbExceptionClassifier.IsUniqueViolation(new DbUpdateException("save", mySql)));
    }

    [Fact]
    public void UnrelatedException_IsNotUniqueViolation()
    {
        Assert.False(DbExceptionClassifier.IsUniqueViolation(
            new DbUpdateException("save", new InvalidOperationException("Duplicate unique"))));
        Assert.False(DbExceptionClassifier.IsUniqueViolation(null));
    }
}

public sealed class RegistrationRaceTests
{
    private static RegisterAccountRequest Candidate(string email) => new()
    {
        Email = email,
        Password = "Str0ng!Pass",
        FullName = "Race Person",
        AccountType = AccountType.Candidate
    };

    [Fact]
    public async Task Register_UniqueViolationWhileCreatingUser_Returns409_AndNothingPersisted()
    {
        var interceptor = new FailingSaveInterceptor();
        await using var fx = await TestFixture.CreateAsync(interceptor);
        interceptor.ArmOnSave(1); // Identity's user insert loses the race for the email index.

        var result = await fx.Service.RegisterAsync(Candidate("race@example.com"));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Null(await fx.UserManager.FindByEmailAsync("race@example.com"));
    }

    [Fact]
    public async Task Register_UniqueViolationWhileCreatingCompany_Returns409_AndUserRolledBack()
    {
        var interceptor = new FailingSaveInterceptor();
        await using var fx = await TestFixture.CreateAsync(interceptor);
        interceptor.ArmOnSave(2); // user insert succeeds; company/employer insert loses the name-index race.

        var result = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "owner@example.com",
            Password = "Str0ng!Pass",
            FullName = "Owner Person",
            AccountType = AccountType.Employer,
            CompanyName = "Racy Co"
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Conflict, result.ErrorType);
        Assert.Null(await fx.UserManager.FindByEmailAsync("owner@example.com"));
        Assert.Equal(0, await fx.Db.CompanyProfiles.CountAsync());
        Assert.Equal(0, await fx.Db.Employers.CountAsync());
    }
}

public sealed class ProfileUpdateRollbackTests
{
    [Fact]
    public async Task UpdateEmail_UserNameRejectedAfterEmailSaved_RollsBackAndDoesNotLeakStaleState()
    {
        await using var fx = await TestFixture.CreateAsync();
        var registered = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "old@example.com",
            Password = "Str0ng!Pass",
            FullName = "Some Person",
            AccountType = AccountType.Candidate
        });
        Assert.True(registered.Success, string.Join("; ", registered.Errors));
        var userId = registered.Data!.UserId;

        // Valid as an email (Identity saves it first) but an apostrophe is not an allowed user-name
        // character, so SetUserNameAsync fails after the email change was already written.
        var result = await fx.Service.UpdateProfileAsync(userId, new UpdateProfileRequest { Email = "o'brien@example.com" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        var persisted = await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal("old@example.com", persisted.Email);
        Assert.Equal("old@example.com", persisted.UserName);
        // The shared context must not hand back the half-modified user as if it had been saved.
        var reloaded = await fx.UserManager.FindByIdAsync(userId);
        Assert.Equal("old@example.com", reloaded!.Email);
    }

    [Fact]
    public async Task UpdateEmail_ValidChange_StillCommits()
    {
        await using var fx = await TestFixture.CreateAsync();
        var registered = await fx.Service.RegisterAsync(new RegisterAccountRequest
        {
            Email = "before@example.com",
            Password = "Str0ng!Pass",
            FullName = "Some Person",
            AccountType = AccountType.Candidate
        });
        var userId = registered.Data!.UserId;

        var result = await fx.Service.UpdateProfileAsync(userId, new UpdateProfileRequest { Email = "after@example.com" });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var persisted = await fx.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal("after@example.com", persisted.Email);
        Assert.Equal("after@example.com", persisted.UserName);
    }
}
