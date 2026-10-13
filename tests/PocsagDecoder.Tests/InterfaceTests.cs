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

    [Fact]
    public void Fe2AlarmMatchesDataFormatV2()
    {
        var t = new WebhookTarget
        {
            Kind = TargetKinds.Fe2, Password = "geheim", Sender = "Decoder",
            Recipient = "{ric}{subric_upper}, Wache1", TitleTemplate = "B3", KeywordDescription = "Brand",
            TextTemplate = "Zeile 1\n{message}",
        };
        var json = WebhookSender.BuildFe2Alarm(t, Sample(), "Test");
        Assert.Equal("ALARM", json["type"]!.GetValue<string>());
        Assert.Equal("geheim", json["authorization"]!.GetValue<string>());
        Assert.Equal("Decoder", json["sender"]!.GetValue<string>());
        var data = json["data"]!;
        Assert.Equal("B3", data["keyword"]!.GetValue<string>());
        Assert.Equal("Brand", data["keyword_description"]!.GetValue<string>());
        Assert.Equal(new[] { "Zeile 1", "Brand Gebäude" }, data["message"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(new[] { "1234567A", "Wache1" }, data["units"]!.AsArray().Select(n => n!["address"]!.GetValue<string>()));
        Assert.Equal("1234567A-202610131405", data["externalId"]!.GetValue<string>());
        Assert.StartsWith("2026-10-13T14:05:00.000", json["timestamp"]!.GetValue<string>());
    }

    [Fact]
    public void Fe2WithoutUnitsUsesDefaultUnits()
    {
        var t = new WebhookTarget { Kind = TargetKinds.Fe2, Recipient = "" };
        Assert.Empty(WebhookSender.BuildFe2Alarm(t, Sample(), "Test")["data"]!["units"]!.AsArray());
    }

    [Theory]
    [InlineData(200, "{\"status\":\"OK\"}", true, "1234567A")]
    [InlineData(200, "{\"status\":\"NOT_OK\",\"error\":\"Kein Absender\"}", false, "Kein Absender")]
    [InlineData(409, "{\"status\":\"NOT_OK\",\"error\":\"x\"}", false, "Gültige Absender")]
    public async Task Fe2EvaluatesResponse(int code, string answer, bool ok, string expected)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        string? received = null;
        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            using (var r = new StreamReader(ctx.Request.InputStream)) received = await r.ReadToEndAsync();
            ctx.Response.StatusCode = code;
            var bytes = Encoding.UTF8.GetBytes(answer);
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });
        var t = new WebhookTarget { Kind = TargetKinds.Fe2, Url = $"http://127.0.0.1:{port}/rest/external/http/alarm/v2", Recipient = "{ric}{subric_upper}", Retries = 0 };
        var result = await new WebhookSender().SendAsync(t, Sample(), "Test");
        await server;
        Assert.Equal(ok, result.Success);
        Assert.Contains(expected, result.Detail);
        Assert.Contains("\"units\"", received);
    }

    /// <summary>Byte für Byte so, wie BosMon es im Mitschnitt gesendet hat (Netzwerkereignis, Format Erweitert; RIC durch eine Beispiel-RIC ersetzt).</summary>
    private const string BosMonCapture = "3C3F786D6C2076657273696F6E3D22312E302220656E636F64696E673D225554462D3822207374616E64616C6F6E653D22796573223F3E0D0A3C6576656E7420666F726D61743D2232223E0D0A3C6E616D653E302E3020546573743C2F6E616D653E0D0A3C74656C656772616D3E0D0A3C66756E6374696F6E3E613C2F66756E6374696F6E3E0D0A3C6D6573736167653E203C2F6D6573736167653E0D0A3C747970653E333C2F747970653E0D0A3C616464726573733E313233343536373C2F616464726573733E0D0A3C74696D657374616D703E313739313932303938323530323C2F74696D657374616D703E0D0A3C666C6167733E313034383537363C2F666C6167733E0D0A3C6465736372697074696F6E3E0D0A3C66756E6374696F6E3E0D0A3C73686F72743E416C61726D3C2F73686F72743E0D0A3C6C6F6E673E45696E7361747A3C2F6C6F6E673E0D0A3C2F66756E6374696F6E3E0D0A3C2F6465736372697074696F6E3E0D0A3C2F74656C656772616D3E0D0A3C2F6576656E743E0D0A";

    private static PocsagMessage BosMonSample() => new()
    {
        Ric = 1234567, Function = 0, Text = "", Baud = 1200, ContentType = PocsagContentType.Alpha,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1791920982502).LocalDateTime,
    };

    [Fact]
    public void BosMonEventMatchesCaptureExactly()
    {
        var xml = BosMonNetworkEvent.Build("0.0 Test", BosMonSample(), "", "Alarm", "Einsatz");
        Assert.Equal(BosMonCapture, Convert.ToHexString(Encoding.UTF8.GetBytes(xml)));
    }

    [Fact]
    public void BosMonEventEscapesTextAndOmitsEmptyDescription()
    {
        var xml = BosMonNetworkEvent.Build("A&B", Sample(), "Brand <3> \"Halle\"", null, null);
        Assert.Contains("<name>A&amp;B</name>", xml);
        Assert.Contains("<message>Brand &lt;3&gt; &quot;Halle&quot;</message>", xml);
        Assert.DoesNotContain("<description>", xml);
        Assert.NotNull(System.Xml.Linq.XDocument.Parse(xml));
    }

    [Fact]
    public async Task BosMonSendsOneEventPerConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ms = new MemoryStream();
            await client.GetStream().CopyToAsync(ms);   // endet, wenn der Sender die Verbindung schließt
            return Encoding.UTF8.GetString(ms.ToArray());
        });
        var t = new WebhookTarget { Kind = TargetKinds.BosMon, Host = "127.0.0.1", Port = port, TitleTemplate = "{rule}", TextTemplate = "{message}", Retries = 0 };
        var result = await new WebhookSender().SendAsync(t, Sample(), "Probealarm");
        Assert.True(result.Success, result.Detail);
        var xml = await received;
        listener.Stop();
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        Assert.Equal("Probealarm", doc.Root!.Element("name")!.Value);
        var tg = doc.Root.Element("telegram")!;
        Assert.Equal("1234567", tg.Element("address")!.Value);
        Assert.Equal("a", tg.Element("function")!.Value);
        Assert.Equal("Brand Gebäude", tg.Element("message")!.Value);
        Assert.Equal("3", tg.Element("type")!.Value);
    }
}
