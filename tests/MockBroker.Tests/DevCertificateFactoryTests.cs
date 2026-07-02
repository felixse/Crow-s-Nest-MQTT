namespace CrowsNestMqtt.MockBrokerTests;

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CrowsNestMqtt.MockAzureEventGridBroker;
using Xunit;

/// <summary>
/// Unit tests for <see cref="DevCertificateFactory.CreateSelfSignedPfxBytes"/>.
/// The factory produces the self-signed cert consumed by <see cref="MockBroker"/>
/// when TLS is enabled; these tests pin its output shape so a regression there
/// surfaces immediately without needing to spin up the MQTT listener.
/// </summary>
public sealed class DevCertificateFactoryTests
{
    [Fact]
    public void CreateSelfSignedPfxBytes_ReturnsLoadablePfxWithPrivateKey()
    {
        var pfx = DevCertificateFactory.CreateSelfSignedPfxBytes("127.0.0.1");

        Assert.NotNull(pfx);
        Assert.NotEmpty(pfx);

        using var cert = X509CertificateLoader.LoadPkcs12(
            pfx,
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

        Assert.True(cert.HasPrivateKey, "PFX must contain a private key so MqttServer can complete TLS handshake.");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("mock-eg.dev.local")]
    public void CreateSelfSignedPfxBytes_SubjectCnContainsHostname(string hostname)
    {
        var pfx = DevCertificateFactory.CreateSelfSignedPfxBytes(hostname);
        using var cert = X509CertificateLoader.LoadPkcs12(pfx, password: null);

        Assert.Contains($"CN={hostname}", cert.Subject, StringComparison.Ordinal);
        Assert.Contains("O=CrowsNestMqtt Mock Event Grid", cert.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSelfSignedPfxBytes_DeclaresServerAuthEnhancedKeyUsage()
    {
        var pfx = DevCertificateFactory.CreateSelfSignedPfxBytes("localhost");
        using var cert = X509CertificateLoader.LoadPkcs12(pfx, password: null);

        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), oid => oid.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void CreateSelfSignedPfxBytes_DeclaresDigitalSignatureAndKeyEnciphermentUsage()
    {
        var pfx = DevCertificateFactory.CreateSelfSignedPfxBytes("localhost");
        using var cert = X509CertificateLoader.LoadPkcs12(pfx, password: null);

        var usage = cert.Extensions.OfType<X509KeyUsageExtension>().Single();
        Assert.True(usage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature));
        Assert.True(usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment));
    }

    [Fact]
    public void CreateSelfSignedPfxBytes_SubjectAlternativeNamesIncludeHostnameLocalhostAndLoopback()
    {
        var pfx = DevCertificateFactory.CreateSelfSignedPfxBytes("mock-eg.dev.local");
        using var cert = X509CertificateLoader.LoadPkcs12(pfx, password: null);

        var san = cert.Extensions
            .FirstOrDefault(e => string.Equals(e.Oid?.Value, "2.5.29.17", StringComparison.Ordinal));
        Assert.NotNull(san);

        var rendered = san!.Format(multiLine: true);
        Assert.Contains("mock-eg.dev.local", rendered, StringComparison.Ordinal);
        Assert.Contains("localhost", rendered, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSelfSignedPfxBytes_ValidityIsApproximatelyOneYear()
    {
        var pfx = DevCertificateFactory.CreateSelfSignedPfxBytes("localhost");
        using var cert = X509CertificateLoader.LoadPkcs12(pfx, password: null);

        var lifespan = cert.NotAfter - cert.NotBefore;
        // The factory sets NotBefore = now-5min, NotAfter = NotBefore + 1 year.
        // Tolerate a few minutes of drift in either direction.
        Assert.InRange(lifespan.TotalDays, 364, 367);
    }

    [Fact]
    public void CreateSelfSignedPfxBytes_ProducesDistinctCertsOnEachCall()
    {
        // Each invocation generates a fresh RSA key, so two adjacent calls must
        // yield certs with different thumbprints. This proves the factory isn't
        // caching a static instance behind the scenes.
        var first = DevCertificateFactory.CreateSelfSignedPfxBytes("localhost");
        var second = DevCertificateFactory.CreateSelfSignedPfxBytes("localhost");

        using var certA = X509CertificateLoader.LoadPkcs12(first, password: null);
        using var certB = X509CertificateLoader.LoadPkcs12(second, password: null);

        Assert.NotEqual(certA.Thumbprint, certB.Thumbprint);
    }
}
