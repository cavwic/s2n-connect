using System.Text.Json;
using S2N.Models;
using S2N.Services;

try
{
    await RunChecksAsync();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
finally
{
    AppPaths.Cleanup();
}

static async Task RunChecksAsync()
{
    var passed = 0;
    void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        passed++;
    }

    string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
    string[] Strings(JsonElement element) => element.EnumerateArray().Select(x => x.GetString()!).ToArray();

    Directory.CreateDirectory(AppPaths.DataDirectory);
    Directory.CreateDirectory(AppPaths.LogDirectory);
    var store = new SettingsStore();
    var settings = await store.LoadAsync();
    Check(!settings.GlobalMode && !settings.AutoStart && !settings.AutoConnect, "fresh routing/startup/connection are opt-in");
    Check(!settings.HasSuccessfulConnection, "fresh settings do not pretend a prior successful connection");

    settings.ThemeMode = "  DARK ";
    settings.AutoConnect = true;
    settings.HasSuccessfulConnection = false;
    settings.ManagedConfigPath = "";
    settings.VpsProfilePath = "";
    settings.ProcessNames = null!;
    settings.DomainSuffixes = null!;
    settings.KnownHostFingerprints = null!;
    await store.SaveAsync(settings);
    settings = await store.LoadAsync();
    Check(settings.ThemeMode == "dark", "theme is normalized on save/load");
    Check(!settings.AutoConnect, "auto-connect is blocked before first successful connection");
    Check(settings.ProcessNames is not null && settings.DomainSuffixes is not null && settings.KnownHostFingerprints is not null, "null collections are repaired");
    Check(settings.ManagedConfigPath == AppPaths.ManagedConfigPath && settings.VpsProfilePath == AppPaths.VpsProfilePath, "empty paths are repaired");
    settings.HasSuccessfulConnection = true;
    settings.AutoConnect = true;
    settings.ThemeMode = "unknown";
    await store.SaveAsync(settings);
    settings = await store.LoadAsync();
    Check(settings.AutoConnect && settings.HasSuccessfulConnection, "auto-connect persists after explicit successful connection");
    Check(settings.ThemeMode == "system", "unknown theme falls back to system");
    Check(!File.Exists(AppPaths.SettingsPath + ".tmp"), "settings save finishes atomic temporary-file replacement");
    Check(await store.LoadVpsProfileAsync(Path.Combine(AppPaths.DataDirectory, "missing-profile.json")) is null, "missing VPS profile is handled");

    var profile = new VpsProfile
    {
        ServerIp = "203.0.113.10",
        ServerPort = 443,
        Uuid = "11111111-1111-4111-8111-111111111111",
        PublicKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        ShortId = "0123456789abcdef",
        ServerName = "www.example.com"
    };
    await store.SaveVpsProfileAsync(profile, AppPaths.VpsProfilePath);
    var restored = await store.LoadVpsProfileAsync(AppPaths.VpsProfilePath);
    Check(restored?.ServerIp == profile.ServerIp && restored.Uuid == profile.Uuid && restored.ServerName == profile.ServerName, "synthetic VPS profile round-trips");

    settings.ProcessNames = [" chrome.exe ", "CHROME.EXE", "", "  ", "ChatGPT.exe"];
    settings.DomainSuffixes = [" GOOGLE.COM ", "google.com", "https://CHATGPT.COM/path?q=x", ".OpenAI.com.", "", "  "];
    var configService = new SingBoxConfigService();
    foreach (var global in new[] { false, true })
    {
        settings.GlobalMode = global;
        settings.ManagedConfigPath = Path.Combine(AppPaths.DataDirectory, global ? "synthetic-global.json" : "synthetic-rules.json");
        await configService.WriteAsync(settings, profile, "192.168.1.1");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settings.ManagedConfigPath));
        var root = document.RootElement;
        var route = root.GetProperty("route");
        Check(Text(route, "final") == (global ? "vless-out" : "direct"), $"global={global}: correct default outbound");
        var rules = route.GetProperty("rules").EnumerateArray().ToArray();
        var privateIndex = Array.FindIndex(rules, r => r.TryGetProperty("ip_is_private", out var value) && value.GetBoolean());
        var processIndex = Array.FindIndex(rules, r => r.TryGetProperty("process_name", out _));
        var domainIndex = Array.FindIndex(rules, r => r.TryGetProperty("domain_suffix", out _));
        Check(privateIndex >= 0 && privateIndex < processIndex && privateIndex < domainIndex && Text(rules[privateIndex], "outbound") == "direct", $"global={global}: private destinations stay direct before process/domain proxy rules");
        var healthIndex = Array.FindIndex(rules, r => r.TryGetProperty("inbound", out var inbound) && Strings(inbound).Contains("health-in"));
        Check(healthIndex > privateIndex && Text(rules[healthIndex], "outbound") == "vless-out", $"global={global}: health inbound probes VPS while private destinations retain precedence");
        Check(Text(rules[processIndex], "outbound") == "vless-out" && Strings(rules[processIndex].GetProperty("process_name")).SequenceEqual(new[] { "chrome.exe", "ChatGPT.exe" }), $"global={global}: process rules proxy with blank removal and case-insensitive deduplication");
        Check(Text(rules[domainIndex], "outbound") == "vless-out" && Strings(rules[domainIndex].GetProperty("domain_suffix")).SequenceEqual(new[] { "google.com", "chatgpt.com", "openai.com" }), $"global={global}: domain suffix normalization, URL extraction and deduplication");
        Check(rules.Any(r => r.TryGetProperty("port", out var port) && port.GetInt32() == 53 && Text(r, "action") == "hijack-dns"), $"global={global}: client DNS is hijacked for configured resolution");

        var inbounds = root.GetProperty("inbounds").EnumerateArray().ToArray();
        var tun = inbounds.Single(x => Text(x, "type") == "tun");
        var exclusions = Strings(tun.GetProperty("route_exclude_address"));
        Check(new[] { "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.168.0.0/16", "224.0.0.0/4", "240.0.0.0/4", "203.0.113.10/32" }.All(exclusions.Contains), $"global={global}: LAN, reserved IPv4 and VPS address bypass TUN");
        Check(inbounds.Single(x => Text(x, "type") == "mixed").GetProperty("set_system_proxy").GetBoolean() == false, $"global={global}: health inbound does not modify system proxy");
        var vless = root.GetProperty("outbounds").EnumerateArray().Single(x => Text(x, "tag") == "vless-out");
        Check(Text(vless, "server") == profile.ServerIp && vless.GetProperty("server_port").GetInt32() == profile.ServerPort && Text(vless, "uuid") == profile.Uuid && Text(vless.GetProperty("tls"), "server_name") == profile.ServerName, $"global={global}: synthetic VPS settings are carried through");

        var dns = root.GetProperty("dns");
        var servers = dns.GetProperty("servers").EnumerateArray().ToArray();
        var lan = servers.Single(x => Text(x, "tag") == "lan-dns");
        var remote = servers.Single(x => Text(x, "tag") == "remote-doh");
        Check(Text(lan, "server") == "192.168.1.1" && Text(lan, "type") == "udp" && lan.GetProperty("server_port").GetInt32() == 53, $"global={global}: local DNS uses provided LAN resolver");
        Check(Text(remote, "type") == "https" && Text(remote, "server") == "1.1.1.1" && Text(remote, "detour") == "vless-out" && Text(remote.GetProperty("tls"), "server_name") == "cloudflare-dns.com", $"global={global}: public DoH is encrypted and detoured through VPS");
        Check(Text(dns, "final") == "remote-doh" && Text(route.GetProperty("default_domain_resolver"), "server") == "remote-doh", $"global={global}: public DNS and route resolver default to remote DoH");
        var dnsRules = dns.GetProperty("rules").EnumerateArray().ToArray();
        Check(dnsRules.All(r => Text(r, "server") == "lan-dns") && dnsRules.Any(r => r.TryGetProperty("domain_regex", out var regex) && Strings(regex).Contains("^[^.]+$")) && dnsRules.Any(r => r.TryGetProperty("domain_suffix", out var suffixes) && new[] { "local", "lan", "home.arpa", "in-addr.arpa", "ip6.arpa" }.All(Strings(suffixes).Contains)), $"global={global}: single-label, LAN and reverse-lookup domains retain LAN DNS");
    }

    profile.ServerIp = "2001:db8::10";
    settings.ManagedConfigPath = Path.Combine(AppPaths.DataDirectory, "synthetic-ipv6-vps.json");
    await configService.WriteAsync(settings, profile, "192.168.1.1");
    using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(settings.ManagedConfigPath)))
    {
        var tun = document.RootElement.GetProperty("inbounds").EnumerateArray().Single(x => Text(x, "type") == "tun");
        Check(Strings(tun.GetProperty("route_exclude_address")).Contains("2001:db8::10/128"), "IPv6 VPS exclusion uses a /128 host route");
    }

    profile.ServerIp = "203.0.113.10";
    foreach (var global in new[] { false, true })
    foreach (var processesPresent in new[] { false, true })
    foreach (var domainsPresent in new[] { false, true })
    {
        settings.GlobalMode = global;
        settings.ProcessNames = processesPresent ? ["chrome.exe"] : [];
        settings.DomainSuffixes = domainsPresent ? ["example.com"] : [];
        settings.ManagedConfigPath = Path.Combine(AppPaths.DataDirectory, $"synthetic-g{global}-p{processesPresent}-d{domainsPresent}.json");
        await configService.WriteAsync(settings, profile, "192.168.1.1");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settings.ManagedConfigPath));
        var route = document.RootElement.GetProperty("route");
        var rules = route.GetProperty("rules").EnumerateArray().ToArray();
        var label = $"global={global}, processes={processesPresent}, domains={domainsPresent}";
        Check(rules.Any(r => r.TryGetProperty("process_name", out _)) == processesPresent && rules.Any(r => r.TryGetProperty("domain_suffix", out _)) == domainsPresent, label + ": empty user rules are omitted");
        Check(rules.Where(r => r.TryGetProperty("outbound", out var outbound) && outbound.GetString() == "vless-out").All(r => r.EnumerateObject().Any(p => p.Name is "inbound" or "process_name" or "domain_suffix" && p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0)), label + ": every proxy rule has non-empty match conditions");
        Check(Text(route, "final") == (global ? "vless-out" : "direct"), label + ": empty rules preserve selected default outbound");
    }

    settings.ProcessNames = ["", "  "];
    settings.DomainSuffixes = ["", "  "];
    await configService.WriteAsync(settings, profile, "192.168.1.1");
    using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(settings.ManagedConfigPath)))
    {
        var rules = document.RootElement.GetProperty("route").GetProperty("rules").EnumerateArray();
        Check(!rules.Any(r => r.TryGetProperty("process_name", out _) || r.TryGetProperty("domain_suffix", out _)), "blank-only user rules are omitted");
    }

    settings.DomainSuffixes = ["bad/domain"];
    var invalidDomainRejected = false;
    try { await configService.WriteAsync(settings, profile, "192.168.1.1"); }
    catch (InvalidDataException) { invalidDomainRejected = true; }
    Check(invalidDomainRejected, "invalid domain rule is rejected");
    Console.WriteLine($"PASS: {passed} configuration and settings assertions. Temporary storage only; no network or system changes.");
}
