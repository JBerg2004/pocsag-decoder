using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Descriptions;
using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly WebhookSender _sender = new();
    private readonly DuplicateFilter _duplicates = new();

    /// <summary>Markiert Wiederholungen für die Anzeige (unabhängig von Telegrammereignissen).</summary>
    private readonly DuplicateFilter _repeatMarker = new();

    private TimeSpan RepeatWindow => TimeSpan.FromSeconds(Settings.DuplicateWindowSeconds > 0 ? Settings.DuplicateWindowSeconds : 30);
    private readonly CsvMessageLog _csv = new();
    private readonly RicDirectory _directory = new();
    private RicEntry? _selectedDescription;
    private string _descriptionSearch = string.Empty;
    private string _descriptionStatus = string.Empty;
    private DecoderPipeline? _pipeline;

    private bool _isRunning;
    private string _statusText = "Gestoppt";
    private int _receivedCount;
    private int _forwardedCount;
    private FilterRule? _selectedRule;
    private WebhookTarget? _selectedTarget;
    private MessageItem? _selectedMessage;
    private string _ruleError = string.Empty;
    private string _targetTestResult = string.Empty;
    private string _simRic = "1234567";
    private string _simSubRic = "A";
    private string _simText = "Probealarm";

    public MainViewModel()
    {
        Settings = LoadSettings();
        Rules = new ObservableCollection<FilterRule>(Settings.Rules);
        Targets = new ObservableCollection<WebhookTarget>(Settings.Targets);

        Descriptions = new ObservableCollection<RicEntry>(Settings.RicEntries);
        foreach (var d in Descriptions) d.PropertyChanged += OnDescriptionChanged;
        Descriptions.CollectionChanged += (_, e) =>
        {
            foreach (RicEntry d in e.NewItems ?? Array.Empty<RicEntry>()) d.PropertyChanged += OnDescriptionChanged;
            foreach (RicEntry d in e.OldItems ?? Array.Empty<RicEntry>()) d.PropertyChanged -= OnDescriptionChanged;
            RebuildDirectory();
        };
        DescriptionsView = new ListCollectionView(Descriptions)
        {
            Filter = o => o is RicEntry d && DescriptionFilter(d),
        };
        DescriptionsView.SortDescriptions.Add(new SortDescription(nameof(RicEntry.Ric), ListSortDirection.Ascending));
        _directory.SetGlobalSubRics(Settings.GlobalSubRics);
        RebuildDirectory();

        InitPanes();
        InitErrorHandling();
        InitTargets();

        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        ClearMessagesCommand = new RelayCommand(() => Messages.Clear());
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());
        SaveCommand = new RelayCommand(Save);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        SimulateCommand = new RelayCommand(Simulate);

        AddRuleCommand = new RelayCommand(AddRule);
        RuleFromMessageCommand = new RelayCommand(RuleFromMessage, () => SelectedMessage is not null);
        RemoveRuleCommand = new RelayCommand(() => { if (SelectedRule is not null) Rules.Remove(SelectedRule); }, () => SelectedRule is not null);
        MoveRuleUpCommand = new RelayCommand(() => MoveRule(-1), () => SelectedRule is not null && Rules.IndexOf(SelectedRule) > 0);
        MoveRuleDownCommand = new RelayCommand(() => MoveRule(1), () => SelectedRule is not null && Rules.IndexOf(SelectedRule) < Rules.Count - 1);

        AddTargetCommand = new RelayCommand(AddTarget);
        RemoveTargetCommand = new RelayCommand(() => { if (SelectedTarget is not null) Targets.Remove(SelectedTarget); }, () => SelectedTarget is not null);
        TestTargetCommand = new RelayCommand(TestTargetAsync, () => SelectedTarget is not null);

        AddDescriptionCommand = new RelayCommand(AddDescription);
        DescriptionFromMessageCommand = new RelayCommand(DescriptionFromMessage, () => SelectedMessage is not null);
        RemoveDescriptionCommand = new RelayCommand(() => { if (SelectedDescription is not null) { EndDescriptionEdit(); Descriptions.Remove(SelectedDescription); } }, () => SelectedDescription is not null);
        ClearDescriptionsCommand = new RelayCommand(ClearDescriptions, () => Descriptions.Count > 0);

        SelectedRule = Rules.FirstOrDefault();
        SelectedTarget = Targets.FirstOrDefault();
        _duplicates.Window = TimeSpan.FromSeconds(Settings.DuplicateWindowSeconds);
        _repeatMarker.Window = RepeatWindow;
        TemplateRenderer.LowercaseSubRic = Settings.LowercaseSubRic;
    }

    public AppSettings Settings { get; }
    public DecoderOptions Decoder => Settings.Decoder;

    public ObservableCollection<MessageItem> Messages { get; } = new();
    public ObservableCollection<FilterRule> Rules { get; }
    public ObservableCollection<WebhookTarget> Targets { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<RicEntry> Descriptions { get; }
    public ListCollectionView DescriptionsView { get; }

    public IReadOnlyList<int> ErrorCorrectionLevels { get; } = [0, 1, 2];
    public IReadOnlyList<string> SubRicChoices { get; } = ["Alle", "A", "B", "C", "D"];
    public IReadOnlyList<string> SimSubRicChoices { get; } = ["A", "B", "C", "D"];
    public IReadOnlyList<string> DecodeModes { get; } = ["alpha", "auto", "numeric", "skyper"];
    public IReadOnlyList<string> HttpMethods { get; } = ["POST", "PUT", "PATCH", "GET"];
    public IReadOnlyList<string> ContentTypes { get; } = ["application/json", "application/x-www-form-urlencoded", "text/plain", "application/xml"];
    public IReadOnlyList<TextMatchMode> TextModes { get; } = Enum.GetValues<TextMatchMode>();
    public IReadOnlyList<KeyValuePair<string, string>> Placeholders { get; } =
        TemplateRenderer.PlaceholderHelp.Select(kv => new KeyValuePair<string, string>("{" + kv.Key + "}", kv.Value)).ToList();

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ClearMessagesCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand SimulateCommand { get; }
    public RelayCommand AddRuleCommand { get; }
    public RelayCommand RuleFromMessageCommand { get; }
    public RelayCommand RemoveRuleCommand { get; }
    public RelayCommand MoveRuleUpCommand { get; }
    public RelayCommand MoveRuleDownCommand { get; }
    public RelayCommand AddTargetCommand { get; }
    public RelayCommand RemoveTargetCommand { get; }
    public RelayCommand TestTargetCommand { get; }
    public RelayCommand AddDescriptionCommand { get; }
    public RelayCommand DescriptionFromMessageCommand { get; }
    public RelayCommand RemoveDescriptionCommand { get; }
    public RelayCommand ClearDescriptionsCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public int ReceivedCount { get => _receivedCount; private set => Set(ref _receivedCount, value); }
    public int ForwardedCount { get => _forwardedCount; private set => Set(ref _forwardedCount, value); }

    public MessageItem? SelectedMessage { get => _selectedMessage; set => Set(ref _selectedMessage, value); }

    public FilterRule? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (_selectedRule is not null) _selectedRule.PropertyChanged -= OnRuleChanged;
            Set(ref _selectedRule, value);
            if (_selectedRule is not null) _selectedRule.PropertyChanged += OnRuleChanged;
            RuleError = _selectedRule?.Validate() ?? string.Empty;
            OnPropertyChanged(nameof(RuleRicPreview));
        }
    }

    /// <summary>Zeigt, welche bekannten RICs (mit Bezeichnung) das RIC-Feld der Regel erfasst.</summary>
    public string RuleRicPreview
    {
        get
        {
            if (SelectedRule is null) return string.Empty;
            if (!RicPattern.TryParse(SelectedRule.RicPattern, out var pattern, out _)) return string.Empty;
            if (pattern.MatchesAll) return "Erfasst alle RICs.";
            var hits = Descriptions.Where(d => pattern.IsMatch(d.Ric)).OrderBy(d => d.Ric).ToList();
            if (hits.Count == 0) return "Keine RIC aus der Beschreibungsliste erfasst.";
            var names = hits.Take(12).Select(d => d.Name.Trim().Length > 0 ? $"{d.Ric:D7} {d.Name.Trim()}" : d.Ric.ToString("D7"));
            var more = hits.Count > 12 ? $" … (+{hits.Count - 12})" : string.Empty;
            return $"Erfasst {hits.Count}: " + string.Join(", ", names) + more;
        }
    }

    public string SubRicLabelA => SubRicLabel(0);
    public string SubRicLabelB => SubRicLabel(1);
    public string SubRicLabelC => SubRicLabel(2);
    public string SubRicLabelD => SubRicLabel(3);

    private string SubRicLabel(int i)
    {
        var name = i < Settings.GlobalSubRics.Count ? (Settings.GlobalSubRics[i] ?? "").Trim() : "";
        var letter = (char)('A' + i);
        return name.Length > 0 ? $"{letter} {name}" : $"{letter} (Fkt. {i})";
    }

    /// <summary>
    /// Übernimmt die im Auswahlfenster angehakten RICs in die Regel. Bereiche und
    /// Platzhalter im RIC-Feld bleiben erhalten, einzelne RICs werden ersetzt.
    /// </summary>
    public void SetRuleRics(IEnumerable<int> rics)
    {
        if (SelectedRule is null) return;
        var keep = (SelectedRule.RicPattern ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p != "*" && !int.TryParse(p, out _));
        var parts = keep.Concat(rics.Distinct().OrderBy(r => r).Select(r => r.ToString("D7")));
        SelectedRule.RicPattern = string.Join(", ", parts);
    }

    /// <summary>Einzelne RICs, die aktuell im RIC-Feld der Regel stehen.</summary>
    public IReadOnlyCollection<int> SelectedRuleSingleRics =>
        (SelectedRule?.RicPattern ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p.Trim(), out var r) ? r : -1).Where(r => r >= 0).ToHashSet();

    public string RuleError { get => _ruleError; private set => Set(ref _ruleError, value); }

    public WebhookTarget? SelectedTarget
    {
        get => _selectedTarget;
        set { Set(ref _selectedTarget, value); TargetTestResult = string.Empty; OnPropertyChanged(nameof(SelectedTargetKind)); OnPropertyChanged(nameof(SelectedTargetUsedBy)); }
    }

    public string TargetTestResult { get => _targetTestResult; private set => Set(ref _targetTestResult, value); }

    /// <summary>SubRIC-Buchstaben klein (a–d) anzeigen und senden.</summary>
    public bool LowercaseSubRic
    {
        get => Settings.LowercaseSubRic;
        set
        {
            if (Settings.LowercaseSubRic == value) return;
            Settings.LowercaseSubRic = value;
            TemplateRenderer.LowercaseSubRic = value;
            foreach (var item in Messages) item.RefreshDescription();
            OnPropertyChanged();
            Save();
        }
    }

    public int DuplicateWindowSeconds
    {
        get => Settings.DuplicateWindowSeconds;
        set
        {
            Settings.DuplicateWindowSeconds = Math.Max(0, value);
            _duplicates.Window = TimeSpan.FromSeconds(Settings.DuplicateWindowSeconds);
            _repeatMarker.Window = RepeatWindow;
            OnPropertyChanged();
        }
    }

    // --- Fenster im Reiter Telegramme ----------------------------------------

    public ObservableCollection<MessagePane> Panes { get; } = new();
    public RelayCommand AddPaneCommand { get; private set; } = null!;

    private MessagePane? _activePane;

    /// <summary>Ausgewähltes Fenster (Klick hinein); darauf wirkt das Menü „Fenster“.</summary>
    public MessagePane? ActivePane
    {
        get => _activePane;
        set
        {
            if (value is null || _activePane == value) return;
            if (_activePane is not null) _activePane.IsActive = false;
            _activePane = value;
            _activePane.IsActive = true;
            OnPropertyChanged();
        }
    }

    /// <summary>Fenster an eine andere Stelle verschieben (Reihenfolge wird gespeichert).</summary>
    public void MovePane(MessagePane pane, int newIndex)
    {
        var old = Panes.IndexOf(pane);
        if (old < 0) return;
        newIndex = Math.Clamp(newIndex, 0, Panes.Count - 1);
        if (old == newIndex) return;
        Panes.Move(old, newIndex);
        Settings.ReceivePanes.Remove(pane.Settings);
        Settings.ReceivePanes.Insert(newIndex, pane.Settings);
        RequestSave();
    }
    public IReadOnlyList<string> PaneLayouts { get; } = ["Untereinander", "Nebeneinander", "Raster"];

    public string PaneLayout
    {
        get => Settings.PaneLayout;
        set
        {
            if (Settings.PaneLayout == value) return;
            Settings.PaneLayout = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PaneColumns));
            RequestSave();
        }
    }

    /// <summary>Spalten für das UniformGrid der Fenster.</summary>
    public int PaneColumns => PaneLayout switch
    {
        "Nebeneinander" => Math.Max(1, Panes.Count),
        "Raster" => Panes.Count <= 1 ? 1 : 2,
        _ => 1,
    };

    /// <summary>Auswahlliste für die Fenster: „Alle Telegramme“ und die Namen der Telegrammereignisse.</summary>
    public ObservableCollection<string> EventNames { get; } = new();

    private readonly Dictionary<FilterRule, string> _ruleNames = new();

    private void TrackRules()
    {
        foreach (var r in Rules) WatchRule(r);
        Rules.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (FilterRule r in e.NewItems) WatchRule(r);
            if (e.OldItems is not null) foreach (FilterRule r in e.OldItems) { r.PropertyChanged -= OnWatchedRuleChanged; _ruleNames.Remove(r); }
            UpdateEventNames();
        };
        UpdateEventNames();
    }

    private void WatchRule(FilterRule rule)
    {
        _ruleNames[rule] = rule.Name;
        rule.PropertyChanged -= OnWatchedRuleChanged;
        rule.PropertyChanged += OnWatchedRuleChanged;
    }

    private void OnWatchedRuleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not FilterRule rule) return;
        if (e.PropertyName == nameof(FilterRule.Name))
        {
            var old = _ruleNames.GetValueOrDefault(rule, "");
            _ruleNames[rule] = rule.Name;
            foreach (var p in Panes) p.RenameEvent(old, rule.Name);
            UpdateEventNames();
            RequestSave();
            return;
        }
        // Bedingungen geändert: Fenster mit diesem Ereignis neu filtern.
        foreach (var p in Panes.Where(p => p.Settings.Event == rule.Name)) p.Refresh();
    }

    private void UpdateEventNames()
    {
        var names = new List<string> { MessagePane.AllEvents };
        names.AddRange(Rules.Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
        if (names.SequenceEqual(EventNames)) return;
        EventNames.Clear();
        foreach (var n in names) EventNames.Add(n);
        foreach (var p in Panes) p.NotifyEventsChanged();
    }

    private void InitPanes()
    {
        if (Settings.ReceivePanes.Count == 0)
            Settings.ReceivePanes.Add(new ReceivePaneSettings { HiddenColumns = Settings.HiddenColumns.ToList() });
        foreach (var p in Settings.ReceivePanes) Panes.Add(new MessagePane(this, p));
        Panes.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PaneColumns));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        };
        AddPaneCommand = new RelayCommand(AddPane);
        ActivePane = Panes.FirstOrDefault();
        TrackRules();
    }

    private void AddPane()
    {
        var s = new ReceivePaneSettings { Title = $"Fenster {Panes.Count + 1}", HiddenColumns = Panes.LastOrDefault()?.Settings.HiddenColumns.ToList() ?? new() };
        Settings.ReceivePanes.Add(s);
        var pane = new MessagePane(this, s);
        Panes.Add(pane);
        ActivePane = pane;
        RequestSave();
    }

    public void RemovePane(MessagePane pane)
    {
        if (Panes.Count <= 1) return;
        var index = Panes.IndexOf(pane);
        Panes.Remove(pane);
        Settings.ReceivePanes.Remove(pane.Settings);
        if (pane == _activePane)
        {
            _activePane = null;
            ActivePane = Panes[Math.Min(index, Panes.Count - 1)];
        }
        RequestSave();
    }

    private void RefreshPanes()
    {
        foreach (var p in Panes) p.Refresh();
    }

    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    /// <summary>Speichert kurz verzögert (z.B. während im Filterfeld getippt wird).</summary>
    public void RequestSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(quiet: true); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // --- Decoder ----------------------------------------------------------

    public void StartIfConfigured()
    {
        if (!Settings.AutoStart) return;
        // Beim automatischen Start kein Meldungsfenster, sondern Fehlermeldung (läuft evtl. im Hintergrund).
        if (!TryStart(out var error))
        {
            if (Settings.ErrorOnDecoder) ReportError("decoder", "Empfang konnte beim Programmstart nicht gestartet werden: " + error);
            if (Settings.AutoRestart) ScheduleRestart();
        }
    }

    private void Start()
    {
        if (IsRunning) return;
        CancelRestart();
        if (!TryStart(out var error))
            MessageBox.Show(error + "\n\nBitte unter „Einstellungen“ die Pfade zu rtl_fm.exe und multimon-ng.exe prüfen.",
                "Start fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Startet den Empfang; bei Fehler steht die Ursache in <paramref name="error"/>.</summary>
    private bool TryStart(out string error)
    {
        error = string.Empty;
        var pipeline = new DecoderPipeline(Settings.Decoder);
        try
        {
            pipeline.MessageReceived += (_, m) => Application.Current.Dispatcher.BeginInvoke(() => { NoteMessageReceived(); OnMessage(m); });
            pipeline.Log += (_, line) => Application.Current.Dispatcher.BeginInvoke(() => AddLog(line));
            pipeline.Stopped += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_pipeline != pipeline) return;
                _pipeline = null;
                IsRunning = false;
                StatusText = "Decoder unerwartet beendet – siehe Protokoll";
                OnDecoderCrashed();
            });
            _pipeline = pipeline;
            pipeline.Start();
            IsRunning = true;
            NoteMessageReceived(); // Zeitmessung für „keine Meldung seit …“ beginnt jetzt
            StatusText = Settings.Decoder.Mode == DecoderSourceMode.RtlSdr
                ? $"Empfange auf {Settings.Decoder.FrequencyMHz:0.000} MHz"
                : "Empfange (eigener Befehl)";
            return true;
        }
        catch (Exception ex)
        {
            AddLog("Fehler beim Start: " + ex.Message);
            StatusText = "Start fehlgeschlagen";
            _pipeline = null;
            error = ex.Message;
            return false;
        }
    }

    private async void Stop()
    {
        CancelRestart();
        if (_pipeline is null) return;
        var p = _pipeline;
        _pipeline = null;
        await p.StopAsync();
        IsRunning = false;
        StatusText = "Gestoppt";
    }

    public async Task ShutdownAsync()
    {
        if (_pipeline is not null) await _pipeline.StopAsync();
        Save();
    }

    private void Simulate()
    {
        if (!int.TryParse(SimRic.Trim(), out var ric) || ric is < 0 or > 2097151)
        {
            MessageBox.Show("RIC muss eine Zahl zwischen 0 und 2097151 sein.", "Testnachricht");
            return;
        }
        if (!Confirm($"Testnachricht für RIC {ric:D7} {SimSubRic} wirklich einspeisen?\n\n"
                     + "Sie läuft durch die Telegrammereignisse und wird an alle passenden Schnittstellen gesendet (z. B. Home Assistant)."))
            return;
        PocsagMessage.TryParseSubRic(SimSubRic, out var func);
        OnMessage(new PocsagMessage
        {
            Baud = 1200,
            Ric = ric,
            Function = Math.Max(0, func),
            ContentType = string.IsNullOrEmpty(SimText) ? PocsagContentType.Tone : PocsagContentType.Alpha,
            Text = SimText ?? string.Empty,
            Raw = "(Testnachricht)",
        });
    }

    /// <summary>Sicherheitsabfrage, bevor eine Probe nach außen gesendet wird.</summary>
    private static bool Confirm(string text) =>
        MessageBox.Show(Application.Current.MainWindow, text, "Probe senden", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            == MessageBoxResult.Yes;

    public string SimRic { get => _simRic; set { if (Set(ref _simRic, value)) OnPropertyChanged(nameof(SimDescription)); } }
    public string SimSubRic { get => _simSubRic; set { if (Set(ref _simSubRic, value)) OnPropertyChanged(nameof(SimDescription)); } }

    /// <summary>Vorschau der Beschreibung für die Test-RIC.</summary>
    public string SimDescription
    {
        get
        {
            if (!int.TryParse(SimRic?.Trim(), out var ric)) return string.Empty;
            PocsagMessage.TryParseSubRic(SimSubRic, out var func);
            var d = _directory.Describe(ric, Math.Max(0, func));
            return d.Length == 0 ? "(keine Beschreibung hinterlegt)" : d;
        }
    }
    public string SimText { get => _simText; set => Set(ref _simText, value); }

    private void OnMessage(PocsagMessage message)
    {
        _directory.Annotate(message);
        var item = new MessageItem(message);
        // Regeln vor dem Einfügen prüfen, damit Fensterfilter wie „nur Regeltreffer“ die Zeile richtig einsortieren.
        var hits = RuleEngine.Match(Rules, message);
        item.Matched = hits.Count > 0;
        item.IsRepeat = _repeatMarker.IsDuplicate(message);
        Messages.Insert(0, item);
        while (Messages.Count > Math.Max(100, Settings.MaxMessages)) Messages.RemoveAt(Messages.Count - 1);
        ReceivedCount++;
        if (item.Matched) MessageMatched?.Invoke(this, item);

        if (hits.Count > 0 && _duplicates.IsDuplicate(message))
        {
            var repeats = hits.Where(r => r.SendRepeats).ToList();
            item.Forwarding = "Wiederholung – nicht erneut gesendet";
            if (repeats.Count == 0)
            {
                WriteCsv(item);
                return;
            }
            hits = repeats;
        }

        var pending = new List<Task>();
        foreach (var rule in hits)
        {
            if (rule.TargetNames.Count == 0)
            {
                item.AppendForwarding($"{rule.Name}: markiert");
                continue;
            }
            foreach (var name in rule.TargetNames)
            {
                var target = Targets.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    item.AppendForwarding($"{rule.Name}: Ziel \"{name}\" fehlt");
                    item.HasError = true;
                    continue;
                }
                if (!target.Enabled)
                {
                    item.AppendForwarding($"{target.Name}: deaktiviert");
                    continue;
                }
                pending.Add(ForwardAsync(item, rule, target));
            }
        }

        if (pending.Count == 0) WriteCsv(item);
        else _ = Task.WhenAll(pending).ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => WriteCsv(item)));
    }

    private async Task ForwardAsync(MessageItem item, FilterRule rule, WebhookTarget target)
    {
        item.AppendForwarding($"{target.Name}: sende…");
        var result = await _sender.SendAsync(target, item.Message, rule.Name);
        var text = $"{target.Name}: {(result.Success ? "✓ " : "✗ ")}{result.Detail}";
        target.ReportResult(result.Success, result.Detail, DateTime.Now);
        item.Forwarding = item.Forwarding.Replace($"{target.Name}: sende…", text);
        if (result.Success) ForwardedCount++;
        else
        {
            item.HasError = true;
            OnSendFailed(target, result.Detail);
        }
        AddLog($"Senden RIC {item.Ric}{item.SubRic} via \"{rule.Name}\" an {target.Name}: {result.Detail} (Versuche: {result.Attempts})");
    }

    private void WriteCsv(MessageItem item)
    {
        if (!Settings.LogToCsv) return;
        try { _csv.Append(item.Message, item.Forwarding); }
        catch (Exception ex)
        {
            AddLog("CSV-Protokoll fehlgeschlagen: " + ex.Message);
            OnCsvFailed(ex.Message);
        }
    }

    private void AddLog(string line)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (LogLines.Count > 2000) LogLines.RemoveAt(0);
    }

    // --- Regeln -----------------------------------------------------------

    private void AddRule()
    {
        var rule = new FilterRule { Name = $"Ereignis {Rules.Count + 1}", TargetName = Targets.FirstOrDefault()?.Name ?? string.Empty };
        Rules.Add(rule);
        SelectedRule = rule;
    }

    private void RuleFromMessage()
    {
        if (SelectedMessage is null) return;
        var m = SelectedMessage.Message;
        var rule = new FilterRule
        {
            Name = $"RIC {m.RicFormatted} {m.SubRic}",
            RicPattern = m.RicFormatted,
            SubRicA = m.Function == 0,
            SubRicB = m.Function == 1,
            SubRicC = m.Function == 2,
            SubRicD = m.Function == 3,
            TargetName = Targets.FirstOrDefault()?.Name ?? string.Empty,
        };
        Rules.Add(rule);
        SelectedRule = rule;
        RuleCreated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Eine empfangene Nachricht hat mindestens eine Regel getroffen.</summary>
    public event EventHandler<MessageItem>? MessageMatched;

    /// <summary>Mit Windows starten (Eintrag unter HKCU\...\Run, startet im Infobereich).</summary>
    public bool StartWithWindows
    {
        get => WindowsAutostart.IsEnabled();
        set
        {
            try { WindowsAutostart.Set(value); }
            catch (Exception ex) { AddLog("Autostart konnte nicht geändert werden: " + ex.Message); }
            OnPropertyChanged();
        }
    }

    /// <summary>Damit die Oberfläche zum Regel-Reiter wechseln kann.</summary>
    public event EventHandler? RuleCreated;

    private void MoveRule(int delta)
    {
        if (SelectedRule is null) return;
        var i = Rules.IndexOf(SelectedRule);
        var j = i + delta;
        if (j < 0 || j >= Rules.Count) return;
        var keep = SelectedRule;
        Rules.Move(i, j);
        SelectedRule = keep;
    }

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        RuleError = SelectedRule?.Validate() ?? string.Empty;
        if (e.PropertyName == nameof(FilterRule.RicPattern)) OnPropertyChanged(nameof(RuleRicPreview));
    }

    // --- Ziele ------------------------------------------------------------

    private void AddTarget() => AddTarget(TargetKinds.Webhook);

    private async Task TestTargetAsync()
    {
        if (SelectedTarget is null) return;
        if (!Confirm($"Testnachricht wirklich an „{SelectedTarget.Name}“ senden?")) return;
        PocsagMessage.TryParseSubRic(SimSubRic, out var func);
        var message = new PocsagMessage
        {
            Baud = 1200,
            Ric = int.TryParse(SimRic, out var ric) ? ric : 1234567,
            Function = Math.Max(0, func),
            ContentType = PocsagContentType.Alpha,
            Text = string.IsNullOrEmpty(SimText) ? "Testnachricht" : SimText,
            Raw = "(Test)",
        };
        _directory.Annotate(message);
        TargetTestResult = "Sende…";
        var result = await _sender.SendAsync(SelectedTarget, message, "Test");
        TargetTestResult = (result.Success ? "✓ Erfolgreich: " : "✗ Fehler: ") + result.Detail;
        SelectedTarget.ReportResult(result.Success, "Test: " + result.Detail, DateTime.Now);
        AddLog($"Test an {SelectedTarget.Name}: {result.Detail}");
    }

    // --- RIC-Beschreibungen ----------------------------------------------

    public RicEntry? SelectedDescription { get => _selectedDescription; set => Set(ref _selectedDescription, value); }

    public string DescriptionSearch
    {
        get => _descriptionSearch;
        set { if (Set(ref _descriptionSearch, value)) DescriptionsView.Refresh(); }
    }

    public string DescriptionStatus { get => _descriptionStatus; private set => Set(ref _descriptionStatus, value); }

    private bool DescriptionFilter(RicEntry d)
    {
        var q = DescriptionSearch?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return d.RicFormatted.Contains(q) || d.Ric.ToString().Contains(q)
            || new[] { d.Name, d.ShortName, d.Location, d.BosMonUser, d.A, d.B, d.C, d.D }.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void OnDescriptionChanged(object? sender, PropertyChangedEventArgs e) => RebuildDirectory();

    /// <summary>Allgemeine SubRIC-Bedeutungen, z.B. "A Vollalarm · B Kleinalarm".</summary>
    public string GlobalSubRicSummary
    {
        get
        {
            var parts = Settings.GlobalSubRics.Select((n, i) => (n, i))
                .Where(x => !string.IsNullOrWhiteSpace(x.n))
                .Select(x => $"{(char)('A' + x.i)} = {x.n.Trim()}");
            var s = string.Join("   ", parts);
            return s.Length == 0 ? "Allgemeine SubRICs: noch nicht zugeordnet" : "Allgemeine SubRICs:   " + s;
        }
    }

    public void SetGlobalSubRics(IReadOnlyList<string> names)
    {
        Settings.GlobalSubRics = Enumerable.Range(0, 4).Select(i => i < names.Count ? (names[i] ?? "").Trim() : "").ToList();
        _directory.SetGlobalSubRics(Settings.GlobalSubRics);
        RebuildDirectory();
        OnPropertyChanged(nameof(GlobalSubRicSummary));
        foreach (var n in new[] { nameof(SubRicLabelA), nameof(SubRicLabelB), nameof(SubRicLabelC), nameof(SubRicLabelD) })
            OnPropertyChanged(n);
        Save();
    }

    private void RebuildDirectory()
    {
        _directory.Rebuild(Descriptions);
        foreach (var item in Messages)
        {
            _directory.Annotate(item.Message);
            item.RefreshDescription();
        }
        RefreshPanes();
        var subs = Descriptions.Sum(d => new[] { d.A, d.B, d.C, d.D }.Count(t => t.Length > 0));
        DescriptionStatus = $"{Descriptions.Count} RICs, {subs} SubRIC-Beschreibungen";
        OnPropertyChanged(nameof(SimDescription));
        OnPropertyChanged(nameof(RuleRicPreview));
    }

    /// <summary>Offene Bearbeitung im Raster abschließen, sonst darf die Liste nicht geändert werden.</summary>
    private void EndDescriptionEdit()
    {
        if (DescriptionsView.IsAddingNew) DescriptionsView.CommitNew();
        if (DescriptionsView.IsEditingItem) DescriptionsView.CommitEdit();
    }

    private void AddDescription()
    {
        EndDescriptionEdit();
        var d = new RicEntry { Ric = int.TryParse(DescriptionSearch, out var ric) ? ric : 0, Name = "Neue RIC" };
        Descriptions.Add(d);
        SelectedDescription = d;
    }

    private void DescriptionFromMessage()
    {
        if (SelectedMessage is null) return;
        var m = SelectedMessage.Message;
        var existing = Descriptions.FirstOrDefault(d => d.Ric == m.Ric);
        var d = existing ?? new RicEntry { Ric = m.Ric };
        if (existing is null) Descriptions.Add(d);
        DescriptionSearch = string.Empty;
        SelectedDescription = d;
        DescriptionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Damit die Oberfläche zum Beschreibungs-Reiter wechseln kann.</summary>
    public event EventHandler? DescriptionRequested;

    private void ClearDescriptions()
    {
        if (MessageBox.Show($"Alle {Descriptions.Count} Beschreibungen löschen?", "RIC-Beschreibungen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Descriptions.Clear();
    }

    /// <summary>Importiert eine Datei; <paramref name="replace"/> ersetzt alle vorhandenen Einträge.</summary>
    public void ImportDescriptions(string path, bool replace)
    {
        ImportResult result;
        try
        {
            result = RicDirectory.ParseFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Datei konnte nicht gelesen werden:\n" + ex.Message, "Import", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        EndDescriptionEdit();
        if (replace) Descriptions.Clear();
        int added = 0, updated = 0;
        foreach (var e in result.Entries)
        {
            var existing = Descriptions.FirstOrDefault(d => d.Ric == e.Ric);
            if (existing is null) { Descriptions.Add(e); added++; }
            else
            {
                var before = RicDirectory.ToCsv([existing]);
                if (result.FillEmptyOnly) existing.FillEmptyFrom(e);
                else existing.MergeFrom(e);
                if (RicDirectory.ToCsv([existing]) != before) updated++;
            }
        }
        DescriptionsView.Refresh();

        var text = $"{added} RICs neu, {updated} RICs aktualisiert aus {Path.GetFileName(path)}.";
        if (result.Errors.Count > 0)
            text += $"\n\n{result.Errors.Count} Zeilen übersprungen:\n" + string.Join("\n", result.Errors.Take(15))
                + (result.Errors.Count > 15 ? "\n…" : string.Empty);
        AddLog("RIC-Import: " + text.Split('\n')[0]);
        MessageBox.Show(text, "Import abgeschlossen", MessageBoxButton.OK,
            result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        Save();
    }

    public void ExportDescriptions(string path)
    {
        File.WriteAllText(path, RicDirectory.ToCsv(Descriptions), new System.Text.UTF8Encoding(true));
        AddLog("RIC-Beschreibungen exportiert: " + path);
    }

    public static void SaveDescriptionTemplate(string path) =>
        File.WriteAllText(path, RicDirectory.Template + Environment.NewLine, new System.Text.UTF8Encoding(true));

    // --- Einstellungen ----------------------------------------------------

    private static AppSettings LoadSettings()
    {
        try
        {
            return SettingsStore.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Einstellungen konnten nicht gelesen werden, es werden Standardwerte verwendet.\n\n" + ex.Message,
                "POCSAG Decoder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return AppSettings.CreateDefault();
        }
    }

    private void Save() => Save(quiet: false);

    private void Save(bool quiet)
    {
        Settings.Rules = Rules.ToList();
        Settings.Targets = Targets.ToList();
        Settings.RicEntries = Descriptions.Where(d => !d.IsEmpty).ToList();
        try
        {
            SettingsStore.Save(Settings);
            SyncTcpServers();
            if (!quiet) AddLog("Einstellungen gespeichert: " + SettingsStore.DefaultPath);
        }
        catch (Exception ex)
        {
            AddLog("Speichern fehlgeschlagen: " + ex.Message);
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_csv.Directory);
            Process.Start(new ProcessStartInfo { FileName = _csv.Directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Der Ordner „{_csv.Directory}“ kann nicht geöffnet werden:\n{ex.Message}", "Ordner öffnen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
