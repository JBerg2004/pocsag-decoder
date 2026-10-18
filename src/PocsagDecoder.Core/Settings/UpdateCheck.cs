using System.Net.Http.Headers;
using System.Text.Json;

namespace PocsagDecoder.Core.Settings;

/// <summary>Neuere Version auf GitHub (Releases). <paramref name="DownloadUrl"/> ist die angehängte .exe, falls vorhanden.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Url, string? DownloadUrl = null, long Size = 0, string? Sha256 = null);

/// <summary>Fragt die neueste Version (GitHub-Release) ab und vergleicht sie mit der laufenden.</summary>
public static class UpdateCheck
{
    public const string Repository = "JBerg2004/pocsag-decoder";
    /// <summary>Kleine Exe (braucht die .NET 8 Desktop Runtime).</summary>
    public const string SmallAsset = "PocsagDecoder.exe";

    /// <summary>Exe mit eingebauter .NET-Laufzeit.</summary>
    public const string SelfContainedAsset = "PocsagDecoder-komplett.exe";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    /// <summary>Neuere Version oder null, wenn keine neuere da ist (oder noch kein Release veröffentlicht wurde).</summary>
    public static async Task<UpdateInfo?> FindNewerAsync(Version current, string preferredAsset = SmallAsset, HttpClient? http = null, CancellationToken ct = default)
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
            if (!TryParseVersion(tag, out var latest) || !IsNewer(latest, current)) return null;
            var (download, size, sha) = FindExeAsset(doc.RootElement, preferredAsset);
            return new UpdateInfo(latest, tag, url, download, size, sha);
        }
        finally
        {
            if (own) http.Dispose();
        }
    }

    /// <summary>Programmdatei unter den Anhängen des Release: bevorzugt <paramref name="preferred"/>, sonst die erste .exe.</summary>
    public static (string? Url, long Size, string? Sha256) FindExeAsset(JsonElement release, string preferred = SmallAsset)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return (null, 0, null);
        var exes = assets.EnumerateArray()
            .Where(a => a.TryGetProperty("name", out var n) && n.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(a => a.GetProperty("name").GetString()!.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exes.Count == 0) return (null, 0, null);
        var asset = exes[0];
        var url = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
        var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var l) ? l : 0;
        // GitHub liefert „sha256:…“ als Prüfsumme der Datei mit.
        var digest = asset.TryGetProperty("digest", out var g) ? g.GetString() : null;
        var sha = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..].ToLowerInvariant() : null;
        return (url, size, sha);
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
