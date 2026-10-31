using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Empfangsqualität, Sicherung der Einstellungen und Update-Hinweis.</summary>
public sealed partial class MainViewModel
{
    private readonly ReceptionQuality _quality = new();
    private bool _skipSaveOnExit;
    private UpdateInfo? _update;
    private string _updateStatus = string.Empty;
    private string? _updateFile;
    private int? _downloadPercent;
    private bool _downloading;
    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private System.Windows.Threading.DispatcherTimer? _logTimer;
    private string _logUsageText = string.Empty;
    private bool _cleaningLogs;

    /// <summary>Einstellungen wurden aus einer Sicherung ersetzt: Programm neu starten.</summary>
    public event EventHandler? RestartRequested;

    public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
    public string VersionText => $"Version {CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

    private void InitMaintenance()
    {
        CheckUpdatesCommand = new RelayCommand(() => CheckForUpdatesAsync(manual: true));
        OpenUpdateCommand = new RelayCommand(OpenUpdate);
        if (Settings.CheckForUpdates)
            _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(() => CheckForUpdatesAsync(manual: false)));
        // Das Programm läuft oft tagelang im Hintergrund: zweimal am Tag erneut nachsehen.
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(12) };
        _updateTimer.Tick += async (_, _) => { if (Settings.CheckForUpdates && _updateFile is null) await CheckForUpdatesAsync(manual: false); };
        _updateTimer.Start();

        // Protokolle aufräumen: kurz nach dem Start und danach stündlich (nach Mitternacht wird der Vortag komprimiert).
        CleanUpLogsCommand = new RelayCommand(() => CleanUpLogsAsync(manual: true));
        _logTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _logTimer.Tick += async (_, _) => await CleanUpLogsAsync(manual: false);
        _logTimer.Start();
        _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(() => CleanUpLogsAsync(manual: false)));
    }

    // --- Empfangsqualität -------------------------------------------------------

    /// <summary>0 = unbekannt, 1 = schlecht, 2 = mittel, 3 = gut</summary>
    public int QualityLevel => _quality.Level;

    public string QualityText => _quality.Percent is { } p
        ? $"Empfang {_quality.LevelText} ({p} %)"
        : "Empfangsqualität: wird ermittelt";

    public string QualityToolTip =>
        "BOS-Alarme werden meist zweimal gesendet. Kommt die Wiederholung mit genau demselben Text an, war der Empfang fehlerfrei; "
        + "weicht sie in einzelnen Zeichen ab, gab es Bitfehler.\n"
        + $"Letzte {_quality.Samples} Wiederholungen: {_quality.Samples - _quality.Faulty} fehlerfrei, {_quality.Faulty} mit Fehlern.\n"
        + "Ab 95 % gut, ab 80 % mittel, darunter schlecht. Abhilfe: Antenne, Gain, ppm-Korrektur oder Fehlerkorrektur prüfen.";

    private void NoteQuality(PocsagMessage message)
    {
        if (message.Raw == "(Testnachricht)") return;
        _quality.Add(message);
        OnPropertyChanged(nameof(QualityLevel));
        OnPropertyChanged(nameof(QualityText));
        OnPropertyChanged(nameof(QualityToolTip));
    }

    // --- Sicherung --------------------------------------------------------------

    /// <summary>Alle Einstellungen (Telegrammereignisse, Schnittstellen, RIC-Liste, Fenster …) in eine Datei.</summary>
    public void CreateBackup(string path)
    {
        if (_settingsReadOnly)
        {
            MessageBox.Show(Application.Current.MainWindow,
                "Die ursprünglichen Einstellungen konnten nicht geladen werden. Bitte zuerst eine gültige Sicherung wiederherstellen. "
                + "Eine Sicherung der vorübergehenden Standardwerte wird nicht erstellt.",
                "Sicherung erstellen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Save(quiet: true);
            SettingsStore.Save(Settings, path);
            AddLog("Sicherung erstellt: " + path);
            MessageBox.Show(Application.Current.MainWindow,
                $"Sicherung gespeichert:\n{path}\n\n{BackupSummary(Settings)}\n\nAchtung: Die Datei enthält auch Webhook-Adressen und Passwörter der Schnittstellen.",
                "Sicherung erstellen", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current.MainWindow, "Die Sicherung konnte nicht gespeichert werden:\n" + ex.Message, "Sicherung erstellen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Einstellungen aus einer Sicherung übernehmen; danach startet das Programm neu.</summary>
    public void RestoreBackup(string path)
    {
        AppSettings backup;
        try
        {
            backup = SettingsStore.Load(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current.MainWindow, "Die Datei ist keine gültige Sicherung:\n" + ex.Message, "Sicherung wiederherstellen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(Application.Current.MainWindow,
            $"Sicherung enthält:\n{BackupSummary(backup)}\n\nAlle jetzigen Einstellungen werden dadurch ersetzt. "
            + "Die bisherigen werden vorher als settings-vorher.json im Einstellungsordner aufgehoben.\n\n"
            + "Das Programm startet danach neu. Fortfahren?",
            "Sicherung wiederherstellen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            Save(quiet: true);
            var current = SettingsStore.DefaultPath;
            if (File.Exists(current))
                File.Copy(current, Path.Combine(SettingsStore.DefaultDirectory, "settings-vorher.json"), overwrite: true);
            SettingsStore.Save(backup, current);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current.MainWindow, "Die Sicherung konnte nicht übernommen werden:\n" + ex.Message, "Sicherung wiederherstellen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _skipSaveOnExit = true;
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string BackupSummary(AppSettings s) =>
        $"• {s.Rules.Count} Telegrammereignisse\n• {s.Targets.Count} Schnittstellen\n• {s.RicEntries.Count} RIC-Beschreibungen\n• {s.ReceivePanes.Count} Fenster unter Telegramme";

    // --- Platz sparen bei den Protokollen ---------------------------------------

    public bool LogCompress
    {
        get => Settings.LogCompress;
        set
        {
            if (Settings.LogCompress == value) return;
            Settings.LogCompress = value;
            OnPropertyChanged();
            RequestSave();
            if (value) _ = CleanUpLogsAsync(manual: false);
        }
    }

    /// <summary>0 = alles behalten.</summary>
    public int LogKeepDays
    {
        get => Settings.LogKeepDays;
        set
        {
            var days = Math.Clamp(value, 0, 36500);
            if (Settings.LogKeepDays == days) return;
            Settings.LogKeepDays = days;
            OnPropertyChanged();
            RequestSave();
        }
    }

    public string LogUsageText { get => _logUsageText; private set => Set(ref _logUsageText, value); }

    public RelayCommand CleanUpLogsCommand { get; private set; } = null!;

    private async Task CleanUpLogsAsync(bool manual)
    {
        if (_cleaningLogs) return;
        _cleaningLogs = true;
        var directory = _csv.Directory;
        var compress = Settings.LogCompress;
        var keep = Settings.LogKeepDays;
        try
        {
            var (result, deleted, usage) = await Task.Run(() =>
            {
                var r = compress ? LogMaintenance.Compress(directory, DateTime.Today) : new LogMaintenance.Result(0, 0, 0);
                var d = LogMaintenance.DeleteOld(directory, DateTime.Today, keep);
                return (r, d, LogMaintenance.Usage(directory));
            });
            if (result.Compressed > 0)
                AddLog($"Protokoll: {result.Compressed} Tag(e) komprimiert, {FormatBytes(result.SavedBytes)} gespart.");
            if (deleted > 0)
                AddLog($"Protokoll: {deleted} Datei(en) älter als {keep} Tage gelöscht.");
            LogUsageText = $"Belegt: {usage.Days} Tag(e), {FormatBytes(usage.Bytes)}"
                + (manual ? $" · aufgeräumt {DateTime.Now:HH:mm}" : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AddLog("Protokoll aufräumen fehlgeschlagen: " + ex.Message);
            if (manual) LogUsageText = "Aufräumen fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _cleaningLogs = false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    // --- Update-Hinweis ---------------------------------------------------------

    public bool CheckForUpdates
    {
        get => Settings.CheckForUpdates;
        set { if (Settings.CheckForUpdates == value) return; Settings.CheckForUpdates = value; OnPropertyChanged(); RequestSave(); }
    }

    public bool AutoDownloadUpdates
    {
        get => Settings.AutoDownloadUpdates;
        set { if (Settings.AutoDownloadUpdates == value) return; Settings.AutoDownloadUpdates = value; OnPropertyChanged(); RequestSave(); }
    }

    public bool HasUpdate => _update is not null;

    /// <summary>Neue Version ist heruntergeladen und geprüft, ein Klick installiert sie.</summary>
    public bool UpdateReady => _updateFile is not null;

    public string UpdateText => _update is null ? string.Empty
        : _updateFile is not null ? $"Version {_update.Version.ToString(3)} bereit – jetzt aktualisieren"
        : _downloading ? $"Version {_update.Version.ToString(3)} wird geladen" + (_downloadPercent is { } p ? $" ({p} %)" : " …")
        : $"Neue Version {_update.Version.ToString(3)} verfügbar";

    public string UpdateToolTip => UpdateReady
        ? "Programm wird ersetzt und neu gestartet. Einstellungen bleiben erhalten, der Empfang ist nur ein paar Sekunden unterbrochen."
        : "Download-Seite auf GitHub öffnen";

    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }

    public RelayCommand CheckUpdatesCommand { get; private set; } = null!;
    public RelayCommand OpenUpdateCommand { get; private set; } = null!;

    private void RaiseUpdateChanged()
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateReady));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(UpdateToolTip));
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (manual) UpdateStatus = "Suche nach Updates …";
        try
        {
            if (_downloading) return;
            var latest = await UpdateCheck.FindLatestAsync(CurrentVersion, PreferredUpdateAsset());
            var found = latest is not null && UpdateCheck.IsNewer(latest.Version, CurrentVersion) ? latest : null;
            if (found?.Version != _update?.Version) _updateFile = null;
            _update = found;
            RaiseUpdateChanged();
            if (_update is null)
            {
                UpdateStatus = $"Du hast die neueste Version ({VersionText.Replace("Version ", "")}"
                    + (latest is null ? ", auf GitHub ist noch keine veröffentlicht" : $", auf GitHub: {latest.Version.ToString(3)}") + $"). Geprüft: {DateTime.Now:HH:mm}";
                return;
            }
            AddLog($"{UpdateText}: {_update.Url}");
            if (_updateFile is null && Settings.AutoDownloadUpdates && _update.DownloadUrl is not null)
                await DownloadUpdateAsync();
            else if (_updateFile is null)
                UpdateStatus = $"{UpdateText}. Klick auf den Hinweis unten in der Statusleiste "
                    + (_update.DownloadUrl is null ? "öffnet die Download-Seite." : "lädt sie herunter und installiert sie.");
        }
        catch (Exception ex)
        {
            if (manual) UpdateStatus = "Update-Suche fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task DownloadUpdateAsync()
    {
        if (_update is null || _downloading) return;
        _downloading = true;
        _downloadPercent = null;
        RaiseUpdateChanged();
        UpdateStatus = UpdateText;
        var progress = new Progress<double>(f =>
        {
            var p = (int)(f * 100);
            if (p == _downloadPercent) return;
            _downloadPercent = p;
            OnPropertyChanged(nameof(UpdateText));
        });
        try
        {
            _updateFile = await UpdateInstaller.DownloadAsync(_update, progress: progress);
            AddLog($"Version {_update.Version.ToString(3)} heruntergeladen und geprüft.");
            UpdateStatus = $"Version {_update.Version.ToString(3)} ist heruntergeladen. Klick auf „Jetzt aktualisieren“ (oder unten in der Statusleiste) installiert sie.";
        }
        catch (Exception ex)
        {
            AddLog("Update-Download fehlgeschlagen: " + ex.Message);
            UpdateStatus = "Download fehlgeschlagen: " + ex.Message + " Ein Klick auf den Hinweis versucht es erneut.";
        }
        finally
        {
            _downloading = false;
            RaiseUpdateChanged();
        }
    }

    /// <summary>Klick auf den Update-Hinweis: herunterladen bzw. installieren.</summary>
    private async Task OpenUpdate()
    {
        if (_update is null) { OpenUrl(UpdateCheck.ReleasesPage); return; }
        if (_downloading) return;
        if (_update.DownloadUrl is null) { OpenUrl(_update.Url); return; }
        if (_updateFile is null)
        {
            await DownloadUpdateAsync();
            if (_updateFile is null) return;
        }
        InstallUpdate();
    }

    private void InstallUpdate()
    {
        if (_update is null || _updateFile is null) return;
        var exe = Environment.ProcessPath;
        if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            OpenUrl(_update.Url);
            return;
        }
        var answer = MessageBox.Show(Application.Current.MainWindow,
            $"Jetzt auf Version {_update.Version.ToString(3)} aktualisieren?\n\n"
            + "Das Programm wird ersetzt und startet neu. Deine Einstellungen bleiben erhalten, der Empfang ist nur ein paar Sekunden unterbrochen.",
            "Update installieren", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            Save(quiet: true);
            UpdateInstaller.Install(_updateFile, exe);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current.MainWindow,
                $"Das Update konnte nicht installiert werden:\n{ex.Message}\n\nDie Download-Seite wird geöffnet, dort kannst du die neue Version von Hand herunterladen.",
                "Update installieren", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenUrl(_update.Url);
            return;
        }
        AddLog($"Update auf Version {_update.Version.ToString(3)} installiert, Neustart.");
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Wer die große Exe mit eingebauter .NET-Laufzeit benutzt, bekommt auch beim Update die große.</summary>
    private static string PreferredUpdateAsset()
    {
        try
        {
            return Environment.ProcessPath is { } exe && new FileInfo(exe).Length > 30_000_000 ? UpdateCheck.SelfContainedAsset : UpdateCheck.SmallAsset;
        }
        catch (IOException) { return UpdateCheck.SmallAsset; }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception) { /* kein Browser verfügbar */ }
    }
}
