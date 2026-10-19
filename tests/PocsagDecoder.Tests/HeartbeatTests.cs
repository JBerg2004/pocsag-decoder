using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.Tests;

public class HeartbeatTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 14, 30, 0);

    [Fact]
    public void FirstHeartbeatIsDueImmediately()
    {
        Assert.True(Heartbeat.IsDue(null, Now, 60));
        Assert.False(Heartbeat.IsDue(Now.AddMinutes(-59), Now, 60));
        Assert.True(Heartbeat.IsDue(Now.AddMinutes(-60), Now, 60));
        Assert.False(Heartbeat.IsDue(null, Now, 0));
    }

    [Fact]
    public void TextPlaceholdersAreFilled()
    {
        var settings = new AppSettings { HeartbeatRic = 1234567, HeartbeatSubRic = "C" };
        var message = Heartbeat.CreateMessage(settings, true, Now.AddMinutes(-12), Now, "LEITSTELLE");
        Assert.Equal("Heartbeat LEITSTELLE: Empfang aktiv, letzte Meldung 14:18 (vor 12 Min.)", message.Text);
        Assert.Equal(1234567, message.Ric);
        Assert.Equal('C', message.SubRic);
    }

    [Fact]
    public void StoppedWithoutMessages()
    {
        var settings = new AppSettings { HeartbeatText = "{state} / {last}" };
        var message = Heartbeat.CreateMessage(settings, false, null, Now, "PC");
        Assert.Equal("gestoppt / keine", message.Text);
        Assert.Equal(0, message.Ric);
        Assert.Equal('A', message.SubRic);
    }

    [Fact]
    public void RuleNameReachesTemplate()
    {
        var message = Heartbeat.CreateMessage(new AppSettings(), true, Now, Now, "PC");
        var body = TemplateRenderer.Render("{rule}: {message}", message, Heartbeat.RuleName, TemplateEscaping.None);
        Assert.Equal("Heartbeat: Heartbeat PC: Empfang aktiv, letzte Meldung 14:30 (gerade eben)", body);
    }

    [Fact]
    public void OlderSettingsWithoutHeartbeatLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{\"MaxMessages\": 100}");
        try
        {
            var settings = SettingsStore.Load(path);
            Assert.False(settings.HeartbeatEnabled);
            Assert.Equal(60, settings.HeartbeatMinutes);
            Assert.Equal(Heartbeat.DefaultText, settings.HeartbeatText);
        }
        finally { File.Delete(path); }
    }
}
