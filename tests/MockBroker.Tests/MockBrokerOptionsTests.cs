namespace CrowsNestMqtt.MockBrokerTests;

using CrowsNestMqtt.MockAzureEventGridBroker;
using Xunit;

/// <summary>
/// Unit tests for <see cref="MockBrokerOptions.FromEnvironment(IReadOnlyDictionary{string,string})"/>. These pin the
/// env-var contract that <c>src/AppHost</c>, <c>tests/AppHost.Tests</c>, and any
/// external orchestrator (Aspire, CI) depends on. Only the dictionary overload
/// is tested so nothing pollutes the ambient process environment.
/// </summary>
public sealed class MockBrokerOptionsTests
{
    [Fact]
    public void FromEnvironment_MissingAllVars_ReturnsDefaults()
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal));

        Assert.Equal("127.0.0.1", options.Host);
        Assert.Null(options.Port);
        Assert.True(options.UseTls);
    }

    [Fact]
    public void FromEnvironment_EmptyHost_FallsBackToDefault()
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.HostEnvVar] = string.Empty,
        });

        Assert.Equal("127.0.0.1", options.Host);
    }

    [Theory]
    [InlineData("192.168.1.5")]
    [InlineData("mock-eg.internal")]
    public void FromEnvironment_CustomHost_IsRespected(string host)
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.HostEnvVar] = host,
        });

        Assert.Equal(host, options.Host);
    }

    [Fact]
    public void FromEnvironment_ValidPort_IsParsed()
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.PortEnvVar] = "42069",
        });

        Assert.Equal(42069, options.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("3.14")]
    public void FromEnvironment_InvalidPort_LeavesPortNullForEphemeralAllocation(string value)
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.PortEnvVar] = value,
        });

        Assert.Null(options.Port);
    }

    [Fact]
    public void FromEnvironment_UseTlsFalse_IsRespected()
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.UseTlsEnvVar] = "false",
        });

        Assert.False(options.UseTls);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    public void FromEnvironment_UseTlsTrueVariants_AreAllAccepted(string value)
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.UseTlsEnvVar] = value,
        });

        Assert.True(options.UseTls);
    }

    [Fact]
    public void FromEnvironment_UseTlsUnparseable_Throws()
    {
        // Preserves the original Program.Main behavior: bool.Parse throws on
        // anything other than "true"/"false" (case-insensitive), which is the
        // signal we want operators to see fast rather than silently defaulting.
        Assert.Throws<FormatException>(() => MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.UseTlsEnvVar] = "maybe",
        }));
    }

    [Fact]
    public void FromEnvironment_UseTlsEmpty_DefaultsToTrue()
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.UseTlsEnvVar] = string.Empty,
        });

        Assert.True(options.UseTls);
    }

    [Fact]
    public void FromEnvironment_AllVarsPresent_ReturnsCombinedOptions()
    {
        var options = MockBrokerOptions.FromEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MockBrokerOptions.HostEnvVar] = "10.0.0.7",
            [MockBrokerOptions.PortEnvVar] = "12345",
            [MockBrokerOptions.UseTlsEnvVar] = "false",
        });

        Assert.Equal("10.0.0.7", options.Host);
        Assert.Equal(12345, options.Port);
        Assert.False(options.UseTls);
    }

    [Fact]
    public void FromEnvironment_NullDictionary_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MockBrokerOptions.FromEnvironment(null!));
    }

    [Fact]
    public void FromEnvironment_ProcessOverload_ReturnsOptions()
    {
        // The parameter-less overload snapshots the real process environment.
        // We can't easily assert specific values without mutating env vars
        // (which would race with other tests), so we only assert that the call
        // succeeds and produces an object with a plausible host default.
        var options = MockBrokerOptions.FromEnvironment();

        Assert.NotNull(options);
        Assert.False(string.IsNullOrEmpty(options.Host));
    }
}
