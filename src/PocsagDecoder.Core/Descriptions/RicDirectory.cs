using System.Globalization;
using System.Text;
using System.Text.Json;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Descriptions;

public sealed record ImportResult(IReadOnlyList<RicEntry> Entries, IReadOnlyList<string> Errors);

/// <summary>
/// Nachschlagen von Beschreibungen zu RIC/SubRIC und Import aus Dateien.
/// Gibt es eine Bezeichnung für die RIC und eine Beschreibung für die SubRIC,
/// werden beide kombiniert ("FF Musterstadt · Vollalarm").
/// </summary>
public sealed partial class RicDirectory
{
    private readonly Dictionary<int, RicEntry> _map = new();
    private readonly string[] _global = ["", "", "", ""];

    /// <summary>
    /// Allgemeine Bedeutung der SubRICs A–D, unabhängig von der RIC.
    /// Ein Eintrag bei der einzelnen RIC hat Vorrang.
    /// </summary>
    public void SetGlobalSubRics(IReadOnlyList<string>? names)
    {
        for (var i = 0; i < 4; i++)
            _global[i] = names is not null && i < names.Count ? (names[i] ?? string.Empty).Trim() : string.Empty;
    }

    public string GetGlobalSubRic(int function) => function is >= 0 and <= 3 ? _global[function] : string.Empty;

    public RicDirectory(IEnumerable<RicEntry>? entries = null)
    {
        if (entries is not null) Rebuild(entries);
    }

    public int Count => _map.Count;

    public void Rebuild(IEnumerable<RicEntry> entries)
    {
        _map.Clear();
        foreach (var e in entries)
        {
            if (_map.TryGetValue(e.Ric, out var existing)) existing.MergeFrom(e);
            else _map[e.Ric] = e.Clone();
        }
    }

    public string? GetRicDescription(int ric) =>
        _map.TryGetValue(ric, out var e) && !string.IsNullOrWhiteSpace(e.Name) ? e.Name.Trim() : null;

    public string? GetRicLocation(int ric) =>
        _map.TryGetValue(ric, out var e) && !string.IsNullOrWhiteSpace(e.Location) ? e.Location.Trim() : null;

    public string? GetRicShortName(int ric) =>
        _map.TryGetValue(ric, out var e) && !string.IsNullOrWhiteSpace(e.ShortName) ? e.ShortName.Trim() : null;

    /// <summary>SubRIC-Text der RIC, sonst die allgemeine SubRIC-Bedeutung.</summary>
    public string? GetSubRicDescription(int ric, int function)
    {
        if (_map.TryGetValue(ric, out var e) && !string.IsNullOrWhiteSpace(e.GetSub(function))) return e.GetSub(function).Trim();
        var g = GetGlobalSubRic(function);
        return g.Length > 0 ? g : null;
    }

    /// <summary>Kombinierte Beschreibung, oder leer wenn nichts hinterlegt ist.</summary>
    public string Describe(int ric, int function)
    {
        var r = GetRicDescription(ric);
        var s = GetSubRicDescription(ric, function);
        if (r is not null && s is not null) return $"{r} · {s}";
        return s ?? r ?? string.Empty;
    }

    /// <summary>"A".."D" → 0..3 (auch a–d oder 1–4 wie im BOS-Funk üblich); leer, "*" oder "alle" = -1.</summary>
    public static int ParseSubRic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return -1;
        var v = value.Trim();
        if (v == "*" || v == "-" || v.Equals("alle", StringComparison.OrdinalIgnoreCase)) return -1;
        if (v.Length == 1)
        {
            var c = char.ToUpperInvariant(v[0]);
            if (c is >= 'A' and <= 'D') return c - 'A';
            if (c is >= '1' and <= '4') return c - '1';
        }
        throw new FormatException($"Ungültige SubRIC \"{value}\" (erlaubt: A–D, 1–4 oder leer)");
    }

    /// <summary>Wandelt Einträge im alten Format (eine Zeile je RIC/SubRIC) um.</summary>
    public static List<RicEntry> FromLegacy(IEnumerable<RicDescription> legacy)
    {
        var result = new Dictionary<int, RicEntry>();
        foreach (var d in legacy)
        {
            if (!result.TryGetValue(d.Ric, out var e)) result[d.Ric] = e = new RicEntry { Ric = d.Ric };
            int f;
            try { f = ParseSubRic(d.SubRic); } catch (FormatException) { continue; }
            if (f < 0) e.Name = d.Description;
            else e.SetSub(f, d.Description);
        }
        return result.Values.OrderBy(e => e.Ric).ToList();
    }

    /// <summary>
    /// Liest CSV/TXT (Trennzeichen ; Tab , oder |) oder JSON. Unterstützte Zeilen:
    /// <list type="bullet">
    /// <item>Spalten je SubRIC: "RIC;Bezeichnung;A;B;C;D" (Kopfzeile optional)</item>
    /// <item>eine Zeile je SubRIC: "RIC;SubRIC;Beschreibung", "RIC;Beschreibung", "1234567A;Beschreibung"</item>
    /// </list>
    /// Zeilen derselben RIC werden zusammengeführt. Leere Zeilen und Kommentare (# oder //) werden übersprungen.
    /// </summary>
    public static ImportResult Parse(string content)
    {
        content = content.TrimStart('﻿');
        if (content.TrimStart().StartsWith('[')) return ParseJson(content);
        if (BosWatchFor().IsMatch(content) && BosWatchAdd().IsMatch(content)) return ParseBosWatch(content);
        return ParseText(content);
    }

    public static ImportResult ParseFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(bytes); // z.B. aus Excel als "CSV (Trennzeichen-getrennt)"
        }
        return Parse(text);
    }

    private sealed class Collector
    {
        private readonly Dictionary<int, RicEntry> _entries = new();
        public List<string> Errors { get; } = new();

        public RicEntry Get(int ric)
        {
            if (!_entries.TryGetValue(ric, out var e)) _entries[ric] = e = new RicEntry { Ric = ric };
            return e;
        }

        public ImportResult Result() => new(_entries.Values.Where(e => !e.IsEmpty).OrderBy(e => e.Ric).ToList(), Errors);
    }

    private static ImportResult ParseText(string content)
    {
        var col = new Collector();
        int nameCol = -1, shortCol = -1, locCol = -1;
        int[]? subCols = null; // Spaltenindex für A..D aus der Kopfzeile
        var seenData = false;
        var lines = content.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;

            var sep = new[] { ';', '\t', ',', '|' }.FirstOrDefault(c => line.Contains(c));
            if (sep == default)
            {
                col.Errors.Add($"Zeile {i + 1}: kein Trennzeichen gefunden");
                continue;
            }
            var parts = line.Split(sep).Select(p => p.Trim().Trim('"').Trim()).ToArray();

            // Kopfzeile: erste Spalte ohne Ziffer
            if (!parts[0].Any(char.IsDigit))
            {
                if (!seenData && TryReadHeader(parts, out var n, out var k, out var o, out var s)) { nameCol = n; shortCol = k; locCol = o; subCols = s; }
                else if (seenData) col.Errors.Add($"Zeile {i + 1}: \"{parts[0]}\" ist keine RIC");
                continue;
            }
            seenData = true;

            try
            {
                var ricField = parts[0];
                int combinedSub = -1;
                var last = char.ToUpperInvariant(ricField[^1]);
                if (last is >= 'A' and <= 'D')
                {
                    combinedSub = last - 'A';
                    ricField = ricField[..^1].TrimEnd('-', ' ', '/');
                }
                if (!int.TryParse(ricField, NumberStyles.None, CultureInfo.InvariantCulture, out var ric) || ric > 2097151)
                {
                    col.Errors.Add($"Zeile {i + 1}: ungültige RIC \"{parts[0]}\"");
                    continue;
                }

                var entry = new RicEntry { Ric = ric };
                if (combinedSub >= 0)
                {
                    entry.SetSub(combinedSub, string.Join(sep, parts.Skip(1)));
                }
                else if (subCols is not null)
                {
                    if (nameCol >= 0 && nameCol < parts.Length) entry.Name = parts[nameCol];
                    if (shortCol >= 0 && shortCol < parts.Length) entry.ShortName = parts[shortCol];
                    if (locCol >= 0 && locCol < parts.Length) entry.Location = parts[locCol];
                    for (var f = 0; f < 4; f++)
                        if (subCols[f] >= 0 && subCols[f] < parts.Length) entry.SetSub(f, parts[subCols[f]]);
                }
                else if (parts.Length >= 5)
                {
                    // Ohne Kopfzeile: RIC;A;B;C;D oder RIC;Bezeichnung;A;B;C;D
                    var offset = parts.Length >= 6 ? 2 : 1;
                    if (offset == 2) entry.Name = parts[1];
                    for (var f = 0; f < 4; f++) entry.SetSub(f, parts[offset + f]);
                }
                else if (parts.Length == 3 && (parts[1].Length <= 2 || IsSubRic(parts[1])))
                {
                    var f = ParseSubRic(parts[1]);
                    if (f < 0) entry.Name = parts[2];
                    else entry.SetSub(f, parts[2]);
                }
                else
                {
                    entry.Name = string.Join(sep, parts.Skip(1));
                }

                if (entry.IsEmpty)
                {
                    col.Errors.Add($"Zeile {i + 1}: keine Beschreibung");
                    continue;
                }
                col.Get(ric).MergeFrom(entry);
            }
            catch (FormatException ex)
            {
                col.Errors.Add($"Zeile {i + 1}: {ex.Message}");
            }
        }
        return col.Result();
    }

    private static bool TryReadHeader(string[] parts, out int nameCol, out int shortCol, out int locCol, out int[] subCols)
    {
        nameCol = -1;
        shortCol = -1;
        locCol = -1;
        subCols = [-1, -1, -1, -1];
        for (var c = 1; c < parts.Length; c++)
        {
            var h = parts[c].ToUpperInvariant().Replace("SUBRIC", "").Replace("SUB-RIC", "").Replace("SUB", "").Trim(' ', '_', '-', ':');
            if (shortCol < 0 && (h.Contains("KURZ") || h.Contains("SHORT"))) shortCol = c;
            else if (locCol < 0 && (h is "ORT" or "ORTSCHAFT" or "STANDORT" or "GEMEINDE" or "LOCATION")) locCol = c;
            else if (h.Length == 1 && h[0] is >= 'A' and <= 'D') subCols[h[0] - 'A'] = c;
            else if (h.Length == 1 && h[0] is >= '1' and <= '4') subCols[h[0] - '1'] = c;
            else if (nameCol < 0 && h.Length > 1) nameCol = c;
        }
        return subCols.Any(x => x >= 0) || shortCol >= 0 || locCol >= 0;
    }

    private static bool IsSubRic(string s)
    {
        try { ParseSubRic(s); return true; }
        catch (FormatException) { return false; }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*-?\s*for:\s*[""']?(?<v>[^""'#\r\n]*?)[""']?\s*(#.*)?$", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex BosWatchFor();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*add:\s*[""']?(?<v>[^""'\r\n]*?)[""']?\s*$", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex BosWatchAdd();

    /// <summary>
    /// BOSWatch-3-Konfiguration (descriptor-Modul): Paare aus "- for: 1234567" und "add: Name".
    /// Nur Einträge mit scanField ric werden gelesen.
    /// </summary>
    private static ImportResult ParseBosWatch(string content)
    {
        var c = new Collector();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var scanRic = true;
        int? pending = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var scan = System.Text.RegularExpressions.Regex.Match(line, @"scanField:\s*[""']?(\w+)");
            if (scan.Success) { scanRic = scan.Groups[1].Value.Equals("ric", StringComparison.OrdinalIgnoreCase); pending = null; continue; }
            if (!scanRic) continue;
            var f = BosWatchFor().Match(line);
            if (f.Success)
            {
                pending = int.TryParse(f.Groups["v"].Value.Trim(), out var ric) ? ric : null;
                if (pending is null) c.Errors.Add($"Zeile {i + 1}: \"{f.Groups["v"].Value.Trim()}\" ist keine RIC");
                continue;
            }
            var a = BosWatchAdd().Match(line);
            if (a.Success && pending is { } r)
            {
                c.Get(r).Name = a.Groups["v"].Value.Trim();
                pending = null;
            }
        }
        return c.Result();
    }

    private static ImportResult ParseJson(string content)
    {
        var col = new Collector();
        try
        {
            using var doc = JsonDocument.Parse(content, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var index = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                index++;
                try
                {
                    var ricEl = Get(el, "ric");
                    var ric = ricEl?.ValueKind == JsonValueKind.Number ? ricEl.Value.GetInt32() : int.Parse(ricEl?.GetString() ?? "", CultureInfo.InvariantCulture);
                    var entry = new RicEntry
                    {
                        Ric = ric,
                        Name = Str(el, "name") ?? Str(el, "bezeichnung") ?? string.Empty,
                        Location = Str(el, "location") ?? Str(el, "ort") ?? string.Empty,
                        ShortName = Str(el, "shortName") ?? Str(el, "kurz") ?? Str(el, "short") ?? string.Empty,
                        A = Str(el, "a") ?? string.Empty,
                        B = Str(el, "b") ?? string.Empty,
                        C = Str(el, "c") ?? string.Empty,
                        D = Str(el, "d") ?? string.Empty,
                    };
                    // Einzelform: {"ric":…, "subric":"A", "description":"…"}
                    var desc = Str(el, "description") ?? Str(el, "beschreibung");
                    if (desc is not null)
                    {
                        var subEl = Get(el, "subric");
                        var f = ParseSubRic(subEl?.ValueKind == JsonValueKind.Number ? subEl.Value.GetInt32().ToString(CultureInfo.InvariantCulture) : subEl?.GetString());
                        if (f < 0) entry.Name = desc;
                        else entry.SetSub(f, desc);
                    }
                    if (entry.IsEmpty) { col.Errors.Add($"Eintrag {index}: keine Beschreibung"); continue; }
                    col.Get(ric).MergeFrom(entry);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                {
                    col.Errors.Add($"Eintrag {index}: {ex.Message}");
                }
            }
        }
        catch (JsonException ex)
        {
            col.Errors.Add("Ungültiges JSON: " + ex.Message);
        }
        return col.Result();

        static JsonElement? Get(JsonElement el, string name)
        {
            foreach (var p in el.EnumerateObject())
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }

        static string? Str(JsonElement el, string name) =>
            Get(el, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    }

    /// <summary>Exportiert im Spaltenformat (Semikolon, für Excel geeignet).</summary>
    public static string ToCsv(IEnumerable<RicEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RIC;Bezeichnung;Kurz;Ort;A;B;C;D");
        foreach (var e in entries.OrderBy(e => e.Ric))
            sb.AppendLine(string.Join(';', e.RicFormatted, Clean(e.Name), Clean(e.ShortName), Clean(e.Location), Clean(e.A), Clean(e.B), Clean(e.C), Clean(e.D)));
        return sb.ToString();

        static string Clean(string s) => s.Trim().Replace(";", ",");
    }

    public const string Template =
        """
        # RIC-Beschreibungen für den POCSAG Decoder
        # Eine Zeile pro RIC: RIC;Bezeichnung;Kurz;Ort;A;B;C;D
        # Bezeichnung = allgemeiner Name der RIC, Kurz = kurze Bezeichnung, Ort = Ort, A–D = Bedeutung der jeweiligen SubRIC.
        # Leere Felder sind erlaubt. Angezeigt wird z.B. "FF Musterstadt · Vollalarm".
        # Zeilen mit # sind Kommentare. Die Datei kann in Excel bearbeitet werden.
        RIC;Bezeichnung;Kurz;Ort;A;B;C;D
        1234567;FF Musterstadt;Musterst.;Musterstadt;Vollalarm;Kleinalarm;Probealarm;Info
        1234568;FF Beispieldorf;Beispield.;Beispieldorf;Einsatz;Atemschutz;;Sirene
        """;
}

public static class RicDirectoryExtensions
{
    /// <summary>Trägt die Beschreibungen in die Nachricht ein.</summary>
    public static void Annotate(this RicDirectory directory, PocsagMessage message)
    {
        message.RicDescription = directory.GetRicDescription(message.Ric) ?? string.Empty;
        message.RicShortName = directory.GetRicShortName(message.Ric) ?? string.Empty;
        message.RicLocation = directory.GetRicLocation(message.Ric) ?? string.Empty;
        message.SubRicDescription = directory.GetSubRicDescription(message.Ric, message.Function) ?? string.Empty;
        message.Description = directory.Describe(message.Ric, message.Function);
    }
}
