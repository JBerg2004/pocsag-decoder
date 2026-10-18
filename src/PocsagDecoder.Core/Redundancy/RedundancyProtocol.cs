using System.Net.Sockets;
using System.Security.Cryptography;
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

    /// <summary>Zufallswert aus der Anfrage, zurückgeschickt in der verschlüsselten Antwort.</summary>
    public string? Nonce { get; init; }
}

/// <summary>
/// Einfaches Zeilenprotokoll über TCP (kein HttpListener, der unter Windows Administratorrechte bräuchte).
/// Slave: „PING &lt;Zufallswert&gt; &lt;Nachweis&gt; &lt;Name&gt;“. Master: eine Zeile mit der Antwort, danach schließt er.
/// Mit Kennwort beweist der Slave per HMAC, dass er es kennt, und der Master verschlüsselt seine Antwort (AES-GCM)
/// samt dem Zufallswert des Slaves. Das Kennwort selbst geht nie über die Leitung, mitgeschnittene Antworten
/// lassen sich nicht wieder einspielen, und auch über das Internet kann niemand die Alarmtexte mitlesen.
/// </summary>
public static class RedundancyProtocol
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string EncryptedPrefix = "ENC ";
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("PocsagDecoder.Redundancy.v1");
    private static readonly object KeyLock = new();
    private static (string Password, byte[] Key)? _keyCache;

    public static string Serialize(MasterStatus status) => JsonSerializer.Serialize(status, Json);

    public static MasterStatus Deserialize(string line) =>
        JsonSerializer.Deserialize<MasterStatus>(line, Json) ?? throw new InvalidDataException("Leere Antwort vom Master.");

    /// <summary>Anfrage des Slaves. Wirft bei Verbindungsfehlern und falschem Kennwort.</summary>
    public static async Task<MasterStatus> QueryAsync(string host, int port, string key, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var client = new TcpClient(); // ohne feste Adressfamilie: IPv4 und IPv6 je nach Adresse
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            await using var stream = client.GetStream();
            var nonce = RandomNumberGenerator.GetBytes(16);
            var request = Encoding.UTF8.GetBytes(BuildRequest(nonce, key, Environment.MachineName) + "\n");
            await stream.WriteAsync(request, cts.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new IOException("Der Master hat die Verbindung ohne Antwort beendet.");
            return ReadResponse(line, nonce, key);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Keine Antwort von {host}:{port}.");
        }
    }

    public static string BuildRequest(byte[] nonce, string key, string name) =>
        $"PING {Convert.ToBase64String(nonce)} {Proof(nonce, key)} {Uri.EscapeDataString(name ?? string.Empty)}";

    /// <summary>Zerlegt die Anfrage; null bei fremdem Inhalt.</summary>
    public static (byte[] Nonce, string Proof, string Peer)? ParseRequest(string? line)
    {
        if (line is null || !line.StartsWith("PING ", StringComparison.Ordinal)) return null;
        var parts = line.Split(' ');
        if (parts.Length < 3) return null;
        try
        {
            var nonce = Convert.FromBase64String(parts[1]);
            if (nonce.Length != 16) return null;
            return (nonce, parts[2], parts.Length > 3 ? Uri.UnescapeDataString(parts[3]) : string.Empty);
        }
        catch (FormatException) { return null; }
    }

    /// <summary>Hat der Slave das richtige Kennwort? Ohne Kennwort am Master wird jede Anfrage angenommen.</summary>
    public static bool CheckProof(byte[] nonce, string proof, string key) =>
        key.Length == 0 || CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proof), Encoding.ASCII.GetBytes(Proof(nonce, key)));

    /// <summary>Antwortzeile des Masters: mit Kennwort verschlüsselt, ohne als einfaches JSON.</summary>
    public static string BuildResponse(MasterStatus status, byte[] nonce, string key)
    {
        if (key.Length == 0) return Serialize(status);
        var plain = Encoding.UTF8.GetBytes(Serialize(status with { Nonce = Convert.ToBase64String(nonce) }));
        var iv = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(DeriveKey(key), tag.Length)) aes.Encrypt(iv, plain, cipher, tag);
        return EncryptedPrefix + Convert.ToBase64String([.. iv, .. tag, .. cipher]);
    }

    public static MasterStatus ReadResponse(string line, byte[] nonce, string key)
    {
        MasterStatus status;
        if (line.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            if (key.Length == 0) throw new UnauthorizedAccessException("Der Master verlangt ein Kennwort, hier ist keins eingetragen.");
            byte[] data;
            try { data = Convert.FromBase64String(line[EncryptedPrefix.Length..]); }
            catch (FormatException) { throw new InvalidDataException("Unlesbare Antwort vom Master."); }
            if (data.Length < 28) throw new InvalidDataException("Unvollständige Antwort vom Master.");
            var plain = new byte[data.Length - 28];
            try
            {
                using var aes = new AesGcm(DeriveKey(key), 16);
                aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain);
            }
            catch (CryptographicException) { throw new UnauthorizedAccessException("Kennwort stimmt nicht mit dem Master überein."); }
            status = Deserialize(Encoding.UTF8.GetString(plain));
            if (status.Nonce != Convert.ToBase64String(nonce)) throw new UnauthorizedAccessException("Antwort passt nicht zur Anfrage (wiederholte oder gefälschte Antwort).");
            return status;
        }
        status = Deserialize(line);
        if (status.Error is { Length: > 0 } error) throw new UnauthorizedAccessException(error);
        if (key.Length > 0) throw new UnauthorizedAccessException("Am Master ist kein Kennwort eingetragen, hier schon. Bitte auf beiden PCs gleich einstellen.");
        return status;
    }

    private static string Proof(byte[] nonce, string key) =>
        key.Length == 0 ? "-" : Convert.ToBase64String(HMACSHA256.HashData(DeriveKey(key), Encoding.ASCII.GetBytes("ping:").Concat(nonce).ToArray()));

    /// <summary>Schlüssel aus dem Kennwort (PBKDF2), einmal berechnet und gemerkt.</summary>
    private static byte[] DeriveKey(string password)
    {
        lock (KeyLock)
        {
            if (_keyCache is { } cached && cached.Password == password) return cached.Key;
            var key = Rfc2898DeriveBytes.Pbkdf2(password, Salt, 200_000, HashAlgorithmName.SHA256, 32);
            _keyCache = (password, key);
            return key;
        }
    }
}
