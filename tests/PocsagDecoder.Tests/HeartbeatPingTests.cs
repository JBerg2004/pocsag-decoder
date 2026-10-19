using System.Net;
using System.Text.Json;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.Tests;

public class HeartbeatPingTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 14, 30, 0);

    private static HeartbeatState State(string stick = HeartbeatState.StickOk) => new()
    {
        Pc = "LEITSTELLE",
        Stick = stick,
        LastMessageAt = Now.AddMinutes(-12),
        QueueLength = 3,
        Time = Now,
    };

    [Fact]
    public void JsonContainsStickLastMessageAndQueue()
    {
        using var doc = JsonDocument.Parse(HeartbeatPing.BuildJson(State()));
        var root = doc.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("LEITSTELLE", root.GetProperty("pc").GetString());
        Assert.Equal("ok", root.GetProperty("stick").GetString());
        Assert.Equal("2026-10-19T14:18:00", root.GetProperty("lastMessage").GetString());
        Assert.Equal(12, root.GetProperty("lastMessageMinutesAgo").GetInt32());
        Assert.Equal(3, root.GetProperty("queueLength").GetInt32());
    }

    [Fact]
    public void JsonWithoutMessagesHasNullLastMessage()
    {
        using var doc = JsonDocument.Parse(HeartbeatPing.BuildJson(State(HeartbeatState.StickError) with { LastMessageAt = null }));
        Assert.Equal("fehler", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("lastMessage").ValueKind);
    }

    [Fact]
    public async Task HealthyPostsToMainUrl()
    {
        var handler = new Recorder(HttpStatusCode.OK);
        var result = await new HeartbeatPing(new HttpClient(handler)).SendAsync("http://hc/abc", "http://hc/abc/fail", State());
        Assert.True(result.Success);
        Assert.False(result.UsedFailUrl);
        Assert.Equal(["http://hc/abc"], handler.Urls);
        Assert.Equal(HttpMethod.Post, handler.Methods[0]);
        Assert.StartsWith("application/json", handler.ContentTypes[0]);
    }

    [Fact]
    public async Task StickErrorUsesFailUrl()
    {
        var handler = new Recorder(HttpStatusCode.OK);
        var result = await new HeartbeatPing(new HttpClient(handler)).SendAsync("http://hc/abc", "http://hc/abc/fail", State(HeartbeatState.StickError));
        Assert.True(result.Success);
        Assert.True(result.UsedFailUrl);
        Assert.Equal(["http://hc/abc/fail"], handler.Urls);
    }

    [Fact]
    public async Task StickErrorWithoutFailUrlGoesToMainUrl()
    {
        var handler = new Recorder(HttpStatusCode.OK);
        await new HeartbeatPing(new HttpClient(handler)).SendAsync("http://hc/abc", "", State(HeartbeatState.StickStopped));
        Assert.Equal(["http://hc/abc"], handler.Urls);
    }

    [Fact]
    public async Task FailedMainUrlAlsoCallsFailUrl()
    {
        var handler = new Recorder(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var result = await new HeartbeatPing(new HttpClient(handler)).SendAsync("http://hc/abc", "http://other/fail", State());
        Assert.False(result.Success);
        Assert.True(result.UsedFailUrl);
        Assert.Equal(["http://hc/abc", "http://other/fail"], handler.Urls);
        Assert.Contains("500", result.Detail);
    }

    [Fact]
    public async Task InvalidUrlIsReportedNotThrown()
    {
        var result = await new HeartbeatPing(new HttpClient(new Recorder(HttpStatusCode.OK))).SendAsync("kein url", "", State());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task PendingCountCoversRunningSends()
    {
        var gate = new TaskCompletionSource();
        var sender = new WebhookSender(new HttpClient(new Blocking(gate.Task)));
        var send = sender.SendAsync(new WebhookTarget { Url = "http://h/" }, new PocsagMessage { Ric = 1, Text = "x" }, "r");
        Assert.Equal(1, sender.PendingCount);
        gate.SetResult();
        await send;
        Assert.Equal(0, sender.PendingCount);
    }

    [Fact]
    public void OlderSettingsGetUrlHeartbeatDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{\"MaxMessages\": 100}");
        try
        {
            var settings = SettingsStore.Load(path);
            Assert.False(settings.HeartbeatUrlEnabled);
            Assert.Equal(5, settings.HeartbeatUrlMinutes);
            Assert.True(settings.PreventSleep);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SleepPreventionSetsAndReleasesFlags()
    {
        var calls = new List<uint>();
        var sleep = new SleepPrevention(f => { calls.Add(f); return 1; });
        Assert.True(sleep.Set(true));
        Assert.True(sleep.Set(true)); // schon aktiv: kein zweiter Aufruf
        Assert.True(sleep.Active);
        Assert.True(sleep.Set(false));
        Assert.False(sleep.Active);
        Assert.Equal([SleepPrevention.EsContinuous | SleepPrevention.EsSystemRequired, SleepPrevention.EsContinuous], calls);
    }

    [Fact]
    public void SleepPreventionReportsRefusal()
    {
        var sleep = new SleepPrevention(_ => 0);
        Assert.False(sleep.Set(true));
        Assert.False(sleep.Active);
    }

    private sealed class Recorder(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        private int _i;
        public List<string> Urls { get; } = new();
        public List<HttpMethod> Methods { get; } = new();
        public List<string> ContentTypes { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            Methods.Add(request.Method);
            ContentTypes.Add(request.Content?.Headers.ContentType?.ToString() ?? "");
            return Task.FromResult(new HttpResponseMessage(codes[Math.Min(_i++, codes.Length - 1)]));
        }
    }

    private sealed class Blocking(Task gate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await gate;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
