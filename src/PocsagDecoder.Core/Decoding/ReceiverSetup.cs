using System.Globalization;

namespace PocsagDecoder.Core.Decoding;

/// <summary>Validierung der vom Einrichtungsassistenten übernommenen Empfangsparameter.</summary>
public static class ReceiverSetup
{
    public static string? ValidateFrequency(string input, out double frequency)
    {
        if (!double.TryParse(input.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out frequency)
            || !double.IsFinite(frequency) || frequency is < 24 or > 1766)
            return "Bitte eine Frequenz zwischen 24 und 1766 MHz eingeben.";
        return null;
    }

    public static string? ValidateWebhookUrl(string input) =>
        Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host)
            ? null : "Bitte eine vollständige HTTP- oder HTTPS-Adresse eingeben.";
}
