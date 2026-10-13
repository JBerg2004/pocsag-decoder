using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Models;
using Xunit;

namespace PocsagDecoder.Tests;

public class FilterTests
{
    private static PocsagMessage Msg(int ric, int func, string text = "", int secondsOffset = 0) => new()
    {
        Ric = ric,
        Function = func,
        Text = text,
        Baud = 1200,
        ContentType = PocsagContentType.Alpha,
        Timestamp = new DateTime(2026, 10, 13, 12, 0, 0).AddSeconds(secondsOffset),
    };

    [Theory]
    [InlineData("", 42, true)]
    [InlineData("*", 42, true)]
    [InlineData("1234567", 1234567, true)]
    [InlineData("1234567", 1234568, false)]
    [InlineData("1000000-1000999", 1000500, true)]
    [InlineData("1000000-1000999", 1001000, false)]
    [InlineData("12345*", 1234599, true)]
    [InlineData("12345*", 1244599, false)]
    [InlineData("00123??", 12345, true)]
    [InlineData("111, 222 ,333", 222, true)]
    public void RicPatternMatches(string pattern, int ric, bool expected)
    {
        Assert.True(RicPattern.TryParse(pattern, out var p, out _));
        Assert.Equal(expected, p.IsMatch(ric));
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("1-x")]
    public void RicPatternRejectsGarbage(string pattern) => Assert.False(RicPattern.TryParse(pattern, out _, out _));

    [Fact]
    public void RuleChecksSubRicAndText()
    {
        var rule = new FilterRule { RicPattern = "1234567", SubRicA = false, SubRicB = false, SubRicC = false, SubRicD = true, Text = "brand" };
        Assert.True(rule.IsMatch(Msg(1234567, 3, "B3 Brand")));
        Assert.False(rule.IsMatch(Msg(1234567, 0, "B3 Brand")));
        Assert.False(rule.IsMatch(Msg(1234567, 3, "TH Verkehrsunfall")));
        Assert.Equal("D", rule.SubRicSummary);
    }

    [Fact]
    public void RuleSupportsRegexAndNotContains()
    {
        Assert.True(new FilterRule { Text = @"^B\d", TextMode = TextMatchMode.Regex }.IsMatch(Msg(1, 0, "B2 Kaminbrand")));
        Assert.False(new FilterRule { Text = "Probe", TextMode = TextMatchMode.NotContains }.IsMatch(Msg(1, 0, "Probealarm")));
        Assert.NotNull(new FilterRule { Text = "(", TextMode = TextMatchMode.Regex }.Validate());
    }

    [Fact]
    public void RuleEngineStopsAfterStopRule()
    {
        var rules = new[]
        {
            new FilterRule { Name = "1", RicPattern = "1" },
            new FilterRule { Name = "2", RicPattern = "1", StopProcessing = true },
            new FilterRule { Name = "3", RicPattern = "1" },
            new FilterRule { Name = "aus", RicPattern = "1", Enabled = false },
        };
        Assert.Equal(new[] { "1", "2" }, RuleEngine.Match(rules, Msg(1, 0)).Select(r => r.Name));
    }

    [Fact]
    public void DuplicateFilterUsesWindow()
    {
        var f = new DuplicateFilter { Window = TimeSpan.FromSeconds(30) };
        Assert.False(f.IsDuplicate(Msg(1, 0, "x")));
        Assert.True(f.IsDuplicate(Msg(1, 0, "x", 10)));
        Assert.False(f.IsDuplicate(Msg(1, 1, "x", 10)));
        Assert.False(f.IsDuplicate(Msg(1, 0, "x", 50)));
        // Leer-Alarm, zweite Aussendung mit Füllzeichen
        Assert.False(f.IsDuplicate(Msg(2, 0, "", 100)));
        Assert.True(f.IsDuplicate(Msg(2, 0, "(", 102)));
    }

    [Fact]
    public void RuleMatchesOnDescription()
    {
        var m = Msg(1234568, 0, "B2 Brand");
        m.RicDescription = "FF Musterstadt";
        m.SubRicDescription = "Vollalarm";
        Assert.True(new FilterRule { DescriptionText = "musterstadt" }.IsMatch(m));
        Assert.True(new FilterRule { DescriptionText = "Beispieldorf | Musterstadt" }.IsMatch(m));
        Assert.True(new FilterRule { DescriptionText = "FF Musterstadt · Vollalarm" }.IsMatch(m));
        Assert.False(new FilterRule { DescriptionText = "Probe", DescriptionMode = TextMatchMode.Contains }.IsMatch(m));
        Assert.False(new FilterRule { DescriptionText = "Vollalarm", DescriptionMode = TextMatchMode.NotContains }.IsMatch(m));
        Assert.True(new FilterRule { DescriptionText = "^FF .* Voll", DescriptionMode = TextMatchMode.Regex }.IsMatch(m));
        Assert.True(new FilterRule { Text = "brand", DescriptionText = "Vollalarm" }.IsMatch(m));
        Assert.False(new FilterRule { Text = "brand", DescriptionText = "Kleinalarm" }.IsMatch(m));
    }

    [Fact]
    public void RuleCanHaveSeveralTargets()
    {
        var rule = new FilterRule { TargetName = "Ziel Eins" };
        rule.SetTarget("Ziel Zwei", true);
        Assert.Equal("Ziel Eins, Ziel Zwei", rule.TargetName);
        Assert.Equal(new[] { "Ziel Eins", "Ziel Zwei" }, rule.TargetNames);
        rule.SetTarget("ziel eins", false);
        Assert.Equal(new[] { "Ziel Zwei" }, rule.TargetNames);
        Assert.Empty(new FilterRule { TargetName = " , " }.TargetNames);
    }
}
