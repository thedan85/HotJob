using System.Net;
using hotjob.DTOs.Authentication;
using hotjob.Models.Users;
using hotjob.Tests.ApiClients;

namespace hotjob.Tests;

public sealed class AccountCompanyApiClientTests
{
    [Fact]
    public async Task GetProfile_HitsProfileRoute()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, new ProfileResponse(
            "user-1", "user@example.com", true, true, false,
            new CandidateProfileDto { FullName = "Cand" }, null));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Account().GetProfileAsync();

        Assert.True(result.Success);
        Assert.Equal("user@example.com", result.Data!.Email);
        Assert.Equal("/api/account/profile", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateProfile_PutsProfileRoute()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, new ProfileResponse(
            "user-1", "user@example.com", true, true, false,
            new CandidateProfileDto { FullName = "Cand" }, null));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Account().UpdateProfileAsync(new UpdateProfileRequest { FullName = "Cand" });

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/account/profile", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Register_PostsAnonymously_WithoutBearer()
    {
        var handler = new QueueHandler();
        handler.EnqueueRaw(HttpStatusCode.Created,
            """{"userId":"u","email":"c@example.com","hasCandidateProfile":true,"hasEmployerProfile":false,"companyProfileId":null}""");
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Account().RegisterAsync(new RegisterAccountRequest
        {
            Email = "c@example.com",
            Password = "Str0ng!Pass",
            FullName = "Cand",
            AccountType = AccountType.Candidate
        });

        Assert.True(result.Success);
        Assert.Equal(HttpStatusCode.Created, result.Status);
        var request = scope.Handler.Requests[0];
        Assert.Equal("/api/account/register", request.RequestUri!.AbsolutePath);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task CompanyPage_GetsCompanyRoute()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(new hotjob.DTOs.Employer.CompanyPageResponse
        {
            Company = new hotjob.DTOs.Employer.CompanyDetailResponse { Id = 1, CompanyName = "Co" },
            Members = [],
            IsOwner = true
        }));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Company().GetPageAsync();

        Assert.True(result.Success);
        Assert.True(result.Data!.IsOwner);
        Assert.Equal("/api/employer/company", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CompanyUpdate_PutsCompanyRoute()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(new hotjob.DTOs.Employer.CompanyDetailResponse
        {
            Id = 1,
            CompanyName = "Co"
        }));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Company().UpdateAsync(new hotjob.DTOs.Employer.UpdateCompanyRequest());

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/company", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CompanyApprove_PostsApproveRoute()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ApiStubs.Envelope(new hotjob.DTOs.Employer.CompanyMemberDto
        {
            Id = 3,
            FullName = "HR",
            MembershipStatus = MembershipStatus.Active
        }));
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Company().ApproveMemberAsync(3);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, scope.Handler.Requests[0].Method);
        Assert.Equal("/api/employer/company/members/3/approve", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CompanyReject_PostsRejectRoute_PassesConflictThrough()
    {
        var handler = new QueueHandler();
        handler.EnqueueJson(HttpStatusCode.Conflict,
            new HotJob.Dtos.ApiResponse<hotjob.DTOs.Employer.CompanyMemberDto>
            {
                Success = false,
                Errors = ["Only pending members can be approved/rejected."]
            });
        var scope = ApiStubs.Scope(handler);

        var result = await scope.Company().RejectMemberAsync(4);

        Assert.False(result.Success);
        Assert.Equal(HttpStatusCode.Conflict, result.Status);
        Assert.Equal(["Only pending members can be approved/rejected."], result.Errors);
        Assert.Equal("/api/employer/company/members/4/reject", scope.Handler.Requests[0].RequestUri!.AbsolutePath);
    }
}
