using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using CrowsNestMqtt.BusinessLogic.Configuration;
using CrowsNestMqtt.BusinessLogic.Services;
using Xunit;

namespace CrowsNestMqtt.UnitTests.Services;

public class AzureAccessTokenProviderTests
{
    /// <summary>Test stub credential that returns a canned token.</summary>
    private sealed class StubTokenCredential : TokenCredential
    {
        public string Token { get; }
        public DateTimeOffset ExpiresOn { get; }
        public int CallCount { get; private set; }

        public StubTokenCredential(string token, DateTimeOffset expiresOn)
        {
            Token = token;
            ExpiresOn = expiresOn;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            CallCount++;
            return new AccessToken(Token, ExpiresOn);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            CallCount++;
            return new ValueTask<AccessToken>(new AccessToken(Token, ExpiresOn));
        }
    }

    [Fact]
    public void Ctor_NullScope_Throws()
    {
        var credential = new StubTokenCredential("token", DateTimeOffset.UtcNow.AddHours(1));
        Assert.Throws<ArgumentException>(() => new AzureAccessTokenProvider(null!, credential));
    }

    [Fact]
    public void Ctor_EmptyScope_Throws()
    {
        var credential = new StubTokenCredential("token", DateTimeOffset.UtcNow.AddHours(1));
        Assert.Throws<ArgumentException>(() => new AzureAccessTokenProvider("   ", credential));
    }

    [Fact]
    public async Task GetTokenAsync_DelegatesToCredential()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var credential = new StubTokenCredential("my-token", expiry);
        var provider = new AzureAccessTokenProvider("https://eventgrid.azure.net/.default", credential);

        var result = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("my-token", result.Token);
        Assert.Equal(expiry, result.ExpiresOn);
        Assert.Equal(1, credential.CallCount);
    }

    [Fact]
    public void AzureAuthenticationMode_DefaultScopeUsedWhenNullOrWhitespace()
    {
        var mode = new AzureAuthenticationMode();
        Assert.Equal(AzureAuthenticationMode.DefaultScope, mode.EffectiveScope);

        var blank = new AzureAuthenticationMode("   ");
        Assert.Equal(AzureAuthenticationMode.DefaultScope, blank.EffectiveScope);
    }

    [Fact]
    public void AzureAuthenticationMode_CustomScopeUsedWhenProvided()
    {
        var custom = new AzureAuthenticationMode("api://my-app/.default");
        Assert.Equal("api://my-app/.default", custom.EffectiveScope);
    }

    [Fact]
    public void AzureAuthenticationMode_AuthMethodConstant()
    {
        // Azure Event Grid requires exactly this method name on the CONNECT packet.
        Assert.Equal("OAUTH2-JWT", AzureAuthenticationMode.AuthenticationMethod);
    }

    // --- CROWSNEST__AZURE_TOKEN_OVERRIDE seam tests ---

    private static string BuildJwt(DateTime expiresUtc)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var key = System.Text.Encoding.ASCII.GetBytes("test-key-that-is-long-enough-for-hmac-signing");
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", "unit-test-user"),
            }),
            Expires = expiresUtc,
            Issuer = "https://sts.local",
            Audience = "https://eventgrid.azure.net",
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    [Fact]
    public async Task GetTokenAsync_WithEnvOverride_ReturnsOverrideAndBypassesCredential()
    {
        var expiry = DateTime.UtcNow.AddMinutes(30);
        var overrideJwt = BuildJwt(expiry);
        var credential = new StubTokenCredential("should-not-be-used", DateTimeOffset.UtcNow.AddDays(1));
        var provider = new AzureAccessTokenProvider("https://eventgrid.azure.net/.default", credential);

        Environment.SetEnvironmentVariable(AzureAccessTokenProvider.TokenOverrideEnvVar, overrideJwt);
        try
        {
            var result = await provider.GetTokenAsync(CancellationToken.None);

            Assert.Equal(overrideJwt, result.Token);
            // Expiry comes from the JWT exp claim (truncated to second precision by the JWT handler).
            Assert.Equal(expiry, result.ExpiresOn.UtcDateTime, TimeSpan.FromSeconds(1));
            Assert.Equal(0, credential.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AzureAccessTokenProvider.TokenOverrideEnvVar, null);
        }
    }

    [Fact]
    public async Task GetTokenAsync_WithEnvOverride_MalformedJwt_UsesFallbackExpiry()
    {
        var credential = new StubTokenCredential("should-not-be-used", DateTimeOffset.UtcNow.AddDays(1));
        var provider = new AzureAccessTokenProvider("https://eventgrid.azure.net/.default", credential);

        Environment.SetEnvironmentVariable(AzureAccessTokenProvider.TokenOverrideEnvVar, "not-a-real-jwt");
        try
        {
            var before = DateTimeOffset.UtcNow;
            var result = await provider.GetTokenAsync(CancellationToken.None);

            Assert.Equal("not-a-real-jwt", result.Token);
            // Fallback: UtcNow + 1h. Allow a generous window around 1h.
            var expectedMin = before.AddMinutes(55);
            var expectedMax = before.AddMinutes(65);
            Assert.InRange(result.ExpiresOn, expectedMin, expectedMax);
            Assert.Equal(0, credential.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AzureAccessTokenProvider.TokenOverrideEnvVar, null);
        }
    }

    [Fact]
    public async Task GetTokenAsync_WithoutEnvOverride_UsesCredentialAsBefore()
    {
        // Make absolutely sure the env var is not lingering from another test.
        Environment.SetEnvironmentVariable(AzureAccessTokenProvider.TokenOverrideEnvVar, null);

        var expiry = DateTimeOffset.UtcNow.AddHours(2);
        var credential = new StubTokenCredential("from-credential", expiry);
        var provider = new AzureAccessTokenProvider("https://eventgrid.azure.net/.default", credential);

        var result = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("from-credential", result.Token);
        Assert.Equal(expiry, result.ExpiresOn);
        Assert.Equal(1, credential.CallCount);
    }

    [Fact]
    public void ReadJwtExpiryOrFallback_ValidJwt_ReturnsExpClaim()
    {
        var expiry = DateTime.UtcNow.AddMinutes(15);
        var jwt = BuildJwt(expiry);

        var result = AzureAccessTokenProvider.ReadJwtExpiryOrFallback(jwt);

        Assert.Equal(expiry, result.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ReadJwtExpiryOrFallback_MalformedJwt_ReturnsOneHourFallback()
    {
        var before = DateTimeOffset.UtcNow;

        var result = AzureAccessTokenProvider.ReadJwtExpiryOrFallback("junk-not-a-jwt");

        Assert.InRange(result, before.AddMinutes(55), before.AddMinutes(65));
    }
}
