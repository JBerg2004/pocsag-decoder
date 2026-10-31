using PocsagDecoder.Core.Decoding;

namespace PocsagDecoder.Tests;

public class ReceiverSetupTests
{
    [Theory]
    [InlineData("173,255", 173.255)]
    [InlineData("173.255", 173.255)]
    [InlineData(" 24 ", 24)]
    [InlineData("1766", 1766)]
    public void AcceptsSupportedFrequencies(string text, double expected)
    {
        Assert.Null(ReceiverSetup.ValidateFrequency(text, out var frequency));
        Assert.Equal(expected, frequency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0")]
    [InlineData("1767")]
    [InlineData("173,255.1")]
    public void RejectsInvalidFrequencies(string text) => Assert.NotNull(ReceiverSetup.ValidateFrequency(text, out _));

    [Theory]
    [InlineData("https://example.test/webhook", true)]
    [InlineData("http://localhost:8123/api/webhook/test", true)]
    [InlineData("ftp://example.test", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("example.test/webhook", false)]
    [InlineData("", false)]
    public void RequiresHttpWebhookAddress(string url, bool valid) =>
        Assert.Equal(valid, ReceiverSetup.ValidateWebhookUrl(url) is null);
}
