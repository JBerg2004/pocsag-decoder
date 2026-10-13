using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>
/// Eine Schnittstelle, an die Telegramme gesendet werden: Webhook/REST, MQTT, Messenger, E-Mail,
/// Alarmierungsdienste, Programm, Ton, Datenbank oder TCP-Ausgabe (siehe <see cref="TargetKinds"/>).
/// </summary>
public sealed class WebhookTarget : INotifyPropertyChanged
{
    private string _kind = TargetKinds.Webhook;
    private string _host = string.Empty;
    private int _port;
    private bool _useTls;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _topic = "pocsag/{ric}/{subric_lower}";
    private bool _retain;
    private string _token = string.Empty;
    private string _recipient = string.Empty;
    private string _sender = string.Empty;
    private string _titleTemplate = DefaultTitleTemplate;
    private string _textTemplate = DefaultTextTemplate;
    private int _priority;
    private string _filePath = string.Empty;
    private string _arguments = string.Empty;
    private string _keywordDescription = "{subric_description}";

    public const string DefaultTitleTemplate = "{ric_short} {subric_description}";
    public const string DefaultTextTemplate = "{date} {time}\n{ric} {subric} – {ric_description} ({ric_location})\n{subric_description}\n{message}";

    private string _name = "Neues Ziel";
    private bool _enabled = true;
    private string _url = "https://example.org/webhook";
    private string _method = "POST";
    private string _contentType = "application/json";
    private string _headers = string.Empty;
    private string _bodyTemplate = DefaultJsonTemplate;
    private int _timeoutSeconds = 10;
    private int _retries = 2;

    public const string DefaultJsonTemplate =
        """
        {
          "ric": "{ric}",
          "subric": "{subric}",
          "function": {function},
          "message": "{message}",
          "description": "{description}",
          "type": "{type}",
          "baud": {baud},
          "timestamp": "{timestamp}",
          "rule": "{rule}"
        }
        """;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>Ziel-URL; Platzhalter werden URL-kodiert eingesetzt.</summary>
    public string Url { get => _url; set => Set(ref _url, value); }

    /// <summary>GET, POST, PUT oder PATCH.</summary>
    public string Method { get => _method; set => Set(ref _method, value); }

    public string ContentType { get => _contentType; set => Set(ref _contentType, value); }

    /// <summary>Zusätzliche Header, eine Zeile pro Header im Format "Name: Wert".</summary>
    public string Headers { get => _headers; set => Set(ref _headers, value); }

    /// <summary>Request-Body mit Platzhaltern (bei GET ignoriert).</summary>
    public string BodyTemplate { get => _bodyTemplate; set => Set(ref _bodyTemplate, value); }

    public int TimeoutSeconds { get => _timeoutSeconds; set => Set(ref _timeoutSeconds, value); }
    public int Retries { get => _retries; set => Set(ref _retries, value); }

    // --- Weitere Schnittstellen-Arten --------------------------------------

    /// <summary>Art der Schnittstelle, Werte aus <see cref="TargetKinds"/>.</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            var v = TargetKinds.Normalize(value);
            if (_kind == v) return;
            _kind = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Kind)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // alle abhängigen Anzeige-Eigenschaften
        }
    }

    /// <summary>Server (MQTT, SMTP) bzw. Adresse, auf der die TCP-Ausgabe lauscht.</summary>
    public string Host { get => _host; set => Set(ref _host, value); }

    /// <summary>Port; 0 = Standard der jeweiligen Art.</summary>
    public int Port { get => _port; set => Set(ref _port, value); }

    public bool UseTls { get => _useTls; set => Set(ref _useTls, value); }
    public string Username { get => _username; set => Set(ref _username, value); }

    /// <summary>Passwort (MQTT, SMTP) bzw. Autorisierung (FE2).</summary>
    public string Password { get => _password; set => Set(ref _password, value); }

    /// <summary>MQTT-Topic mit Platzhaltern.</summary>
    public string Topic { get => _topic; set => Set(ref _topic, value); }

    public bool Retain { get => _retain; set => Set(ref _retain, value); }

    /// <summary>Bot-Token (Telegram), App-Token (Pushover), Accesskey (Divera).</summary>
    public string Token { get => _token; set => Set(ref _token, value); }

    /// <summary>Chat-ID, User-Key, E-Mail-Empfänger, Signal-Empfänger, Divera-RIC oder FE2-Adresse (Platzhalter erlaubt).</summary>
    public string Recipient { get => _recipient; set => Set(ref _recipient, value); }

    /// <summary>Absender (E-Mail, Signal-Nummer, FE2-Sender).</summary>
    public string Sender { get => _sender; set => Set(ref _sender, value); }

    /// <summary>Betreff bzw. Titel/Stichwort mit Platzhaltern.</summary>
    public string TitleTemplate { get => _titleTemplate; set => Set(ref _titleTemplate, value); }

    /// <summary>Nachrichtentext mit Platzhaltern (Messenger, E-Mail, Divera, FE2, TCP-Zeile).</summary>
    public string TextTemplate { get => _textTemplate; set => Set(ref _textTemplate, value); }

    /// <summary>Priorität (Pushover −2…2, Divera: >0 = mit Sonderrechten).</summary>
    public int Priority { get => _priority; set => Set(ref _priority, value); }

    /// <summary>Programm, Ton-Datei oder Datenbank-Datei.</summary>
    public string FilePath { get => _filePath; set => Set(ref _filePath, value); }

    /// <summary>FE2: Stichwort-Beschreibung mit Platzhaltern.</summary>
    public string KeywordDescription { get => _keywordDescription; set => Set(ref _keywordDescription, value); }

    /// <summary>Programm-Argumente mit Platzhaltern.</summary>
    public string Arguments { get => _arguments; set => Set(ref _arguments, value); }

    // --- Anzeige: welche Felder die gewählte Art braucht ---------------------

    [JsonIgnore] public bool UsesHttp => _kind == TargetKinds.Webhook;
    [JsonIgnore] public bool UsesUrl => _kind is TargetKinds.Webhook or TargetKinds.Signal or TargetKinds.Divera or TargetKinds.Fe2;
    [JsonIgnore] public bool UsesHost => _kind is TargetKinds.Mqtt or TargetKinds.Email or TargetKinds.Tcp or TargetKinds.BosMon;
    [JsonIgnore] public bool UsesTls => _kind is TargetKinds.Mqtt or TargetKinds.Email or TargetKinds.BosMon;
    [JsonIgnore] public bool UsesLogin => _kind is TargetKinds.Mqtt or TargetKinds.Email;
    [JsonIgnore] public bool UsesPassword => UsesLogin || _kind == TargetKinds.Fe2;
    [JsonIgnore] public bool UsesTopic => _kind == TargetKinds.Mqtt;
    [JsonIgnore] public bool UsesToken => _kind is TargetKinds.Telegram or TargetKinds.Pushover or TargetKinds.Divera;
    [JsonIgnore] public bool UsesRecipient => _kind is TargetKinds.BosMon or TargetKinds.Telegram or TargetKinds.Signal or TargetKinds.Pushover or TargetKinds.Email or TargetKinds.Divera or TargetKinds.Fe2;
    [JsonIgnore] public bool UsesSender => _kind is TargetKinds.Signal or TargetKinds.Email or TargetKinds.Fe2;
    [JsonIgnore] public bool UsesTitle => _kind is TargetKinds.Pushover or TargetKinds.Email or TargetKinds.Divera or TargetKinds.Fe2 or TargetKinds.BosMon;
    [JsonIgnore] public bool UsesText => _kind is not (TargetKinds.Webhook or TargetKinds.Mqtt or TargetKinds.Sound or TargetKinds.Database or TargetKinds.Program);
    [JsonIgnore] public bool UsesBody => _kind is TargetKinds.Webhook or TargetKinds.Mqtt;
    [JsonIgnore] public bool UsesPriority => _kind is TargetKinds.Pushover or TargetKinds.Divera;
    [JsonIgnore] public bool UsesFile => _kind is TargetKinds.Program or TargetKinds.Sound or TargetKinds.Database;
    [JsonIgnore] public bool UsesArguments => _kind == TargetKinds.Program;
    [JsonIgnore] public bool UsesKeywordDescription => _kind == TargetKinds.Fe2;
    [JsonIgnore] public bool UsesTimeout => _kind is not (TargetKinds.Program or TargetKinds.Sound or TargetKinds.Database or TargetKinds.Tcp);

    [JsonIgnore] public string UrlLabel => _kind switch
    {
        TargetKinds.Signal => "Adresse der signal-cli-rest-api (z. B. http://raspberrypi:8080)",
        TargetKinds.Divera => "API-Adresse",
        TargetKinds.Fe2 => "Adresse des FE2-Servers (Port der Weboberfläche, Standard 83)",
        _ => "Methode und URL",
    };

    [JsonIgnore] public string HostLabel => _kind switch
    {
        TargetKinds.Mqtt => "Broker (Server)",
        TargetKinds.Email => "SMTP-Server",
        TargetKinds.Tcp => "Lauschen auf Adresse",
        TargetKinds.BosMon => "Host",
        _ => "Server",
    };

    [JsonIgnore] public string TokenLabel => _kind switch
    {
        TargetKinds.Telegram => "Bot-Token (von @BotFather)",
        TargetKinds.Pushover => "API-Token der Anwendung",
        TargetKinds.Divera => "Accesskey (Divera: Verwaltung → Schnittstellen)",
        _ => "Token",
    };

    [JsonIgnore] public string RecipientLabel => _kind switch
    {
        TargetKinds.Telegram => "Chat-ID (Person oder Gruppe)",
        TargetKinds.Signal => "Empfänger (Nummern oder Gruppen-ID, mit Komma getrennt)",
        TargetKinds.Pushover => "User- oder Gruppen-Key",
        TargetKinds.Email => "Empfänger (mehrere mit Komma)",
        TargetKinds.Divera => "Divera-RIC (optional, legt die alarmierten Gruppen fest)",
        TargetKinds.BosMon => "Benutzer (wie in BosMon; bei FE2 der Einheiten-Code, z. B. „Staffel 1“; {bosmon_user} = Spalte „BosMon-Benutzer“ der RIC; leer = Zuordnung über die RIC)",
        TargetKinds.Fe2 => "Einheiten (Code der Einheit in FE2, mehrere mit Komma; leer = Standard-Einheiten des Alarmeingangs)",
        _ => "Empfänger",
    };

    [JsonIgnore] public string SenderLabel => _kind switch
    {
        TargetKinds.Signal => "Eigene Signal-Nummer (z. B. +4917012345678)",
        TargetKinds.Email => "Absender-Adresse",
        TargetKinds.Fe2 => "Absender (erscheint in FE2 als Quelle)",
        _ => "Absender",
    };

    [JsonIgnore] public string PasswordLabel => _kind == TargetKinds.Fe2 ? "Autorisierung (wie unter „Gültige Absender“ im FE2-Alarmeingang)" : "Passwort";

    [JsonIgnore] public string TitleLabel => _kind switch
    {
        TargetKinds.BosMon => "Ereignisname (Feld name)",
        TargetKinds.Email => "Betreff",
        TargetKinds.Divera or TargetKinds.Fe2 => "Stichwort",
        _ => "Titel",
    };

    [JsonIgnore] public string TextLabel => _kind switch
    {
        TargetKinds.Tcp => "Ausgabezeile (eine Zeile pro Telegramm)",
        TargetKinds.BosMon => "Nachricht (Feld message)",
        TargetKinds.Fe2 => "Alarmtext (jede Zeile wird in FE2 eine eigene Meldungszeile)",
        _ => "Nachrichtentext",
    };
    [JsonIgnore] public string BodyLabel => _kind == TargetKinds.Mqtt ? "Nutzlast (Payload)" : "Body-Vorlage (bei GET ohne Bedeutung)";

    [JsonIgnore] public string FileLabel => _kind switch
    {
        TargetKinds.Program => "Programm (exe, bat, cmd, ps1 …)",
        TargetKinds.Sound => "Ton-Datei (wav oder mp3)",
        TargetKinds.Database => "Datenbank-Datei (SQLite, wird angelegt)",
        _ => "Datei",
    };

    [JsonIgnore] public string PriorityLabel => _kind == TargetKinds.Divera ? "Mit Sonderrechten (1 = ja, 0 = nein)" : "Priorität (−2 leise … 2 Notfall mit Bestätigung)";

    [JsonIgnore] public string KindHint => TargetKinds.Hint(_kind);
    [JsonIgnore] public string KindIcon => TargetKinds.Icon(_kind);
    [JsonIgnore] public bool KindIconIsText => TargetKinds.IconIsText(_kind);
    [JsonIgnore] public string KindColor => TargetKinds.Color(_kind);
    [JsonIgnore] public bool UsesContent => UsesTitle || UsesText || UsesBody || UsesKeywordDescription;
    [JsonIgnore] public bool UsesAdvanced => UsesTimeout || UsesHttp;

    /// <summary>Kurze Angabe, wohin gesendet wird (für die Liste).</summary>
    [JsonIgnore] public string Summary => _kind switch
    {
        TargetKinds.Webhook or TargetKinds.Signal or TargetKinds.Divera or TargetKinds.Fe2 => HostOf(Url),
        TargetKinds.Mqtt or TargetKinds.Email or TargetKinds.Tcp or TargetKinds.BosMon => Port > 0 ? $"{Host}:{Port}" : Host,
        TargetKinds.Telegram or TargetKinds.Pushover => Recipient.Length > 0 ? "an " + Recipient : string.Empty,
        TargetKinds.Program or TargetKinds.Sound or TargetKinds.Database => SafeFileName(FilePath),
        _ => string.Empty,
    };

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Authority : url;

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path); } catch (ArgumentException) { return path; }
    }

    // --- Laufzeit-Status (nicht gespeichert) ---------------------------------

    private string _lastResult = string.Empty;
    private bool? _lastSuccess;

    /// <summary>Ergebnis des letzten Versands, z. B. „20:38:51 · 200 OK“.</summary>
    [JsonIgnore] public string LastResult { get => _lastResult; private set => Set(ref _lastResult, value); }

    /// <summary>null = in dieser Sitzung noch nichts gesendet.</summary>
    [JsonIgnore] public bool? LastSuccess { get => _lastSuccess; private set => Set(ref _lastSuccess, value); }

    [JsonIgnore] public int SentCount { get; private set; }
    [JsonIgnore] public int FailedCount { get; private set; }

    public void ReportResult(bool success, string detail, DateTime time)
    {
        if (success) SentCount++; else FailedCount++;
        LastSuccess = success;
        LastResult = $"{time:HH:mm:ss} · {detail}";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Statistics)));
    }

    [JsonIgnore] public string Statistics => SentCount + FailedCount == 0
        ? "Seit dem Programmstart noch nichts gesendet"
        : $"Seit dem Programmstart: {SentCount} erfolgreich, {FailedCount} fehlgeschlagen";

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Url) or nameof(Host) or nameof(Port) or nameof(Recipient) or nameof(FilePath))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
    }
}
