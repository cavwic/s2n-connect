using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
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
        ValidatePorts(settings);
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
                    DescribeConnectionError(ex, settings, candidateKeyPath, passphrase));
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
        ValidatePorts(settings);
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

            try
            {
                client.Connect();
            }
            catch (Exception error)
            {
                // Only connection setup is reformatted. Remote command errors must
                // retain their deployment markers for VpsDeploymentService.
                throw new SshConnectionException(
                    DescribeConnectionError(error, settings, keyPath, passphrase));
            }
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
        ValidatePorts(settings);
        // Explicit local HTTP CONNECT only. The SSH host key is still checked
        // against the original VPS address and its previously approved fingerprint.
        var info = settings.SshProxyPort == 0
            ? new ConnectionInfo(settings.VpsHost, settings.VpsPort, settings.VpsUser, authentication)
            : new ConnectionInfo(settings.VpsHost, settings.VpsPort, settings.VpsUser,
                ProxyTypes.Http, "127.0.0.1", settings.SshProxyPort, "", "", authentication);
        info.Timeout = timeout;
        return info;
    }

    private static void ValidatePorts(AppSettings settings)
    {
        if (settings.VpsPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(settings.VpsPort), L10n.T(
                "服务器 SSH 端口须为 1–65535。请填写服务器实际监听的端口。",
                "The server SSH port must be 1–65535. Enter the port the server actually listens on."));
        if (settings.SshProxyPort != 0 && settings.SshProxyPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(settings.SshProxyPort), L10n.T(
                "本机 SSH 代理端口须为 1024–65535；填 0 使用直连。",
                "The local SSH proxy port must be 1024–65535, or 0 for a direct connection."));
    }

    internal static string DescribeConnectionError(
        Exception error,
        AppSettings settings,
        string? privateKeyPath = null,
        string? passphrase = null)
    {
        var causes = new List<Exception>();
        for (Exception? cause = error; cause is not null && causes.Count < 8; cause = cause.InnerException)
            causes.Add(cause);

        var host = settings.VpsHost.Contains(':') && !settings.VpsHost.StartsWith('[')
            ? $"[{settings.VpsHost}]" : settings.VpsHost;
        var route = settings.SshProxyPort == 0
            ? L10n.T("直连", "direct connection")
            : L10n.T($"经本机 HTTP 代理 127.0.0.1:{settings.SshProxyPort}",
                $"via local HTTP proxy 127.0.0.1:{settings.SshProxyPort}");

        string advice;
        // Host trust and authentication take precedence over nested transport errors.
        // An SSH handshake failure alone does not prove that a port is blocked.
        if (causes.OfType<SshConnectionException>().Any(
                cause => cause.DisconnectReason == DisconnectReason.HostKeyNotVerifiable))
            advice = L10n.T(
                "SSH 主机指纹验证未通过。请通过供应商控制台或可信渠道核对指纹，勿跳过主机验证。",
                "SSH host key verification failed. Verify the fingerprint through the provider console or another trusted channel; keep host verification enabled.");
        else if (causes.Any(cause => cause is SshAuthenticationException))
            advice = L10n.T(
                "SSH 身份验证失败。请核对 SSH 用户、私钥、私钥口令及服务器授权；更换连接端口不能修复身份验证。",
                "SSH authentication failed. Check the SSH user, private key, key passphrase and server authorization. Changing the port does not fix authentication.");
        else if (causes.Any(cause => cause is ProxyException))
            advice = L10n.T(
                "HTTP 代理连接失败。请确认本机代理正在运行，并允许 CONNECT 到目标 SSH 端口；此错误不能单独证明服务器端口被阻断。",
                "The HTTP proxy connection failed. Check that the local proxy is running and allows CONNECT to the target SSH port. This alone does not establish that the server port is blocked.");
        else if (causes.Any(cause => cause is TimeoutException or SshOperationTimeoutException
                     || cause is SocketException { SocketErrorCode: SocketError.TimedOut }))
            advice = L10n.T("SSH 连接或响应超时。", "The SSH connection or response timed out.")
                + " " + DescribeNetworkChecks(settings);
        else if (causes.Any(cause => cause is SocketException { SocketErrorCode: SocketError.ConnectionRefused }))
            advice = L10n.T("SSH 连接被拒绝。", "The SSH connection was refused.")
                + " " + DescribeNetworkChecks(settings);
        else if (causes.Any(cause => cause is SocketException))
            advice = L10n.T("SSH 网络连接失败。", "The SSH network connection failed.")
                + " " + DescribeNetworkChecks(settings);
        else
            advice = L10n.T(
                "SSH 连接未完成。请根据下列技术原因检查 SSH 配置，必要时通过供应商控制台核对服务状态。",
                "The SSH connection did not complete. Check the technical reason below and the SSH configuration; use the provider console to check the service if needed.");

        var reason = string.Join(" -> ", causes.Select(cause => cause is SocketException socket
            ? $"{cause.GetType().Name} ({socket.SocketErrorCode}): {cause.Message}"
            : $"{cause.GetType().Name}: {cause.Message}"));
        // Do not include stack traces or exception Data; a parser can include its
        // input in Message. Redact known secrets and PEM material before truncating.
        reason = Regex.Replace(reason,
            @"-----BEGIN [^\r\n]*PRIVATE KEY-----.*?(?:-----END [^\r\n]*PRIVATE KEY-----|$)",
            L10n.T("[私钥已隐藏]", "[private key redacted]"), RegexOptions.Singleline);
        foreach (var secret in new[] { privateKeyPath, settings.SshKeyPath, passphrase })
            if (!string.IsNullOrEmpty(secret))
                reason = reason.Replace(secret, L10n.T("[已隐藏]", "[redacted]"), StringComparison.Ordinal);
        if (reason.Length > 1200) reason = reason[..1200] + "…";
        return L10n.T($"服务器 SSH {host}:{settings.VpsPort}（{route}）。",
                $"Server SSH {host}:{settings.VpsPort} ({route}).")
            + " " + advice + Environment.NewLine
            + L10n.T("技术原因：", "Technical reason: ") + reason;
    }

    private static string DescribeNetworkChecks(AppSettings settings) => settings.SshProxyPort == 0
        ? L10n.T(
            "请核对网络、服务器 SSH 实际监听端口和防火墙。仅在服务器已配置其他 SSH 端口时修改此端口；UDP 443 是 VPN 端口，不能替代 SSH。",
            "Check the network, the server's actual SSH listening port and its firewalls. Change this port only if another SSH port is already configured on the server; the VPN's UDP 443 port cannot replace SSH.")
        : L10n.T(
            "请先检查本机 HTTP 代理及其 CONNECT 规则，再核对服务器 SSH 服务和端口。经代理的连接失败不能单独证明远端端口被阻断。",
            "Check the local HTTP proxy and its CONNECT rules, then the server's SSH service and port. A failed proxied connection alone does not establish that the remote port is blocked.");

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
