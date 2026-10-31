using PocsagDecoder.Core.Models;

namespace PocsagDecoder.App.ViewModels;

/// <summary>Zeile in der Nachrichtenliste.</summary>
public sealed class MessageItem : ObservableObject
{
    private string _forwarding = string.Empty;
    private bool _matched;
    private bool _hasError;

    /// <summary>Gleiche Meldung (RIC, Funktion, Text) kam kurz vorher schon einmal.</summary>
    public bool IsRepeat { get; set; }

    /// <summary>Mindestens ein getroffenes Telegrammereignis will einen Hinweis unten rechts.</summary>
    public bool TrayNotify { get; set; }

    public MessageItem(PocsagMessage message) => Message = message;

    public PocsagMessage Message { get; }

    public DateTime Timestamp => Message.Timestamp;
    public string Ric => Message.RicFormatted;
    public string SubRic => PocsagDecoder.Core.Forwarding.TemplateRenderer.FormatSubRic(Message);
    public int Function => Message.Function;
    public int Baud => Message.Baud;
    public string Type => Message.ContentType.ToString();
    public string Text => Message.Text;

    /// <summary>Beschreibung aus dem RIC-Verzeichnis.</summary>
    public string Description => Message.Description;

    /// <summary>Bezeichnung der RIC.</summary>
    public string RicDescription => Message.RicDescription;

    /// <summary>Ort der RIC.</summary>
    public string RicLocation => Message.RicLocation;

    /// <summary>Kurze Bezeichnung der RIC.</summary>
    public string RicShortName => Message.RicShortName;

    /// <summary>Bedeutung der SubRIC (RIC-eigene oder allgemeine Zuordnung).</summary>
    public string SubRicDescription => Message.SubRicDescription;

    /// <summary>Nach Änderungen am RIC-Verzeichnis neu anzeigen.</summary>
    public void RefreshDescription()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RicDescription));
        OnPropertyChanged(nameof(RicShortName));
        OnPropertyChanged(nameof(RicLocation));
        OnPropertyChanged(nameof(SubRicDescription));
        OnPropertyChanged(nameof(SubRic));
    }

    /// <summary>Mindestens eine Regel hat gegriffen.</summary>
    public bool Matched { get => _matched; set => Set(ref _matched, value); }

    public bool HasError { get => _hasError; set { if (Set(ref _hasError, value)) OnPropertyChanged(nameof(DeliverySummary)); } }

    /// <summary>Ergebnis der Weiterleitung(en), z.B. "Leitstelle: 200 OK".</summary>
    public string Forwarding
    {
        get => _forwarding;
        set
        {
            if (!Set(ref _forwarding, value)) return;
            OnPropertyChanged(nameof(DeliverySummary));
            OnPropertyChanged(nameof(DeliveryLines));
        }
    }

    public string DeliverySummary => HasError ? "Fehler beim Versand"
        : Forwarding.Contains("sende…") ? "Wird gesendet…"
        : Forwarding.Contains("✓") ? "Erfolgreich gesendet"
        : string.IsNullOrEmpty(Forwarding) ? "Nur empfangen" : "Verarbeitet";
    public IReadOnlyList<string> DeliveryLines => string.IsNullOrEmpty(Forwarding)
        ? ["Nur empfangen – keine Weiterleitung ausgelöst."]
        : Forwarding.Split(" | ", StringSplitOptions.RemoveEmptyEntries);
    public string TechnicalDetails => $"{Timestamp:dd.MM.yyyy HH:mm:ss} · {Baud} Baud · {Type} · Funktion {Function}\n{Message.Raw}";

    public void AppendForwarding(string text)
    {
        Forwarding = string.IsNullOrEmpty(Forwarding) ? text : Forwarding + " | " + text;
    }
}
