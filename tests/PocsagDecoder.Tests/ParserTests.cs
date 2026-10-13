using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Models;
using Xunit;

namespace PocsagDecoder.Tests;

public class ParserTests
{
    [Fact]
    public void ParsesAlphaMessage()
    {
        Assert.True(MultimonParser.TryParse("POCSAG1200: Address: 1234567  Function: 3  Alpha:   B3 Brand Wohnhaus<NUL>", out var m));
        Assert.Equal(1200, m!.Baud);
        Assert.Equal(1234567, m.Ric);
        Assert.Equal(3, m.Function);
        Assert.Equal('D', m.SubRic);
        Assert.Equal(PocsagContentType.Alpha, m.ContentType);
        Assert.Equal("B3 Brand Wohnhaus", m.Text);
    }

    [Fact]
    public void ParsesNumericMessageAndPadsRic()
    {
        Assert.True(MultimonParser.TryParse("POCSAG512: Address:   12345  Function: 1  Numeric: 0815 ", out var m));
        Assert.Equal("0012345", m!.RicFormatted);
        Assert.Equal('B', m.SubRic);
        Assert.Equal(PocsagContentType.Numeric, m.ContentType);
        Assert.Equal("0815", m.Text);
    }

    [Fact]
    public void ParsesToneOnly()
    {
        Assert.True(MultimonParser.TryParse("POCSAG2400: Address: 2000001  Function: 0 ", out var m));
        Assert.Equal(PocsagContentType.Tone, m!.ContentType);
        Assert.Equal('A', m.SubRic);
        Assert.Equal(string.Empty, m.Text);
    }

    [Theory]
    [InlineData("multimon-ng 1.3.0")]
    [InlineData("Enabled demodulators: POCSAG512 POCSAG1200 POCSAG2400")]
    [InlineData("")]
    public void IgnoresNonPocsagLines(string line) => Assert.False(MultimonParser.TryParse(line, out _));

    [Fact]
    public void ConvertsGermanUmlauts() =>
        Assert.Equal("Übung Großbrand Ärztehaus", GermanCharset.Convert("]bung Gro~brand [rztehaus"));

    [Fact]
    public void BuildsRtlFmArguments()
    {
        var o = new DecoderOptions { FrequencyMHz = 173.255, Ppm = 42, GainDb = 40 };
        Assert.Equal("-d 0 -f 173.255M -M fm -s 22050 -p 42 -l 0 -g 40.0 -", o.BuildRtlFmArguments());
        Assert.Equal("-t raw -a POCSAG512 -a POCSAG1200 -a POCSAG2400 -f alpha -", o.BuildMultimonArguments());
    }
}
