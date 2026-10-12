using System.IO;
using System.Text;
using System.Text.Json;
using hotjob.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace hotjob.Tests.CommonTests;

/// <summary>RED: unhandled exceptions on /api must come back as JSON, never the HTML error page.</summary>
public sealed class ApiExceptionHandlerTests
{
    [Theory]
    [InlineData("/api/employer/jobs", true)]
    [InlineData("/", false)]
    [InlineData("/api-healthz", false)]
    public async Task Handle_OnlyJsonReturnsApiEnvelope(string path, bool handled)
    {
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        if (!handled)
        {
            Assert.Equal(0, body.Length);
            return;
        }

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var json = JsonDocument.Parse(body).RootElement;
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("An unexpected error occurred.", json.GetProperty("errors")[0].GetString());
    }
}
