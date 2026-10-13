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
}
