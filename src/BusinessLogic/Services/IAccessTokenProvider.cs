namespace CrowsNestMqtt.BusinessLogic.Services;

/// <summary>
/// Represents an OAuth access token together with its expiration timestamp.
/// </summary>
/// <param name="Token">The raw access token string.</param>
/// <param name="ExpiresOn">UTC timestamp at which the token expires.</param>
public readonly record struct AccessTokenResult(string Token, DateTimeOffset ExpiresOn);

/// <summary>
/// Abstraction over an access-token source so authentication flows can be unit-tested
/// without the Azure SDK.
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>
    /// Returns a current (non-expired) access token. Implementations may cache results
    /// internally and return a cached token until it is close to expiring.
    /// </summary>
    Task<AccessTokenResult> GetTokenAsync(CancellationToken cancellationToken = default);
}
