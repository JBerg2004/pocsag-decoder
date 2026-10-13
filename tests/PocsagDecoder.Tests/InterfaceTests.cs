using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.Sqlite;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.Tests;

public class InterfaceTests
{
    private static PocsagMessage Sample() => new()
    {
        Ric = 1234567,
        Function = 0,
        Text = "Brand Gebäude",
        Baud = 1200,
        ContentType = PocsagContentType.Alpha,
        Timestamp = new DateTime(2026, 10, 13, 14, 5, 0),
    };

    [Theory]
    [InlineData("mqtt", TargetKinds.Mqtt)]
    [InlineData(" FE2 ", TargetKinds.Fe2)]
    [InlineData(null, TargetKinds.Webhook)]
    [InlineData("unbekannt", TargetKinds.Webhook)]
    public void NormalizeKind(string? input, string expected) => Assert.Equal(expected, TargetKinds.Normalize(input));

    [Fact]
    public void OldSettingsWithoutKindStayWebhooks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pocsag-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "Targets": [ { "Name": "HA", "Url": "http://ha/api/webhook/x" } ] }""");
        try
        {
            var s = SettingsStore.Load(path);
            Assert.Equal(TargetKinds.Webhook, s.Targets.Single().Kind);
            s.Targets[0].Kind = TargetKinds.Mqtt;
            SettingsStore.Save(s, path);
            Assert.Equal(TargetKinds.Mqtt, SettingsStore.Load(path).Targets.Single().Kind);
            Assert.DoesNotContain("UsesHttp", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MqttConnectPacketHasCredentials()
    {
        var p = MqttPublisher.BuildConnect("id", "user", "pw");
        Assert.Equal(0x10, p[0]);
        Assert.Equal(p.Length - 2, p[1]);
        Assert.Equal("MQTT", Encoding.ASCII.GetString(p, 4, 4));
        Assert.Equal(4, p[8]);          // Protokoll 3.1.1
        Assert.Equal(0xC2, p[9]);       // Benutzer + Passwort + Clean Session
    }

    [Fact]
    public void MqttPublishPacketUsesQos1AndRetain()
    {
        var p = MqttPublisher.BuildPublish("a/b", "hi", retain: true, packetId: 7);
        Assert.Equal(0x33, p[0]);
        Assert.Equal(2 + 3 + 2 + 2, p[1]);
        Assert.Equal("a/b", Encoding.UTF8.GetString(p, 4, 3));
        Assert.Equal(7, p[8]);
        Assert.Equal("hi", Encoding.UTF8.GetString(p, 9, 2));
    }

    [Fact]
    public async Task MqttSendsToBroker()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var s = client.GetStream();
            await ReadPacket(s);                                   // CONNECT
            await s.WriteAsync(new byte[] { 0x20, 0x02, 0, 0 });   // CONNACK ok
            var publish = await ReadPacket(s);
            await s.WriteAsync(new byte[] { 0x40, 0x02, 0, 1 });   // PUBACK
            return publish;
        });

        var target = new WebhookTarget { Kind = TargetKinds.Mqtt, Host = "127.0.0.1", Port = port, Topic = "pocsag/{ric}", BodyTemplate = "{message}", Retries = 0 };
        var result = await new WebhookSender().SendAsync(target, Sample(), "Test");
        listener.Stop();

        Assert.True(result.Success, result.Detail);
        var body = await received;
        var topicLen = body[0] << 8 | body[1];
        Assert.Equal("pocsag/1234567", Encoding.UTF8.GetString(body, 2, topicLen));
        Assert.Equal("Brand Gebäude", Encoding.UTF8.GetString(body, 2 + topicLen + 2, body.Length - topicLen - 4));
    }

    private static async Task<byte[]> ReadPacket(NetworkStream s)
    {
        var b = new byte[1];
        await s.ReadExactlyAsync(b);
        int len = 0, mul = 1;
        do { await s.ReadExactlyAsync(b); len += (b[0] & 0x7F) * mul; mul *= 128; } while ((b[0] & 0x80) != 0);
        var body = new byte[len];
        await s.ReadExactlyAsync(body);
        return body;
    }

    [Fact]
    public async Task TcpOutputSendsLineToConnectedClient()
    {
        var port = FreePort();
        var target = new WebhookTarget { Kind = TargetKinds.Tcp, Host = "127.0.0.1", Port = port, TextTemplate = "{ric};{subric};{message}" };
        var sender = new WebhookSender();
        Assert.Empty(sender.SyncTcpServers([target]));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await Task.Delay(200);
        var result = await sender.SendAsync(target, Sample(), "Test");
        Assert.True(result.Success, result.Detail);

        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        Assert.Equal("1234567;A;Brand Gebäude", await reader.ReadLineAsync());
        sender.SyncTcpServers([]);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task DatabaseStoresTelegram()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pocsag-{Guid.NewGuid():N}.db");
        try
        {
            var target = new WebhookTarget { Kind = TargetKinds.Database, FilePath = path };
            var sender = new WebhookSender();
            Assert.True((await sender.SendAsync(target, Sample(), "Ereignis 1")).Success);
            Assert.True((await sender.SendAsync(target, Sample(), "Ereignis 2")).Success);

            using var db = new SqliteConnection($"Data Source={path}");
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT count(*), max(ric), max(text) FROM telegramme";
            using var r = cmd.ExecuteReader();
            r.Read();
            Assert.Equal(2, r.GetInt32(0));
            Assert.Equal("1234567", r.GetString(1));
            Assert.Equal("Brand Gebäude", r.GetString(2));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingSettingsGiveClearError()
    {
        var target = new WebhookTarget { Kind = TargetKinds.Telegram, Retries = 3 };
        var result = await new WebhookSender().SendAsync(target, Sample(), "Test");
        Assert.False(result.Success);
        Assert.Contains("Bot-Token", result.Detail);
    }

    [Fact]
    public void EveryKindHasIconColorAndCategory()
    {
        foreach (var k in TargetKinds.All)
        {
            Assert.NotEmpty(TargetKinds.Icon(k));
            Assert.StartsWith("#", TargetKinds.Color(k));
            Assert.NotEmpty(TargetKinds.Short(k));
            Assert.NotEmpty(TargetKinds.Category(k));
        }
    }

    [Fact]
    public void SummaryAndStatus()
    {
        var t = new WebhookTarget { Url = "http://homeassistant:8123/api/webhook/abc" };
        Assert.Equal("homeassistant:8123", t.Summary);
        Assert.Null(t.LastSuccess);
        t.ReportResult(false, "Timeout", new DateTime(2026, 1, 1, 12, 0, 0));
        t.ReportResult(true, "200 OK", new DateTime(2026, 1, 1, 12, 0, 5));
        Assert.True(t.LastSuccess);
        Assert.Equal("12:00:05 · 200 OK", t.LastResult);
        Assert.Contains("1 erfolgreich, 1 fehlgeschlagen", t.Statistics);
        Assert.DoesNotContain("LastResult", System.Text.Json.JsonSerializer.Serialize(t));
    }
}
