using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using S2N.Models;
using S2N.Services;

if (args.Length == 3 && args[0] == "certificate")
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var request = new CertificateRequest("CN=s2n.internal", key, HashAlgorithmName.SHA256);
    var names = new SubjectAlternativeNameBuilder();
    names.AddDnsName("s2n.internal");
    request.CertificateExtensions.Add(names.Build());
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
    File.WriteAllText(args[1], certificate.ExportCertificatePem());
    File.WriteAllText(args[2], key.ExportPkcs8PrivateKeyPem());
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(args[2], UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
else if (args.Length == 3 && args[0] == "outbound" || args.Length == 4 && args[0] == "client")
{
    await using var input = args[1] == "-" ? Console.OpenStandardInput() : File.OpenRead(args[1]);
    var profile = await new ProfileImportService().ReadAsync(input);
    // This is the production serializer, including certificate validation and TLS settings.
    var outbound = SingBoxConfigService.BuildOutbound(profile);
    object output = outbound;
    if (args[0] == "client")
    {
        if (!int.TryParse(args[3], out var port) || port is < 1024 or > 65535)
            throw new ArgumentException("The fixture listener must use a nonprivileged local port.");
        output = new
        {
            log = new { level = "error" },
            inbounds = new[] { new { type = "mixed", tag = "fixture-in", listen = "127.0.0.1", listen_port = port } },
            outbounds = new[] { outbound }, route = new { final = "vless-out" },
            dns = new { servers = new[] { new { type = "local", tag = "lan-dns" } } }
        };
    }
    var json = JsonSerializer.Serialize(output);
    if (args[2] == "-") Console.Out.Write(json);
    else File.WriteAllText(args[2], json);
}
else
{
    throw new ArgumentException("Expected certificate <cert> <key>, outbound <profile|-> <output|->, or client <profile|-> <output|-> <port>.");
}
