using System.Windows;
using System.Windows.Threading;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Fehlermeldungen (Hinweis unten rechts, Schnittstelle), Speicherort der CSV-Dateien, Neustart nach Absturz.</summary>
public sealed partial class MainViewModel
{
    private const string NoTarget = "(nicht senden)";
    private readonly Dictionary<string, DateTime> _lastErrors = new();
    private DispatcherTimer? _watchdog;
    private DispatcherTimer? _restartTimer;
    private DateTime _lastMessageAt = DateTime.Now;
    private bool _silenceReported;

    /// <summary>Für den Hinweis unten rechts (Titel, Text).</summary>
    public event EventHandler<(string Title, string Text)>? SystemError;

    // --- Speicherort ---------------------------------------------------------

    public string LogDirectory
    {
        get => _csv.Directory;
        set
        {
            Settings.LogDirectory = string.Equals(value?.Trim(), CsvMessageLog.DefaultDirectory, StringComparison.OrdinalIgnoreCase) ? "" : value?.Trim() ?? "";
            _csv.Directory = Settings.LogDirectory;
            OnPropertyChanged();
            RequestSave();
        }
    }

    public void ResetLogDirectory() => LogDirectory = "";

    // --- Einstellungen für die Oberfläche -----------------------------------

    /// <summary>Auswahl der Schnittstelle, an die Fehler gesendet werden.</summary>
    public IReadOnlyList<string> ErrorTargetChoices => [NoTarget, .. Targets.Select(t => t.Name)];

    public string ErrorTarget
    {
        get => string.IsNullOrEmpty(Settings.ErrorTargetName) ? NoTarget : Settings.ErrorTargetName;
        set
        {
            if (value is null) return;
            Settings.ErrorTargetName = value == NoTarget ? "" : value;
            OnPropertyChanged();
        }
    }

    public int ErrorSilenceMinutes
    {
        get => Settings.ErrorSilenceMinutes;
        set { Settings.ErrorSilenceMinutes = Math.Max(0, value); _silenceReported = false; OnPropertyChanged(); }
    }

    public RelayCommand TestErrorCommand { get; private set; } = null!;

    private void InitErrorHandling()
    {
        _csv.Directory = Settings.LogDirectory;
        Targets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ErrorTargetChoices));
        TestErrorCommand = new RelayCommand(() =>
        {
            if (MessageBox.Show(Application.Current.MainWindow,
                    "Eine Test-Fehlermeldung auslösen?" + (Settings.ErrorTargetName.Length > 0 ? $"\n\nSie wird auch an „{Settings.ErrorTargetName}“ gesendet." : ""),
                    "Fehlermeldung testen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            ReportError("test", "Test der Fehlermeldung", force: true);
        });

        // Einmal pro Minute prüfen, ob noch Meldungen kommen.
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _watchdog.Tick += (_, _) => CheckSilence();
        _watchdog.Start();
    }

    private void NoteMessageReceived()
    {
        _lastMessageAt = DateTime.Now;
        _silenceReported = false;
    }

    private void CheckSilence()
    {
        var minutes = Settings.ErrorSilenceMinutes;
        if (!IsRunning || minutes <= 0 || _silenceReported) return;
        if (DateTime.Now - _lastMessageAt < TimeSpan.FromMinutes(minutes)) return;
        _silenceReported = true;
        ReportError("silence", $"Seit {minutes} Minuten keine Meldung empfangen. Antenne, Stick und Frequenz prüfen.");
    }

    /// <summary>Der Decoder hat sich unerwartet beendet.</summary>
    private void OnDecoderCrashed()
    {
        _decoderFailed = true;
        if (Settings.ErrorOnDecoder)
            ReportError("decoder", "Der Empfang wurde unerwartet beendet (Stick abgezogen oder Programmfehler?)."
                                   + (Settings.AutoRestart ? " Neustart in 30 Sekunden." : ""));
        if (Settings.AutoRestart) ScheduleRestart();
    }

    private void ScheduleRestart()
    {
        _restartTimer?.Stop();
        _restartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _restartTimer.Tick += (_, _) =>
        {
            _restartTimer?.Stop();
            if (IsRunning) return;
            AddLog("Automatischer Neustart des Empfangs …");
            if (!TryStart(out var error))
            {
                if (Settings.ErrorOnDecoder) ReportError("decoder", "Neustart fehlgeschlagen: " + error + " Nächster Versuch in 30 Sekunden.");
                ScheduleRestart();
            }
        };
        _restartTimer.Start();
    }

    private void CancelRestart() => _restartTimer?.Stop();

    private void OnSendFailed(WebhookTarget target, string detail)
    {
        if (Settings.ErrorOnSend) ReportError("send:" + target.Name, $"Senden an „{target.Name}“ fehlgeschlagen: {detail}");
    }

    private void OnCsvFailed(string detail)
    {
        if (Settings.ErrorOnCsv) ReportError("csv", "CSV-Datei konnte nicht geschrieben werden: " + detail);
    }

    /// <summary>
    /// Meldet einen Fehler im Protokoll, unten rechts und optional an eine Schnittstelle.
    /// Gleiche Fehlerart höchstens alle 10 Minuten, damit nichts überflutet wird.
    /// </summary>
    private void ReportError(string kind, string text, bool force = false)
    {
        AddLog("FEHLER: " + text);
        if (!force)
        {
            if (!Settings.ErrorNotify) return;
            if (_lastErrors.TryGetValue(kind, out var last) && DateTime.Now - last < TimeSpan.FromMinutes(10)) return;
        }
        _lastErrors[kind] = DateTime.Now;

        if (Settings.ErrorTray) SystemError?.Invoke(this, ("POCSAG Decoder – Fehler", text));

        var target = Targets.FirstOrDefault(t => string.Equals(t.Name, Settings.ErrorTargetName, StringComparison.OrdinalIgnoreCase));
        if (target is null || kind == "send:" + target.Name) return; // keine Schleife, wenn genau diese Schnittstelle ausfällt
        _ = SendErrorAsync(target, text);
    }

    private async Task SendErrorAsync(WebhookTarget target, string text)
    {
        var message = new PocsagMessage
        {
            Ric = 0,
            Function = 0,
            Baud = 0,
            ContentType = PocsagContentType.Alpha,
            Text = "Fehler: " + text,
            Raw = "(Systemfehler)",
        };
        var result = await _sender.SendAsync(target, message, "Systemfehler");
        AddLog($"Fehlermeldung an {target.Name}: {result.Detail}");
    }
}
