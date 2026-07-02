namespace CrowsNestMqtt.MockBrokerTests;

using System.Text;
using CrowsNestMqtt.MockAzureEventGridBroker;
using MQTTnet;
using MQTTnet.Protocol;
using Xunit;

/// <summary>
/// Covers <see cref="MockBroker.HandleInboundPacketAsync"/>. The interesting
/// branches are triggered by MQTT v5 AUTH packets (client-initiated
/// re-authentication) and by any non-AUTH packet flowing through the same
/// interceptor.
/// </summary>
public sealed class MockBrokerInboundAuthPacketTests
{
    private static readonly TimeSpan LogWaitTimeout = TimeSpan.FromSeconds(10);

    private static async Task<(IMqttClient client, MockBroker broker, CapturingLogWriter log)> ConnectAsync(string clientId)
    {
        var port = TestHelpers.GetFreeTcpPort();
        var log = new CapturingLogWriter();
        var broker = new MockBroker(new MockBrokerOptions("127.0.0.1", port, UseTls: false), log);
        await broker.StartAsync();

        var jwt = TestHelpers.BuildJwt();
        var client = new MqttClientFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .WithTcpServer("127.0.0.1", port)
            .WithClientId(clientId)
            .WithEnhancedAuthentication("OAUTH2-JWT", Encoding.UTF8.GetBytes(jwt))
            .Build();

        var connectResult = await client.ConnectAsync(options, TestContext.Current.CancellationToken);
        Assert.Equal(MqttClientConnectResultCode.Success, connectResult.ResultCode);

        return (client, broker, log);
    }

    [Fact]
    public async Task Reauth_WithValidJwt_LogsJwtSubject()
    {
        var (client, broker, log) = await ConnectAsync("reauth-valid");
        await using var brokerHandle = broker;
        using var clientHandle = client;

        var refreshed = TestHelpers.BuildJwt(subject: "refreshed-sub");
        await client.SendEnhancedAuthenticationExchangeDataAsync(
            new MqttEnhancedAuthenticationExchangeData
            {
                AuthenticationData = Encoding.UTF8.GetBytes(refreshed),
                ReasonCode = MqttAuthenticateReasonCode.ReAuthenticate,
            },
            TestContext.Current.CancellationToken);

        var logged = await log.WaitForLineAsync(
            l => l.Contains("Received AUTH (method=OAUTH2-JWT", StringComparison.Ordinal)
              && l.Contains("reason=ReAuthenticate", StringComparison.Ordinal)
              && l.Contains("JWT sub=refreshed-sub", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(logged, $"Expected re-auth log missing. Rendered log:\n{log.RenderedLog}");

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Reauth_WithGarbageData_LogsJwtParseFailure()
    {
        var (client, broker, log) = await ConnectAsync("reauth-garbage");
        await using var brokerHandle = broker;
        using var clientHandle = client;

        await client.SendEnhancedAuthenticationExchangeDataAsync(
            new MqttEnhancedAuthenticationExchangeData
            {
                AuthenticationData = Encoding.UTF8.GetBytes("not-a-jwt-at-all"),
                ReasonCode = MqttAuthenticateReasonCode.ReAuthenticate,
            },
            TestContext.Current.CancellationToken);

        var logged = await log.WaitForLineAsync(
            l => l.Contains("Received AUTH", StringComparison.Ordinal)
              && l.Contains("JWT parse failed", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(logged, $"Expected 'JWT parse failed' log missing. Rendered log:\n{log.RenderedLog}");

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Reauth_WithEmptyData_LogsZeroDataBytesAndSkipsJwtParse()
    {
        var (client, broker, log) = await ConnectAsync("reauth-empty");
        await using var brokerHandle = broker;
        using var clientHandle = client;

        await client.SendEnhancedAuthenticationExchangeDataAsync(
            new MqttEnhancedAuthenticationExchangeData
            {
                AuthenticationData = Array.Empty<byte>(),
                ReasonCode = MqttAuthenticateReasonCode.ReAuthenticate,
            },
            TestContext.Current.CancellationToken);

        var logged = await log.WaitForLineAsync(
            l => l.Contains("Received AUTH", StringComparison.Ordinal)
              && l.Contains("data-bytes=0", StringComparison.Ordinal)
              && !l.Contains("JWT sub=", StringComparison.Ordinal)
              && !l.Contains("JWT parse failed", StringComparison.Ordinal),
            LogWaitTimeout);
        Assert.True(logged, $"Expected empty-data AUTH log missing. Rendered log:\n{log.RenderedLog}");

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task NonAuthPackets_DoNotEmitReceivedAuthLog()
    {
        var (client, broker, log) = await ConnectAsync("non-auth-packet");
        await using var brokerHandle = broker;
        using var clientHandle = client;

        // A publish flows through the same inbound interceptor but is not an
        // MqttAuthPacket, so it must take the early-return branch.
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic("mock-eg/tests/publish")
            .WithPayload("payload")
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        _ = await client.PublishAsync(msg, TestContext.Current.CancellationToken);

        // Give the broker a moment to process the packet.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(log.Lines, l => l.Contains("Received AUTH", StringComparison.Ordinal));

        await client.DisconnectAsync();
    }
}
