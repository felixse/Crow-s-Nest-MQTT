namespace CrowsNestMqtt.MockAzureEventGridBroker;

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// Generates ephemeral, self-signed X.509 certificates for the mock broker's TLS
/// listener. The certificate exists only in memory and is regenerated on each
/// process start. Clients must be configured to accept untrusted certificates —
/// Crow's NestMQTT already does this by default.
/// </summary>
internal static class DevCertificateFactory
{
    /// <summary>
    /// Creates a self-signed certificate and returns it as a PFX byte array so
    /// callers can hand the raw bytes to APIs (like MQTTnet's
    /// <c>WithEncryptionCertificate(byte[], …)</c>) without any private-key
    /// export gymnastics on Windows.
    /// </summary>
    public static byte[] CreateSelfSignedPfxBytes(string hostname)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={hostname}, O=CrowsNestMqtt Mock Event Grid, OU=Local Development",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Allow TLS server usage.
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // serverAuth
                critical: false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(hostname);
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddYears(1);
        using var cert = request.CreateSelfSigned(notBefore, notAfter);

        return cert.Export(X509ContentType.Pfx);
    }
}
