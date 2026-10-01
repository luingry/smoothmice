using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace SmoothMice.Infrastructure.Updates;

/// <param name="ReleasePageUrl">The release HTML page (same information as GitHub Releases).</param>
/// <param name="InstallerAssetName">The installer file name in the release (e.g. SmoothMice_Setup_0.4.0.exe).</param>
/// <param name="InstallerDownloadUrl">The asset <c>browser_download_url</c> — the official release download URL.</param>
public readonly record struct DownloadProgress(long BytesRead, long? TotalBytes);

public sealed record UpdateQueryResult(
    bool Succeeded,
    string? ErrorMessage,
    bool UpdateAvailable,
    string? LatestVersionLabel,
    string? ReleasePageUrl,
    string? InstallerAssetName,
    string? InstallerDownloadUrl);

/// <summary>
/// Uses the REST API <c>GET /repos/.../releases/latest</c>, which returns the same data as the release page
/// (including <c>assets</c> with the published installer).
/// </summary>
public sealed class GitHubReleaseUpdateChecker : IDisposable
{
    private const string Owner = "luingry";
    private const string Repo = "smoothmice";

    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", UriKind.Absolute);

    private readonly HttpClient _http;
    private readonly bool _disposeClient;

    public GitHubReleaseUpdateChecker(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _http = httpClient;
            _disposeClient = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _disposeClient = true;
        }

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0";
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"SmoothMice/{v} (+https://github.com/luingry/smoothmice)");
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
            _http.Dispose();
    }

    public async Task<UpdateQueryResult> QueryLatestAsync(Version? currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(LatestReleaseUri, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new UpdateQueryResult(false, $"HTTP {(int)resp.StatusCode}: {Trim(json, 240)}", false, null, null, null, null);

            var root = JObject.Parse(json);
            var tag = root["tag_name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(tag))
                return new UpdateQueryResult(false, "Response missing tag_name.", false, null, null, null, null);

            var pageUrl = root["html_url"]?.Value<string>();
            var latest = ParseVersionLoose(tag);
            if (latest is null)
                return new UpdateQueryResult(false, $"Invalid tag: {tag}", false, null, null, null, null);

            var releasePageUrl = string.IsNullOrWhiteSpace(pageUrl)
                ? $"https://github.com/{Owner}/{Repo}/releases/latest"
                : pageUrl;

            var (installerName, installerUrl) = TryPickWindowsSetupAsset(root);

            if (currentVersion is null)
            {
                return new UpdateQueryResult(true, null, false, latest.ToString(3), releasePageUrl, installerName, installerUrl);
            }

            var newer = latest > NormalizeVersion(currentVersion);
            return new UpdateQueryResult(true, null, newer, latest.ToString(3), releasePageUrl, installerName, installerUrl);
        }
        catch (Exception ex)
        {
            return new UpdateQueryResult(false, ex.Message, false, null, null, null, null);
        }
    }

    /// <summary>Downloads the installer from the asset URL (GitHub / objects.githubusercontent.com).</summary>
    public static Task DownloadInstallerToFileAsync(
        string sourceUrl,
        string destinationPath,
        string userAgent,
        CancellationToken ct = default) =>
        DownloadInstallerToFileAsync(sourceUrl, destinationPath, userAgent, progress: null, ct);

    /// <inheritdoc cref="DownloadInstallerToFileAsync(string,string,string,CancellationToken)"/>
    /// <param name="progress">Bytes read and total (when <c>Content-Length</c> is present).</param>
    public static async Task DownloadInstallerToFileAsync(
        string sourceUrl,
        string destinationPath,
        string userAgent,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        if (!IsTrustedGitHubDownloadUrl(sourceUrl))
            throw new InvalidOperationException("Unrecognized download URL (expected GitHub).");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var response = await client
            .GetAsync(new Uri(sourceUrl), HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength is { } len && len >= 0 ? len : null;
        progress?.Report(new DownloadProgress(0, total));

        using var network = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[65536];
        long read = 0;
        while (true)
        {
            var n = await network.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
            if (n <= 0)
                break;
            await file.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
            read += n;
            progress?.Report(new DownloadProgress(read, total));
        }

        // A truncated installer makes Inno Setup fail at startup; report it here instead.
        if (total is { } expected && read != expected)
            throw new IOException($"Incomplete download: received {read} of {expected} bytes.");
    }

    private static (string? Name, string? Url) TryPickWindowsSetupAsset(JObject root)
    {
        var assets = root["assets"] as JArray;
        if (assets is null)
            return (null, null);

        string? fallbackName = null, fallbackUrl = null;

        foreach (var a in assets)
        {
            var name = a["name"]?.Value<string>();
            var url = a["browser_download_url"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.StartsWith("SmoothMice_Setup_", StringComparison.OrdinalIgnoreCase))
                return (name, url);

            if (name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0 && fallbackUrl is null)
            {
                fallbackName = name;
                fallbackUrl = url;
            }
        }

        return (fallbackName, fallbackUrl);
    }

    /// <summary>GitHub redirects asset downloads to <c>objects.githubusercontent.com</c>.</summary>
    public static bool IsTrustedGitHubDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            return false;
        var h = u.Host;
        return h.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || h.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
               || h.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    public static Version NormalizeVersion(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static Version? ParseVersionLoose(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = text.Trim();
        var plus = s.IndexOf('+');
        if (plus >= 0)
            s = s.Substring(0, plus);
        var dash = s.IndexOf('-');
        if (dash > 0)
            s = s.Substring(0, dash);
        s = s.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? NormalizeVersion(v) : null;
    }

    private static string Trim(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s.Substring(0, max) + "…";
    }
}
