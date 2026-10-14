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
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("S2N-Connect", "0.2.1"));
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
            }
        }

        if (packageName is null || packageUrl is null)
            throw new PlatformNotSupportedException(
                L10n.T(
                    $"未找到适用于 {os}/{arch} 的官方 sing-box 安装包。",
                    $"No official sing-box asset was found for {os}/{arch}."));

        if (checksumName is null || checksumUrl is null)
            throw new InvalidOperationException(
                L10n.T(
                    "该发布版本不包含校验和文件。",
                    "The release does not contain a checksum file."));

        return new SingBoxReleaseAsset(
            version,
            packageName,
            new Uri(packageUrl),
            checksumName,
            new Uri(checksumUrl));
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
    string ChecksumName,
    Uri ChecksumUri);
