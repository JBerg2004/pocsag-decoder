using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>
/// Lebenszeichen, das in einem festen Abstand an eine Schnittstelle geht. Bleibt es auf der Gegenseite
/// (Home Assistant, Uptime Kuma, FE2 …) aus, ist der PC, das Programm oder der Empfang ausgefallen.
/// </summary>
public static class Heartbeat
{
    /// <summary>Name des Telegrammereignisses für {rule}.</summary>
    public const string RuleName = "Heartbeat";

    public const string DefaultText = "Heartbeat {pc}: Empfang {state}, letzte Meldung {last}";

    /// <summary>Platzhalter, die nur im Heartbeat-Text gelten.</summary>
    public static IReadOnlyDictionary<string, string> PlaceholderHelp { get; } = new Dictionary<string, string>
    {
        ["pc"] = "Name dieses PCs",
        ["state"] = "„aktiv“ oder „gestoppt“",
        ["last"] = "Zeit der letzten empfangenen Meldung, z. B. „14:05 (vor 12 Min.)“",
    };

    /// <summary>Ist das nächste Lebenszeichen fällig?</summary>
    public static bool IsDue(DateTime? lastSent, DateTime now, int minutes) =>
        minutes > 0 && (lastSent is null || now - lastSent.Value >= TimeSpan.FromMinutes(minutes));

    /// <summary>Baut das Lebenszeichen als Telegramm, damit es mit der Vorlage der Schnittstelle gesendet werden kann.</summary>
    public static PocsagMessage CreateMessage(AppSettings settings, bool receiving, DateTime? lastMessageAt, DateTime now, string machineName)
    {
        var template = string.IsNullOrWhiteSpace(settings.HeartbeatText) ? DefaultText : settings.HeartbeatText;
        var text = template
            .Replace("{pc}", machineName, StringComparison.OrdinalIgnoreCase)
            .Replace("{state}", receiving ? "aktiv" : "gestoppt", StringComparison.OrdinalIgnoreCase)
            .Replace("{last}", FormatLast(lastMessageAt, now), StringComparison.OrdinalIgnoreCase);
        PocsagMessage.TryParseSubRic(settings.HeartbeatSubRic, out var function);
        return new PocsagMessage
        {
            Timestamp = now,
            Ric = Math.Clamp(settings.HeartbeatRic, 0, 2_097_151),
            Function = Math.Max(0, function),
            ContentType = PocsagContentType.Alpha,
            Text = text,
            Raw = "(Heartbeat)",
        };
    }

    public static string FormatLast(DateTime? lastMessageAt, DateTime now)
    {
        if (lastMessageAt is not { } last) return "keine";
        var minutes = (int)Math.Max(0, (now - last).TotalMinutes);
        var time = last.Date == now.Date ? last.ToString("HH:mm") : last.ToString("dd.MM. HH:mm");
        return minutes < 1 ? $"{time} (gerade eben)" : $"{time} (vor {minutes} Min.)";
    }
}
