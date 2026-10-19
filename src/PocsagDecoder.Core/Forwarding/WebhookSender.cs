using System.Net.Http.Headers;
using System.Text;
using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Forwarding;

public sealed record SendResult(bool Success, int? StatusCode, string Detail, int Attempts);

/// <summary>Sendet Telegramme an eine Schnittstelle; HTTP hier, alle anderen Arten in WebhookSender.Kinds.cs.</summary>
public sealed partial class WebhookSender
{
    private readonly HttpClient _http;

    public WebhookSender(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public HttpRequestMessage BuildRequest(WebhookTarget target, PocsagMessage message, string ruleName)
    {
        var url = TemplateRenderer.Render(target.Url, message, ruleName, TemplateEscaping.Url);
        var method = new HttpMethod(string.IsNullOrWhiteSpace(target.Method) ? "POST" : target.Method.Trim().ToUpperInvariant());
        var request = new HttpRequestMessage(method, url);

        if (method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Delete)
        {
            var contentType = string.IsNullOrWhiteSpace(target.ContentType) ? "text/plain" : target.ContentType.Trim();
            var escaping = contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ? TemplateEscaping.Json
                : contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ? TemplateEscaping.Url
                : TemplateEscaping.None;
            var body = TemplateRenderer.Render(target.BodyTemplate, message, ruleName, escaping);
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType.Contains("charset", StringComparison.OrdinalIgnoreCase) ? contentType : contentType + "; charset=utf-8");
        }

        foreach (var line in (target.Headers ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = TemplateRenderer.Render(line[(colon + 1)..].Trim(), message, ruleName, TemplateEscaping.None);
            if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content?.Headers.TryAddWithoutValidation(name, value);
        }

        request.Headers.UserAgent.ParseAdd("PocsagDecoder/1.0");
        return request;
    }

    private int _pending;

    /// <summary>Sendungen, die gerade laufen oder auf einen neuen Versuch warten (Warteschlange für den Heartbeat).</summary>
    public int PendingCount => Volatile.Read(ref _pending);

    public async Task<SendResult> SendAsync(WebhookTarget target, PocsagMessage message, string ruleName, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _pending);
        try { return await SendCoreAsync(target, message, ruleName, ct).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _pending); }
    }

    private async Task<SendResult> SendCoreAsync(WebhookTarget target, PocsagMessage message, string ruleName, CancellationToken ct)
    {
        if (target.Kind != TargetKinds.Webhook) return await SendOtherAsync(target, message, ruleName, ct).ConfigureAwait(false);
        var attempts = 0;
        var maxAttempts = Math.Max(1, target.Retries + 1);
        SendResult last = new(false, null, "nicht gesendet", 0);

        while (attempts < maxAttempts)
        {
            attempts++;
            try
            {
                using var request = BuildRequest(target, message, ruleName);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, target.TimeoutSeconds)));
                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                    return new SendResult(true, status, $"{status} {response.ReasonPhrase}", attempts);

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (body.Length > 200) body = body[..200] + "…";
                last = new SendResult(false, status, $"{status} {response.ReasonPhrase} {body}".Trim(), attempts);
                if (status is >= 400 and < 500 and not 408 and not 429) break; // Client-Fehler nicht wiederholen
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new SendResult(false, null, "abgebrochen", attempts);
            }
            catch (OperationCanceledException)
            {
                last = new SendResult(false, null, "Zeitüberschreitung", attempts);
            }
            catch (Exception ex) when (ex is HttpRequestException or UriFormatException or FormatException or InvalidOperationException)
            {
                last = new SendResult(false, null, ex.Message, attempts);
                if (ex is UriFormatException or FormatException or InvalidOperationException) break;
            }

            if (attempts < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, attempts * 2)), ct).ConfigureAwait(false);
        }
        return last with { Attempts = attempts };
    }
}
