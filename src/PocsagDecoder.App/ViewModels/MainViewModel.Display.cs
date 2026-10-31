using System.Windows.Threading;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Darstellung: Design, SubRIC-Farben, Schriftgröße und Statusleiste.</summary>
public sealed partial class MainViewModel
{
    private DispatcherTimer? _clockTimer;
    private DateTime? _lastReceivedAt;
    private string _clock = string.Empty;
    private string _lastReceivedText = "Noch keine Meldung empfangen";

    public string ReceiverSummary => IsRunning ? "Aktiv" : "Gestoppt";
    public string FrequencySummary => Settings.Decoder.Mode == Core.Decoding.DecoderSourceMode.CustomCommand
        ? "Eigener Empfangsbefehl" : $"{Settings.Decoder.FrequencyMHz:0.000} MHz";
    public string LastMessageTime => _lastReceivedAt?.ToString("HH:mm:ss") ?? "Noch keine";
    public string PendingSummary => _sender.PendingCount == 0 ? "Keine offenen" : $"{_sender.PendingCount} ausstehend";
    public string QualitySummary => _quality.Percent is { } percent ? $"{percent} % · {_quality.LevelText}" : "Wird ermittelt";
    public string ForwardedSummary => $"{ForwardedCount} erfolgreich · {Messages.Count(m => m.HasError)} mit Fehler (in Liste)";

    public bool CompactReceiveList
    {
        get => Settings.CompactReceiveList;
        set
        {
            if (Settings.CompactReceiveList == value) return;
            Settings.CompactReceiveList = value;
            OnPropertyChanged();
            foreach (var pane in Panes) pane.RefreshColumns();
            RequestSave();
        }
    }

    private void InitDisplay()
    {
        ThemeManager.Apply(Settings.Theme);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
    }

    // --- Design ---------------------------------------------------------------

    public bool ThemeSystem { get => Settings.Theme is not (ThemeManager.Light or ThemeManager.Dark); set { if (value) SetTheme(ThemeManager.FollowSystem); } }
    public bool ThemeLight { get => Settings.Theme == ThemeManager.Light; set { if (value) SetTheme(ThemeManager.Light); } }
    public bool ThemeDark { get => Settings.Theme == ThemeManager.Dark; set { if (value) SetTheme(ThemeManager.Dark); } }

    private void SetTheme(string mode)
    {
        if (Settings.Theme == mode) return;
        Settings.Theme = mode;
        ThemeManager.Apply(mode);
        OnPropertyChanged(nameof(ThemeSystem));
        OnPropertyChanged(nameof(ThemeLight));
        OnPropertyChanged(nameof(ThemeDark));
        RequestSave();
    }

    public bool SubRicColors
    {
        get => Settings.SubRicColors;
        set { if (Settings.SubRicColors == value) return; Settings.SubRicColors = value; OnPropertyChanged(); RequestSave(); }
    }

    /// <summary>Schriftgröße der Telegrammlisten (9 bis 28).</summary>
    public double ListFontSize
    {
        get => Settings.ListFontSize is >= 9 and <= 28 ? Settings.ListFontSize : 12.5;
        set
        {
            var size = Math.Round(Math.Clamp(value, 9, 28) * 2) / 2;
            if (Settings.ListFontSize == size) return;
            Settings.ListFontSize = size;
            OnPropertyChanged();
            RequestSave();
        }
    }

    // --- Statusleiste -----------------------------------------------------------

    public string Clock { get => _clock; private set => Set(ref _clock, value); }

    /// <summary>„Letzter Empfang: vor 3 Min. (12:41:07)“.</summary>
    public string LastReceivedText { get => _lastReceivedText; private set => Set(ref _lastReceivedText, value); }

    private void NoteReceivedForStatus()
    {
        _lastReceivedAt = DateTime.Now;
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Clock = now.ToString("dd.MM.yyyy  HH:mm:ss");
        foreach (var name in new[] { nameof(ReceiverSummary), nameof(FrequencySummary), nameof(LastMessageTime), nameof(PendingSummary), nameof(ForwardedSummary) })
            OnPropertyChanged(name);
        if (_lastReceivedAt is not { } last) return;
        var ago = now - last;
        var text = ago.TotalSeconds < 60 ? "gerade eben"
            : ago.TotalMinutes < 60 ? $"vor {(int)ago.TotalMinutes} Min."
            : ago.TotalHours < 24 ? $"vor {(int)ago.TotalHours} Std. {ago.Minutes} Min."
            : $"vor {(int)ago.TotalDays} Tagen";
        LastReceivedText = $"Letzter Empfang: {text} ({last:HH:mm:ss})";
    }
}
