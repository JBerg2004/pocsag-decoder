using System.Windows;
using System.Windows.Threading;
using PocsagDecoder.Core.Forwarding;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Heartbeat: regelmäßiges Lebenszeichen an eine Schnittstelle.</summary>
public sealed partial class MainViewModel
{
    private DispatcherTimer? _heartbeatTimer;
    private DateTime? _lastHeartbeatAt;
    private string _heartbeatStatus = "Noch kein Heartbeat gesendet.";

    public RelayCommand SendHeartbeatCommand { get; private set; } = null!;

    public IReadOnlyList<KeyValuePair<string, string>> HeartbeatPlaceholders { get; } =
        Heartbeat.PlaceholderHelp.Select(kv => new KeyValuePair<string, string>("{" + kv.Key + "}", kv.Value)).ToList();

    public bool HeartbeatEnabled
    {
        get => Settings.HeartbeatEnabled;
        set
        {
            if (Settings.HeartbeatEnabled == value) return;
            Settings.HeartbeatEnabled = value;
            _lastHeartbeatAt = null; // beim Einschalten gleich das erste senden
            OnPropertyChanged();
            RequestSave();
        }
    }

    public int HeartbeatMinutes
    {
        get => Settings.HeartbeatMinutes;
        set { Settings.HeartbeatMinutes = Math.Clamp(value, 1, 24 * 60); OnPropertyChanged(); RequestSave(); }
    }

    /// <summary>Auswahl der Schnittstelle; dieselbe Liste wie bei den Fehlermeldungen.</summary>
    public string HeartbeatTarget
    {
        get => string.IsNullOrEmpty(Settings.HeartbeatTargetName) ? NoTarget : Settings.HeartbeatTargetName;
        set
        {
            if (value is null) return;
            Settings.HeartbeatTargetName = value == NoTarget ? "" : value;
            OnPropertyChanged();
            RequestSave();
        }
    }

    public bool HeartbeatOnlyWhileReceiving
    {
        get => Settings.HeartbeatOnlyWhileReceiving;
        set { if (Settings.HeartbeatOnlyWhileReceiving == value) return; Settings.HeartbeatOnlyWhileReceiving = value; OnPropertyChanged(); RequestSave(); }
    }

    public string HeartbeatText
    {
        get => Settings.HeartbeatText;
        set { Settings.HeartbeatText = value ?? ""; OnPropertyChanged(); RequestSave(); }
    }

    public string HeartbeatRic
    {
        get => Settings.HeartbeatRic == 0 ? "" : Settings.HeartbeatRic.ToString("D7");
        set
        {
            Settings.HeartbeatRic = int.TryParse(value?.Trim(), out var ric) ? Math.Clamp(ric, 0, 2_097_151) : 0;
            OnPropertyChanged();
            RequestSave();
        }
    }

    public string HeartbeatSubRic
    {
        get => Settings.HeartbeatSubRic;
        set { if (value is null) return; Settings.HeartbeatSubRic = value; OnPropertyChanged(); RequestSave(); }
    }

    public string HeartbeatStatus { get => _heartbeatStatus; private set => Set(ref _heartbeatStatus, value); }

    private void InitHeartbeat()
    {
        SendHeartbeatCommand = new RelayCommand(() =>
        {
            if (FindHeartbeatTarget() is null)
            {
                MessageBox.Show(Application.Current.MainWindow, "Bitte zuerst eine Schnittstelle auswählen.", "Heartbeat",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _ = SendHeartbeatAsync();
        });

        // Alle 30 Sekunden prüfen, ob das nächste Lebenszeichen fällig ist.
        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _heartbeatTimer.Tick += (_, _) => { CheckHeartbeat(); CheckHeartbeatUrl(); };
        _heartbeatTimer.Start();
    }

    private WebhookTarget? FindHeartbeatTarget() =>
        Targets.FirstOrDefault(t => string.Equals(t.Name, Settings.HeartbeatTargetName, StringComparison.OrdinalIgnoreCase));

    private void CheckHeartbeat()
    {
        if (!Settings.HeartbeatEnabled || FindHeartbeatTarget() is null) return;
        if (Settings.HeartbeatOnlyWhileReceiving && !IsRunning) return;
        if (!Heartbeat.IsDue(_lastHeartbeatAt, DateTime.Now, Settings.HeartbeatMinutes)) return;
        _ = SendHeartbeatAsync();
    }

    private async Task SendHeartbeatAsync()
    {
        var target = FindHeartbeatTarget();
        if (target is null) return;
        var now = DateTime.Now;
        _lastHeartbeatAt = now;
        var message = Heartbeat.CreateMessage(Settings, IsRunning, _lastReceivedAt, now, Environment.MachineName);
        var result = await _sender.SendAsync(target, message, Heartbeat.RuleName);
        HeartbeatStatus = $"Letzter Heartbeat {now:HH:mm:ss} an „{target.Name}“: {(result.Success ? "OK" : "fehlgeschlagen")} ({result.Detail})";
        if (result.Success) AddLog($"Heartbeat an {target.Name}: {result.Detail}");
        else OnSendFailed(target, result.Detail);
    }
    // --- Heartbeat an eine URL (healthchecks.io, Home-Assistant-Webhook …) ---------------

    private readonly HeartbeatPing _ping = new();
    private DateTime? _lastPingAt;
    private bool _pingRunning;

    /// <summary>Empfang ist abgebrochen oder ließ sich nicht starten (Stick abgezogen?), bis zum nächsten Start.</summary>
    private bool _decoderFailed;
    private string _heartbeatUrlStatus = "Noch kein Heartbeat an die URL gesendet.";

    public RelayCommand TestHeartbeatUrlCommand => _testHeartbeatUrl ??= new RelayCommand(TestHeartbeatUrl);
    private RelayCommand? _testHeartbeatUrl;

    public bool HeartbeatUrlEnabled
    {
        get => Settings.HeartbeatUrlEnabled;
        set
        {
            if (Settings.HeartbeatUrlEnabled == value) return;
            Settings.HeartbeatUrlEnabled = value;
            _lastPingAt = null;
            OnPropertyChanged();
            RequestSave();
        }
    }

    public string HeartbeatUrl
    {
        get => Settings.HeartbeatUrl;
        set { Settings.HeartbeatUrl = value?.Trim() ?? ""; OnPropertyChanged(); RequestSave(); }
    }

    public string HeartbeatFailUrl
    {
        get => Settings.HeartbeatFailUrl;
        set { Settings.HeartbeatFailUrl = value?.Trim() ?? ""; OnPropertyChanged(); RequestSave(); }
    }

    public int HeartbeatUrlMinutes
    {
        get => Settings.HeartbeatUrlMinutes;
        set { Settings.HeartbeatUrlMinutes = Math.Clamp(value, 1, 24 * 60); OnPropertyChanged(); RequestSave(); }
    }

    public string HeartbeatUrlStatus { get => _heartbeatUrlStatus; private set => Set(ref _heartbeatUrlStatus, value); }

    /// <summary>Aktueller Zustand für den Heartbeat: Stick, letzte Meldung, Warteschlange.</summary>
    private HeartbeatState CurrentHeartbeatState() => new()
    {
        Pc = Environment.MachineName,
        Stick = IsRunning ? HeartbeatState.StickOk : _decoderFailed ? HeartbeatState.StickError : HeartbeatState.StickStopped,
        LastMessageAt = _lastReceivedAt,
        QueueLength = _sender.PendingCount,
        Time = DateTime.Now,
    };

    private void CheckHeartbeatUrl()
    {
        if (!Settings.HeartbeatUrlEnabled || string.IsNullOrWhiteSpace(Settings.HeartbeatUrl)) return;
        if (!Heartbeat.IsDue(_lastPingAt, DateTime.Now, Settings.HeartbeatUrlMinutes)) return;
        _ = SendHeartbeatUrlAsync();
    }

    private void TestHeartbeatUrl()
    {
        if (string.IsNullOrWhiteSpace(Settings.HeartbeatUrl))
        {
            MessageBox.Show(Application.Current.MainWindow, "Bitte zuerst eine URL eintragen.", "Heartbeat",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _ = SendHeartbeatUrlAsync();
    }

    private async Task SendHeartbeatUrlAsync()
    {
        if (_pingRunning) return;
        _pingRunning = true;
        _lastPingAt = DateTime.Now;
        try
        {
            var state = CurrentHeartbeatState();
            var result = await _ping.SendAsync(Settings.HeartbeatUrl, Settings.HeartbeatFailUrl, state);
            var where = result.UsedFailUrl && result.Success ? "Fehler-URL" : "URL";
            HeartbeatUrlStatus = $"Letzter Heartbeat {state.Time:HH:mm:ss} an {where} (Stick {state.Stick}, Warteschlange {state.QueueLength}): "
                                 + $"{(result.Success ? "OK" : "fehlgeschlagen")} ({result.Detail})";
            if (result.Success) AddLog($"Heartbeat an {where}: {result.Detail}");
            else
            {
                AddLog($"Heartbeat an URL fehlgeschlagen: {result.Detail}");
                if (Settings.ErrorOnSend) ReportError("heartbeat", "Heartbeat an die URL fehlgeschlagen: " + result.Detail);
            }
        }
        finally { _pingRunning = false; }
    }
}
