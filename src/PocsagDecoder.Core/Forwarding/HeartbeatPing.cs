using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PocsagDecoder.Core.Forwarding;

/// <summary>Zustand des Decoders, wie er im Heartbeat an die URL geht.</summary>
public sealed record HeartbeatState
{
    public string Pc { get; init; } = Environment.MachineName;

    /// <summary>„ok“, „gestoppt“ oder „Fehler“ (Empfang unerwartet beendet, Stick abgezogen?).</summary>
    public string Stick { get; init; } = StickStopped;

    public DateTime? LastMessageAt { get; init; }

    /// <summary>Sendungen an Schnittstellen, die noch nicht fertig sind.</summary>
    public int QueueLength { get; init; }

    public DateTime Time { get; init; } = DateTime.Now;

    public const string StickOk = "ok";
    public const string StickStopped = "gestoppt";
    public const string StickError = "Fehler";

    public bool Healthy => Stick == StickOk;
}

/// <summary>Ergebnis eines Heartbeats: an welche URL und mit welchem Ergebnis.</summary>
public sealed record HeartbeatPingResult(bool Success, string Url, string Detail, bool UsedFailUrl);

/// <summary>
/// Heartbeat per HTTP: POST mit JSON an eine frei wählbare URL, z. B. healthchecks.io oder einen
/// Home-Assistant-Webhook. Ist der Empfang nicht in Ordnung oder schlägt die Anfrage fehl,
/// wird stattdessen die Fehler-URL aufgerufen (falls eingetragen).
/// </summary>
public sealed class HeartbeatPing
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly HttpClient _http;

    public HeartbeatPing(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static string BuildJson(HeartbeatState state) => JsonSerializer.Serialize(new Payload
    {
        Status = state.Healthy ? "ok" : "fehler",
        Pc = state.Pc,
        Stick = state.Stick,
        LastMessage = state.LastMessageAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
        LastMessageMinutesAgo = state.LastMessageAt is { } last ? (int)Math.Max(0, (state.Time - last).TotalMinutes) : null,
        QueueLength = state.QueueLength,
        Time = state.Time.ToString("yyyy-MM-ddTHH:mm:ss"),
    }, Json);

    public static HttpRequestMessage BuildRequest(string url, HeartbeatState state)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url.Trim())
        {
            Content = new StringContent(BuildJson(state), Encoding.UTF8),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
        request.Headers.UserAgent.ParseAdd("PocsagDecoder/1.0");
        return request;
    }

    public async Task<HeartbeatPingResult> SendAsync(string url, string failUrl, HeartbeatState state, CancellationToken ct = default)
    {
        var hasFail = !string.IsNullOrWhiteSpace(failUrl);
        if (!state.Healthy && hasFail)
            return await PostAsync(failUrl, state, true, ct).ConfigureAwait(false);

        var result = await PostAsync(url, state, false, ct).ConfigureAwait(false);
        if (result.Success || !hasFail) return result;

        // Haupt-URL nicht erreichbar: die Fehler-URL bekommt Bescheid, das Ergebnis bleibt ein Fehler.
        var fail = await PostAsync(failUrl, state, true, ct).ConfigureAwait(false);
        return result with { Detail = $"{result.Detail}; Fehler-URL: {fail.Detail}", UsedFailUrl = true };
    }

    private async Task<HeartbeatPingResult> PostAsync(string url, HeartbeatState state, bool isFail, CancellationToken ct)
    {
        try
        {
            using var request = BuildRequest(url, state);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            return new HeartbeatPingResult(response.IsSuccessStatusCode, url, $"{status} {response.ReasonPhrase}".Trim(), isFail);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new HeartbeatPingResult(false, url, "Zeitüberschreitung", isFail);
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or InvalidOperationException or ArgumentException)
        {
            return new HeartbeatPingResult(false, url, ex.Message, isFail);
        }
    }

    private sealed class Payload
    {
        public string Status { get; init; } = "";
        public string Pc { get; init; } = "";
        public string Stick { get; init; } = "";
        public string? LastMessage { get; init; }
        public int? LastMessageMinutesAgo { get; init; }
        public int QueueLength { get; init; }
        public string Time { get; init; } = "";
    }
}
