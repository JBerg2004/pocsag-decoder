using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Forwarding;

public enum TemplateEscaping
{
    None,
    Json,
    Url,
}

/// <summary>
/// Ersetzt Platzhalter wie {ric}, {subric}, {message} in Vorlagen.
/// Unbekannte Platzhalter bleiben unverändert stehen.
/// </summary>
public static partial class TemplateRenderer
{
    public static readonly IReadOnlyDictionary<string, string> PlaceholderHelp = new Dictionary<string, string>
    {
        ["ric"] = "RIC, 7-stellig (z.B. 0123456)",
        ["ric_raw"] = "RIC ohne führende Nullen",
        ["subric"] = "SubRIC als Buchstabe (A–D oder a–d, je nach Einstellung)",
        ["subric_upper"] = "SubRIC immer groß A–D",
        ["subric_lower"] = "SubRIC immer klein a–d",
        ["subric_num"] = "SubRIC als Zahl 1–4",
        ["function"] = "Funktionsbits 0–3",
        ["message"] = "Nachrichtentext",
        ["type"] = "Alpha, Numeric, Tone oder Skyper",
        ["baud"] = "Baudrate (512/1200/2400)",
        ["timestamp"] = "Zeitpunkt ISO 8601",
        ["unix"] = "Unix-Zeitstempel (Sekunden)",
        ["date"] = "Datum TT.MM.JJJJ",
        ["time"] = "Uhrzeit HH:MM:SS",
        ["description"] = "Beschreibung (RIC + SubRIC)",
        ["ric_description"] = "Beschreibung der RIC",
        ["ric_short"] = "Kurze Bezeichnung der RIC",
        ["ric_location"] = "Ort der RIC",
        ["subric_description"] = "Beschreibung der SubRIC",
        ["rule"] = "Name des Telegrammereignisses, das ausgelöst hat",
        ["raw"] = "Unveränderte Decoder-Zeile",
    };

    /// <summary>SubRIC-Buchstaben klein (a–d) statt groß (A–D) anzeigen und senden.</summary>
    public static bool LowercaseSubRic { get; set; }

    public static string FormatSubRic(PocsagMessage m) =>
        LowercaseSubRic ? char.ToLowerInvariant(m.SubRic).ToString() : m.SubRic.ToString();

    [GeneratedRegex(@"\{(?<name>[a-z_]+)\}")]
    private static partial Regex PlaceholderRegex();

    private static readonly JavaScriptEncoder JsonEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    public static string Render(string template, PocsagMessage message, string ruleName, TemplateEscaping escaping)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        var values = GetValues(message, ruleName);
        return PlaceholderRegex().Replace(template, m =>
        {
            if (!values.TryGetValue(m.Groups["name"].Value, out var value)) return m.Value;
            return escaping switch
            {
                TemplateEscaping.Json => JsonEscape(value),
                TemplateEscaping.Url => Uri.EscapeDataString(value),
                _ => value,
            };
        });
    }

    public static Dictionary<string, string> GetValues(PocsagMessage m, string ruleName)
    {
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>
        {
            ["ric"] = m.RicFormatted,
            ["ric_raw"] = m.Ric.ToString(inv),
            ["subric"] = FormatSubRic(m),
            ["subric_upper"] = m.SubRic.ToString(),
            ["subric_lower"] = char.ToLowerInvariant(m.SubRic).ToString(),
            ["subric_num"] = m.SubRicNumber.ToString(inv),
            ["function"] = m.Function.ToString(inv),
            ["message"] = m.Text,
            ["type"] = m.ContentType.ToString(),
            ["baud"] = m.Baud.ToString(inv),
            ["timestamp"] = new DateTimeOffset(m.Timestamp).ToString("yyyy-MM-ddTHH:mm:sszzz", inv),
            ["unix"] = new DateTimeOffset(m.Timestamp).ToUnixTimeSeconds().ToString(inv),
            ["date"] = m.Timestamp.ToString("dd.MM.yyyy", inv),
            ["time"] = m.Timestamp.ToString("HH:mm:ss", inv),
            ["description"] = m.Description,
            ["ric_description"] = m.RicDescription,
            ["ric_short"] = m.RicShortName,
            ["ric_location"] = m.RicLocation,
            ["subric_description"] = m.SubRicDescription,
            ["rule"] = ruleName,
            ["raw"] = m.Raw,
        };
    }

    private static string JsonEscape(string value)
    {
        // JsonSerializer liefert "…" inklusive Anführungszeichen – die Vorlage setzt sie selbst.
        var quoted = JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JsonEncoder });
        return quoted[1..^1];
    }
}
