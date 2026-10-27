using System.Globalization;
using hotjob.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace hotjob.Controllers;

/// <summary>VNPay server-to-server IPN and browser return endpoints.</summary>
[ApiController]
[Route("api/vnpay")]
[AllowAnonymous]
public sealed class VnPayController(IBillingService billingService) : ControllerBase
{
    /// <summary>Server-to-server confirmation. Always replies 200 with a VNPay response code.</summary>
    [HttpGet("ipn")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ipn(CancellationToken cancellationToken)
    {
        var result = await billingService.ProcessCallbackAsync(ToDictionary(), cancellationToken);
        return Ok(new { RspCode = result.RspCode, Message = result.Message });
    }

    /// <summary>Browser return. Processes idempotently, then redirects to the result page.</summary>
    [HttpGet("return")]
    public async Task<IActionResult> Return(CancellationToken cancellationToken)
    {
        var result = await billingService.ProcessCallbackAsync(ToDictionary(), cancellationToken);

        var status = result.RspCode switch
        {
            "00" when result.Granted => "success",
            "00" => "processed",
            "02" => "processed",
            _ => "failed"
        };

        var url = $"/payment/result?status={status}";
        if (!string.IsNullOrWhiteSpace(result.Purpose))
        {
            url += $"&purpose={Uri.EscapeDataString(result.Purpose)}";
        }

        if (result.Amount is { } amount)
        {
            url += $"&amount={amount.ToString(CultureInfo.InvariantCulture)}";
        }

        if (!string.IsNullOrWhiteSpace(result.TxnRef))
        {
            url += $"&txnRef={Uri.EscapeDataString(result.TxnRef)}";
        }

        return Redirect(url);
    }

    private IReadOnlyDictionary<string, string?> ToDictionary() =>
        Request.Query.ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value.FirstOrDefault(),
            StringComparer.OrdinalIgnoreCase);
}
