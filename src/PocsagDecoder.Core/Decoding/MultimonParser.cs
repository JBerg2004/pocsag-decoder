using System.Globalization;
using System.Text.RegularExpressions;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Decoding;

/// <summary>
/// Parst die Textausgabe von multimon-ng, z.B.
/// <c>POCSAG1200: Address: 1234567  Function: 0  Alpha:   Einsatz BMA</c>.
/// </summary>
public static partial class MultimonParser
{
    [GeneratedRegex(
        @"POCSAG(?<baud>\d+):\s*Address:\s*(?<ric>\d+)\s+Function:\s*(?<func>\d)(?:\s+(?<type>Alpha|Numeric|Skyper):\s?(?<text>.*))?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    public static bool TryParse(string? line, out PocsagMessage? message, DateTime? timestamp = null)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var m = LineRegex().Match(line);
        if (!m.Success) return false;

        var type = m.Groups["type"].Success
            ? m.Groups["type"].Value switch
            {
                "Alpha" => PocsagContentType.Alpha,
                "Numeric" => PocsagContentType.Numeric,
                "Skyper" => PocsagContentType.Skyper,
                _ => PocsagContentType.Tone,
            }
            : PocsagContentType.Tone;

        message = new PocsagMessage
        {
            Timestamp = timestamp ?? DateTime.Now,
            Baud = int.Parse(m.Groups["baud"].Value, CultureInfo.InvariantCulture),
            Ric = int.Parse(m.Groups["ric"].Value, CultureInfo.InvariantCulture),
            Function = int.Parse(m.Groups["func"].Value, CultureInfo.InvariantCulture) & 0x3,
            ContentType = type,
            Text = CleanText(m.Groups["text"].Value),
            Raw = line,
        };
        return true;
    }

    /// <summary>
    /// Entfernt Steuerzeichen, die multimon-ng als &lt;NUL&gt;, &lt;EOT&gt; usw. ausgibt,
    /// und schneidet Leerzeichen ab.
    /// </summary>
    public static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = ControlTokenRegex().Replace(text, string.Empty);
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        return text.Trim();
    }

    [GeneratedRegex(@"<(NUL|SOH|STX|ETX|EOT|ENQ|ACK|BEL|BS|HT|LF|VT|FF|CR|SO|SI|DLE|DC1|DC2|DC3|DC4|NAK|SYN|ETB|CAN|EM|SUB|ESC|FS|GS|RS|US|DEL)>")]
    private static partial Regex ControlTokenRegex();
}
