using System.Net.Http.Headers;
using System.Text.Json;
using S2N.Localization;

namespace S2N.Services;

public sealed class GitHubReleaseClient
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/SagerNet/sing-box/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubReleaseClient()
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
    {
    }

    internal GitHubReleaseClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("S2N-Connect", "0.4.0-beta.1"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
    }

    public async Task<SingBoxReleaseAsset> ResolveLatestAssetAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var doc = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken);

        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString()
                  ?? throw new InvalidOperationException(
                       L10n.T("缺少发布标签。", "Missing release tag."));
        var version = tag.TrimStart('v');

        string os = PlatformInfo.OsToken;
        string arch = PlatformInfo.ArchitectureToken;
        string suffix = os == "windows" ? ".zip" : ".tar.gz";
        string token = $"-{os}-{arch}";

        string? packageName = null;
        string? packageUrl = null;
        string? packageDigest = null;
        string? checksumName = null;
        string? checksumUrl = null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";

            if (name.Contains("checksums", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                checksumName = name;
                checksumUrl = url;
                continue;
            }

            if (name.StartsWith($"sing-box-{version}", StringComparison.OrdinalIgnoreCase)
                && name.Contains(token, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && !name.Contains("-legacy-", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("-musl", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("-glibc", StringComparison.OrdinalIgnoreCase))
            {
                packageName = name;
                packageUrl = url;
                packageDigest = asset.TryGetProperty("digest", out var digest)
                    ? digest.GetString()
                    : null;
            }
        }

        if (packageName is null || packageUrl is null)
            throw new PlatformNotSupportedException(
                L10n.T(
                    $"未找到适用于 {os}/{arch} 的官方 sing-box 安装包。",
                    $"No official sing-box asset was found for {os}/{arch}."));

        // Some official releases publish SHA-256 through GitHub's asset
        // metadata instead of a separate checksum file. Never fall back if
        // a checksum file was published but later fails verification.
        if (checksumName is null)
            RequireSha256Digest(packageDigest);

        return new SingBoxReleaseAsset(
            version,
            packageName,
            new Uri(packageUrl),
            checksumName,
            checksumUrl is null ? null : new Uri(checksumUrl),
            packageDigest);
    }

    internal static string RequireSha256Digest(string? digest)
    {
        if (digest is null
            || digest.Length != 71
            || !digest.StartsWith("sha256:", StringComparison.Ordinal)
            || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException(
                L10n.T(
                    "该发布包缺少有效的官方 SHA-256 摘要，已拒绝安装。",
                    "The release asset has no valid official SHA-256 digest; installation was refused."));

        return digest[7..];
    }

    public async Task DownloadAsync(
        Uri uri,
        string targetPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        await using var output = File.Create(targetPath);

        var buffer = new byte[128 * 1024];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(
                   buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;
            if (total is > 0)
                progress?.Report((double)readTotal / total.Value);
        }
    }
}

public sealed record SingBoxReleaseAsset(
    string Version,
    string PackageName,
    Uri PackageUri,
    string? ChecksumName,
    Uri? ChecksumUri,
    string? PackageDigest);
