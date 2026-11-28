using System.Net;
using System.Text;

namespace hotjob.Services.Email.Templates;

/// <summary>
/// Renders the shared HotJob transactional-email shell using table layouts and
/// inline CSS only. No scripts, external stylesheets, remote fonts, or images.
/// </summary>
public static class EmailLayout
{
    private const string FontStack = "'Segoe UI', Arial, Helvetica, sans-serif";
    private const string Primary = "#0E3B2E";
    private const string Accent = "#E4572E";
    private const string Ink = "#15171A";
    private const string InkSoft = "#2A2E33";
    private const string Muted = "#62676D";
    private const string PageBackground = "#F7F6F2";
    private const string Surface = "#FFFFFF";
    private const string Border = "#E3E0D6";
    private const string Tint = "#EAF3EE";

    public static string Render(EmailLayoutContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(content.Heading))
            throw new ArgumentException("An email heading is required.", nameof(content));
        if (string.IsNullOrWhiteSpace(content.ActionLabel))
            throw new ArgumentException("An action label is required.", nameof(content));

        var actionUrl = EmailActionUrl.Encode(content.ActionUrl);
        var paragraphs = RenderParagraphs(content.Paragraphs);
        var expiry = RenderExpiry(content.ExpiryText);
        var security = RenderSecurity(content.SecurityNotice);
        var footerNote = string.IsNullOrWhiteSpace(content.FooterNote)
            ? "You received this email because of activity on your HotJob account."
            : content.FooterNote;
        var year = DateTime.UtcNow.Year;

        return $$"""
            <!doctype html>
            <html lang="en" xmlns="http://www.w3.org/1999/xhtml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:v="urn:schemas-microsoft-com:vml">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta http-equiv="X-UA-Compatible" content="IE=edge">
              <meta name="x-apple-disable-message-reformatting">
              <meta name="format-detection" content="telephone=no,address=no,email=no,date=no,url=no">
              <meta name="color-scheme" content="light">
              <meta name="supported-color-schemes" content="light">
              <title>{{Encode(content.Heading)}}</title>
              <!--[if mso]>
              <xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml>
              <![endif]-->
            </head>
            <body style="margin:0;padding:0;width:100%;background-color:{{PageBackground}};-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;">
              <div style="display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;color:{{PageBackground}};">
                {{Encode(content.Preheader)}}&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;
              </div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{{PageBackground}};border-collapse:collapse;">
                <tr>
                  <td align="center" style="padding:28px 12px;">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:600px;border-collapse:collapse;">
                      <tr>
                        <td style="padding:0 4px 16px 4px;font-family:{{FontStack}};font-size:26px;line-height:30px;font-weight:800;letter-spacing:-0.04em;color:{{Ink}};">
                          hotjob<span style="color:{{Accent}};">.</span>
                        </td>
                      </tr>
                      <tr>
                        <td style="background-color:{{Surface}};border:1px solid {{Border}};border-top:4px solid {{Primary}};border-radius:16px;padding:34px 30px 32px 30px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
                            <tr>
                              <td style="font-family:{{FontStack}};font-size:12px;line-height:16px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:{{Primary}};">
                                {{Encode(content.Eyebrow)}}
                              </td>
                            </tr>
                            <tr>
                              <td style="padding-top:10px;font-family:{{FontStack}};font-size:26px;line-height:34px;font-weight:700;color:{{Ink}};">
                                {{Encode(content.Heading)}}
                              </td>
                            </tr>
                            <tr>
                              <td style="padding-top:18px;">
                                {{paragraphs}}
                                {{expiry}}
                                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:26px 0 0;border-collapse:collapse;">
                                  <tr>
                                    <td align="center" bgcolor="{{Primary}}" style="border-radius:10px;">
                                      <a href="{{actionUrl}}" target="_blank" rel="noopener noreferrer" style="display:inline-block;padding:14px 28px;font-family:{{FontStack}};font-size:16px;line-height:20px;font-weight:600;color:#FFFFFF;text-decoration:none;border-radius:10px;">
                                        {{Encode(content.ActionLabel)}}
                                      </a>
                                    </td>
                                  </tr>
                                </table>
                                <p style="margin:26px 0 6px;font-family:{{FontStack}};font-size:13px;line-height:20px;color:{{Muted}};">
                                  If the button doesn't work, copy and paste this link into your browser:
                                </p>
                                <p style="margin:0;font-family:{{FontStack}};font-size:13px;line-height:20px;color:{{Primary}};word-break:break-all;overflow-wrap:anywhere;">
                                  <a href="{{actionUrl}}" style="color:{{Primary}};text-decoration:underline;">{{actionUrl}}</a>
                                </p>
                                {{security}}
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="padding:20px 8px 0 8px;font-family:{{FontStack}};font-size:12px;line-height:19px;color:{{Muted}};">
                          <p style="margin:0 0 6px;font-size:15px;line-height:20px;font-weight:800;letter-spacing:-0.03em;color:{{Ink}};">
                            hotjob<span style="color:{{Accent}};">.</span>
                          </p>
                          <p style="margin:0 0 6px;">Find work that fits.</p>
                          <p style="margin:0;">{{Encode(footerNote)}}</p>
                          <p style="margin:6px 0 0;">&copy; {{year}} HotJob. All rights reserved.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string RenderParagraphs(IReadOnlyList<string> paragraphs)
    {
        if (paragraphs.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var isLast = index == paragraphs.Count - 1;
            var isGreeting = index == 0;
            builder.Append("<p style=\"margin:0 0 ")
                .Append(isLast ? "0" : "16px")
                .Append(";font-family:").Append(FontStack)
                .Append(";font-size:16px;line-height:26px;font-weight:")
                .Append(isGreeting ? "600" : "400")
                .Append(";color:").Append(isGreeting ? Ink : InkSoft)
                .Append(";\">")
                .Append(Encode(paragraphs[index]))
                .Append("</p>");
        }

        return builder.ToString();
    }

    private static string RenderExpiry(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return $$"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:22px 0 0;border-collapse:collapse;">
              <tr>
                <td style="background-color:{{Tint}};border-left:3px solid {{Primary}};border-radius:8px;padding:14px 16px;font-family:{{FontStack}};font-size:14px;line-height:22px;color:{{InkSoft}};">
                  {{Encode(text)}}
                </td>
              </tr>
            </table>
            """;
    }

    private static string RenderSecurity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return $$"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:28px 0 0;border-top:1px solid {{Border}};border-collapse:collapse;">
              <tr>
                <td style="padding-top:18px;font-family:{{FontStack}};font-size:13px;line-height:21px;color:{{Muted}};">
                  {{Encode(text)}}
                </td>
              </tr>
            </table>
            """;
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
