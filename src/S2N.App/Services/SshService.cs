using System.Security.Cryptography;
using Renci.SshNet;
using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

public sealed class SshService
{
    public async Task<SshTestResult> TestConnectionAsync(
        AppSettings settings,
        string candidateKeyPath,
        string? passphrase,
        Func<string, bool> confirmUnknownFingerprint,
        CancellationToken cancellationToken = default)
    {
        ValidatePrivateKey(candidateKeyPath);

        return await Task.Run(() =>
        {
            using var key = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(candidateKeyPath)
                : new PrivateKeyFile(candidateKeyPath, passphrase);

            var auth = new PrivateKeyAuthenticationMethod(
                settings.VpsUser,
                key);

            var info = CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(10));

            using var client = new SshClient(info);
            string? observedFingerprint = null;
            client.HostKeyReceived += (_, e) =>
            {
                observedFingerprint =
                    "SHA256:" + Convert.ToBase64String(
                        SHA256.HashData(e.HostKey));

                var hostKey = $"{settings.VpsHost}:{settings.VpsPort}";
                if (settings.KnownHostFingerprints.TryGetValue(
                        hostKey,
                        out var expected))
                {
                    e.CanTrust = string.Equals(
                        expected,
                        observedFingerprint,
                        StringComparison.Ordinal);
                    return;
                }

                e.CanTrust = confirmUnknownFingerprint(
                    observedFingerprint);
            };

            try
            {
                client.Connect();
                using var command = client.CreateCommand(
                    "printf 'S2N_OK\\n'; uname -s; uname -m");
                command.CommandTimeout = TimeSpan.FromSeconds(10);
                var output = command.Execute();
                if (command.ExitStatus != 0
                    || !output.Contains("S2N_OK"))
                    throw new InvalidOperationException(command.Error);

                client.Disconnect();
                return new SshTestResult(
                    true,
                    observedFingerprint ?? "",
                    output.Trim(),
                    "");
            }
            catch (Exception ex)
            {
                return new SshTestResult(
                    false,
                    observedFingerprint ?? "",
                    "",
                    ex.Message);
            }
        }, cancellationToken);
    }

    public async Task<string> ExecuteAsync(
        AppSettings settings,
        string keyPath,
        string? passphrase,
        string commandText,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var key = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(keyPath)
                : new PrivateKeyFile(keyPath, passphrase);

            var auth = new PrivateKeyAuthenticationMethod(
                settings.VpsUser,
                key);
            var info = CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(15));

            using var client = new SshClient(info);
            client.HostKeyReceived += (_, e) =>
            {
                var fingerprint =
                    "SHA256:" + Convert.ToBase64String(
                        SHA256.HashData(e.HostKey));
                var hostKey = $"{settings.VpsHost}:{settings.VpsPort}";
                e.CanTrust =
                    settings.KnownHostFingerprints.TryGetValue(
                        hostKey,
                        out var expected)
                    && string.Equals(
                        expected,
                        fingerprint,
                        StringComparison.Ordinal);
            };

            client.Connect();
            using var command = client.CreateCommand(commandText);
            command.CommandTimeout = TimeSpan.FromMinutes(5);
            var output = command.Execute();
            if (command.ExitStatus != 0)
                throw new InvalidOperationException(
                    L10n.T("远程命令失败：", "Remote command failed: ")
                    + L10n.TranslateTechnicalMessage(command.Error));

            client.Disconnect();
            return output;
        }, cancellationToken);
    }

    internal static ConnectionInfo CreateConnectionInfo(
        AppSettings settings,
        AuthenticationMethod authentication,
        TimeSpan timeout)
    {
        if (settings.SshProxyPort != 0 && settings.SshProxyPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(settings.SshProxyPort), L10n.T(
                "本机 SSH 代理端口须为 1024–65535；填 0 使用直连。",
                "The local SSH proxy port must be 1024–65535, or 0 for a direct connection."));
        // Explicit local HTTP CONNECT only. The SSH host key is still checked
        // against the original VPS address and its previously approved fingerprint.
        var info = settings.SshProxyPort == 0
            ? new ConnectionInfo(settings.VpsHost, settings.VpsPort, settings.VpsUser, authentication)
            : new ConnectionInfo(settings.VpsHost, settings.VpsPort, settings.VpsUser,
                ProxyTypes.Http, "127.0.0.1", settings.SshProxyPort, "", "", authentication);
        info.Timeout = timeout;
        return info;
    }

    public static void ValidatePrivateKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException(
                L10n.T("未找到 SSH 私钥文件。", "SSH private key file was not found."),
                path);

        var header = File.ReadLines(path).Take(3)
            .FirstOrDefault(x =>
                x.Contains("PRIVATE KEY", StringComparison.Ordinal)
                || x.StartsWith(
                    "PuTTY-User-Key-File-",
                    StringComparison.Ordinal));

        if (header is null)
            throw new InvalidDataException(
                L10n.T(
                "所选文件不像受支持的 SSH 私钥。",
                "The selected file does not look like a supported SSH private key."));
    }
}

public sealed record SshTestResult(
    bool Success,
    string Fingerprint,
    string ServerInfo,
    string Error);
