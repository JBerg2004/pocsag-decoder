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
                TargetKinds.Mqtt => "homeassistant.local",
                _ => t.Host,
            };
        }
        if (t.Kind == TargetKinds.Email && t.Port == 0) { t.UseTls = true; t.Port = 587; }
        if (t.Kind == TargetKinds.Mqtt && t.Port == 0) t.Port = 1883;
        if (t.Kind == TargetKinds.Tcp && t.Port == 0) t.Port = 9000;
        if (t.Kind == TargetKinds.Database && string.IsNullOrWhiteSpace(t.FilePath)) t.FilePath = WebhookSender.DefaultDatabasePath;
        if (t.Kind == TargetKinds.Fe2 && string.IsNullOrWhiteSpace(t.Recipient)) t.Recipient = "{ric}{subric_upper}";
        if (t.Kind == TargetKinds.Fe2 && string.IsNullOrWhiteSpace(t.Sender)) t.Sender = "POCSAG Decoder";
        if (t.Kind == TargetKinds.Tcp && t.TextTemplate == WebhookTarget.DefaultTextTemplate)
            t.TextTemplate = "{date};{time};{ric};{subric};{function};{message};{ric_description};{subric_description}";
        if (t.Kind == TargetKinds.Mqtt && t.BodyTemplate.Length == 0) t.BodyTemplate = WebhookTarget.DefaultJsonTemplate;
    }

    private void InitTargets()
    {
        _sender.PlaySound = PlaySoundAsync;
        Targets.CollectionChanged += (_, _) => SyncTcpServers();
        foreach (var t in Targets) t.PropertyChanged += OnTargetChanged;
        Targets.CollectionChanged += (_, e) =>
        {
            foreach (WebhookTarget t in e.NewItems ?? Array.Empty<WebhookTarget>()) t.PropertyChanged += OnTargetChanged;
            foreach (WebhookTarget t in e.OldItems ?? Array.Empty<WebhookTarget>()) t.PropertyChanged -= OnTargetChanged;
        };
        SyncTcpServers();
    }

    private void OnTargetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WebhookTarget.Enabled) or nameof(WebhookTarget.Host) or nameof(WebhookTarget.Port))
            RequestTcpSync();
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
