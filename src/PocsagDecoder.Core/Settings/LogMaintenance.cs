using System.IO.Compression;
using System.Text;

namespace PocsagDecoder.Core.Settings;

/// <summary>
/// Platz sparen bei den CSV-Tagesdateien: abgeschlossene Tage werden mit GZip komprimiert (ca. ein Achtel der Größe),
/// auf Wunsch werden sehr alte Tage gelöscht. Der Verlauf liest beide Formen.
/// </summary>
public static class LogMaintenance
{
    public sealed record Result(int Compressed, long SavedBytes, int Deleted);

    /// <summary>Alle Tage vor <paramref name="today"/> komprimieren. Die Originaldatei wird erst gelöscht, wenn die .gz geprüft ist.</summary>
    public static Result Compress(string directory, DateTime today)
    {
        var count = 0;
        long saved = 0;
        if (!Directory.Exists(directory)) return new Result(0, 0, 0);
        foreach (var csv in Directory.GetFiles(directory, "pocsag-*.csv"))
        {
            if (MessageHistory.DayOf(csv) is not { } day || day >= today.Date) continue;
            var gz = csv + ".gz";
            var temp = csv + ".tmp.gz"; // endet auf .gz, damit das Prüflesen entpackt
            var before = new FileInfo(csv).Length + (File.Exists(gz) ? new FileInfo(gz).Length : 0);
            // Kam nach dem Komprimieren noch eine Zeile für den Tag dazu, beide zusammenführen.
            var text = MessageHistory.ReadAllText(csv);
            if (File.Exists(gz)) text = MessageHistory.ReadAllText(gz) + text[(text.IndexOf('\n') + 1)..]; // ohne zweite Kopfzeile
            using (var file = File.Create(temp))
            using (var zip = new GZipStream(file, CompressionLevel.SmallestSize))
            using (var writer = new StreamWriter(zip, new UTF8Encoding(false)))
                writer.Write(text);
            if (MessageHistory.ReadAllText(temp) != text)
            {
                File.Delete(temp);
                continue;
            }
            File.Move(temp, gz, overwrite: true);
            File.Delete(csv);
            saved += before - new FileInfo(gz).Length;
            count++;
        }
        return new Result(count, saved, 0);
    }

    /// <summary>Tagesdateien löschen, die älter als <paramref name="keepDays"/> Tage sind (0 = nie).</summary>
    public static int DeleteOld(string directory, DateTime today, int keepDays)
    {
        if (keepDays <= 0 || !Directory.Exists(directory)) return 0;
        var limit = today.Date.AddDays(-keepDays);
        var deleted = 0;
        foreach (var file in Directory.GetFiles(directory, "pocsag-*.csv*"))
        {
            if (MessageHistory.DayOf(file) is not { } day || day >= limit) continue;
            File.Delete(file);
            deleted++;
        }
        return deleted;
    }

    /// <summary>Anzahl Tage und belegter Platz.</summary>
    public static (int Days, long Bytes) Usage(string directory)
    {
        if (!Directory.Exists(directory)) return (0, 0);
        var files = Directory.GetFiles(directory, "pocsag-*.csv*").Where(f => MessageHistory.DayOf(f) is not null).ToList();
        return (files.Select(MessageHistory.DayOf).Distinct().Count(), files.Sum(f => new FileInfo(f).Length));
    }
}
