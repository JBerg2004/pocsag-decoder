using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PocsagDecoder.Core.Settings;

/// <summary>
/// Lädt eine neue Programmversion herunter und tauscht die laufende .exe aus.
/// Eine laufende .exe lässt sich unter Windows nicht überschreiben, aber umbenennen:
/// alte Datei → „.old“, neue an ihre Stelle, danach Neustart. Die „.old“ wird beim nächsten Start gelöscht.
/// </summary>
public static class UpdateInstaller
{
    public const string OldSuffix = ".old";

    /// <summary>Ordner für heruntergeladene Versionen.</summary>
    public static string DownloadDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PocsagDecoder", "update");

    /// <summary>Lädt die .exe des Release und prüft Größe, Prüfsumme und Dateikopf. Liefert den Pfad der fertigen Datei.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, string? directory = null, HttpClient? http = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (update.DownloadUrl is null) throw new InvalidOperationException("Das Release enthält keine Programmdatei (.exe).");
        directory ??= DownloadDirectory;
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"PocsagDecoder-{update.Version.ToString(3)}.exe");
        if (File.Exists(target) && Verify(target, update) is null) return target;

        var own = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var partial = target + ".part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PocsagDecoder", update.Version.ToString(3)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            if (Verify(partial, update) is { } problem) throw new InvalidDataException(problem);
            File.Move(partial, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(partial)) try { File.Delete(partial); } catch (IOException) { }
            if (own) http.Dispose();
        }
    }

    /// <summary>null wenn die Datei in Ordnung ist, sonst der Grund.</summary>
    public static string? Verify(string path, UpdateInfo update)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024) return "Die heruntergeladene Datei ist leer oder unvollständig.";
        if (update.Size > 0 && info.Length != update.Size) return $"Die heruntergeladene Datei hat {info.Length} statt {update.Size} Bytes.";
        using (var stream = File.OpenRead(path))
        {
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') return "Die heruntergeladene Datei ist kein Windows-Programm.";
            if (update.Sha256 is { Length: > 0 } expected)
            {
                stream.Position = 0;
                var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (actual != expected) return "Die Prüfsumme der heruntergeladenen Datei stimmt nicht.";
            }
        }
        return null;
    }

    /// <summary>Ersetzt <paramref name="exePath"/> durch <paramref name="newExe"/>. Bei einem Fehler bleibt die alte Datei erhalten.</summary>
    public static void Install(string newExe, string exePath)
    {
        var old = exePath + OldSuffix;
        if (File.Exists(old)) File.Delete(old);
        File.Move(exePath, old);
        try
        {
            File.Copy(newExe, exePath);
        }
        catch
        {
            if (File.Exists(exePath)) File.Delete(exePath);
            File.Move(old, exePath);
            throw;
        }
    }

    /// <summary>Nach einem Update aufräumen: alte .exe und heruntergeladene Dateien.</summary>
    public static void CleanUp(string exePath)
    {
        var old = exePath + OldSuffix;
        if (!File.Exists(old)) return;
        for (var attempt = 0; attempt < 10 && File.Exists(old); attempt++)
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(1000); }
        }
        try
        {
            if (Directory.Exists(DownloadDirectory))
                foreach (var file in Directory.GetFiles(DownloadDirectory)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* beim nächsten Mal */ }
    }
}
