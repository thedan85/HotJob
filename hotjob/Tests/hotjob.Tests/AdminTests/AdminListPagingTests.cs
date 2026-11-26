using hotjob.Services.Admin;
using hotjob.Tests.Support;

namespace hotjob.Tests.AdminTests;

public sealed class AdminListPagingTests
{
    [Fact]
    public async Task GetUsers_PagesAndReportsTotal()
    {
        await using var fx = await TestFixture.CreateAsync();
        for (var index = 0; index < 3; index++)
        {
            await fx.SeedUserAsync($"paged{index}@example.com");
        }
        var service = fx.CreateAdminService();

        var first = await service.GetUsersAsync(1, 2);
        var second = await service.GetUsersAsync(2, 2);

        Assert.True(first.Success, string.Join("; ", first.Errors));
        Assert.Equal(3, first.Data!.TotalCount);
        Assert.Equal(2, first.Data.Items.Count);
        Assert.Equal(2, first.Data.TotalPages);
        Assert.Single(second.Data!.Items);
        Assert.DoesNotContain(second.Data.Items, user => first.Data.Items.Any(other => other.UserId == user.UserId));
    }
}
