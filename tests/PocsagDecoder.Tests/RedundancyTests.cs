using PocsagDecoder.Core.Redundancy;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.Tests;

public class RedundancyTests
{
    private static readonly DateTime T0 = new(2026, 10, 18, 12, 0, 0);

    private static MasterStatus Healthy(DateTime now) => new() { Running = true, Time = now, LastReceived = now.AddSeconds(-30) };

    [Fact]
    public async Task MasterAnswersSlave()
    {
        var seen = new List<string>();
        using var server = new MasterServer(0, "geheim 1", () => new MasterStatus
        {
            Version = "Version 9.9.9",
            Running = true,
            Recent = { new SeenMessage(DateTime.Now, 1234567, 0, "Einsatz") },
        });
        server.SlaveSeen += (peer, _) => { lock (seen) seen.Add(peer); };
        server.Start();

        var status = await RedundancyProtocol.QueryAsync("127.0.0.1", server.Port, "geheim 1", TimeSpan.FromSeconds(5));
        Assert.True(status.Running);
        Assert.Equal("Version 9.9.9", status.Version);
        Assert.Equal(1234567, Assert.Single(status.Recent).Ric);
        Assert.Single(seen);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            RedundancyProtocol.QueryAsync("127.0.0.1", server.Port, "falsch", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task UnreachableMasterThrows()
    {
        using var server = new MasterServer(0, "", () => new MasterStatus());
        server.Start();
        var port = server.Port;
        server.Dispose();
        await Assert.ThrowsAnyAsync<Exception>(() => RedundancyProtocol.QueryAsync("127.0.0.1", port, "", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void SlaveTakesOverAfterTimeoutAndReturns()
    {
        var f = new FailoverMonitor(TimeSpan.FromSeconds(20));
        Assert.False(f.Report(T0, Healthy(T0)));
        Assert.False(f.Report(T0.AddSeconds(5), null, "Zeitüberschreitung"));
        Assert.False(f.Active);
        Assert.Contains("nicht erreichbar", f.Problem);
        Assert.True(f.Report(T0.AddSeconds(25), null));
        Assert.True(f.Active);

        // erst nach zwei gesunden Antworten zurück
        Assert.False(f.Report(T0.AddSeconds(30), Healthy(T0.AddSeconds(30))));
        Assert.True(f.Active);
        Assert.True(f.Report(T0.AddSeconds(35), Healthy(T0.AddSeconds(35))));
        Assert.False(f.Active);
    }

    [Fact]
    public void StoppedMasterCountsAsFailure()
    {
        var f = new FailoverMonitor(TimeSpan.FromSeconds(10));
        f.Report(T0, new MasterStatus { Running = false, Time = T0 });
        f.Report(T0.AddSeconds(10), new MasterStatus { Running = false, Time = T0.AddSeconds(10) });
        Assert.True(f.Active);
        Assert.Contains("gestoppt", f.Problem);
    }

    [Fact]
    public void SilentMasterCountsAsFailureOnlyIfSlaveReceives()
    {
        var f = new FailoverMonitor(TimeSpan.FromSeconds(10));
        var masterTime = T0.AddHours(1); // Uhr des Masters geht anders
        var silent = new MasterStatus { Running = true, Time = masterTime, LastReceived = masterTime.AddMinutes(-20) };
        f.Report(T0, silent);
        f.Report(T0.AddSeconds(15), silent);
        Assert.False(f.Active); // Slave hat selbst nichts empfangen: vermutlich einfach Ruhe

        for (var i = 0; i < 3; i++) f.NoteLocalMessage(T0.AddSeconds(20 + i));
        f.Report(T0.AddSeconds(25), silent);
        f.Report(T0.AddSeconds(40), silent);
        Assert.True(f.Active);
        Assert.Contains("nichts empfangen", f.Problem);
    }

    [Fact]
    public void ComparesMessagesWithMaster()
    {
        var now = T0;
        var masterClock = now.AddSeconds(-90); // Uhr des Masters 90 s nach
        var status = new MasterStatus
        {
            Time = masterClock,
            Recent = { new SeenMessage(masterClock.AddSeconds(-5), 1234567, 0, "Brand Wohnhaus Musterstr. 1") },
        };
        Assert.True(FailoverMonitor.MasterHas(status, now, 1234567, 0, "Brand Wohnhaus Musterstr. 1", now.AddSeconds(-6)));
        Assert.True(FailoverMonitor.MasterHas(status, now, 1234567, 0, "Brand Wohnhaus Musterstr.!1", now.AddSeconds(-6))); // Bitfehler
        Assert.False(FailoverMonitor.MasterHas(status, now, 1234567, 1, "Brand Wohnhaus Musterstr. 1", now)); // andere SubRIC
        Assert.False(FailoverMonitor.MasterHas(status, now, 1234568, 0, "Brand Wohnhaus Musterstr. 1", now));
        Assert.False(FailoverMonitor.MasterHas(status, now, 1234567, 0, "Probealarm", now));
    }

    [Fact]
    public void OldSettingsHaveRedundancyOff()
    {
        var s = new AppSettings();
        Assert.Equal(RedundancyModes.Off, s.RedundancyMode);
        Assert.True(s.FailoverSendMissed);
    }
}
