using System.IO.Compression;
using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace S2N.Services;

public sealed class SingBoxInstaller
{
    private readonly GitHubReleaseClient _releaseClient = new();

    public async Task<string> InstallLatestAsync(
        string installDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
            throw new ArgumentException(
                "The installation directory is required.",
                nameof(installDirectory));

        Directory.CreateDirectory(installDirectory);
        var release = await _releaseClient.ResolveLatestAssetAsync(
            cancellationToken);

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "s2n-sing-box-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var packagePath = Path.Combine(tempRoot, release.PackageName);
            var checksumPath = Path.Combine(tempRoot, release.ChecksumName);

            await _releaseClient.DownloadAsync(
                release.PackageUri,
                packagePath,
                progress,
                cancellationToken);
            await _releaseClient.DownloadAsync(
                release.ChecksumUri,
                checksumPath,
                null,
                cancellationToken);

            VerifyChecksum(packagePath, checksumPath, release.PackageName);

            var extractPath = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(extractPath);
            Extract(packagePath, extractPath);

            var executable = Directory
                .EnumerateFiles(
                    extractPath,
                    PlatformInfo.ExecutableName,
                    SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new FileNotFoundException(
                    "sing-box executable was not found in the release package.");

            foreach (var file in Directory.EnumerateFiles(
                         Path.GetDirectoryName(executable)!,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                File.Copy(
                    file,
                    Path.Combine(installDirectory, Path.GetFileName(file)),
                    true);
            }

            var installedExecutable = Path.Combine(
                installDirectory,
                PlatformInfo.ExecutableName);

            if (!PlatformInfo.IsWindows)
                File.SetUnixFileMode(
                    installedExecutable,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);

            await ValidateAsync(installedExecutable, cancellationToken);
            return installedExecutable;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public async Task ValidateAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException(
                "sing-box executable does not exist.",
                executablePath);

        var result = await ProcessRunner.RunAsync(
            executablePath,
            ["version"],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0
            || !result.StdOut.Contains(
                "sing-box version",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Downloaded sing-box failed validation: "
                + result.StdErr);
        }
    }

    private static void VerifyChecksum(
        string packagePath,
        string checksumPath,
        string packageName)
    {
        var line = File.ReadLines(checksumPath)
            .FirstOrDefault(x =>
                x.EndsWith("  " + packageName, StringComparison.Ordinal)
                || x.EndsWith(" *" + packageName, StringComparison.Ordinal));

        if (line is null)
            throw new InvalidOperationException(
                "Package checksum was not found in the official checksum file.");

        var expected = line.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        using var stream = File.OpenRead(packagePath);
        var actual = Convert.ToHexString(
            SHA256.HashData(stream)).ToLowerInvariant();

        if (!string.Equals(
                expected,
                actual,
                StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException(
                "sing-box package SHA-256 verification failed.");
    }

    private static void Extract(string packagePath, string extractPath)
    {
        if (packagePath.EndsWith(
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(
                packagePath,
                extractPath,
                overwriteFiles: true);
            return;
        }

        using var archive = ArchiveFactory.Open(packagePath);
        foreach (var entry in archive.Entries.Where(x => !x.IsDirectory))
        {
            entry.WriteToDirectory(
                extractPath,
                new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
        }
    }
}
