using hotjob.Services.Candidate.ResumeParsing;

namespace hotjob.Tests.CandidateTests.ResumeParsing;

public sealed class ResumeContactRedactorTests
{
    [Fact]
    public void ContactsRemoved_BeforeTruncation_WithoutRemovingDates()
    {
        var text = """
            Skills: C#
            fake.person@example.test +84 912 345 678
            https://example.test/person www.example.com linkedin.com/in/fake
            Address: 123 Example Street
            Địa chỉ: 42 Đường Ví Dụ
            2021-2024
            09/2021 – 06/2023
            Sep 2021 - Present
            Tháng 9/2021 - Hiện tại
            """ + new string('x', 29_900) + " late@example.test";
        var result = new ResumeContactRedactor(TimeProvider.System).Redact(text);
        Assert.True(result.Success);
        Assert.True(result.Data!.Length <= 30_000);
        foreach (var secret in new[] { "fake.person@", "+84 912", "https://", "www.", "linkedin.com", "123 Example", "42 Đường", "late@" })
            Assert.DoesNotContain(secret, result.Data);
        Assert.Contains("2021-2024", result.Data);
        Assert.Contains("09/2021 – 06/2023", result.Data);
    }
    [Fact]
    public void OnlyContactInformation_IsNotSent()
    {
        Assert.False(new ResumeContactRedactor(TimeProvider.System).Redact("fake@example.test").Success);
    }
}
