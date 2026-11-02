using HotJob.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace hotjob.Controllers;

/// <summary>Keeps automatic model-validation errors in the API envelope for opted-in controllers.</summary>
public sealed class ApiValidationEnvelopeAttribute : ActionFilterAttribute
{
    public ApiValidationEnvelopeAttribute()
    {
        // Run before MVC's automatic -2000 filter without changing other controllers.
        // Source: https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.0/src/Mvc/Mvc.Core/src/Infrastructure/ModelStateInvalidFilter.cs
        Order = -2001;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.Result is not null || context.ModelState.IsValid)
            return;

        context.Result = new BadRequestObjectResult(new ApiResponse<object>
        {
            Success = false,
            Errors = context.ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "The request is invalid."
                    : error.ErrorMessage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        });
    }
}
