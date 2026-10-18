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

    /// <summary>Einstellungen wurden aus einer Sicherung ersetzt: Programm neu starten.</summary>
    public event EventHandler? RestartRequested;

    public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
    public string VersionText => $"Version {CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

    private void InitMaintenance()
    {
        CheckUpdatesCommand = new RelayCommand(() => CheckForUpdatesAsync(manual: true));
        OpenUpdateCommand = new RelayCommand(() => OpenUrl(_update?.Url ?? UpdateCheck.ReleasesPage));
        if (Settings.CheckForUpdates)
            _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(() => CheckForUpdatesAsync(manual: false)));
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

    // --- Update-Hinweis ---------------------------------------------------------

    public bool CheckForUpdates
    {
        get => Settings.CheckForUpdates;
        set { if (Settings.CheckForUpdates == value) return; Settings.CheckForUpdates = value; OnPropertyChanged(); RequestSave(); }
    }

    public bool HasUpdate => _update is not null;
    public string UpdateText => _update is null ? string.Empty : $"Neue Version {_update.Version.ToString(3)} verfügbar";
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }

    public RelayCommand CheckUpdatesCommand { get; private set; } = null!;
    public RelayCommand OpenUpdateCommand { get; private set; } = null!;

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (manual) UpdateStatus = "Suche nach Updates …";
        try
        {
            _update = await UpdateCheck.FindNewerAsync(CurrentVersion);
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateText));
            if (_update is not null) AddLog($"{UpdateText}: {_update.Url}");
            UpdateStatus = _update is null
                ? $"Du hast die neueste Version ({VersionText.Replace("Version ", "")}). Geprüft: {DateTime.Now:HH:mm}"
                : $"{UpdateText} – Klick auf den Hinweis unten in der Statusleiste öffnet die Download-Seite.";
        }
        catch (Exception ex)
        {
            if (manual) UpdateStatus = "Update-Suche fehlgeschlagen: " + ex.Message;
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception) { /* kein Browser verfügbar */ }
    }
}
