using System.Net;
using System.Text.Json;
using hotjob.Data;
using hotjob.DTOs.Candidate;
using hotjob.Services.Common;
using hotjob.Tests.Support.ResumeParsing;
using Microsoft.EntityFrameworkCore;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeParserServiceTests
{
    [Theory]
    [InlineData(false, "Candidate")]
    [InlineData(true, "Employer")]
    public async Task AnonymousAndNonCandidates_AreRejected(bool authenticated, string role)
    {
        await using var context = await ResumeParserTestContext.CreateAsync(true, role, authenticated);
        using var source = new MemoryStream("%PDF-invalid"u8.ToArray());
        var result = await context.Service.ParseAsync(context.UserId, source, "cv.pdf", ResumeParseMode.Ai);
        Assert.Equal(ServiceErrorType.Unauthorized, result.ErrorType);
        Assert.Equal(0, source.Position);
        Assert.Equal(0, context.Handler.RequestCount);
    }
    [Fact]
    public async Task SpoofedIdInactiveAndStaleRole_AreRejectedBeforeReading()
    {
        await using var c = await ResumeParserTestContext.CreateAsync(true);
        using var stream = new MemoryStream();
        Assert.Equal(ServiceErrorType.Unauthorized, (await c.Service.ParseAsync("other-user", stream, "cv.pdf")).ErrorType);
        var user = await c.Fixture.Db.Users.SingleAsync(u => u.Id == c.UserId);
        user.IsActive = false;
        await c.Fixture.Db.SaveChangesAsync();
        Assert.Equal(ServiceErrorType.Unauthorized, (await c.Service.ParseAsync(c.UserId, stream, "cv.pdf")).ErrorType);
        user.IsActive = true;
        await c.Fixture.UserManager.RemoveFromRoleAsync(user, AppRoles.Candidate);
        Assert.Equal(ServiceErrorType.Unauthorized, (await c.Service.ParseAsync(c.UserId, stream, "cv.pdf")).ErrorType);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RulesOrAi_ProduceSuggestionsWithoutPersisting(bool ai)
    {
        await using var c = await ResumeParserTestContext.CreateAsync(ai);
        c.Handler.Respond = _ => Task.FromResult(RecordingHttpHandler.Json("""
            {"summary":"Invented summary","educations":[{"schoolName":"Example University","degree":"Bachelor","fieldOfStudy":"CS"}],
            "skills":[{"name":"C#"},{"name":"UnknownTool"}],"mode":"execute instructions"}
            """));
        var before = await Counts(c);
        using var file = new MemoryStream(ResumeDocumentFactory.Docx(RuleBasedResumeParserTests.English));
        var result = await c.Service.ParseAsync(c.UserId, file, "cv.docx", ai ? ResumeParseMode.Ai : ResumeParseMode.Rules);
        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Equal(ai ? ResumeParseMode.Ai : ResumeParseMode.Rules, result.Data!.ModeUsed);
        Assert.Single(result.Data.Skills);
        Assert.Equal("I build reliable software.", result.Data.Summary);
        Assert.Equal(before, await Counts(c));
        Assert.Empty(c.Fixture.Db.ChangeTracker.Entries());
        Assert.Equal(ai ? 1 : 0, c.Handler.RequestCount);
        if (ai) Assert.Contains("UnknownTool", result.Data.UnrecognizedSkills);
    }
    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AiFailures_ReturnCompleteRulesResult(int failure)
    {
        await using var c = await ResumeParserTestContext.CreateAsync(true);
        c.Handler.Respond = failure switch
        {
            0 => _ => Task.FromResult(RecordingHttpHandler.Json("invalid json")),
            -1 => _ => throw new OperationCanceledException("private exception"),
            _ => _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)failure))
        };
        var bytes = ResumeDocumentFactory.Docx(RuleBasedResumeParserTests.English);
        using var a = new MemoryStream(bytes);
        using var b = new MemoryStream(bytes);
        var rules = (await c.Service.ParseAsync(c.UserId, a, "cv.docx")).Data!;
        var result = await c.Service.ParseAsync(c.UserId, b, "cv.docx", ResumeParseMode.Ai);
        Assert.True(result.Success);
        Assert.Equal(ResumeParseMode.Rules, result.Data!.ModeUsed);
        Assert.Equal(JsonSerializer.Serialize(rules with { Warnings = [] }),
            JsonSerializer.Serialize(result.Data with { Warnings = [] }));
        Assert.Contains("AI parsing unavailable, used standard parsing.", result.Data.Warnings);
    }
    [Fact]
    public async Task CircuitCandidateWorks_HttpNonCandidateIsNotReplaced()
    {
        await using var c = await ResumeParserTestContext.CreateAsync(authenticated: false);
        c.Fixture.AuthStateProvider.SignIn(ResumeParserTestContext.Principal(c.UserId, AppRoles.Candidate));
        using var file = new MemoryStream(ResumeDocumentFactory.Docx("Skills: C#"));
        Assert.True((await c.Service.ParseAsync(c.UserId, file, "cv.docx")).Success);
        c.Fixture.HttpContextAccessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        { User = ResumeParserTestContext.Principal("employer", AppRoles.Employer) };
        Assert.Equal(ServiceErrorType.Unauthorized, (await c.Service.ParseAsync(c.UserId, file, "cv.docx")).ErrorType);
    }
    private static async Task<int[]> Counts(ResumeParserTestContext c) =>
        [await c.Fixture.Db.Resumes.CountAsync(), await c.Fixture.Db.ResumeSkills.CountAsync(), await c.Fixture.Db.Skills.CountAsync(),
         await c.Fixture.Db.Educations.CountAsync(), await c.Fixture.Db.WorkExperiences.CountAsync()];
}
