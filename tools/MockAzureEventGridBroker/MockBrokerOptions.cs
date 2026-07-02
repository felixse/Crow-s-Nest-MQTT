namespace CrowsNestMqtt.MockAzureEventGridBroker;

using System.Globalization;
using System.IO;

/// <summary>
/// Configuration for <see cref="MockBroker"/>. Encapsulates the host, TCP port,
/// and TLS toggle so the same options can be produced either from environment
/// variables (production entry point) or from test code.
/// </summary>
internal sealed record MockBrokerOptions(string Host, int? Port, bool UseTls)
{
    internal const string HostEnvVar = "MOCK_EG_HOST";
    internal const string PortEnvVar = "MOCK_EG_PORT";
    internal const string UseTlsEnvVar = "MOCK_EG_USE_TLS";

    internal const string DefaultHost = "127.0.0.1";

    /// <summary>
    /// Builds a <see cref="MockBrokerOptions"/> instance from the supplied
    /// environment-variable dictionary. Matches the historical Program.Main
    /// behavior:
    ///  * <c>MOCK_EG_HOST</c> — defaults to <c>127.0.0.1</c> when null/empty.
    ///  * <c>MOCK_EG_PORT</c> — parsed as an integer; anything unparseable or
    ///    missing leaves <see cref="Port"/> as <c>null</c>, signaling the caller
    ///    to allocate an ephemeral port at bind time.
    ///  * <c>MOCK_EG_USE_TLS</c> — defaults to <c>true</c> when null/empty;
    ///    otherwise parsed via <see cref="bool.Parse(string)"/> (throws on
    ///    invalid input to match the original behavior).
    /// </summary>
    public static MockBrokerOptions FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var host = environment.TryGetValue(HostEnvVar, out var hostValue) && !string.IsNullOrEmpty(hostValue)
            ? hostValue
            : DefaultHost;

        int? port = null;
        if (environment.TryGetValue(PortEnvVar, out var portValue)
            && int.TryParse(portValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
        {
            port = parsedPort;
        }

        var useTls = true;
        if (environment.TryGetValue(UseTlsEnvVar, out var useTlsValue) && !string.IsNullOrEmpty(useTlsValue))
        {
            useTls = bool.Parse(useTlsValue);
        }

        return new MockBrokerOptions(host, port, useTls);
    }

    /// <summary>
    /// Convenience overload that snapshots the process environment. Used by
    /// <see cref="Program.Main"/>; tests should prefer the dictionary overload
    /// to keep them hermetic.
    /// </summary>
    public static MockBrokerOptions FromEnvironment()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [HostEnvVar] = Environment.GetEnvironmentVariable(HostEnvVar),
            [PortEnvVar] = Environment.GetEnvironmentVariable(PortEnvVar),
            [UseTlsEnvVar] = Environment.GetEnvironmentVariable(UseTlsEnvVar),
        };
        return FromEnvironment(snapshot);
    }
}
