using System.Globalization;
using System.Text;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Settings;

/// <summary>Eine Zeile aus den CSV-Tagesdateien.</summary>
public sealed record HistoryEntry(PocsagMessage Message, string Forwarding);

/// <summary>Liest die CSV-Tagesdateien (pocsag-JJJJ-MM-TT.csv) für die Suche im Verlauf.</summary>
public static class MessageHistory
{
    /// <summary>Alle Meldungen von <paramref name="from"/> bis einschließlich <paramref name="to"/>, älteste zuerst.</summary>
    public static IEnumerable<HistoryEntry> Read(string directory, DateTime from, DateTime to)
    {
        if (!System.IO.Directory.Exists(directory)) yield break;
        var files = System.IO.Directory.GetFiles(directory, "pocsag-*.csv")
            .Select(f => (File: f, Day: DayOf(f)))
            .Where(x => x.Day is { } d && d >= from.Date && d <= to.Date)
            .OrderBy(x => x.Day);
        foreach (var (file, _) in files)
        {
            string[] lines;
            try
            {
                // Die Datei ist evtl. gerade zum Schreiben geöffnet.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                lines = reader.ReadToEnd().Split('\n');
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var line in lines.Skip(1))
                if (TryParseLine(line.TrimEnd('\r'), out var entry)) yield return entry!;
        }
    }

    private static DateTime? DayOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return DateTime.TryParseExact(name.Replace("pocsag-", ""), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>Zeit;Baud;RIC;SubRIC;Funktion;Typ;Text;Weiterleitung</summary>
    public static bool TryParseLine(string line, out HistoryEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        var f = SplitCsv(line);
        if (f.Count < 7) return false;
        if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return false;
        if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ric)) return false;
        int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud);
        int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var function);
        Enum.TryParse<PocsagContentType>(f[5], out var type);
        entry = new HistoryEntry(new PocsagMessage
        {
            Timestamp = time,
            Baud = baud,
            Ric = ric,
            Function = function & 0x3,
            ContentType = type,
            Text = f[6],
            Raw = "(Verlauf)",
        }, f.Count > 7 ? f[7] : string.Empty);
        return true;
    }

    /// <summary>Trennt an ; außerhalb von Anführungszeichen, "" steht für ein ".</summary>
    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ';') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
