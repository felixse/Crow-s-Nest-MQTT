namespace CrowsNestMqtt.MockBrokerTests;

using System.Text;
using CrowsNestMqtt.MockAzureEventGridBroker;
using MQTTnet;
using Xunit;

/// <summary>
/// Exercises every branch of
/// <see cref="MockBroker.HandleValidatingConnectionAsync"/> through real
/// MQTT client CONNECT attempts. Each test spins up its own broker on an
/// ephemeral port so the tests can run without cross-talk.
/// </summary>
public sealed class MockBrokerAuthValidationTests
{
    private static readonly TimeSpan LogWaitTimeout = TimeSpan.FromSeconds(10);

    private static async Task<(MockBroker broker, int port, CapturingLogWriter log)> StartBrokerAsync(bool useTls = false)
    {
        var port = TestHelpers.GetFreeTcpPort();
        var log = new CapturingLogWriter();
        var broker = new MockBroker(new MockBrokerOptions("127.0.0.1", port, useTls), log);
        await broker.StartAsync();
        return (broker, port, log);
    }

    private static MqttClientOptionsBuilder BaseClientOptions(int port, string clientId) =>
        new MqttClientOptionsBuilder()
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .WithTcpServer("127.0.0.1", port)
            .WithClientId(clientId);

    [Fact]
    public async Task Connect_WithoutEnhancedAuth_IsRejected_WithMissingMethodMessage()
    {
        var (broker, port, log) = await StartBrokerAsync();
        await using var brokerHandle = broker;

        using var client = new MqttClientFactory().CreateMqttClient();
        var options = BaseClientOptions(port, "connect-no-enhanced-auth").Build();

        await TryConnectAsync(client, options);

        var rejected = await log.WaitForLineAsync(
            l => l.Contains("expected AuthenticationMethod 'OAUTH2-JWT'", StringComparison.Ordinal)
              && l.Contains("got '(null)'", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(rejected, $"Expected rejection log missing. Rendered log:\n{log.RenderedLog}");
    }

    [Fact]
    public async Task Connect_WithWrongAuthMethod_IsRejected_AndLogsSuppliedMethod()
    {
        var (broker, port, log) = await StartBrokerAsync();
        await using var brokerHandle = broker;

        using var client = new MqttClientFactory().CreateMqttClient();
        var options = BaseClientOptions(port, "connect-wrong-method")
            .WithEnhancedAuthentication("SCRAM-SHA-1", Encoding.UTF8.GetBytes("does-not-matter"))
            .Build();

        await TryConnectAsync(client, options);

        var rejected = await log.WaitForLineAsync(
            l => l.Contains("got 'SCRAM-SHA-1'", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(rejected, $"Expected 'got SCRAM-SHA-1' message missing. Rendered log:\n{log.RenderedLog}");
    }

    [Fact]
    public async Task Connect_WithEmptyAuthData_IsRejected_WithNotAuthorized()
    {
        var (broker, port, log) = await StartBrokerAsync();
        await using var brokerHandle = broker;

        using var client = new MqttClientFactory().CreateMqttClient();
        var options = BaseClientOptions(port, "connect-empty-data")
            .WithEnhancedAuthentication("OAUTH2-JWT", Array.Empty<byte>())
            .Build();

        await TryConnectAsync(client, options);

        var rejected = await log.WaitForLineAsync(
            l => l.Contains("AuthenticationData is empty", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(rejected, $"Expected empty-data rejection log missing. Rendered log:\n{log.RenderedLog}");
    }

    [Fact]
    public async Task Connect_WithMalformedJwt_IsRejected_WithNotAuthorized()
    {
        var (broker, port, log) = await StartBrokerAsync();
        await using var brokerHandle = broker;

        using var client = new MqttClientFactory().CreateMqttClient();
        var options = BaseClientOptions(port, "connect-malformed-jwt")
            .WithEnhancedAuthentication("OAUTH2-JWT", Encoding.UTF8.GetBytes("not-a-real-jwt"))
            .Build();

        await TryConnectAsync(client, options);

        var rejected = await log.WaitForLineAsync(
            l => l.Contains("is not a valid JWT", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(rejected, $"Expected malformed-JWT rejection log missing. Rendered log:\n{log.RenderedLog}");
    }

    [Fact]
    public async Task Connect_WithValidJwt_IsAccepted_AndLogsSubjectAndExpiry()
    {
        var (broker, port, log) = await StartBrokerAsync();
        await using var brokerHandle = broker;

        var jwt = TestHelpers.BuildJwt(subject: "aspire-user");
        using var client = new MqttClientFactory().CreateMqttClient();
        var options = BaseClientOptions(port, "connect-valid-jwt")
            .WithEnhancedAuthentication("OAUTH2-JWT", Encoding.UTF8.GetBytes(jwt))
            .Build();

        var result = await client.ConnectAsync(options, TestContext.Current.CancellationToken);
        Assert.Equal(MqttClientConnectResultCode.Success, result.ResultCode);

        var accepted = await log.WaitForLineAsync(
            l => l.Contains("Accepted CONNECT", StringComparison.Ordinal)
              && l.Contains("sub=aspire-user", StringComparison.Ordinal)
              && !l.Contains("exp=(no exp)", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(accepted, $"Expected acceptance-with-sub log missing. Rendered log:\n{log.RenderedLog}");

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Connect_WithJwtMissingExp_IsAccepted_AndLogsNoExpPlaceholder()
    {
        var (broker, port, log) = await StartBrokerAsync();
        await using var brokerHandle = broker;

        var jwt = TestHelpers.BuildJwt(subject: "no-exp-user", omitExpiration: true);
        using var client = new MqttClientFactory().CreateMqttClient();
        var options = BaseClientOptions(port, "connect-jwt-no-exp")
            .WithEnhancedAuthentication("OAUTH2-JWT", Encoding.UTF8.GetBytes(jwt))
            .Build();

        var result = await client.ConnectAsync(options, TestContext.Current.CancellationToken);
        Assert.Equal(MqttClientConnectResultCode.Success, result.ResultCode);

        var accepted = await log.WaitForLineAsync(
            l => l.Contains("Accepted CONNECT", StringComparison.Ordinal)
              && l.Contains("exp=(no exp)", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(accepted, $"Expected 'exp=(no exp)' log missing. Rendered log:\n{log.RenderedLog}");

        await client.DisconnectAsync();
    }

    private static async Task TryConnectAsync(IMqttClient client, MqttClientOptions options)
    {
        // The broker may surface a rejection either as a non-Success result code
        // or as a thrown exception depending on MQTTnet's build. Either shape is
        // acceptable for the tests in this file; we only care about the
        // broker's captured log.
        try
        {
            _ = await client.ConnectAsync(options, TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (ex is MQTTnet.Adapter.MqttConnectingFailedException or MQTTnet.Exceptions.MqttCommunicationException)
        {
        }
    }
}
