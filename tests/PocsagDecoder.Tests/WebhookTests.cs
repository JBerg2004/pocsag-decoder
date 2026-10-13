using System.Net;
using System.Text.Json;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using Xunit;

namespace PocsagDecoder.Tests;

public class WebhookTests
{
    private static readonly PocsagMessage Sample = new()
    {
        Ric = 123456,
        Function = 2,
        Text = "Einsatz \"Brand\" Hauptstr. 1\\2",
        Baud = 1200,
        ContentType = PocsagContentType.Alpha,
        Timestamp = new DateTime(2026, 10, 13, 14, 5, 0),
    };

    [Fact]
    public void DefaultJsonTemplateProducesValidJson()
    {
        var body = TemplateRenderer.Render(WebhookTarget.DefaultJsonTemplate, Sample, "Regel ä", TemplateEscaping.Json);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("0123456", doc.RootElement.GetProperty("ric").GetString());
        Assert.Equal("C", doc.RootElement.GetProperty("subric").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("function").GetInt32());
        Assert.Equal(Sample.Text, doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("Regel ä", doc.RootElement.GetProperty("rule").GetString());
    }

    [Fact]
    public void UrlPlaceholdersAreEncoded()
    {
        var target = new WebhookTarget { Method = "GET", Url = "http://host/api?ric={ric}&sub={subric}&text={message}" };
        using var req = new WebhookSender().BuildRequest(target, Sample, "r");
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Null(req.Content);
        Assert.Contains("ric=0123456&sub=C&text=Einsatz%20%22Brand%22", req.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SendsPostWithHeadersAndRetriesOnServerError()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var sender = new WebhookSender(new HttpClient(handler));
        var target = new WebhookTarget { Url = "http://host/hook", Headers = "Authorization: Bearer abc\nX-Ric: {ric}", Retries = 1 };

        var result = await sender.SendAsync(target, Sample, "r");

        Assert.True(result.Success);
        Assert.Equal(2, result.Attempts);
        var last = handler.Requests.Last();
        Assert.Equal("Bearer abc", last.Headers["Authorization"]);
        Assert.Equal("0123456", last.Headers["X-Ric"]);
        Assert.Contains("\"subric\": \"C\"", last.Body);
        Assert.StartsWith("application/json", last.ContentType);
    }

    [Fact]
    public async Task DoesNotRetryClientErrors()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var result = await new WebhookSender(new HttpClient(handler)).SendAsync(new WebhookTarget { Url = "http://h/", Retries = 3 }, Sample, "r");
        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal(1, result.Attempts);
    }

    private sealed record Captured(Dictionary<string, string> Headers, string Body, string ContentType);

    private sealed class FakeHandler(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        private int _i;
        public List<Captured> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new Captured(
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct),
                request.Content?.Headers.ContentType?.ToString() ?? ""));
            return new HttpResponseMessage(codes[Math.Min(_i++, codes.Length - 1)]);
        }
    }
}
