using System.Text.Json;
using System.Text.Json.Serialization;
using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Descriptions;
using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Forwarding;

namespace PocsagDecoder.Core.Settings;

/// <summary>Ein Fenster im Reiter Telegramme.</summary>
public sealed class ReceivePaneSettings
{
    public string Title { get; set; } = "Alle Meldungen";
    public string Ric { get; set; } = string.Empty;
    public string SubRic { get; set; } = "Alle";
    public string Text { get; set; } = string.Empty;

    /// <summary>Sucht in Bezeichnung, Kurzbezeichnung, Ort und SubRIC-Bedeutung.</summary>
    public string Description { get; set; } = string.Empty;

    public bool OnlyMatched { get; set; }

    /// <summary>Wiederholungen derselben Meldung ausblenden.</summary>
    public bool HideRepeats { get; set; }

    /// <summary>Name eines Telegrammereignisses; leer = alle Meldungen.</summary>
    public string Event { get; set; } = string.Empty;
    public List<string> HiddenColumns { get; set; } = new();
}

public sealed class AppSettings
{
    public DecoderOptions Decoder { get; set; } = new();
    public List<FilterRule> Rules { get; set; } = new();
    public List<WebhookTarget> Targets { get; set; } = new();
    /// <summary>RIC-Verzeichnis: Bezeichnung und SubRIC A–D je RIC.</summary>
    public List<RicEntry> RicEntries { get; set; } = new();

    /// <summary>Allgemeine Bedeutung der SubRICs A, B, C, D für alle RICs.</summary>
    public List<string> GlobalSubRics { get; set; } = ["", "", "", ""];

    /// <summary>Fenster im Reiter Telegramme, jedes mit eigenem Filter und eigenen Spalten.</summary>
    public List<ReceivePaneSettings> ReceivePanes { get; set; } = new();

    /// <summary>Anordnung der Fenster: Untereinander, Nebeneinander oder Raster.</summary>
    public string PaneLayout { get; set; } = "Untereinander";

    /// <summary>Älteres Format (ein Fenster), wird in das erste Fenster übernommen.</summary>
    public List<string> HiddenColumns { get; set; } = new();

    /// <summary>Älteres Format, wird beim Laden in <see cref="RicEntries"/> übernommen.</summary>
    public List<RicDescription>? Descriptions { get; set; }

    /// <summary>Wiederholungen gleicher Nachrichten innerhalb dieses Fensters nicht erneut weiterleiten (0 = aus).</summary>
    public int DuplicateWindowSeconds { get; set; } = 60;

    /// <summary>Maximal angezeigte Nachrichten in der Liste.</summary>
    public int MaxMessages { get; set; } = 5000;

    /// <summary>Alle empfangenen Nachrichten zusätzlich als CSV-Tagesdatei speichern.</summary>
    public bool LogToCsv { get; set; } = true;

    /// <summary>Ordner für die CSV-Tagesdateien; leer = Standardordner.</summary>
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>Bei Systemfehlern benachrichtigen.</summary>
    public bool ErrorNotify { get; set; } = true;

    /// <summary>Fehler als Hinweis unten rechts anzeigen.</summary>
    public bool ErrorTray { get; set; } = true;

    /// <summary>Fehler zusätzlich an diese Schnittstelle senden (leer = nicht senden).</summary>
    public string ErrorTargetName { get; set; } = string.Empty;

    public bool ErrorOnDecoder { get; set; } = true;
    public bool ErrorOnSend { get; set; } = true;
    public bool ErrorOnCsv { get; set; } = true;

    /// <summary>Melden, wenn so viele Minuten keine Meldung kam (0 = aus).</summary>
    public int ErrorSilenceMinutes { get; set; }

    /// <summary>Empfang nach einem Absturz automatisch neu starten.</summary>
    public bool AutoRestart { get; set; }

    public bool AutoStart { get; set; }

    /// <summary>Beim Minimieren nur noch als Symbol im Infobereich anzeigen.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Schließen (X) blendet das Fenster nur aus, der Empfang läuft weiter.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Beim Programmstart direkt im Infobereich starten.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Hinweis im Infobereich bei Regeltreffern, solange das Fenster ausgeblendet ist.</summary>
    public bool TrayNotifications { get; set; } = true;

    /// <summary>SubRIC-Buchstaben klein (a–d) anzeigen und in {subric} senden.</summary>
    public bool LowercaseSubRic { get; set; }

    /// <summary>Design: System (wie Windows), Light oder Dark.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>SubRIC in den Telegrammlisten farbig kennzeichnen (A rot, B orange, C blau, D grau).</summary>
    public bool SubRicColors { get; set; } = true;

    /// <summary>Schriftgröße der Telegrammlisten.</summary>
    public double ListFontSize { get; set; } = 12.5;

    /// <summary>Beim Programmstart auf GitHub nach einer neueren Version suchen.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Gefundene neue Version gleich herunterladen; installiert wird erst nach Klick.</summary>
    public bool AutoDownloadUpdates { get; set; } = true;

    public static AppSettings CreateDefault() => new()
    {
        Targets =
        {
            new WebhookTarget { Name = "Beispiel-Webhook", Enabled = false, Url = "https://example.org/webhook" },
        },
        Rules =
        {
            new FilterRule { Name = "Alle Nachrichten", Enabled = false, RicPattern = "*", TargetName = "Beispiel-Webhook" },
        },
    };
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PocsagDecoder");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return AppSettings.CreateDefault();
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.CreateDefault();
        if (settings.Descriptions is { Count: > 0 } && settings.RicEntries.Count == 0)
            settings.RicEntries = RicDirectory.FromLegacy(settings.Descriptions);
        settings.Descriptions = null;
        return settings;
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
