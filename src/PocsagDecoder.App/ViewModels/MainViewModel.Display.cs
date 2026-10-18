using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Darstellung: Design, SubRIC-Farben, Alarm-Banner, Schriftgröße und Statusleiste.</summary>
public sealed partial class MainViewModel
{
    private const int MaxAlarms = 5;
    private DispatcherTimer? _clockTimer;
    private DateTime? _lastReceivedAt;
    private string _clock = string.Empty;
    private string _lastReceivedText = "Noch keine Meldung empfangen";

    /// <summary>Aktive Alarme im Banner oben, neueste zuerst.</summary>
    public ObservableCollection<MessageItem> ActiveAlarms { get; } = new();

    public bool HasAlarms => ActiveAlarms.Count > 0;

    /// <summary>Neuester Alarm, groß im Banner.</summary>
    public MessageItem? LatestAlarm => ActiveAlarms.FirstOrDefault();

    /// <summary>Ältere Alarme, kleiner darunter.</summary>
    public IReadOnlyList<MessageItem> OlderAlarms => ActiveAlarms.Skip(1).ToList();

    public RelayCommand DismissAlarmsCommand { get; private set; } = null!;

    private void InitDisplay()
    {
        ThemeManager.Apply(Settings.Theme);
        DismissAlarmsCommand = new RelayCommand(() => ActiveAlarms.Clear());
        ActiveAlarms.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAlarms));
            OnPropertyChanged(nameof(LatestAlarm));
            OnPropertyChanged(nameof(OlderAlarms));
        };

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

    // --- Alarm-Banner -----------------------------------------------------------

    public bool AlarmBanner
    {
        get => Settings.AlarmBanner;
        set
        {
            if (Settings.AlarmBanner == value) return;
            Settings.AlarmBanner = value;
            if (!value) ActiveAlarms.Clear();
            OnPropertyChanged();
            RequestSave();
        }
    }

    public bool AlarmBannerA { get => AlarmOn('A'); set => SetAlarmOn('A', value); }
    public bool AlarmBannerB { get => AlarmOn('B'); set => SetAlarmOn('B', value); }
    public bool AlarmBannerC { get => AlarmOn('C'); set => SetAlarmOn('C', value); }
    public bool AlarmBannerD { get => AlarmOn('D'); set => SetAlarmOn('D', value); }

    private bool AlarmOn(char subRic) => (Settings.AlarmBannerSubRics ?? string.Empty).Contains(subRic, StringComparison.OrdinalIgnoreCase);

    private void SetAlarmOn(char subRic, bool on)
    {
        if (AlarmOn(subRic) == on) return;
        var letters = "ABCD".Where(c => c == subRic ? on : AlarmOn(c));
        Settings.AlarmBannerSubRics = string.Concat(letters);
        OnPropertyChanged("AlarmBanner" + subRic);
        RequestSave();
    }

    /// <summary>Treffer eines Telegrammereignisses: bei passender SubRIC ins Banner, Wiederholungen nicht.</summary>
    private void ShowAlarmIfWanted(MessageItem item)
    {
        if (!Settings.AlarmBanner || !item.Matched || item.IsRepeat) return;
        if (item.Function is < 0 or > 3 || !AlarmOn("ABCD"[item.Function])) return;
        ActiveAlarms.Insert(0, item);
        while (ActiveAlarms.Count > MaxAlarms) ActiveAlarms.RemoveAt(ActiveAlarms.Count - 1);
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
        if (_lastReceivedAt is not { } last) return;
        var ago = now - last;
        var text = ago.TotalSeconds < 60 ? "gerade eben"
            : ago.TotalMinutes < 60 ? $"vor {(int)ago.TotalMinutes} Min."
            : ago.TotalHours < 24 ? $"vor {(int)ago.TotalHours} Std. {ago.Minutes} Min."
            : $"vor {(int)ago.TotalDays} Tagen";
        LastReceivedText = $"Letzter Empfang: {text} ({last:HH:mm:ss})";
    }
}
