namespace PocsagDecoder.Core.Forwarding;

/// <summary>Arten von Schnittstellen. Die Werte stehen so in der settings.json.</summary>
public static class TargetKinds
{
    public const string Webhook = "Webhook / REST";
    public const string Mqtt = "MQTT";
    public const string Telegram = "Telegram";
    public const string Signal = "Signal";
    public const string Pushover = "Pushover";
    public const string Email = "E-Mail (SMTP)";
    public const string Divera = "Divera 24/7";
    public const string Fe2 = "FE2";
    public const string Program = "Programm starten";
    public const string Sound = "Ton abspielen";
    public const string Database = "Datenbank (SQLite)";
    public const string Tcp = "TCP-Ausgabe";

    public static IReadOnlyList<string> All { get; } =
        [Webhook, Mqtt, Telegram, Signal, Pushover, Email, Divera, Fe2, Program, Sound, Database, Tcp];

    public static string Normalize(string? kind) =>
        All.FirstOrDefault(k => string.Equals(k, kind?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Webhook;

    public static int DefaultPort(string kind, bool tls) => kind switch
    {
        Mqtt => tls ? 8883 : 1883,
        Email => tls ? 587 : 25,
        Tcp => 9000,
        _ => 0,
    };

    public static string Hint(string kind) => kind switch
    {
        Webhook => "Allgemeiner HTTP-Aufruf, z. B. Home Assistant, Node-RED oder eigene Server.",
        Mqtt => "Veröffentlicht jedes Telegramm auf einem MQTT-Broker (z. B. Mosquitto in Home Assistant). Topic und Nutzlast mit Platzhaltern.",
        Telegram => "Nachricht über einen eigenen Telegram-Bot. Bot bei @BotFather anlegen, dem Bot einmal schreiben bzw. ihn in die Gruppe holen, Chat-ID z. B. über @userinfobot ermitteln.",
        Signal => "Benötigt einen laufenden signal-cli-rest-api-Dienst (z. B. als Docker auf einem Raspberry Pi) mit registrierter Nummer.",
        Pushover => "Push-Nachricht über pushover.net. Priorität 2 = Notfall, wiederholt sich bis zur Bestätigung.",
        Email => "Versand über einen SMTP-Server. TLS = STARTTLS (meist Port 587). Port 465 (reines SSL) wird nicht unterstützt.",
        Divera => "Löst einen Alarm in Divera 24/7 aus (Web-API mit Accesskey).",
        Fe2 => "Sendet an die Externe Schnittstelle von FE2 (Alamos). In FE2 unter Eingänge eine „Externe Schnittstelle“ anlegen und das Passwort hier eintragen.",
        Program => "Startet ein Programm oder Skript. Argumente mit Platzhaltern, Werte mit Leerzeichen in Anführungszeichen setzen.",
        Sound => "Spielt am PC einen Ton ab (wav oder mp3).",
        Database => "Speichert jedes Telegramm in einer SQLite-Datenbank für eigene Auswertungen (z. B. mit DB Browser for SQLite oder Excel).",
        Tcp => "Andere Programme können sich per TCP verbinden und bekommen jedes Telegramm als Textzeile. 127.0.0.1 = nur dieser PC, 0.0.0.0 = ganzes Netzwerk.",
        _ => string.Empty,
    };

    /// <summary>Symbol aus „Segoe MDL2 Assets“ / „Segoe Fluent Icons“.</summary>
    public static string Icon(string kind) => kind switch
    {
        Webhook => "\uE774",   // Globus
        Mqtt => "\uE701",      // Funk
        Telegram => "\uE724",  // Senden
        Signal => "\uE8BD",    // Nachricht
        Pushover => "\uEA8F",  // Glocke
        Email => "\uE715",     // Brief
        Divera => "\uE7BA",    // Warnung
        Fe2 => "\uE945",       // Blitz
        Program => "\uE756",   // Eingabeaufforderung
        Sound => "\uE767",     // Lautsprecher
        Database => "\uE8F1",  // Bibliothek
        Tcp => "\uE839",       // Netzwerkkabel
        _ => "\uE774",
    };

    /// <summary>Farbe des Symbols (Hex).</summary>
    public static string Color(string kind) => kind switch
    {
        Webhook => "#2563EB",
        Mqtt => "#7C3AED",
        Telegram => "#0EA5E9",
        Signal => "#3A76F0",
        Pushover => "#0891B2",
        Email => "#475569",
        Divera => "#DC2626",
        Fe2 => "#EA580C",
        Program => "#334155",
        Sound => "#DB2777",
        Database => "#0D9488",
        Tcp => "#4F46E5",
        _ => "#6B7280",
    };

    public static string Category(string kind) => kind switch
    {
        Webhook or Mqtt or Tcp => "Netzwerk und Smart Home",
        Telegram or Signal or Pushover or Email => "Nachrichten aufs Handy",
        Divera or Fe2 => "Alarmierungssysteme",
        _ => "Auf diesem PC",
    };

    /// <summary>Kurzbeschreibung für die Auswahl beim Anlegen.</summary>
    public static string Short(string kind) => kind switch
    {
        Webhook => "HTTP-Aufruf, z. B. Home Assistant oder Node-RED",
        Mqtt => "An einen MQTT-Broker veröffentlichen",
        Tcp => "Andere Programme per TCP verbinden lassen",
        Telegram => "Nachricht über einen eigenen Bot",
        Signal => "Über signal-cli-rest-api",
        Pushover => "Push-Nachricht, auch als Notfall",
        Email => "E-Mail über einen SMTP-Server",
        Divera => "Alarm in Divera 24/7 auslösen",
        Fe2 => "Externe Schnittstelle von FE2 (Alamos)",
        Program => "Programm oder Skript starten",
        Sound => "Ton am PC abspielen",
        Database => "In einer SQLite-Datei speichern",
        _ => string.Empty,
    };
}
