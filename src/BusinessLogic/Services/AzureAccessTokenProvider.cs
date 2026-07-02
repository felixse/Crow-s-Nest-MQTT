namespace CrowsNestMqtt.BusinessLogic.Services;

using System.IdentityModel.Tokens.Jwt;
using Azure.Core;
using Azure.Identity;
using CrowsNestMqtt.Utils;

/// <summary>
/// <see cref="IAccessTokenProvider"/> backed by
/// <see cref="DefaultAzureCredential"/>. Probes environment variables, managed identity,
/// Visual Studio, Azure CLI, Azure PowerShell, and interactive browser in order.
/// Tokens are returned directly from the underlying credential, which performs its own
/// caching according to the Azure SDK conventions.
///
/// <para>
/// <b>Local-testing override:</b> when the <c>CROWSNEST__AZURE_TOKEN_OVERRIDE</c>
/// environment variable is set, the value is returned as-is and
/// <see cref="DefaultAzureCredential"/> is bypassed entirely. This is intended for
/// running the Aspire host with the bundled mock Azure Event Grid broker (which does
/// not verify JWT signatures) and MUST NOT be set in production. Token expiry is
/// derived from the JWT <c>exp</c> claim when the override value parses as a JWT;
/// otherwise a 1-hour fallback lifetime is used so the refresh timer still fires.
/// </para>
/// </summary>
public sealed class AzureAccessTokenProvider : IAccessTokenProvider
{
    /// <summary>
    /// Environment variable that short-circuits credential acquisition and returns
    /// a pre-issued token. Intended for local Aspire testing only.
    /// </summary>
    public const string TokenOverrideEnvVar = "CROWSNEST__AZURE_TOKEN_OVERRIDE";

    private readonly TokenCredential _credential;
    private readonly string _scope;

    /// <summary>
    /// Creates a provider that requests tokens for the given OAuth scope.
    /// </summary>
    /// <param name="scope">The OAuth scope, e.g. <c>https://eventgrid.azure.net/.default</c>.</param>
    public AzureAccessTokenProvider(string scope)
        : this(scope, new DefaultAzureCredential(includeInteractiveCredentials: true))
    {
    }

    /// <summary>
    /// Test-friendly constructor that accepts an arbitrary <see cref="TokenCredential"/>.
    /// </summary>
    public AzureAccessTokenProvider(string scope, TokenCredential credential)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scope must not be null or whitespace.", nameof(scope));
        }

        _scope = scope;
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    }

    /// <inheritdoc />
    public async Task<AccessTokenResult> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var overrideToken = Environment.GetEnvironmentVariable(TokenOverrideEnvVar);
        if (!string.IsNullOrEmpty(overrideToken))
        {
            var expiresOn = ReadJwtExpiryOrFallback(overrideToken);
            AppLogger.Information(
                $"Using {TokenOverrideEnvVar} for Azure token (LOCAL TESTING ONLY). Expires at {expiresOn:O}.");
            return new AccessTokenResult(overrideToken, expiresOn);
        }

        AppLogger.Information($"Acquiring Azure access token for scope {_scope}.");
        var token = await _credential
            .GetTokenAsync(new TokenRequestContext(new[] { _scope }), cancellationToken)
            .ConfigureAwait(false);

        AppLogger.Information(
            $"Azure access token acquired. Expires at {token.ExpiresOn:O}.");

        return new AccessTokenResult(token.Token, token.ExpiresOn);
    }

    /// <summary>
    /// Attempts to read the <c>exp</c> claim from <paramref name="tokenString"/> as a
    /// JWT. Falls back to <c>UtcNow + 1h</c> when the value is not a valid JWT or
    /// has no <c>exp</c> claim.
    /// </summary>
    internal static DateTimeOffset ReadJwtExpiryOrFallback(string tokenString)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
            if (jwt.ValidTo != default)
            {
                return new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            AppLogger.Warning(
                $"{TokenOverrideEnvVar} value is not a valid JWT; using fallback 1-hour expiry. Error: {ex.Message}");
        }

        return DateTimeOffset.UtcNow.AddHours(1);
    }
}
