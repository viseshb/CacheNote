using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CacheNote.Core.Cloud;
using CacheNote.Core.Updates;

namespace CacheNote_App.Services;

public sealed class UpdateCheckResult
{
    public bool Available { get; init; }
    public string LatestVersion { get; init; } = "";
    public string? DownloadUrl { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Checks GitHub Releases for a newer build and, on request, downloads + runs the setup. Repo is
/// configured via GITHUB_OWNER/GITHUB_REPO in .env. Verifiable end-to-end only against a real repo;
/// the version-comparison logic is unit-tested (see SemVerTests).
/// </summary>
public sealed class GitHubUpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CloudConfig _cfg;

    public GitHubUpdateService(CloudConfig cfg) => _cfg = cfg;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cfg.GithubOwner) && !string.IsNullOrWhiteSpace(_cfg.GithubRepo);

    public string CurrentVersion =>
        typeof(GitHubUpdateService).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new UpdateCheckResult { Message = "Auto-update isn't configured. Set GITHUB_OWNER and GITHUB_REPO in .env." };

        try
        {
            var url = $"https://api.github.com/repos/{_cfg.GithubOwner}/{_cfg.GithubRepo}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("CacheNote-Updater");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new UpdateCheckResult { Message = $"Update check failed ({(int)resp.StatusCode})." };

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";

            string? assetUrl = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                        break;
                    }
                }
            }

            if (SemVer.IsNewer(tag, CurrentVersion))
                return new UpdateCheckResult { Available = true, LatestVersion = tag, DownloadUrl = assetUrl, Message = $"Update available: {tag}" };

            return new UpdateCheckResult { LatestVersion = tag, Message = "You're up to date." };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { Message = "Update check error: " + ex.Message };
        }
    }

    // Separate client for the installer download: the API client's 30s Timeout covers the WHOLE
    // body, and a ~78MB asset needs >20Mbps to finish in 30s — slower links failed every time.
    private static readonly HttpClient Download = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>Download the setup asset and launch it (installs over the current copy).</summary>
    public async Task<bool> DownloadAndRunAsync(string url, CancellationToken ct = default)
    {
        try
        {
            // Unique temp name: a fixed path collides with a still-running/locked previous setup.
            var tmp = Path.Combine(Path.GetTempPath(), $"CacheNoteSetup-{Guid.NewGuid():N}.exe");
            using (var resp = await Download.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(tmp);
                await src.CopyToAsync(dst, ct);
            }
            Process.Start(new ProcessStartInfo { FileName = tmp, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
