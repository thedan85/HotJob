namespace hotjob.Services.Email.Templates;

internal static class EmailCopy
{
    public static string Greeting(string? recipientName)
    {
        if (string.IsNullOrWhiteSpace(recipientName))
            return "Hi there,";

        var name = recipientName.Trim();
        return $"Hi {name},";
    }
}
