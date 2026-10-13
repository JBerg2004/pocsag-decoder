using System.Collections.Concurrent;
using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Models;
using Xunit;

namespace PocsagDecoder.Tests;

/// <summary>
/// Ende-zu-Ende: ein Ersatz für rtl_fm gibt ein synthetisches POCSAG-Signal aus,
/// das echte multimon-ng dekodiert es, die Pipeline liefert RIC/SubRIC/Text.
/// Läuft nur unter Linux/macOS mit installiertem multimon-ng.
/// </summary>
public class PipelineIntegrationTests
{
    private static string? FindMultimon()
    {
        if (OperatingSystem.IsWindows()) return null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var p = Path.Combine(dir, "multimon-ng");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1200)]
    [InlineData(2400)]
    public async Task DecodesSyntheticSignalThroughPipeline(int baud)
    {
        var multimon = FindMultimon();
        if (multimon is null) return; // multimon-ng nicht installiert

        var dir = Directory.CreateTempSubdirectory("pocsag-test").FullName;
        var raw = Path.Combine(dir, "signal.raw");
        await File.WriteAllBytesAsync(raw, PocsagSignal.Generate(baud,
            (1234567, 3, "B3 Brand [ltere Gro~k}che"),
            (12345, 0, ""),
            (2000001, 1, "Test 123")));
        var fakeRtlFm = Path.Combine(dir, "rtl_fm");
        await File.WriteAllTextAsync(fakeRtlFm, $"#!/bin/sh\ncat '{raw}'\nsleep 1\n");
        File.SetUnixFileMode(fakeRtlFm, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var received = new ConcurrentQueue<PocsagMessage>();
        var pipeline = new DecoderPipeline(new DecoderOptions { RtlFmPath = fakeRtlFm, MultimonPath = multimon });
        pipeline.MessageReceived += (_, m) => received.Enqueue(m);
        pipeline.Start();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (received.Count < 3 && DateTime.UtcNow < deadline) await Task.Delay(100);
        await pipeline.StopAsync();

        var list = received.ToList();
        Assert.Equal(3, list.Count);
        Assert.All(list, m => Assert.Equal(baud, m.Baud));

        Assert.Equal(1234567, list[0].Ric);
        Assert.Equal('D', list[0].SubRic);
        Assert.Equal("B3 Brand Ältere Großküche", list[0].Text);

        Assert.Equal("0012345", list[1].RicFormatted);
        Assert.Equal('A', list[1].SubRic);
        Assert.Equal(PocsagContentType.Tone, list[1].ContentType);

        Assert.Equal(2000001, list[2].Ric);
        Assert.Equal('B', list[2].SubRic);
        Assert.Equal("Test 123", list[2].Text);
    }
}
