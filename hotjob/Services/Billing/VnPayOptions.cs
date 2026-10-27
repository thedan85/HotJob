namespace hotjob.Services.Billing;

/// <summary>Strongly-typed <c>VnPay</c> configuration section.</summary>
public sealed class VnPayOptions
{
    public const string SectionName = "VnPay";

    /// <summary>Merchant code issued by VNPay. Empty means online payment is disabled.</summary>
    public string TmnCode { get; set; } = string.Empty;

    /// <summary>HMAC-SHA512 secret issued by VNPay. Never committed.</summary>
    public string HashSecret { get; set; } = string.Empty;

    /// <summary>Payment gateway base URL.</summary>
    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    /// <summary>Absolute URL the browser returns to after paying.</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>Public URL registered with VNPay for server-to-server IPN calls.</summary>
    public string IpnUrl { get; set; } = string.Empty;

    public string Version { get; set; } = "2.1.0";

    public string Locale { get; set; } = "vn";

    /// <summary>Whether a merchant code and secret are both present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TmnCode) && !string.IsNullOrWhiteSpace(HashSecret);
}
