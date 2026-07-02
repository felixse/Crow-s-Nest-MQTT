using CrowsNestMqtt.BusinessLogic.Services;
using Xunit;

namespace CrowsNestMqtt.UnitTests.Services;

public class MqttHostnameNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrWhitespace_ReturnsAsIsWithNoNotes(string? input)
    {
        var result = MqttHostnameNormalizer.Normalize(input);

        Assert.False(result.WasChanged);
        Assert.Null(result.Port);
        Assert.Empty(result.Notes);
    }

    [Theory]
    [InlineData("broker.hivemq.com")]
    [InlineData("localhost")]
    [InlineData("ns.region-1.ts.eventgrid.azure.net")]
    public void Normalize_AlreadyClean_ReturnsAsIsWithNoNotes(string input)
    {
        var result = MqttHostnameNormalizer.Normalize(input);

        Assert.Equal(input, result.Hostname);
        Assert.Null(result.Port);
        Assert.False(result.WasChanged);
    }

    [Theory]
    [InlineData("https://broker.example.com", "broker.example.com", "stripped 'https://' scheme")]
    [InlineData("http://broker.example.com", "broker.example.com", "stripped 'http://' scheme")]
    [InlineData("mqtt://broker.example.com", "broker.example.com", "stripped 'mqtt://' scheme")]
    [InlineData("mqtts://broker.example.com", "broker.example.com", "stripped 'mqtts://' scheme")]
    [InlineData("ws://broker.example.com", "broker.example.com", "stripped 'ws://' scheme")]
    [InlineData("wss://broker.example.com", "broker.example.com", "stripped 'wss://' scheme")]
    [InlineData("tcp://broker.example.com", "broker.example.com", "stripped 'tcp://' scheme")]
    [InlineData("HTTPS://Broker.Example.Com", "Broker.Example.Com", "stripped 'https://' scheme")]
    public void Normalize_StripsUrlScheme(string input, string expectedHost, string expectedNote)
    {
        var result = MqttHostnameNormalizer.Normalize(input);

        Assert.Equal(expectedHost, result.Hostname);
        Assert.Contains(expectedNote, result.Notes);
    }

    [Fact]
    public void Normalize_StripsPath()
    {
        var result = MqttHostnameNormalizer.Normalize("broker.example.com/api/events");

        Assert.Equal("broker.example.com", result.Hostname);
        Assert.Contains("stripped path '/api/events'", result.Notes);
    }

    [Fact]
    public void Normalize_StripsSchemeAndPath()
    {
        var result = MqttHostnameNormalizer.Normalize("https://broker.example.com/some/path?x=1");

        Assert.Equal("broker.example.com", result.Hostname);
        Assert.Contains("stripped 'https://' scheme", result.Notes);
        Assert.Contains("stripped path '/some/path?x=1'", result.Notes);
    }

    [Theory]
    [InlineData("broker.example.com:1883", "broker.example.com", 1883)]
    [InlineData("broker.example.com:8883", "broker.example.com", 8883)]
    [InlineData("mqtts://broker.example.com:8883", "broker.example.com", 8883)]
    [InlineData("https://broker.example.com:443/api", "broker.example.com", 443)]
    public void Normalize_ExtractsTrailingPort(string input, string expectedHost, int expectedPort)
    {
        var result = MqttHostnameNormalizer.Normalize(input);

        Assert.Equal(expectedHost, result.Hostname);
        Assert.Equal(expectedPort, result.Port);
    }

    [Fact]
    public void Normalize_IgnoresInvalidPortStrings()
    {
        var result = MqttHostnameNormalizer.Normalize("broker.example.com:notaport");

        // ':notaport' is not extracted as a port — the whole thing stays as hostname.
        Assert.Equal("broker.example.com:notaport", result.Hostname);
        Assert.Null(result.Port);
    }

    [Fact]
    public void Normalize_IgnoresOutOfRangePort()
    {
        var result = MqttHostnameNormalizer.Normalize("broker.example.com:99999");

        Assert.Equal("broker.example.com:99999", result.Hostname);
        Assert.Null(result.Port);
    }

    [Fact]
    public void Normalize_TrimsWhitespace()
    {
        var result = MqttHostnameNormalizer.Normalize("   broker.example.com   ");

        Assert.Equal("broker.example.com", result.Hostname);
        Assert.Contains("trimmed surrounding whitespace", result.Notes);
    }

    [Fact]
    public void Normalize_StripsUserInfoPrefix()
    {
        var result = MqttHostnameNormalizer.Normalize("user:pass@broker.example.com:1883");

        Assert.Equal("broker.example.com", result.Hostname);
        Assert.Equal(1883, result.Port);
        Assert.Contains("stripped userinfo prefix", result.Notes);
    }

    // --- Azure Event Grid HTTP → MQTT suffix rewriting ---

    [Fact]
    public void Normalize_RewritesEventGridHttpToMqttSuffix()
    {
        var input = "https://egroutealkopkedev.northeurope-1.eventgrid.azure.net/api/events";
        var result = MqttHostnameNormalizer.Normalize(input);

        Assert.Equal("egroutealkopkedev.northeurope-1.ts.eventgrid.azure.net", result.Hostname);
        Assert.Contains(result.Notes, n => n.Contains("Event Grid HTTP suffix", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalize_LeavesEventGridMqttSuffixAlone()
    {
        var input = "myns.eastus-1.ts.eventgrid.azure.net";
        var result = MqttHostnameNormalizer.Normalize(input);

        Assert.Equal(input, result.Hostname);
        Assert.DoesNotContain(result.Notes, n => n.Contains("suffix", StringComparison.Ordinal));
        Assert.False(result.WasChanged);
    }

    [Fact]
    public void Normalize_RewriteAppliedAfterSchemeAndPathStripping()
    {
        // A single realistic pasted-URL scenario end-to-end.
        var input = "  HTTPS://EGRoutEAlKopkeDev.northeurope-1.eventgrid.azure.net/api/events  ";
        var result = MqttHostnameNormalizer.Normalize(input);

        Assert.Equal("EGRoutEAlKopkeDev.northeurope-1.ts.eventgrid.azure.net", result.Hostname);
        Assert.True(result.WasChanged);
        Assert.Contains("trimmed surrounding whitespace", result.Notes);
        Assert.Contains("stripped 'https://' scheme", result.Notes);
        Assert.Contains("stripped path '/api/events'", result.Notes);
        Assert.Contains(result.Notes, n => n.Contains("Event Grid", StringComparison.Ordinal));
    }
}
