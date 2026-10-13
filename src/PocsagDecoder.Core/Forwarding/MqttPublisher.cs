using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>
/// Minimaler MQTT-3.1.1-Client: verbinden, eine Nachricht mit QoS 1 veröffentlichen, trennen.
/// Reicht für das Weiterleiten einzelner Telegramme und braucht keine Zusatzbibliothek.
/// </summary>
public static class MqttPublisher
{
    public static async Task PublishAsync(string host, int port, bool tls, string? username, string? password,
        string topic, string payload, bool retain, CancellationToken ct)
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
            var clientId = "pocsag-decoder-" + Guid.NewGuid().ToString("N")[..8];
            await stream.WriteAsync(BuildConnect(clientId, username, password), ct).ConfigureAwait(false);
            var connack = await ReadPacketAsync(stream, ct).ConfigureAwait(false);
            if (connack.Type != 2 || connack.Body.Length < 2)
                throw new IOException("Keine gültige Antwort vom MQTT-Broker.");
            if (connack.Body[1] != 0)
                throw new IOException(connack.Body[1] switch
                {
                    4 => "MQTT: Benutzername oder Passwort falsch.",
                    5 => "MQTT: nicht berechtigt.",
                    _ => $"MQTT: Verbindung abgelehnt (Code {connack.Body[1]}).",
                });

            const ushort packetId = 1;
            await stream.WriteAsync(BuildPublish(topic, payload, retain, packetId), ct).ConfigureAwait(false);
            var puback = await ReadPacketAsync(stream, ct).ConfigureAwait(false);
            if (puback.Type != 4) throw new IOException("MQTT: Broker hat die Nachricht nicht bestätigt.");

            await stream.WriteAsync(new byte[] { 0xE0, 0x00 }, ct).ConfigureAwait(false); // DISCONNECT
        }
    }

    public static byte[] BuildConnect(string clientId, string? username, string? password)
    {
        var body = new List<byte>();
        AddString(body, "MQTT");
        body.Add(4); // Protokoll 3.1.1
        byte flags = 0x02; // Clean Session
        if (!string.IsNullOrEmpty(username)) flags |= 0x80;
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password)) flags |= 0x40;
        body.Add(flags);
        body.Add(0); body.Add(60); // Keep-Alive 60 s
        AddString(body, clientId);
        if ((flags & 0x80) != 0) AddString(body, username!);
        if ((flags & 0x40) != 0) AddString(body, password!);
        return Packet(0x10, body);
    }

    public static byte[] BuildPublish(string topic, string payload, bool retain, ushort packetId)
    {
        var body = new List<byte>();
        AddString(body, topic);
        body.Add((byte)(packetId >> 8)); body.Add((byte)packetId);
        body.AddRange(Encoding.UTF8.GetBytes(payload));
        return Packet((byte)(0x30 | 0x02 | (retain ? 0x01 : 0)), body); // QoS 1
    }

    private static byte[] Packet(byte header, List<byte> body)
    {
        var result = new List<byte> { header };
        var len = body.Count;
        do
        {
            var digit = (byte)(len % 128);
            len /= 128;
            if (len > 0) digit |= 0x80;
            result.Add(digit);
        } while (len > 0);
        result.AddRange(body);
        return result.ToArray();
    }

    private static void AddString(List<byte> target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        target.Add((byte)(bytes.Length >> 8));
        target.Add((byte)bytes.Length);
        target.AddRange(bytes);
    }

    private static async Task<(int Type, byte[] Body)> ReadPacketAsync(Stream stream, CancellationToken ct)
    {
        var header = await ReadByteAsync(stream, ct).ConfigureAwait(false);
        int length = 0, multiplier = 1;
        byte digit;
        do
        {
            digit = await ReadByteAsync(stream, ct).ConfigureAwait(false);
            length += (digit & 0x7F) * multiplier;
            multiplier *= 128;
        } while ((digit & 0x80) != 0);
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        return (header >> 4, body);
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken ct)
    {
        var b = new byte[1];
        await stream.ReadExactlyAsync(b, ct).ConfigureAwait(false);
        return b[0];
    }
}
