using System.Security.Claims;
using hotjob.Controllers;
using hotjob.DTOs.ML;
using hotjob.Services.Common;
using hotjob.Services.ML;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace hotjob.Tests.MLTests;

/// <summary>The dev-only training trigger must disappear unless explicitly enabled in Development.</summary>
public sealed class DevMatchModelControllerTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task SeedAndTrain_WhenNotDevOrNotEnabled_ReturnsNotFound_AndDoesNotSeed(
        string environmentName,
        bool enabled)
    {
        var service = new RecordingSyntheticService();
        var controller = CreateController(service, environmentName, enabled);

        var result = await controller.SeedAndTrain(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task SeedAndTrain_WhenDevelopmentAndEnabled_RunsForCurrentAdmin()
    {
        var service = new RecordingSyntheticService();
        var controller = CreateController(service, "Development", enabled: true);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-user-id")],
                    "test"))
            }
        };

        var result = await controller.SeedAndTrain(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, service.Calls);
        Assert.Equal("admin-user-id", service.TrainedByUserId);
    }

    private static DevMatchModelController CreateController(
        ISyntheticMatchTrainingService service,
        string environmentName,
        bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevTools:SyntheticTrainingEnabled"] = enabled.ToString()
            })
            .Build();
        return new DevMatchModelController(service, new TestEnvironment(environmentName), configuration);
    }

    private sealed class RecordingSyntheticService : ISyntheticMatchTrainingService
    {
        public int Calls { get; private set; }

        public string? TrainedByUserId { get; private set; }

        public Task<ServiceResult<SyntheticMatchTrainingResultDto>> SeedAndTrainAsync(
            string? trainedByUserId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            TrainedByUserId = trainedByUserId;
            return Task.FromResult(ServiceResult<SyntheticMatchTrainingResultDto>.Ok(
                new SyntheticMatchTrainingResultDto(
                    CandidateCount: 0,
                    CandidateCreated: 0,
                    ApplicationCount: 0,
                    ApplicationCreated: 0,
                    OfferedCount: 0,
                    RejectedCount: 0,
                    WithdrawnCount: 0,
                    Training: new MatchTrainingResultDto(false, false, 0, null, null, "test"))));
        }
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "hotjob.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
