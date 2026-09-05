using System.Net.Http.Headers;
using System.Text.Json;

namespace BGModdingTool.Core.Services;

/// <summary>
/// Downloads mod archives from direct URLs or GitHub repositories.
/// For a bare GitHub repo URL the latest release is resolved via the GitHub API
/// (preferring an uploaded archive asset, falling back to the source zipball).
/// </summary>
public sealed class ModDownloader : IDisposable
{
    private static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar", ".tar.gz", ".tgz", ".iemod"];
    private readonly HttpClient _http;

    public ModDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BGModdingTool", "1.0"));
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    /// <summary>Downloads to destDir and returns the local archive path.</summary>
    public async Task<string> DownloadArchiveAsync(string url, string destDir,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        url = url.Trim();
        Directory.CreateDirectory(destDir);

        if (TryParseGitHubRepo(url, out var owner, out var repo) && !LooksLikeDirectFile(url))
        {
            status?.Report($"Looking up the latest release of {owner}/{repo} on GitHub…");
            var (assetUrl, fileName) = await ResolveGitHubReleaseAsync(owner, repo, ct);
            status?.Report($"Downloading {fileName}…");
            return await DownloadToFileAsync(assetUrl, Path.Combine(destDir, fileName), status, ct);
        }

        var name = FileNameFromUrl(url);
        status?.Report($"Downloading {name}…");
        return await DownloadToFileAsync(url, Path.Combine(destDir, name), status, ct);
    }

    /// <summary>
    /// Downloads the latest official WeiDU (WeiDUorg/weidu) Windows build and
    /// extracts weidu.exe into toolsDir. Returns the path to weidu.exe.
    /// </summary>
    public async Task<string> DownloadWeiduAsync(string toolsDir,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        status?.Report("Looking up the latest WeiDU on GitHub…");
        var release = await GetJsonAsync($"https://api.github.com/repos/WeiDUorg/weidu/releases/latest", ct);
        // Prefer the regular Windows package: "+legacy" ships only the 32-bit build,
        // which runs out of memory on big EET installs (Talents of Faerûn, SCS).
        var asset = release.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString()!.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.GetProperty("name").GetString()!.Contains("legacy", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("No Windows WeiDU package found in the latest release.");

        var name = asset.GetProperty("name").GetString()!;
        var url = asset.GetProperty("browser_download_url").GetString()!;
        Directory.CreateDirectory(toolsDir);
        var archive = Path.Combine(toolsDir, name);
        status?.Report($"Downloading {name}…");
        await DownloadToFileAsync(url, archive, status, ct);

        status?.Report("Extracting weidu.exe…");
        var weiduPath = Path.Combine(toolsDir, "weidu.exe");
        using (var arch = SharpCompress.Archives.ArchiveFactory.OpenArchive(archive))
        {
            var entry = arch.Entries
                .Where(e => !e.IsDirectory && Path.GetFileName(e.Key ?? "").Equals("weidu.exe", StringComparison.OrdinalIgnoreCase))
                // prefer 64-bit build when the archive ships both
                .OrderByDescending(e => (e.Key ?? "").Contains("amd64") || (e.Key ?? "").Contains("x86_64"))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("The WeiDU archive does not contain weidu.exe.");
            using var src = entry.OpenEntryStream();
            using var dst = File.Create(weiduPath);
            await src.CopyToAsync(dst, ct);
        }
        try { File.Delete(archive); } catch { }
        return weiduPath;
    }

    private async Task<string> DownloadToFileAsync(string url, string destPath,
        IProgress<string>? status, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Honor server-provided filename when we guessed poorly.
        var suggested = response.Content.Headers.ContentDisposition?.FileNameStar
                     ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        if (!string.IsNullOrEmpty(suggested))
            destPath = Path.Combine(Path.GetDirectoryName(destPath)!, SanitizeFileName(suggested));

        var total = response.Content.Headers.ContentLength;
        var tmp = destPath + ".part";
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long done = 0; int read; var lastReport = DateTime.MinValue;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(300))
                {
                    lastReport = DateTime.UtcNow;
                    status?.Report(total is > 0
                        ? $"Downloading {Path.GetFileName(destPath)}: {done / 1048576.0:F1}/{total / 1048576.0:F1} MB"
                        : $"Downloading {Path.GetFileName(destPath)}: {done / 1048576.0:F1} MB");
                }
            }
        }
        File.Move(tmp, destPath, overwrite: true);
        return destPath;
    }

    private async Task<(string Url, string FileName)> ResolveGitHubReleaseAsync(string owner, string repo, CancellationToken ct)
    {
        JsonElement release;
        try
        {
            release = await GetJsonAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", ct);
        }
        catch (HttpRequestException)
        {
            // No releases — fall back to the default-branch source archive.
            return ($"https://api.github.com/repos/{owner}/{repo}/zipball", $"{repo}.zip");
        }

        var assets = release.TryGetProperty("assets", out var a) ? a.EnumerateArray().ToList() : [];
        // Prefer Windows/universal packages; avoid picking a Linux/macOS build by accident.
        var best = assets
            .Where(x => IsArchiveName(x.GetProperty("name").GetString()!))
            .OrderByDescending(x => AssetScore(x.GetProperty("name").GetString()!))
            .FirstOrDefault();
        if (best.ValueKind != JsonValueKind.Undefined)
            return (best.GetProperty("browser_download_url").GetString()!, best.GetProperty("name").GetString()!);

        var zipball = release.GetProperty("zipball_url").GetString()!;
        var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : "latest";
        return (zipball, $"{repo}-{tag}.zip");
    }

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.Clone();
    }

    private static bool TryParseGitHubRepo(string url, out string owner, out string repo)
    {
        owner = repo = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2) return false;
        owner = parts[0];
        repo = parts[1].EndsWith(".git") ? parts[1][..^4] : parts[1];
        return true;
    }

    private static bool LooksLikeDirectFile(string url) =>
        url.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase) || IsArchiveName(url);

    private static int AssetScore(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("lin") && !n.Contains("win")) return -10;
        if (n.Contains("osx") || n.Contains("mac")) return -10;
        if (n.Contains("win")) return 10;
        if (n.EndsWith(".iemod")) return 5; // platform-independent WeiDU package
        return 0;
    }

    private static bool IsArchiveName(string name) =>
        ArchiveExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static string FileNameFromUrl(string url)
    {
        var name = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.AbsolutePath) : Path.GetFileName(url);
        return string.IsNullOrWhiteSpace(name) ? "download.zip" : SanitizeFileName(name);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public void Dispose() => _http.Dispose();
}
