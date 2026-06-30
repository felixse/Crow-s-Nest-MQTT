namespace CrowsNestMqtt.BusinessLogic.Configuration;

/// <summary>
/// Specifies the transport protocol used to connect to an MQTT broker.
/// </summary>
public enum TransportProtocol
{
    /// <summary>
    /// Standard TCP connection (default).
    /// </summary>
    Tcp = 0,

    /// <summary>
    /// WebSocket connection (ws:// or wss:// with TLS).
    /// </summary>
    WebSocket = 1
}
