using System.Globalization;

namespace PocsagDecoder.Core.Filtering;

/// <summary>
/// RIC-Muster: leer oder "*" = alle; sonst kommagetrennte Liste aus
/// einzelnen RICs ("1234567"), Bereichen ("1000000-1000999") und
/// Platzhaltern ("12345*", "12?4567").
/// </summary>
public sealed class RicPattern
{
    private readonly List<Func<int, string, bool>> _parts = new();

    public string Source { get; }
    public bool MatchesAll { get; }

    private RicPattern(string source, bool matchesAll)
    {
        Source = source;
        MatchesAll = matchesAll;
    }

    public static bool TryParse(string? text, out RicPattern pattern, out string? error)
    {
        error = null;
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0 || text == "*")
        {
            pattern = new RicPattern(text, true);
            return true;
        }

        pattern = new RicPattern(text, false);
        foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            var dash = part.IndexOf('-');
            if (dash > 0)
            {
                if (!int.TryParse(part[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var from) ||
                    !int.TryParse(part[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var to))
                {
                    error = $"Ungültiger Bereich: {part}";
                    return false;
                }
                if (from > to) (from, to) = (to, from);
                pattern._parts.Add((ric, _) => ric >= from && ric <= to);
            }
            else if (part.Contains('*') || part.Contains('?'))
            {
                if (part.Any(c => !char.IsDigit(c) && c != '*' && c != '?'))
                {
                    error = $"Ungültiges Muster: {part}";
                    return false;
                }
                var regex = new System.Text.RegularExpressions.Regex(
                    "^" + System.Text.RegularExpressions.Regex.Escape(part).Replace("\\*", "\\d*").Replace("\\?", "\\d") + "$");
                // Gegen 7-stellige Schreibweise und gegen die Zahl ohne führende Nullen prüfen.
                pattern._parts.Add((ric, padded) => regex.IsMatch(padded) || regex.IsMatch(ric.ToString(CultureInfo.InvariantCulture)));
            }
            else if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var single))
            {
                pattern._parts.Add((ric, _) => ric == single);
            }
            else
            {
                error = $"Ungültige RIC: {part}";
                return false;
            }
        }
        return true;
    }

    public bool IsMatch(int ric)
    {
        if (MatchesAll) return true;
        var padded = ric.ToString("D7", CultureInfo.InvariantCulture);
        return _parts.Any(p => p(ric, padded));
    }
}
