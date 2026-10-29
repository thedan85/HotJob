using System.Net;
using hotjob.Models.Billing;
using hotjob.Services.Billing;
using Microsoft.Extensions.Options;

namespace hotjob.Tests.BillingTests;

/// <summary>VNPay URL building and callback signature rules (contract D14).</summary>
public sealed class VnPayServiceTests
{
    private const string Secret = "test-hash-secret";

    private static VnPayService Service(string secret = Secret) => new(Options.Create(new VnPayOptions
    {
        TmnCode = "TESTCODE",
        HashSecret = secret,
        BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
        ReturnUrl = "https://localhost:5001/api/vnpay/return"
    }));

    private static Payment Payment(decimal amount = 500_000) => new()
    {
        PayerUserId = "user-1",
        Purpose = PaymentPurpose.EmployerPlan,
        Amount = amount,
        VnpTxnRef = "HJ260101120000abcdef"
    };

    private static Dictionary<string, string?> ParseQuery(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => WebUtility.UrlDecode(part[0]),
                part => (string?)WebUtility.UrlDecode(part.Length > 1 ? part[1] : string.Empty));
    }

    [Fact]
    public void CreatePaymentUrl_SignsEveryRequiredParameter_AndAmountTimes100()
    {
        var url = Service().CreatePaymentUrl(
            Payment(),
            "203.0.113.7",
            new DateTimeOffset(2026, 10, 27, 3, 4, 5, TimeSpan.Zero));

        var query = ParseQuery(url);

        Assert.Equal("2.1.0", query["vnp_Version"]);
        Assert.Equal("pay", query["vnp_Command"]);
        Assert.Equal("TESTCODE", query["vnp_TmnCode"]);
        Assert.Equal("50000000", query["vnp_Amount"]);
        Assert.Equal("VND", query["vnp_CurrCode"]);
        Assert.Equal("HJ260101120000abcdef", query["vnp_TxnRef"]);
        Assert.Equal("203.0.113.7", query["vnp_IpAddr"]);
        Assert.Equal("20261027100405", query["vnp_CreateDate"]);
        Assert.Equal("20261027101905", query["vnp_ExpireDate"]);
        Assert.Equal("vn", query["vnp_Locale"]);
        Assert.False(string.IsNullOrWhiteSpace(query["vnp_SecureHash"]));
    }

    [Fact]
    public void ValidateCallback_AcceptsUrlBuiltByThisService()
    {
        var service = Service();
        var url = service.CreatePaymentUrl(Payment(), "127.0.0.1", DateTimeOffset.UtcNow);
        var query = ParseQuery(url);
        query["vnp_ResponseCode"] = "00";
        query["vnp_TransactionStatus"] = "00";
        query["vnp_SecureHash"] = VnPayService.ComputeHash(VnPayService.BuildSignedPayload(query), Secret);

        var result = service.ValidateCallback(query);

        Assert.True(result.IsValidSignature);
        Assert.Equal("HJ260101120000abcdef", result.TxnRef);
        Assert.Equal(50_000_000, result.AmountInCents);
    }

    [Fact]
    public void ValidateCallback_RejectsTamperedAmount()
    {
        var service = Service();
        var query = new Dictionary<string, string?>
        {
            ["vnp_TxnRef"] = "TX1",
            ["vnp_Amount"] = "100",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionStatus"] = "00"
        };
        query["vnp_SecureHash"] = VnPayService.ComputeHash(VnPayService.BuildSignedPayload(query), Secret);
        query["vnp_Amount"] = "99999999";

        Assert.False(service.ValidateCallback(query).IsValidSignature);
    }

    [Fact]
    public void ValidateCallback_RejectsMissingHashAndUnconfiguredMerchant()
    {
        var query = new Dictionary<string, string?>
        {
            ["vnp_TxnRef"] = "TX1",
            ["vnp_Amount"] = "100"
        };

        Assert.False(Service().ValidateCallback(query).IsValidSignature);

        var unconfigured = new VnPayService(Options.Create(new VnPayOptions()));
        Assert.False(unconfigured.IsConfigured);
        Assert.False(unconfigured.ValidateCallback(query).IsValidSignature);
    }

    [Fact]
    public void BuildSignedPayload_SortsByKey_AndSkipsHashAndEmptyValues()
    {
        var payload = VnPayService.BuildSignedPayload(new Dictionary<string, string?>
        {
            ["vnp_B"] = "two words",
            ["vnp_A"] = "1",
            ["vnp_SecureHash"] = "ignored",
            ["vnp_SecureHashType"] = "ignored",
            ["vnp_Empty"] = "",
            ["other"] = "ignored"
        });

        Assert.Equal("vnp_A=1&vnp_B=two+words", payload);
    }

    [Fact]
    public void ComputeHash_IsLowercaseHmacSha512()
    {
        var hash = VnPayService.ComputeHash("a=1&b=2", Secret);

        Assert.Equal(128, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.NotEqual(VnPayService.ComputeHash("a=1&b=2", "other"), hash);
    }
}
