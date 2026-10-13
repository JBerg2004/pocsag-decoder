using System.Text;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Settings;

/// <summary>Schreibt empfangene Nachrichten in eine CSV-Datei pro Tag.</summary>
public sealed class CsvMessageLog
{
    private readonly object _lock = new();

    public static string DefaultDirectory { get; } = Path.Combine(SettingsStore.DefaultDirectory, "logs");

    private string _directory;

    /// <summary>Zielordner; leer oder null = Standardordner.</summary>
    public string Directory
    {
        get => _directory;
        set => _directory = string.IsNullOrWhiteSpace(value) ? DefaultDirectory : value.Trim();
    }

    public CsvMessageLog(string? directory = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
    }

    public void Append(PocsagMessage m, string forwarded)
    {
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var file = Path.Combine(Directory, $"pocsag-{m.Timestamp:yyyy-MM-dd}.csv");
            var isNew = !File.Exists(file);
            using var w = new StreamWriter(file, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew));
            if (isNew) w.WriteLine("Zeit;Baud;RIC;SubRIC;Funktion;Typ;Text;Weiterleitung");
            w.WriteLine(string.Join(';',
                m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                m.Baud,
                m.RicFormatted,
                m.SubRic,
                m.Function,
                m.ContentType,
                Quote(m.Text),
                Quote(forwarded)));
        }
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
