using System.Windows;
using System.Windows.Media;
using PocsagDecoder.Core.Forwarding;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Schnittstellen-Arten: Auswahl, Standardwerte, Ton und TCP-Ausgabe.</summary>
public sealed partial class MainViewModel
{
    private readonly List<MediaPlayer> _players = new();

    public IReadOnlyList<string> TargetKindChoices => TargetKinds.All;

    /// <summary>Art der gewählten Schnittstelle; beim Umstellen werden sinnvolle Startwerte gesetzt.</summary>
    public string SelectedTargetKind
    {
        get => SelectedTarget?.Kind ?? TargetKinds.Webhook;
        set
        {
            var t = SelectedTarget;
            if (t is null || t.Kind == TargetKinds.Normalize(value)) return;
            var oldUrl = t.Url;
            t.Kind = value;
            ApplyKindDefaults(t, oldUrl);
            OnPropertyChanged();
            SyncTcpServers();
        }
    }

    private static readonly string[] DefaultUrls =
    [
        "https://example.org/webhook",
        "https://app.divera247.com/api/alarm",
        "http://localhost:83/rest/external/http/alarm/v2",
        "http://raspberrypi:8080",
        "",
    ];

    private static void ApplyKindDefaults(WebhookTarget t, string oldUrl)
    {
        // URL nur ersetzen, wenn noch ein Beispielwert drinsteht.
        bool keepUrl = !DefaultUrls.Contains(oldUrl.Trim());
        string? url = t.Kind switch
        {
            TargetKinds.Webhook => "https://example.org/webhook",
            TargetKinds.Divera => "https://app.divera247.com/api/alarm",
            TargetKinds.Fe2 => "http://localhost:83/rest/external/http/alarm/v2",
            TargetKinds.Signal => "http://raspberrypi:8080",
            _ => null,
        };
        if (url is not null && !keepUrl) t.Url = url;

        if (string.IsNullOrWhiteSpace(t.Host))
        {
            t.Host = t.Kind switch
            {
                TargetKinds.Tcp => "127.0.0.1",
                TargetKinds.BosMon => "localhost",
                TargetKinds.Mqtt => "homeassistant.local",
                _ => t.Host,
            };
        }
        if (t.Kind == TargetKinds.Email && t.Port == 0) { t.UseTls = true; t.Port = 587; }
        if (t.Kind == TargetKinds.Mqtt && t.Port == 0) t.Port = 1883;
        if (t.Kind == TargetKinds.Tcp && t.Port == 0) t.Port = 9000;
        if (t.Kind == TargetKinds.BosMon && t.Port == 0) t.Port = 5566;
        if (t.Kind == TargetKinds.BosMon && t.TitleTemplate == WebhookTarget.DefaultTitleTemplate) t.TitleTemplate = "{rule}";
        if (t.Kind == TargetKinds.BosMon && t.TextTemplate == WebhookTarget.DefaultTextTemplate) t.TextTemplate = "{message}";
        if (t.Kind == TargetKinds.BosMon && string.IsNullOrWhiteSpace(t.Recipient)) t.Recipient = "{bosmon_user}";
        if (t.Kind == TargetKinds.Database && string.IsNullOrWhiteSpace(t.FilePath)) t.FilePath = WebhookSender.DefaultDatabasePath;
        if (t.Kind == TargetKinds.Fe2 && string.IsNullOrWhiteSpace(t.Recipient)) t.Recipient = "{ric}{subric_upper}";
        if (t.Kind == TargetKinds.Fe2 && string.IsNullOrWhiteSpace(t.Sender)) t.Sender = "POCSAG Decoder";
        if (t.Kind == TargetKinds.Fe2 && t.TextTemplate == WebhookTarget.DefaultTextTemplate)
            t.TextTemplate = "{ric_description} {subric_description}\n{message}";
        if (t.Kind == TargetKinds.Fe2 && t.TitleTemplate == WebhookTarget.DefaultTitleTemplate)
            t.TitleTemplate = "{ric_short}";
        if (t.Kind == TargetKinds.Tcp && t.TextTemplate == WebhookTarget.DefaultTextTemplate)
            t.TextTemplate = "{date};{time};{ric};{subric};{function};{message};{ric_description};{subric_description}";
        if (t.Kind == TargetKinds.Mqtt && t.BodyTemplate.Length == 0) t.BodyTemplate = WebhookTarget.DefaultJsonTemplate;
    }

    /// <summary>Legt eine Schnittstelle der gewählten Art an und wählt sie aus.</summary>
    public void AddTarget(string kind)
    {
        var baseName = kind == TargetKinds.Webhook ? "Webhook" : kind;
        var name = baseName;
        for (var i = 2; Targets.Any(t => t.Name == name); i++) name = $"{baseName} {i}";
        var target = new WebhookTarget { Name = name, Kind = kind };
        ApplyKindDefaults(target, target.Url);
        Targets.Add(target);
        SelectedTarget = target;
    }

    public void DuplicateTarget()
    {
        if (SelectedTarget is not { } src) return;
        var json = System.Text.Json.JsonSerializer.Serialize(src);
        var copy = System.Text.Json.JsonSerializer.Deserialize<WebhookTarget>(json)!;
        copy.Id = Guid.NewGuid();
        var name = src.Name + " (Kopie)";
        for (var i = 2; Targets.Any(t => t.Name == name); i++) name = $"{src.Name} (Kopie {i})";
        copy.Name = name;
        Targets.Insert(Targets.IndexOf(src) + 1, copy);
        SelectedTarget = copy;
    }

    /// <summary>Telegrammereignisse, die die gewählte Schnittstelle benutzen.</summary>
    public string SelectedTargetUsedBy
    {
        get
        {
            if (SelectedTarget is not { } t) return string.Empty;
            var rules = Rules.Where(r => r.TargetNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).Select(r => r.Name).ToList();
            return rules.Count == 0
                ? "Wird noch von keinem Telegrammereignis benutzt"
                : "Benutzt von: " + string.Join(", ", rules);
        }
    }

    public void RefreshTargetUsage() => OnPropertyChanged(nameof(SelectedTargetUsedBy));

    private readonly Dictionary<Guid, string> _targetNames = new();

    /// <summary>Beim Umbenennen einer Schnittstelle die Telegrammereignisse mitziehen.</summary>
    private void TrackTargetName(WebhookTarget t)
    {
        if (_targetNames.TryGetValue(t.Id, out var old) && old != t.Name && old.Length > 0
            && Targets.All(o => o == t || o.Name != old))
        {
            foreach (var r in Rules.Where(r => r.TargetNames.Contains(old)))
            {
                r.SetTarget(old, false);
                r.SetTarget(t.Name, true);
            }
            RequestSave();
        }
        _targetNames[t.Id] = t.Name;
    }

    private void InitTargets()
    {
        foreach (var t in Targets) _targetNames[t.Id] = t.Name;
        _sender.PlaySound = PlaySoundAsync;
        Targets.CollectionChanged += (_, _) => SyncTcpServers();
        foreach (var t in Targets) t.PropertyChanged += OnTargetChanged;
        Targets.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(SelectedTargetUsedBy)); RequestSave(); };
        Targets.CollectionChanged += (_, e) =>
        {
            foreach (WebhookTarget t in e.NewItems ?? Array.Empty<WebhookTarget>()) { t.PropertyChanged += OnTargetChanged; _targetNames[t.Id] = t.Name; }
            foreach (WebhookTarget t in e.OldItems ?? Array.Empty<WebhookTarget>()) t.PropertyChanged -= OnTargetChanged;
        };
        SyncTcpServers();
    }

    private void OnTargetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WebhookTarget.Enabled) or nameof(WebhookTarget.Host) or nameof(WebhookTarget.Port))
            RequestTcpSync();
        if (e.PropertyName is not (nameof(WebhookTarget.LastResult) or nameof(WebhookTarget.LastSuccess)
                or nameof(WebhookTarget.Statistics) or nameof(WebhookTarget.Summary)))
            RequestSave();
        if (e.PropertyName == nameof(WebhookTarget.Name) && sender is WebhookTarget t)
        {
            TrackTargetName(t);
            OnPropertyChanged(nameof(SelectedTargetUsedBy));
        }
    }

    private System.Windows.Threading.DispatcherTimer? _tcpTimer;

    /// <summary>Verzögert, damit beim Tippen der Portnummer nicht jede Ziffer einen Server öffnet.</summary>
    private void RequestTcpSync()
    {
        if (_tcpTimer is null)
        {
            _tcpTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _tcpTimer.Tick += (_, _) => { _tcpTimer.Stop(); SyncTcpServers(); };
        }
        _tcpTimer.Stop();
        _tcpTimer.Start();
    }

    private void SyncTcpServers()
    {
        var before = _tcpErrors;
        var errors = _sender.SyncTcpServers(Targets);
        _tcpErrors = string.Join("\n", errors);
        if (_tcpErrors.Length > 0 && _tcpErrors != before)
        {
            foreach (var e in errors) AddLog("TCP-Ausgabe: " + e);
        }
    }

    private string _tcpErrors = string.Empty;

    private Task PlaySoundAsync(string file)
    {
        var done = new TaskCompletionSource();
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var player = new MediaPlayer();
            _players.Add(player);
            void Finish(string? error)
            {
                if (error is null) done.TrySetResult();
                else done.TrySetException(new System.IO.IOException(error));
            }
            player.MediaOpened += (_, _) => { player.Play(); Finish(null); };
            player.MediaFailed += (_, e) => { _players.Remove(player); player.Close(); Finish("Ton konnte nicht abgespielt werden: " + e.ErrorException?.Message); };
            player.MediaEnded += (_, _) => { _players.Remove(player); player.Close(); };
            player.Open(new Uri(file, UriKind.Absolute));
        });
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => done.TrySetException(new TimeoutException("Ton-Datei konnte nicht geöffnet werden.")));
        return done.Task;
    }
}
