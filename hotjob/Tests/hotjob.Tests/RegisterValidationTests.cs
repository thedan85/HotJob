using hotjob.DTOs.Authentication;
using hotjob.Services.Common;
using hotjob.Tests.Support;

namespace hotjob.Tests;

public sealed class RegisterValidationTests
{
    private static RegisterAccountRequest ValidCandidate(string email = "cand@example.com") => new()
    {
        Email = email,
        Password = "Str0ng!Pass",
        FullName = "Test Candidate",
        AccountType = AccountType.Candidate
    };

    private static RegisterAccountRequest ValidEmployerNewCompany(string email = "emp@example.com") => new()
    {
        Email = email,
        Password = "Str0ng!Pass",
        FullName = "Test Employer",
        Position = "HR",
        AccountType = AccountType.Employer,
        CompanyName = "Acme Corp"
    };

    [Fact]
    public async Task NullRequest_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var result = await fx.Service.RegisterAsync(null!);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    [InlineData("@missing.com")]
    public async Task InvalidEmail_ReturnsValidation(string email)
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidCandidate();
        req.Email = email;
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    // missing uppercase
    [InlineData("abcdef1!")]
    // missing lowercase
    [InlineData("ABCDEF1!")]
    // missing digit
    [InlineData("Abcdefg!")]
    // missing special char
    [InlineData("Abcdef12")]
    // too short
    [InlineData("A1!a")]
    public async Task WeakPassword_ReturnsValidation(string password)
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidCandidate();
        req.Password = password;
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task MissingFullName_Candidate_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidCandidate();
        req.FullName = "";
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("Full name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WhitespaceFullName_Candidate_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidCandidate();
        req.FullName = "   ";
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task InvalidAccountType_Zero_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidCandidate();
        req.AccountType = (AccountType)0;
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("account type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvalidAccountType_Undefined_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidCandidate();
        req.AccountType = (AccountType)99;
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Employer_MissingCompanyInfo_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidEmployerNewCompany();
        req.CompanyName = null;
        req.CompanyProfileId = null;
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("company", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Employer_WhitespaceCompanyName_TreatedAsMissing()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidEmployerNewCompany();
        req.CompanyName = "   ";
        req.CompanyProfileId = null;
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Employer_BothCompanyIdAndName_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidEmployerNewCompany();
        req.CompanyProfileId = 1;
        req.CompanyName = "Acme";
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
        Assert.Contains(result.Errors, e => e.Contains("not both", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Employer_MissingFullName_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidEmployerNewCompany();
        req.FullName = "  ";
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Position_TooLong_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidEmployerNewCompany();
        req.Position = new string('x', 101);
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task CompanyName_TooLong_ReturnsValidation()
    {
        await using var fx = await TestFixture.CreateAsync();
        var req = ValidEmployerNewCompany();
        req.CompanyName = new string('x', 201);
        var result = await fx.Service.RegisterAsync(req);
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorType.Validation, result.ErrorType);
    }
}
