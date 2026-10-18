using System.Net.Http.Headers;
using System.Text.Json;

namespace PocsagDecoder.Core.Settings;

/// <summary>Neuere Version auf GitHub (Releases).</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Url);

/// <summary>Fragt die neueste Version (GitHub-Release) ab und vergleicht sie mit der laufenden.</summary>
public static class UpdateCheck
{
    public const string Repository = "JBerg2004/pocsag-decoder";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    /// <summary>Neuere Version oder null, wenn keine neuere da ist (oder noch kein Release veröffentlicht wurde).</summary>
    public static async Task<UpdateInfo?> FindNewerAsync(Version current, HttpClient? http = null, CancellationToken ct = default)
    {
        var own = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PocsagDecoder", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
            return TryParseVersion(tag, out var latest) && IsNewer(latest, current) ? new UpdateInfo(latest, tag, url) : null;
        }
        finally
        {
            if (own) http.Dispose();
        }
    }

    /// <summary>„v1.2.0“, „1.2“, „Version 1.2.3“ → Version.</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var digits = new string(tag.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray()).TrimEnd('.');
        if (!digits.Contains('.')) digits += ".0";
        return Version.TryParse(digits, out version!);
    }

    /// <summary>Vergleicht nur Haupt-, Neben- und Patch-Nummer.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
