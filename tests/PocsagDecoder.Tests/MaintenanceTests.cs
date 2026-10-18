using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.Tests;

public class MaintenanceTests
{
    [Fact]
    public void ParsesHistoryLineWithQuotes()
    {
        Assert.True(MessageHistory.TryParseLine("2026-10-13 12:00:05;1200;1234567;C;2;Alpha;\"Probe; \"\"Text\"\"\";\"HA: OK\"", out var e));
        Assert.Equal(new DateTime(2026, 10, 13, 12, 0, 5), e!.Message.Timestamp);
        Assert.Equal(1234567, e.Message.Ric);
        Assert.Equal(2, e.Message.Function);
        Assert.Equal(PocsagContentType.Alpha, e.Message.ContentType);
        Assert.Equal("Probe; \"Text\"", e.Message.Text);
        Assert.Equal("HA: OK", e.Forwarding);
        Assert.False(MessageHistory.TryParseLine("Zeit;Baud;RIC;SubRIC;Funktion;Typ;Text;Weiterleitung", out _));
    }

    [Fact]
    public void ReadsHistoryFilesInRange()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pocsag-hist-{Guid.NewGuid():N}");
        try
        {
            var log = new CsvMessageLog(dir);
            foreach (var day in new[] { 10, 11, 12 })
                log.Append(new PocsagMessage { Timestamp = new DateTime(2026, 10, day, 8, 0, 0), Ric = 1000000 + day, Function = 0, ContentType = PocsagContentType.Alpha, Text = "Probe " + day }, "");
            var found = MessageHistory.Read(dir, new DateTime(2026, 10, 11), new DateTime(2026, 10, 12)).ToList();
            Assert.Equal(new[] { 1000011, 1000012 }, found.Select(f => f.Message.Ric));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static PocsagMessage Msg(int ric, string text, int second) => new()
    {
        Ric = ric, Function = 0, ContentType = PocsagContentType.Alpha, Text = text,
        Timestamp = new DateTime(2026, 10, 13, 12, 0, 0).AddSeconds(second),
    };

    [Fact]
    public void QualityComparesRepeats()
    {
        var q = new ReceptionQuality();
        for (var i = 0; i < 4; i++)
        {
            q.Add(Msg(1000 + i, "Brand Gebaeude Hauptstrasse 1", i * 200));
            q.Add(Msg(1000 + i, "Brand Gebaeude Hauptstrasse 1", i * 200 + 10));
        }
        Assert.Null(q.Percent); // erst 4 Wiederholungen
        q.Add(Msg(2000, "Brand Gebaeude Hauptstrasse 1", 2000));
        q.Add(Msg(2000, "Brand Geb#eude Hauptstr?sse 1", 2010)); // Bitfehler
        q.Add(Msg(3000, "Probealarm", 3000));
        q.Add(Msg(3000, "Ganz andere neue Meldung mit viel Text", 3010)); // neue Meldung, zählt nicht
        Assert.Equal(5, q.Samples);
        Assert.Equal(1, q.Faulty);
        Assert.Equal(80, q.Percent);
        Assert.Equal("mittel", q.LevelText);
    }

    [Theory]
    [InlineData("v1.2.0", "1.1.0", true)]
    [InlineData("1.1", "1.1.0", false)]
    [InlineData("Version 1.0.5", "1.1.0", false)]
    [InlineData("v2.0.0-beta", "1.9.9", true)]
    public void ComparesVersions(string tag, string current, bool newer)
    {
        Assert.True(UpdateCheck.TryParseVersion(tag, out var v));
        Assert.Equal(newer, UpdateCheck.IsNewer(v, Version.Parse(current)));
    }

    [Fact]
    public void XlsxColumnNames()
    {
        Assert.Equal("A", XlsxWriter.Column(0));
        Assert.Equal("Z", XlsxWriter.Column(25));
        Assert.Equal("AA", XlsxWriter.Column(26));
    }
}
