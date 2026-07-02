namespace CrowsNestMqtt.MockBrokerTests;

using System.IdentityModel.Tokens.Jwt;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Small helpers shared across the mock-broker test suites: free-port
/// allocation, JWT builders, and a minimal MQTT client factory that trusts the
/// broker's self-signed certificate.
/// </summary>
internal static class TestHelpers
{
    private static readonly byte[] HmacKey = Encoding.ASCII.GetBytes("test-key-that-is-long-enough-for-hmac-signing");

    public static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static string BuildJwt(
        string subject = "test-user",
        string issuer = "https://sts.local",
        string audience = "https://eventgrid.azure.net",
        DateTime? expires = null,
        DateTime? notBefore = null,
        bool omitExpiration = false)
    {
        if (omitExpiration)
        {
            // JwtSecurityTokenHandler auto-fills exp/nbf/iat via
            // SetDefaultTimesOnTokenCreation, and CreateToken(descriptor)
            // ignores that flag in some builds — so we hand-craft a compliant
            // 3-segment HMAC-signed JWT with no 'exp' claim. The mock broker
            // only calls ReadJwtToken (no signature validation), but a
            // syntactically correct signature keeps the token robust across
            // JwtSecurityTokenHandler versions.
            var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
            var payloadJson = JsonSerializer.Serialize(new { iss = issuer, aud = audience, sub = subject });
            var headerSegment = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(headerJson));
            var payloadSegment = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
            var signingInput = $"{headerSegment}.{payloadSegment}";

            using var hmac = new HMACSHA256(HmacKey);
            var signature = Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
            return $"{signingInput}.{signature}";
        }

        var handler = new JwtSecurityTokenHandler();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", subject) });
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(HmacKey),
                SecurityAlgorithms.HmacSha256Signature),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
