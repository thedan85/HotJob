using HotJob.Dtos;
using hotjob.Controllers;
using hotjob.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Tests.CommonTests;

/// <summary>RED: shared controller base must map ServiceResult errors to envelope status codes.</summary>
public sealed class ApiControllerBaseTests
{
    private sealed class TestController : ApiControllerBase
    {
        public IActionResult Show<T>(ServiceResult<T> result) => FromResult(result);
        public IActionResult ShowCreated<T>(ServiceResult<T> result) => FromResultCreated(result);
    }

    private static TestController Create() => new()
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Theory]
    [InlineData(ServiceErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ServiceErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ServiceErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ServiceErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    public void FromResult_Error_MapsToExpectedStatus_WithEnvelope(
        ServiceErrorType errorType, int expectedStatus)
    {
        var controller = Create();

        var action = controller.Show(ServiceResult<string>.Fail(errorType, "boom"));

        var status = Assert.IsAssignableFrom<ObjectResult>(action);
        Assert.Equal(expectedStatus, status.StatusCode);
        var body = Assert.IsType<ApiResponse<object>>(status.Value);
        Assert.False(body.Success);
        Assert.Contains("boom", body.Errors);
    }

    [Fact]
    public void FromResult_Success_ReturnsOkEnvelope()
    {
        var controller = Create();

        var action = controller.Show(ServiceResult<string>.Ok("hi"));

        var ok = Assert.IsType<OkObjectResult>(action);
        var body = Assert.IsType<ApiResponse<string>>(ok.Value);
        Assert.True(body.Success);
        Assert.Equal("hi", body.Data);
    }

    [Fact]
    public void FromResultCreated_Success_Returns201Envelope()
    {
        var controller = Create();

        var action = controller.ShowCreated(ServiceResult<string>.Ok("hi"));

        var created = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var body = Assert.IsType<ApiResponse<string>>(created.Value);
        Assert.True(body.Success);
    }
}
