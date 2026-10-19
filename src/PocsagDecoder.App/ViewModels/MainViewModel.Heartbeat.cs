using System.Windows;
using System.Windows.Threading;
using PocsagDecoder.Core.Forwarding;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Heartbeat: regelmäßiges Lebenszeichen an eine Schnittstelle.</summary>
public sealed partial class MainViewModel
{
    private DispatcherTimer? _heartbeatTimer;
    private DateTime? _lastHeartbeatAt;
    private DateTime? _lastReceivedAt;
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
        _heartbeatTimer.Tick += (_, _) => CheckHeartbeat();
        _heartbeatTimer.Start();
    }

    /// <summary>Für {last}: wann zuletzt wirklich eine Meldung kam.</summary>
    private void NoteHeartbeatMessage() => _lastReceivedAt = DateTime.Now;

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
}
