using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>Ein Ziel für die Weiterleitung (REST-API, Webhook, einfacher Webaufruf).</summary>
public sealed class WebhookTarget : INotifyPropertyChanged
{
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

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
