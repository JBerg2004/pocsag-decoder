using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security;
using System.Text;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>
/// Nachbau des BosMon-„Netzwerkereignisses“ im Format „Erweitert“ (event format="2"), so wie BosMon es
/// z. B. an den BosMon-Eingang von FE2 schickt: je Telegramm eine TCP-Verbindung, ein XML-Dokument
/// (UTF-8, CRLF), danach wird die Verbindung geschlossen. Aufgebaut nach einem Mitschnitt von BosMon.
/// </summary>
public static class BosMonNetworkEvent
{
    /// <summary>Telegrammtyp POCSAG in BosMon.</summary>
    public const int TypePocsag = 3;

    /// <summary>Wert des Feldes flags bei POCSAG-Telegrammen im Mitschnitt.</summary>
    public const int FlagsPocsag = 1048576;

    public static string Build(string eventName, PocsagMessage m, string message, string? functionShort, string? functionLong, string? user = null)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append("\r\n");

        Line("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        Line("<event format=\"2\">");
        Line($"<name>{Esc(eventName)}</name>");
        if (!string.IsNullOrWhiteSpace(user)) Line($"<user>{Esc(user.Trim())}</user>"); // BosMon: Feld „Benutzer“, in FE2 der Einheiten-Code
        Line("<telegram>");
        Line($"<function>{char.ToLowerInvariant(m.SubRic)}</function>");
        Line($"<message>{Esc(MessageField(m, message))}</message>");
        Line($"<type>{TypePocsag}</type>");
        Line($"<address>{m.RicFormatted}</address>");
        Line($"<timestamp>{new DateTimeOffset(m.Timestamp).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}</timestamp>");
        Line($"<flags>{FlagsPocsag}</flags>");
        if (!string.IsNullOrWhiteSpace(functionShort) || !string.IsNullOrWhiteSpace(functionLong))
        {
            Line("<description>");
            Line("<function>");
            Line($"<short>{Esc(functionShort ?? functionLong ?? "")}</short>");
            Line($"<long>{Esc(functionLong ?? functionShort ?? "")}</long>");
            Line("</function>");
            Line("</description>");
        }
        Line("</telegram>");
        Line("</event>");
        return sb.ToString();
    }

    /// <summary>
    /// Wie BosMon (Mitschnitte vom 27.10.2026): der Text mit seinen Leerzeichen am Anfang und Ende,
    /// ein reiner Tonruf mit leerer Nachricht, ein Text-Telegramm ohne lesbaren Inhalt mit einem Leerzeichen.
    /// </summary>
    private static string MessageField(PocsagMessage m, string message)
    {
        if (message == m.Text && !string.IsNullOrEmpty(m.UntrimmedText)) return m.UntrimmedText;
        return message.Length > 0 || m.ContentType == PocsagContentType.Tone ? message : " ";
    }

    private static string Esc(string s) => SecurityElement.Escape(s) ?? "";

    public static async Task SendAsync(string host, int port, bool tls, string xml, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
        Stream stream = client.GetStream();
        if (tls)
        {
            var ssl = new SslStream(stream, false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct).ConfigureAwait(false);
            stream = ssl;
        }
        await using (stream.ConfigureAwait(false))
        {
            var data = new UTF8Encoding(false).GetBytes(xml);
            await stream.WriteAsync(data, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
    }
}
