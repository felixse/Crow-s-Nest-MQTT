namespace CrowsNestMqtt.BusinessLogic.Configuration;

/// <summary>
/// Authentication mode for Azure Event Grid namespace MQTT brokers.
/// Uses <c>Azure.Identity.DefaultAzureCredential</c> to obtain an Entra ID access token
/// that is sent to the broker via MQTT v5 enhanced authentication with method
/// <c>OAUTH2-JWT</c>. The token is fetched on every connection and proactively refreshed
/// via MQTT v5 <c>AUTH</c> packets before it expires.
/// </summary>
/// <param name="Scope">
/// Optional scope override. Defaults to <see cref="DefaultScope"/> when null or empty.
/// </param>
public sealed record AzureAuthenticationMode(string? Scope = null) : AuthenticationMode
{
    /// <summary>
    /// Default OAuth scope used to acquire an Event Grid access token.
    /// </summary>
    public const string DefaultScope = "https://eventgrid.azure.net/.default";

    /// <summary>
    /// MQTT v5 enhanced-authentication method name used by Azure Event Grid.
    /// </summary>
    public const string AuthenticationMethod = "OAUTH2-JWT";

    /// <summary>
    /// Returns the effective scope, falling back to <see cref="DefaultScope"/> when
    /// <see cref="Scope"/> is null or whitespace.
    /// </summary>
    public string EffectiveScope =>
        string.IsNullOrWhiteSpace(Scope) ? DefaultScope : Scope!;
}
