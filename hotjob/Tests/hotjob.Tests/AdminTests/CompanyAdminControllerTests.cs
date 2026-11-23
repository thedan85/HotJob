using System.Security.Claims;
using HotJob.Dtos;
using hotjob.Controllers.Admin;
using hotjob.DTOs.Admin;
using hotjob.Models.Users;
using hotjob.Services.Admin;
using hotjob.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.AdminTests;

/// <summary>Controller-level checks: admin id comes from claims, and results map to 204/400/401/404/409.</summary>
public sealed class CompanyAdminControllerTests
{
    private static CompanyAdminController CreateController(TestFixture fx, string? adminId)
    {
        var claims = adminId is null
            ? Array.Empty<Claim>()
            : new[] { new Claim(ClaimTypes.NameIdentifier, adminId) };

        return new CompanyAdminController(new CompanyAdminService(fx.Db, NullLogger<CompanyAdminService>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                }
            }
        };
    }

    [Fact]
    public async Task Approve_UsesAdminIdFromClaims()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Pending Co");

        var result = await CreateController(fx, adminId).Approve(company.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.Equal(adminId, company.VerifiedByAdminId);
    }

    [Fact]
    public async Task Approve_WithoutIdentityClaim_Returns401_AndDoesNothing()
    {
        await using var fx = await TestFixture.CreateAsync();
        var company = await AdminSeed.CompanyAsync(fx, "Pending Co");

        var result = await CreateController(fx, adminId: null).Approve(company.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Equal(CompanyStatus.Pending, company.Status);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_Returns409()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Approved Co", CompanyStatus.Approved);

        var result = await CreateController(fx, adminId).Approve(company.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Reject_BlankReason_Returns400()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Pending Co");

        var result = await CreateController(fx, adminId)
            .Reject(company.Id, new RejectCompanyRequest("   "), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Deactivate_WithoutBody_Succeeds()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Active Co", CompanyStatus.Approved);

        var result = await CreateController(fx, adminId).Deactivate(company.Id, null, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(company.IsActive);
        Assert.Equal(adminId, company.DeactivatedByAdminId);
    }

    [Fact]
    public async Task GetDetail_Missing_Returns404()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);

        var result = await CreateController(fx, adminId).GetDetail(999, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task GetDetail_Found_ReturnsEnvelope()
    {
        await using var fx = await TestFixture.CreateAsync();
        var adminId = await AdminSeed.AdminAsync(fx);
        var company = await AdminSeed.CompanyAsync(fx, "Detail Co");

        var result = await CreateController(fx, adminId).GetDetail(company.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var envelope = Assert.IsType<ApiResponse<CompanyDetailDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal("Detail Co", envelope.Data!.CompanyName);
    }
}
