using System.Security.Cryptography;
using Renci.SshNet;
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

            var info = new ConnectionInfo(
                settings.VpsHost,
                settings.VpsPort,
                settings.VpsUser,
                auth)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

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
            var info = new ConnectionInfo(
                settings.VpsHost,
                settings.VpsPort,
                settings.VpsUser,
                auth)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

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
                    $"Remote command failed: {command.Error}");

            client.Disconnect();
            return output;
        }, cancellationToken);
    }

    public static void ValidatePrivateKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException(
                "SSH private key file was not found.",
                path);

        var header = File.ReadLines(path).Take(3)
            .FirstOrDefault(x =>
                x.Contains("PRIVATE KEY", StringComparison.Ordinal)
                || x.StartsWith(
                    "PuTTY-User-Key-File-",
                    StringComparison.Ordinal));

        if (header is null)
            throw new InvalidDataException(
                "The selected file does not look like a supported SSH private key.");
    }
}

public sealed record SshTestResult(
    bool Success,
    string Fingerprint,
    string ServerInfo,
    string Error);
