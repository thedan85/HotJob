using System.Net;
using System.Text;
using System.Text.Json;

namespace hotjob.Tests.Support.ResumeParsing;

internal sealed class RecordingHttpHandler : HttpMessageHandler, IHttpClientFactory
{
    public int RequestCount { get; private set; }
    public string LastBody { get; private set; } = "";
    public Uri? LastUri { get; private set; }
    public string? Authorization { get; private set; }
    public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        _ => Task.FromResult(Json("{\"skills\":[{\"name\":\"C#\"}]}"));
    public static HttpResponseMessage Json(string inner, string finish = "stop") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        { choices = new[] { new { finish_reason = finish, message = new { role = "assistant", content = inner } } } }), Encoding.UTF8, "application/json")
    };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestCount++;
        LastBody = await request.Content!.ReadAsStringAsync(ct);
        LastUri = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();
        return await Respond(ct);
    }
    public HttpClient CreateClient(string name) => new(this, false) { Timeout = Timeout.InfiniteTimeSpan };
}
