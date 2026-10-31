using System.Net;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Tests;

public class SendCancellationTests
{
    [Theory]
    [InlineData(TargetKinds.Webhook)]
    [InlineData(TargetKinds.Telegram)]
    public async Task CancellationDuringRetryReturnsFailureAndClearsPendingCount(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new CancelAfterResponse(cancellation));
        var sender = new WebhookSender(http);
        var target = new WebhookTarget
        {
            Kind = kind, Url = "http://example.test/hook", Retries = 2,
            Token = "test", Recipient = "123",
        };
        var result = await sender.SendAsync(target, new PocsagMessage { Text = "Test" }, "Test", cancellation.Token);
        Assert.False(result.Success);
        Assert.Equal("abgebrochen", result.Detail);
        Assert.Equal(0, sender.PendingCount);
    }

    private sealed class CancelAfterResponse(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            cancellation.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server error"),
            });
        }
    }
}
