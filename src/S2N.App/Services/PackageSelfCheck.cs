using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Renci.SshNet;
using S2N.Models;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace S2N.Services;

/// <summary>Offline checks executed by the published application and its shipped dependencies.</summary>
internal static class PackageSelfCheck
{
    internal static int Run() => RunIsolated("package", checks =>
        RunChecksAsync(checks).GetAwaiter().GetResult());

    internal static int RunIsolated(string suite, Action<CheckContext> action)
    {
        var checks = new CheckContext();
        var previousDirectory = Environment.GetEnvironmentVariable("S2N_DATA_DIRECTORY");
        var temporaryParent = Path.GetFullPath(Path.GetTempPath());
        var directory = Path.Combine(temporaryParent, "S2N.PackageCheck-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var created = false;
        try
        {
            // This generated directory is the only place the checks may write.
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("Package-check directory already exists.");
            Directory.CreateDirectory(directory);
            created = true;
            Environment.SetEnvironmentVariable("S2N_DATA_DIRECTORY", directory);
            checks.Directory = directory;
            action(checks);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { Environment.SetEnvironmentVariable("S2N_DATA_DIRECTORY", previousDirectory); }
            catch (Exception error) { failure ??= error; }
            try
            {
                // Recheck the final absolute target before deleting our own fixtures.
                var fullDirectory = Path.GetFullPath(directory);
                if (created && string.Equals(Path.GetDirectoryName(fullDirectory),
                        Path.TrimEndingDirectorySeparator(temporaryParent),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    && Path.GetFileName(fullDirectory).StartsWith("S2N.PackageCheck-", StringComparison.Ordinal))
                    System.IO.Directory.Delete(fullDirectory, recursive: true);
                else if (created)
                    throw new IOException("Package-check cleanup target is invalid.");
            }
            catch (Exception error) { failure ??= error; }
        }

        // The reporter deliberately does not use reflected JSON serialization.
        // A broken JSON metadata configuration must still produce a safe failure report.
        using var report = new MemoryStream();
        using (var writer = new Utf8JsonWriter(report))
        {
            writer.WriteStartObject();
            writer.WriteString("result", failure is null ? "PASS" : "FAIL");
            writer.WriteString("suite", suite);
            writer.WriteNumber("checks", checks.Count);
            writer.WriteStartArray("completed_groups");
            foreach (var group in checks.CompletedGroups) writer.WriteStringValue(group);
            writer.WriteEndArray();
            if (failure is not null)
            {
                writer.WriteString("failed_group", checks.Group);
                writer.WriteString("error_type", failure.GetType().Name);
                // Never print exception messages, paths, key material, or profile JSON.
            }
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(report.ToArray()));
        return failure is null ? 0 : 1;
    }

    private static async Task RunChecksAsync(CheckContext checks)
    {
        checks.Begin("settings");
        checks.Expect(Path.GetFullPath(AppPaths.DataDirectory) == checks.Directory);
        var settings = new AppSettings
        {
            VpsHost = "203.0.113.23", VpsPort = 2222, VpsUser = "package-check-user",
            SshKeyPath = Path.Combine(checks.Directory, "synthetic-key.pem"),
            SingBoxInstallDirectory = Path.Combine(checks.Directory, "synthetic-core"),
            SingBoxExecutablePath = Path.Combine(checks.Directory, "synthetic-core", "not-an-executable"),
            ManagedConfigPath = Path.Combine(checks.Directory, "config.json"),
            VpsProfilePath = Path.Combine(checks.Directory, "vps-profile.json"),
            PreferredTransport = "vless", LocalProxyPort = 23456, SshProxyPort = 2080,
            ThemeMode = "dark", GlobalMode = true, AutoStart = true,
            AutoConnect = true, HasSuccessfulConnection = true,
            ProcessNames = ["package-check.exe", "synthetic-browser.exe"],
            DomainSuffixes = ["example.com", "example.org"],
            KnownHostFingerprints = new() { ["203.0.113.23:2222"] = "synthetic-fingerprint" }
        };
        var store = new SettingsStore();
        await store.SaveAsync(settings);
        var restored = await store.LoadAsync();
        checks.Expect(restored.VpsHost == settings.VpsHost && restored.VpsPort == 2222 && restored.VpsUser == settings.VpsUser);
        checks.Expect(restored.SshKeyPath == settings.SshKeyPath);
        checks.Expect(restored.SingBoxInstallDirectory == settings.SingBoxInstallDirectory
            && restored.SingBoxExecutablePath == settings.SingBoxExecutablePath);
        checks.Expect(restored.ManagedConfigPath == settings.ManagedConfigPath && restored.VpsProfilePath == settings.VpsProfilePath);
        checks.Expect(restored.PreferredTransport == "vless" && restored.LocalProxyPort == 23456 && restored.SshProxyPort == 2080);
        checks.Expect(restored.ThemeMode == "dark" && restored.GlobalMode && restored.AutoStart
            && restored.AutoConnect && restored.HasSuccessfulConnection);
        checks.Expect(restored.ProcessNames.SequenceEqual(settings.ProcessNames));
        checks.Expect(restored.DomainSuffixes.SequenceEqual(settings.DomainSuffixes));
        checks.Expect(restored.KnownHostFingerprints.Count == 1
            && restored.KnownHostFingerprints["203.0.113.23:2222"] == "synthetic-fingerprint");
        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(AppPaths.SettingsPath)))
            checks.Expect(document.RootElement.GetProperty("VpsPort").GetInt32() == 2222
                && document.RootElement.GetProperty("KnownHostFingerprints").EnumerateObject().Count() == 1);
        checks.Complete();

        using var certificateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=s2n.internal", certificateKey, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("s2n.internal");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var fixtures = new[]
        {
            new VpsProfile
            {
                Transport = "vless", ServerIp = "203.0.113.23", ServerPort = 443,
                Uuid = "00000000-0000-4000-8000-000000000001", PublicKey = new string('A', 43),
                ShortId = "12ab", ServerName = "package-check.example.com"
            },
            new VpsProfile
            {
                Transport = "hysteria2", ServerIp = "203.0.113.23", ServerPort = 443,
                Password = "synthetic-package-check-password", ServerName = "s2n.internal",
                CertificatePem = certificate.ExportCertificatePem()
            }
        };
        foreach (var fixture in fixtures)
            await CheckProfileAsync(checks, store, settings, VpsProfileValidator.Normalize(fixture));

        checks.Begin("ownership-json");
        var record = new OwnedCoreProcess(12345, 123456789L,
            settings.SingBoxExecutablePath, settings.ManagedConfigPath, true,
            Path.Combine(checks.Directory, "synthetic-launcher"), "s2n-package-check", "synthetic-guid",
            "synthetic-pnp-id", true, true);
        var owned = JsonSerializer.Deserialize<OwnedCoreProcess>(JsonSerializer.Serialize(record));
        checks.Expect(owned == record);
        var identity = new WindowsAdapterIdentity("synthetic-guid", "synthetic-pnp-id");
        var adapter = JsonSerializer.Deserialize<WindowsAdapterIdentity>(JsonSerializer.Serialize(identity));
        checks.Expect(adapter == identity);
        checks.Complete();

        checks.Begin("ssh-key-and-construction");
        using (var rsa = RSA.Create(2048))
            CheckSshKey(checks, settings, "rsa", rsa.ExportPkcs8PrivateKeyPem());
        using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            CheckSshKey(checks, settings, "ecdsa", ecdsa.ExportPkcs8PrivateKeyPem());
        checks.Complete();

        checks.Begin("archives");
        CheckArchives(checks);
        checks.Complete();

        checks.Begin("deployment-resources");
        checks.Expect(VpsDeploymentService.CreateDeploymentScript("hysteria2", "203.0.113.23")
            .Contains("S2N_HYSTERIA_PY", StringComparison.Ordinal));
        checks.Expect(VpsDeploymentService.CreateDeploymentScript("vless", "203.0.113.23")
            .Contains("S2N_PROFILE_JSON=", StringComparison.Ordinal));
        checks.Complete();
    }

    private static async Task CheckProfileAsync(CheckContext checks, SettingsStore store, AppSettings settings, VpsProfile profile)
    {
        checks.Begin(profile.Transport + "-profile-config-exports");
        await store.SaveVpsProfileAsync(profile, settings.VpsProfilePath);
        CheckProfileValues(checks, (await store.LoadVpsProfileAsync(settings.VpsProfilePath))!, profile, checkDate: true);
        var deployed = VpsDeploymentService.ParseDeploymentProfile(JsonSerializer.Serialize(profile), profile.Transport, profile.ServerIp);
        CheckProfileValues(checks, deployed, profile, checkDate: true);
        var importer = new ProfileImportService();
        var android = new MobileProfileExportService().Serialize(profile);
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(android)))
            CheckProfileValues(checks, await importer.ReadAsync(stream), profile);
        using (var document = JsonDocument.Parse(android))
        {
            var root = document.RootElement;
            checks.Expect(root.GetProperty("serverIp").GetString() == profile.ServerIp
                && root.GetProperty("serverPort").GetInt32() == 443 && !root.TryGetProperty("outbounds", out _));
            if (profile.Transport == "hysteria2")
                checks.Expect(root.GetProperty("schemaVersion").GetInt32() == 2
                    && root.GetProperty("certificatePem").GetString() == profile.CertificatePem);
            CheckNoLocalSecrets(checks, root);
        }
        var ios = new ThirdPartyProfileExportService().Serialize(profile);
        using (var document = JsonDocument.Parse(ios))
        {
            var root = document.RootElement;
            CheckOutbound(checks, root, profile);
            checks.Expect(root.GetProperty("inbounds")[0].GetProperty("type").GetString() == "tun"
                && root.GetProperty("route").GetProperty("final").GetString() == "proxy");
            CheckNoLocalSecrets(checks, root);
        }
        CheckProfileValues(checks, importer.Parse(Encoding.UTF8.GetBytes(ios)), profile);
        foreach (var global in new[] { false, true })
        {
            settings.GlobalMode = global;
            settings.ProcessNames = global ? [] : ["package-check.exe"];
            settings.DomainSuffixes = global ? [] : ["example.com"];
            await new SingBoxConfigService().WriteAsync(settings, profile, "192.0.2.53", interfaceName: "s2n-package-check");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settings.ManagedConfigPath));
            var root = document.RootElement;
            CheckOutbound(checks, root, profile);
            checks.Expect(root.GetProperty("route").GetProperty("final").GetString() == (global ? "vless-out" : "direct"));
            var rules = root.GetProperty("route").GetProperty("rules").EnumerateArray().ToArray();
            checks.Expect(rules.Any(rule => rule.TryGetProperty("process_name", out _)) == !global);
            checks.Expect(rules.Any(rule => rule.TryGetProperty("domain_suffix", out _)) == !global);
            var tun = root.GetProperty("inbounds").EnumerateArray().Single(value => value.GetProperty("type").GetString() == "tun");
            checks.Expect(tun.GetProperty("interface_name").GetString() == "s2n-package-check"
                && tun.GetProperty("route_exclude_address").EnumerateArray().Any(value => value.GetString() == "203.0.113.23/32"));
            var health = root.GetProperty("inbounds")[0];
            checks.Expect(health.GetProperty("listen").GetString() == "127.0.0.1"
                && health.GetProperty("listen_port").GetInt32() == settings.LocalProxyPort);
        }
        checks.Complete();
    }

    private static void CheckProfileValues(CheckContext checks, VpsProfile actual, VpsProfile expected, bool checkDate = false)
    {
        checks.Expect(actual is not null);
        checks.Expect(actual.Transport == expected.Transport && actual.ServerIp == expected.ServerIp
            && actual.ServerPort == expected.ServerPort && actual.ServerName == expected.ServerName);
        checks.Expect(actual.Uuid == expected.Uuid && actual.PublicKey == expected.PublicKey && actual.ShortId == expected.ShortId);
        checks.Expect(actual.Password == expected.Password && actual.CertificatePem == expected.CertificatePem);
        if (checkDate) checks.Expect(actual.UpdatedAt == expected.UpdatedAt);
    }

    private static void CheckOutbound(CheckContext checks, JsonElement root, VpsProfile profile)
    {
        var outbound = root.GetProperty("outbounds").EnumerateArray()
            .Single(value => value.GetProperty("type").GetString() == profile.Transport);
        checks.Expect(outbound.GetProperty("server").GetString() == profile.ServerIp
            && outbound.GetProperty("server_port").GetInt32() == profile.ServerPort);
        var tls = outbound.GetProperty("tls");
        checks.Expect(tls.GetProperty("enabled").GetBoolean()
            && tls.GetProperty("server_name").GetString() == profile.ServerName
            && (!tls.TryGetProperty("insecure", out var insecure) || !insecure.GetBoolean()));
        if (profile.Transport == "hysteria2")
        {
            checks.Expect(outbound.GetProperty("password").GetString() == profile.Password);
            checks.Expect(string.Join("\n", tls.GetProperty("certificate").EnumerateArray().Select(value => value.GetString()))
                == profile.CertificatePem.Trim());
        }
        else
            checks.Expect(outbound.GetProperty("uuid").GetString() == profile.Uuid
                && tls.GetProperty("reality").GetProperty("public_key").GetString() == profile.PublicKey
                && tls.GetProperty("reality").GetProperty("short_id").GetString() == profile.ShortId);
    }

    private static void CheckNoLocalSecrets(CheckContext checks, JsonElement root)
    {
        bool Safe(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject().All(property =>
                property.Name.ToLowerInvariant() is not ("sshkeypath" or "vpsuser" or "vpsport" or "knownhostfingerprints" or "privatekey")
                && Safe(property.Value)),
            JsonValueKind.Array => value.EnumerateArray().All(Safe),
            JsonValueKind.String => !(value.GetString() ?? "").Contains("PRIVATE KEY", StringComparison.Ordinal),
            _ => true
        };
        checks.Expect(Safe(root));
    }

    private static void CheckSshKey(CheckContext checks, AppSettings settings, string kind, string pem)
    {
        var path = Path.Combine(checks.Directory, "synthetic-" + kind + ".pem");
        File.WriteAllText(path, pem);
        SshService.ValidatePrivateKey(path);
        using var key = new PrivateKeyFile(path);
        using var authentication = new PrivateKeyAuthenticationMethod("package-check-user", key);
        foreach (var proxyPort in new[] { 0, 2080 })
        {
            settings.VpsPort = 2222;
            settings.SshProxyPort = proxyPort;
            var info = SshService.CreateConnectionInfo(settings, authentication, TimeSpan.FromSeconds(1));
            checks.Expect(info.Host == "203.0.113.23" && info.Port == 2222 && info.Username == "package-check-user");
            checks.Expect(info.AuthenticationMethods.Count == 1 && info.AuthenticationMethods[0] == authentication);
            checks.Expect(proxyPort == 0 ? info.ProxyType == ProxyTypes.None
                : info.ProxyType == ProxyTypes.Http && info.ProxyHost == "127.0.0.1" && info.ProxyPort == 2080);
            using var client = new SshClient(info);
            checks.Expect(!client.IsConnected); // Intentionally never call Connect.
        }
    }

    private static void CheckArchives(CheckContext checks)
    {
        const string content = "S2N offline packaged dependency fixture\n";
        const string entryName = "fixtures/check.txt";
        var zip = Path.Combine(checks.Directory, "fixture.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var output = new StreamWriter(archive.CreateEntry(entryName).Open(), new UTF8Encoding(false)))
            output.Write(content);
        var zipOutput = Path.Combine(checks.Directory, "zip-output");
        ZipFile.ExtractToDirectory(zip, zipOutput, overwriteFiles: true);
        checks.Expect(File.ReadAllText(Path.Combine(zipOutput, entryName)) == content);

        var tarGz = Path.Combine(checks.Directory, "fixture.tar.gz");
        var data = Encoding.UTF8.GetBytes(content);
        var header = new byte[512];
        Encoding.ASCII.GetBytes(entryName).CopyTo(header, 0);
        WriteOctal(header, 100, 8, 420);
        WriteOctal(header, 108, 8, 0);
        WriteOctal(header, 116, 8, 0);
        WriteOctal(header, 124, 12, data.Length);
        WriteOctal(header, 136, 12, 0);
        Array.Fill(header, (byte)' ', 148, 8);
        header[156] = (byte)'0';
        Encoding.ASCII.GetBytes("ustar\0" + "00").CopyTo(header, 257);
        var checksum = header.Sum(value => (int)value);
        Encoding.ASCII.GetBytes(Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);
        using (var file = File.Create(tarGz))
        using (var compressed = new GZipStream(file, CompressionLevel.SmallestSize))
        {
            compressed.Write(header);
            compressed.Write(data);
            compressed.Write(new byte[(512 - data.Length % 512) % 512 + 1024]);
        }
        var tarOutput = Path.Combine(checks.Directory, "tar-output");
        Directory.CreateDirectory(tarOutput);
        // The same reader/extraction API and options used by SingBoxInstaller.
        using (var reader = ReaderFactory.OpenReader(tarGz))
            reader.WriteAllToDirectory(tarOutput, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        checks.Expect(File.ReadAllText(Path.Combine(tarOutput, entryName)) == content);
    }

    private static void WriteOctal(byte[] header, int offset, int width, int value) =>
        Encoding.ASCII.GetBytes(Convert.ToString(value, 8).PadLeft(width - 1, '0') + "\0").CopyTo(header, offset);

    internal sealed class CheckContext
    {
        internal string Directory { get; set; } = "";
        internal string Group { get; private set; } = "isolation";
        internal int Count { get; private set; }
        internal List<string> CompletedGroups { get; } = [];
        internal void Begin(string group) => Group = group;
        internal void Complete() => CompletedGroups.Add(Group);
        internal void Expect([DoesNotReturnIf(false)] bool condition)
        {
            if (!condition) throw new InvalidOperationException("Packaged check failed.");
            Count++;
        }
    }
}
