using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>Versand für MQTT, Messenger, E-Mail, Alarmierungsdienste, Programm, Ton, Datenbank und TCP.</summary>
public sealed partial class WebhookSender
{
    private readonly Dictionary<Guid, TcpBroadcaster> _tcp = new();

    /// <summary>Spielt eine Ton-Datei ab (wird von der Oberfläche gesetzt, da plattformabhängig).</summary>
    public Func<string, Task>? PlaySound { get; set; }

    private static bool IsLocal(string kind) => kind is TargetKinds.Program or TargetKinds.Sound or TargetKinds.Database or TargetKinds.Tcp;

    private async Task<SendResult> SendOtherAsync(WebhookTarget target, PocsagMessage message, string ruleName, CancellationToken ct)
    {
        var maxAttempts = IsLocal(target.Kind) ? 1 : Math.Max(1, target.Retries + 1);
        var attempts = 0;
        SendResult last = new(false, null, "nicht gesendet", 0);
        while (attempts < maxAttempts)
        {
            attempts++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, target.TimeoutSeconds)));
            try
            {
                var detail = await SendOnceAsync(target, message, ruleName, IsLocal(target.Kind) ? ct : timeout.Token).ConfigureAwait(false);
                return new SendResult(true, null, detail, attempts);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new SendResult(false, null, "abgebrochen", attempts);
            }
            catch (OperationCanceledException)
            {
                last = new SendResult(false, null, "Zeitüberschreitung", attempts);
            }
            catch (ConfigurationException ex)
            {
                return new SendResult(false, null, ex.Message, attempts); // Einstellungsfehler nicht wiederholen
            }
            catch (Exception ex)
            {
                last = new SendResult(false, null, ex.InnerException?.Message ?? ex.Message, attempts);
            }
            if (attempts < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, attempts * 2)), ct).ConfigureAwait(false);
        }
        return last with { Attempts = attempts };
    }

    private sealed class ConfigurationException(string message) : Exception(message);

    private static string Need(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new ConfigurationException($"{what} fehlt.") : value.Trim();

    private static string Text(string template, PocsagMessage m, string rule) =>
        TemplateRenderer.Render(template, m, rule, TemplateEscaping.None).Replace("\\n", "\n").Trim();

    private static int PortOf(WebhookTarget t) => t.Port > 0 ? t.Port : TargetKinds.DefaultPort(t.Kind, t.UseTls);

    private Task<string> SendOnceAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct) => t.Kind switch
    {
        TargetKinds.Mqtt => SendMqttAsync(t, m, rule, ct),
        TargetKinds.Telegram => SendTelegramAsync(t, m, rule, ct),
        TargetKinds.Signal => SendSignalAsync(t, m, rule, ct),
        TargetKinds.Pushover => SendPushoverAsync(t, m, rule, ct),
        TargetKinds.Email => SendEmailAsync(t, m, rule, ct),
        TargetKinds.Divera => SendDiveraAsync(t, m, rule, ct),
        TargetKinds.Fe2 => SendFe2Async(t, m, rule, ct),
        TargetKinds.Program => Task.FromResult(StartProgram(t, m, rule)),
        TargetKinds.Sound => PlaySoundAsync(t),
        TargetKinds.Database => Task.Run(() => SaveToDatabase(t, m, rule), ct),
        TargetKinds.Tcp => SendTcpAsync(t, m, rule, ct),
        _ => throw new ConfigurationException("Unbekannte Art: " + t.Kind),
    };

    // --- MQTT ----------------------------------------------------------------

    private static async Task<string> SendMqttAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var host = Need(t.Host, "Broker");
        var topic = Need(TemplateRenderer.Render(t.Topic, m, rule, TemplateEscaping.None), "Topic");
        var payload = TemplateRenderer.Render(t.BodyTemplate, m, rule, TemplateEscaping.Json);
        await MqttPublisher.PublishAsync(host, PortOf(t), t.UseTls, t.Username, t.Password, topic, payload, t.Retain, ct).ConfigureAwait(false);
        return "veröffentlicht auf " + topic;
    }

    // --- Messenger -----------------------------------------------------------

    private async Task<string> SendTelegramAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var token = Need(t.Token, "Bot-Token");
        var chat = Need(TemplateRenderer.Render(t.Recipient, m, rule, TemplateEscaping.None), "Chat-ID");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chat,
            ["text"] = Text(t.TextTemplate, m, rule),
        });
        using var response = await _http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", form, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException($"Telegram: {(int)response.StatusCode} {Describe(body, "description")}");
        return "an Telegram gesendet";
    }

    private async Task<string> SendSignalAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var url = Need(t.Url, "Adresse").TrimEnd('/');
        var recipients = Need(TemplateRenderer.Render(t.Recipient, m, rule, TemplateEscaping.None), "Empfänger")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var json = new JsonObject
        {
            ["message"] = Text(t.TextTemplate, m, rule),
            ["number"] = Need(t.Sender, "Eigene Signal-Nummer"),
            ["recipients"] = new JsonArray(recipients.Select(r => (JsonNode)r).ToArray()),
        };
        using var response = await _http.PostAsync(url + "/v2/send", JsonContent(json), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException($"Signal: {(int)response.StatusCode} {Shorten(body)}");
        return "an Signal gesendet";
    }

    private async Task<string> SendPushoverAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["token"] = Need(t.Token, "API-Token"),
            ["user"] = Need(TemplateRenderer.Render(t.Recipient, m, rule, TemplateEscaping.None), "User-Key"),
            ["message"] = Text(t.TextTemplate, m, rule) is { Length: > 0 } msg ? msg : "(ohne Text)",
            ["title"] = Text(t.TitleTemplate, m, rule),
            ["priority"] = Math.Clamp(t.Priority, -2, 2).ToString(),
        };
        if (t.Priority >= 2)
        {
            fields["retry"] = "60";   // Notfall: jede Minute erneut …
            fields["expire"] = "1800"; // … bis zu 30 Minuten oder bis zur Bestätigung
        }
        using var response = await _http.PostAsync("https://api.pushover.net/1/messages.json", new FormUrlEncodedContent(fields), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException($"Pushover: {(int)response.StatusCode} {Shorten(body)}");
        return "an Pushover gesendet";
    }

    // --- E-Mail --------------------------------------------------------------

    private static async Task<string> SendEmailAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var host = Need(t.Host, "SMTP-Server");
        var from = Need(t.Sender, "Absender");
        var to = Need(TemplateRenderer.Render(t.Recipient, m, rule, TemplateEscaping.None), "Empfänger");
        using var mail = new MailMessage { From = new MailAddress(from), Subject = Text(t.TitleTemplate, m, rule), Body = Text(t.TextTemplate, m, rule) };
        mail.SubjectEncoding = Encoding.UTF8;
        mail.BodyEncoding = Encoding.UTF8;
        foreach (var address in to.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            mail.To.Add(address);
        using var smtp = new SmtpClient(host, PortOf(t)) { EnableSsl = t.UseTls, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrEmpty(t.Username)) smtp.Credentials = new NetworkCredential(t.Username, t.Password);
        await smtp.SendMailAsync(mail, ct).ConfigureAwait(false);
        return "E-Mail gesendet an " + to;
    }

    // --- Alarmierungsdienste -------------------------------------------------

    private async Task<string> SendDiveraAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var key = Need(t.Token, "Accesskey");
        var url = string.IsNullOrWhiteSpace(t.Url) ? "https://app.divera247.com/api/alarm" : t.Url.Trim();
        url += (url.Contains('?') ? "&" : "?") + "accesskey=" + Uri.EscapeDataString(key);
        var fields = new Dictionary<string, string>
        {
            ["type"] = Text(t.TitleTemplate, m, rule) is { Length: > 0 } title ? title : "Alarm",
            ["text"] = Text(t.TextTemplate, m, rule),
            ["priority"] = t.Priority > 0 ? "1" : "0",
        };
        var ric = TemplateRenderer.Render(t.Recipient, m, rule, TemplateEscaping.None).Trim();
        if (ric.Length > 0) fields["ric"] = ric;
        using var response = await _http.PostAsync(url, new FormUrlEncodedContent(fields), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || body.Contains("\"success\":false", StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Divera: {(int)response.StatusCode} {Shorten(body)}");
        return "Alarm in Divera ausgelöst";
    }

    private async Task<string> SendFe2Async(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var url = Need(t.Url, "Adresse");
        var address = TemplateRenderer.Render(string.IsNullOrWhiteSpace(t.Recipient) ? "{ric}{subric_upper}" : t.Recipient, m, rule, TemplateEscaping.None).Trim();
        var json = new JsonObject
        {
            ["type"] = "ALARM",
            ["timestamp"] = m.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            ["sender"] = string.IsNullOrWhiteSpace(t.Sender) ? "POCSAG Decoder" : t.Sender.Trim(),
            ["authorization"] = t.Password ?? "",
            ["data"] = new JsonObject
            {
                ["keyword"] = Text(t.TitleTemplate, m, rule),
                ["message"] = new JsonArray(Text(t.TextTemplate, m, rule)),
                ["address"] = address,
                ["custom"] = new JsonObject
                {
                    ["ric"] = m.RicFormatted,
                    ["subric"] = m.SubRic.ToString(),
                    ["function"] = m.Function.ToString(),
                    ["description"] = m.RicDescription ?? "",
                    ["location"] = m.RicLocation ?? "",
                },
            },
        };
        using var response = await _http.PostAsync(url, JsonContent(json), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException($"FE2: {(int)response.StatusCode} {Shorten(body)}");
        return "an FE2 gesendet (" + address + ")";
    }

    // --- Lokal: Programm, Ton, Datenbank, TCP --------------------------------

    private static string StartProgram(WebhookTarget t, PocsagMessage m, string rule)
    {
        var file = Need(t.FilePath, "Programm");
        var args = TemplateRenderer.Render(t.Arguments ?? "", m, rule, TemplateEscaping.None).Replace("\r", " ").Replace("\n", " ");
        var info = new ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = false, CreateNoWindow = true };
        if (file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "powershell.exe";
            info.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{file}\" {args}";
        }
        else if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                 && !file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            info.UseShellExecute = true; // andere Dateien mit dem verknüpften Programm öffnen
        }
        using var p = Process.Start(info) ?? throw new IOException("Programm konnte nicht gestartet werden.");
        return "gestartet: " + Path.GetFileName(file);
    }

    private async Task<string> PlaySoundAsync(WebhookTarget t)
    {
        var file = Need(t.FilePath, "Ton-Datei");
        if (!File.Exists(file)) throw new ConfigurationException("Ton-Datei nicht gefunden: " + file);
        if (PlaySound is null) throw new ConfigurationException("Ton abspielen wird hier nicht unterstützt.");
        await PlaySound(file).ConfigureAwait(false);
        return "Ton abgespielt";
    }

    private static string SaveToDatabase(WebhookTarget t, PocsagMessage m, string rule)
    {
        var path = string.IsNullOrWhiteSpace(t.FilePath) ? DefaultDatabasePath : Environment.ExpandEnvironmentVariables(t.FilePath.Trim());
        SqliteMessageStore.Insert(path, m, rule);
        return "gespeichert in " + Path.GetFileName(path);
    }

    public static string DefaultDatabasePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PocsagDecoder", "telegramme.db");

    private async Task<string> SendTcpAsync(WebhookTarget t, PocsagMessage m, string rule, CancellationToken ct)
    {
        var server = GetTcpServer(t);
        var line = TemplateRenderer.Render(t.TextTemplate, m, rule, TemplateEscaping.None);
        var count = await server.BroadcastAsync(line, ct).ConfigureAwait(false);
        return count == 1 ? "an 1 Verbindung gesendet" : $"an {count} Verbindungen gesendet";
    }

    private TcpBroadcaster GetTcpServer(WebhookTarget t)
    {
        var address = string.IsNullOrWhiteSpace(t.Host) ? "127.0.0.1" : t.Host.Trim();
        var port = PortOf(t);
        lock (_tcp)
        {
            if (_tcp.TryGetValue(t.Id, out var existing))
            {
                if (existing.Address == address && existing.Port == port) return existing;
                existing.Dispose();
                _tcp.Remove(t.Id);
            }
            try
            {
                var server = new TcpBroadcaster(address, port);
                _tcp[t.Id] = server;
                return server;
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or FormatException)
            {
                throw new ConfigurationException($"TCP-Ausgabe auf {address}:{port} nicht möglich: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Startet die TCP-Ausgaben aller aktiven Schnittstellen sofort (damit sich Programme schon vor dem
    /// ersten Telegramm verbinden können) und beendet nicht mehr benötigte. Liefert Fehlermeldungen.
    /// </summary>
    public IReadOnlyList<string> SyncTcpServers(IEnumerable<WebhookTarget> targets)
    {
        var errors = new List<string>();
        var wanted = targets.Where(t => t.Enabled && t.Kind == TargetKinds.Tcp).ToList();
        lock (_tcp)
        {
            foreach (var id in _tcp.Keys.Where(id => wanted.All(w => w.Id != id)).ToList())
            {
                _tcp[id].Dispose();
                _tcp.Remove(id);
            }
        }
        foreach (var t in wanted)
        {
            try { GetTcpServer(t); }
            catch (ConfigurationException ex) { errors.Add($"{t.Name}: {ex.Message}"); }
        }
        return errors;
    }

    // --- Hilfen --------------------------------------------------------------

    private static StringContent JsonContent(JsonNode json) =>
        new(json.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            Encoding.UTF8, "application/json");

    private static string Shorten(string body) => body.Length > 200 ? body[..200] + "…" : body;

    private static string Describe(string body, string field)
    {
        try { return JsonNode.Parse(body)?[field]?.GetValue<string>() ?? Shorten(body); }
        catch { return Shorten(body); }
    }
}
