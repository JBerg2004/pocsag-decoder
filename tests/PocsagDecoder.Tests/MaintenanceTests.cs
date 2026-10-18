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

    [Fact]
    public void FindsExeAssetWithChecksum()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"tag_name":"v1.2.0","assets":[
              {"name":"Quellcode.zip","browser_download_url":"https://example.org/a.zip","size":5},
              {"name":"PocsagDecoder.exe","browser_download_url":"https://example.org/PocsagDecoder.exe","size":2048,"digest":"sha256:ABCDEF"}]}
            """);
        var (url, size, sha) = UpdateCheck.FindExeAsset(doc.RootElement);
        Assert.Equal("https://example.org/PocsagDecoder.exe", url);
        Assert.Equal(2048, size);
        Assert.Equal("abcdef", sha);
    }

    [Fact]
    public void VerifiesAndInstallsUpdate()
    {
        var dir = Directory.CreateTempSubdirectory("pocsag-update").FullName;
        try
        {
            var bytes = new byte[4096];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            bytes[100] = 42;
            var download = Path.Combine(dir, "neu.exe");
            File.WriteAllBytes(download, bytes);
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

            Assert.Null(UpdateInstaller.Verify(download, new UpdateInfo(new Version(1, 2, 0), "v1.2.0", "", "x", bytes.Length, sha)));
            Assert.NotNull(UpdateInstaller.Verify(download, new UpdateInfo(new Version(1, 2, 0), "v1.2.0", "", "x", bytes.Length, "00")));
            Assert.NotNull(UpdateInstaller.Verify(download, new UpdateInfo(new Version(1, 2, 0), "v1.2.0", "", "x", 10, null)));

            var exe = Path.Combine(dir, "PocsagDecoder.exe");
            File.WriteAllText(exe, "alt");
            UpdateInstaller.Install(download, exe);
            Assert.Equal(bytes, File.ReadAllBytes(exe));
            Assert.Equal("alt", File.ReadAllText(exe + UpdateInstaller.OldSuffix));
            UpdateInstaller.CleanUp(exe);
            Assert.False(File.Exists(exe + UpdateInstaller.OldSuffix));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PicksHighestPublishedRelease()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            [{"tag_name":"v1.4.0","draft":true},
             {"tag_name":"v1.3.0-beta","prerelease":true},
             {"tag_name":"v1.2.0","html_url":"https://example.org/v1.2.0","assets":[]},
             {"tag_name":"v1.10.0","html_url":"https://example.org/v1.10.0","assets":[{"name":"PocsagDecoder.exe","browser_download_url":"https://example.org/x.exe","size":3}]}]
            """);
        var latest = UpdateCheck.PickLatest(doc.RootElement);
        Assert.NotNull(latest);
        Assert.Equal(new Version(1, 10, 0), latest.Version);
        Assert.Equal("https://example.org/x.exe", latest.DownloadUrl);
    }

    [Fact]
    public void CompressesOldDaysAndHistoryStillReadsThem()
    {
        var dir = Directory.CreateTempSubdirectory("pocsag-logs").FullName;
        try
        {
            const string header = "Zeit;Baud;RIC;SubRIC;Funktion;Typ;Text;Weiterleitung\n";
            File.WriteAllText(Path.Combine(dir, "pocsag-2026-10-16.csv"), header + "2026-10-16 10:00:00;1200;1000011;A;0;Alpha;\"Probealarm\";\"\"\n");
            File.WriteAllText(Path.Combine(dir, "pocsag-2026-10-18.csv"), header + "2026-10-18 10:00:00;1200;1000012;B;1;Alpha;\"Heute\";\"\"\n");

            var result = LogMaintenance.Compress(dir, new DateTime(2026, 10, 18));
            Assert.Equal(1, result.Compressed);
            Assert.True(File.Exists(Path.Combine(dir, "pocsag-2026-10-16.csv.gz")));
            Assert.False(File.Exists(Path.Combine(dir, "pocsag-2026-10-16.csv")));
            Assert.True(File.Exists(Path.Combine(dir, "pocsag-2026-10-18.csv"))); // heute bleibt

            // Nachzügler für einen schon komprimierten Tag wird angehängt
            File.WriteAllText(Path.Combine(dir, "pocsag-2026-10-16.csv"), header + "2026-10-16 23:59:59;1200;1000013;C;2;Alpha;\"Spät\";\"\"\n");
            LogMaintenance.Compress(dir, new DateTime(2026, 10, 18));

            var all = MessageHistory.Read(dir, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)).Select(e => e.Message.Ric).ToList();
            Assert.Equal(new[] { 1000011, 1000013, 1000012 }, all);
            Assert.Equal(2, LogMaintenance.Usage(dir).Days);

            Assert.Equal(1, LogMaintenance.DeleteOld(dir, new DateTime(2026, 10, 18), 1));
            Assert.Equal(0, LogMaintenance.DeleteOld(dir, new DateTime(2026, 10, 18), 0));
            Assert.Single(MessageHistory.Read(dir, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
