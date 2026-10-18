using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PocsagDecoder.Core.Redundancy;

public static class RedundancyModes
{
    public const string Off = "Aus";
    public const string Master = "Master";
    public const string Slave = "Slave";
}

/// <summary>Eine Meldung, die der Master empfangen hat (für den Abgleich mit dem Slave).</summary>
public sealed record SeenMessage(DateTime Time, int Ric, int Function, string Text);

/// <summary>Antwort des Masters auf eine Anfrage des Slaves.</summary>
public sealed record MasterStatus
{
    public string Version { get; init; } = string.Empty;

    /// <summary>Der Empfang läuft (Start gedrückt, Decoder nicht abgestürzt).</summary>
    public bool Running { get; init; }

    public DateTime? LastReceived { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;

    /// <summary>Meldungen der letzten Minuten.</summary>
    public List<SeenMessage> Recent { get; init; } = new();

    public string? Error { get; init; }
}

/// <summary>
/// Einfaches Zeilenprotokoll über TCP (kein HttpListener, der unter Windows Administratorrechte bräuchte):
/// Slave sendet „PING &lt;Kennwort&gt; &lt;Name&gt;“, Master antwortet mit einer JSON-Zeile und schließt.
/// </summary>
public static class RedundancyProtocol
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(MasterStatus status) => JsonSerializer.Serialize(status, Json);

    public static MasterStatus Deserialize(string line) =>
        JsonSerializer.Deserialize<MasterStatus>(line, Json) ?? throw new InvalidDataException("Leere Antwort vom Master.");

    /// <summary>Anfrage des Slaves. Wirft bei Verbindungsfehlern.</summary>
    public static async Task<MasterStatus> QueryAsync(string host, int port, string key, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            await using var stream = client.GetStream();
            var request = Encoding.UTF8.GetBytes($"PING {Escape(key)} {Escape(Environment.MachineName)}\n");
            await stream.WriteAsync(request, cts.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new IOException("Der Master hat die Verbindung ohne Antwort beendet.");
            var status = Deserialize(line);
            if (status.Error is { Length: > 0 } error) throw new UnauthorizedAccessException(error);
            return status;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Keine Antwort von {host}:{port}.");
        }
    }

    /// <summary>Zerlegt „PING &lt;Kennwort&gt; &lt;Name&gt;“; null bei anderem Inhalt.</summary>
    public static (string Key, string Peer)? ParseRequest(string? line)
    {
        if (line is null || !line.StartsWith("PING", StringComparison.Ordinal)) return null;
        var parts = line.Split(' ');
        return (parts.Length > 1 ? Unescape(parts[1]) : string.Empty, parts.Length > 2 ? Unescape(parts[2]) : string.Empty);
    }

    // Leerzeichen im Kennwort oder Rechnernamen nicht als Trenner werten.
    private static string Escape(string s) => Uri.EscapeDataString(s ?? string.Empty);
    private static string Unescape(string s) => Uri.UnescapeDataString(s);
}
