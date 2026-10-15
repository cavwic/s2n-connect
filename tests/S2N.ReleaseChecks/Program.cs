using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using S2N.Services;

var temporaryDirectory = Directory.CreateTempSubdirectory("S2N.ReleaseChecks-").FullName;
try
{
    await RunChecksAsync(temporaryDirectory);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
finally
{
    var directory = Path.GetFullPath(temporaryDirectory);
    var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
        + Path.DirectorySeparatorChar;
    if (directory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(directory).StartsWith("S2N.ReleaseChecks-", StringComparison.Ordinal))
        Directory.Delete(directory, recursive: true);
}

static async Task RunChecksAsync(string directory)
{
    const string packageName = "sing-box-1.14.3-windows-amd64.zip";
    var packagePath = Path.Combine(directory, packageName);
    var checksumPath = Path.Combine(directory, "checksums.txt");
    var bytes = Encoding.UTF8.GetBytes("Synthetic package bytes. Never execute this fixture.");
    await File.WriteAllBytesAsync(packagePath, bytes);
    var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    var digest = "sha256:" + expectedHash;
    var wrongHash = new string('0', 64);
    var passed = 0;

    void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        passed++;
    }

    void Rejects(Action action, string description)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidOperationException
            or InvalidDataException or CryptographicException or IOException)
        {
            passed++;
            return;
        }
        throw new InvalidOperationException("FAIL: " + description);
    }

    async Task RejectsResolutionAsync(string json, string description)
    {
        try { await ResolveAsync(json); }
        catch (InvalidDataException)
        {
            passed++;
            return;
        }
        throw new InvalidOperationException("FAIL: " + description);
    }

    string ReleaseJson(string? packageDigest, bool withChecksums = false, bool omitDigest = false)
    {
        var package = new Dictionary<string, object?>
        {
            ["name"] = packageName,
            ["browser_download_url"] = "https://example.invalid/" + packageName
        };
        if (!omitDigest) package["digest"] = packageDigest;
        var assets = new List<object> { package };
        if (withChecksums)
            assets.Add(new
            {
                name = "sing-box-1.14.3-checksums.txt",
                browser_download_url = "https://example.invalid/checksums.txt"
            });
        return JsonSerializer.Serialize(new { tag_name = "v1.14.3", assets });
    }

    var checksumRelease = await ResolveAsync(ReleaseJson(null, withChecksums: true));
    Check(checksumRelease.ChecksumUri is not null && checksumRelease.ChecksumName is not null,
        "checksum releases remain supported without an asset digest");
    await File.WriteAllTextAsync(checksumPath, expectedHash + "  " + packageName + "\n");
    SingBoxInstaller.VerifyPackage(packagePath, checksumPath, checksumRelease);
    Check(true, "checksum file validates the package hash");

    var bothSources = await ResolveAsync(ReleaseJson("sha256:" + wrongHash, withChecksums: true));
    SingBoxInstaller.VerifyPackage(packagePath, checksumPath, bothSources);
    Check(true, "published checksum file takes priority over the asset digest");
    await File.WriteAllTextAsync(checksumPath, expectedHash.ToUpperInvariant() + " *" + packageName + "\n");
    SingBoxInstaller.VerifyPackage(packagePath, checksumPath, checksumRelease);
    Check(true, "binary checksum marker and uppercase hex are supported");

    var matchingDigestAndChecksum = await ResolveAsync(ReleaseJson(digest, withChecksums: true));
    await File.WriteAllTextAsync(checksumPath, wrongHash + "  " + packageName + "\n");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, checksumPath, matchingDigestAndChecksum),
        "mismatching checksum must not fall back to a valid asset digest");
    await File.WriteAllTextAsync(checksumPath, "not-a-hash  " + packageName + "\n");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, checksumPath, matchingDigestAndChecksum),
        "malformed checksum must not fall back to the asset digest");
    await File.WriteAllTextAsync(checksumPath, expectedHash + "  unrelated.zip\n");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, checksumPath, matchingDigestAndChecksum),
        "missing package checksum must not fall back to the asset digest");
    await File.WriteAllTextAsync(checksumPath,
        expectedHash + "  " + packageName + "\n" + expectedHash + "  " + packageName + "\n");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, checksumPath, matchingDigestAndChecksum),
        "duplicate matching checksum entries are rejected");
    await File.WriteAllTextAsync(checksumPath,
        "malformed  " + packageName + "\n" + expectedHash + "  " + packageName + "\n");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, checksumPath, matchingDigestAndChecksum),
        "a valid line cannot hide a duplicate malformed checksum for the same package");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, null, matchingDigestAndChecksum),
        "missing downloaded checksum is rejected even when the digest matches");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, Path.Combine(directory, "missing.txt"), matchingDigestAndChecksum),
        "unavailable checksum file is rejected even when the digest matches");

    var digestRelease = await ResolveAsync(ReleaseJson(digest));
    Check(digestRelease.ChecksumUri is null && digestRelease.PackageDigest == digest,
        "a digest-only release resolves with its selected package digest");
    SingBoxInstaller.VerifyPackage(packagePath, null, digestRelease);
    Check(true, "digest-only releases validate the package hash");
    var uppercaseDigest = await ResolveAsync(ReleaseJson("sha256:" + expectedHash.ToUpperInvariant()));
    SingBoxInstaller.VerifyPackage(packagePath, null, uppercaseDigest);
    Check(true, "asset digest hexadecimal values are case insensitive");

    await RejectsResolutionAsync(ReleaseJson(null, omitDigest: true), "absent digest is rejected");
    foreach (var invalidDigest in new string?[]
    {
        null, "", "sha512:" + expectedHash, "sha256:" + expectedHash[..63],
        "sha256:" + new string('g', 64), digest + " ", "SHA256:" + expectedHash
    })
        await RejectsResolutionAsync(ReleaseJson(invalidDigest), "invalid digest is rejected");

    var wrongDigestRelease = await ResolveAsync(ReleaseJson("sha256:" + wrongHash));
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, null, wrongDigestRelease),
        "mismatching asset digest is rejected");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, null, digestRelease with { PackageDigest = null }),
        "verification also rejects a missing digest independently of resolution");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, null, digestRelease with { PackageDigest = "sha256:invalid" }),
        "verification also rejects malformed metadata independently of resolution");

    await File.AppendAllTextAsync(packagePath, "tampered");
    Rejects(() => SingBoxInstaller.VerifyPackage(packagePath, null, digestRelease),
        "package tampering after resolution is rejected");

    Console.WriteLine($"PASS: {passed} isolated release integrity checks (no network or executable launch).");
}

static async Task<SingBoxReleaseAsset> ResolveAsync(string json)
{
    using var client = new HttpClient(new SyntheticReleaseHandler(json));
    return await new GitHubReleaseClient(client).ResolveLatestAssetAsync();
}

sealed class SyntheticReleaseHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get
            || request.RequestUri?.AbsoluteUri != "https://api.github.com/repos/SagerNet/sing-box/releases/latest")
            throw new InvalidOperationException("Unexpected request in isolated release checks.");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}
