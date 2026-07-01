namespace CrowsNestMqtt.MockAzureEventGridBroker;

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;

/// <summary>
/// Development-only MQTT broker that emulates Azure Event Grid namespace enhanced
/// authentication (<c>OAUTH2-JWT</c>). Accepts CONNECT packets whose
/// AuthenticationMethod is <c>OAUTH2-JWT</c> and AuthenticationData contains a
/// well-formed JWT. Signature and audience are NOT verified. Do not use in
/// production or expose beyond localhost.
///
/// Configuration via env vars:
///  <list type="bullet">
///   <item><c>MOCK_EG_PORT</c> — TCP/TLS port to listen on (default: dynamic).</item>
///   <item><c>MOCK_EG_HOST</c> — host to bind (default: 127.0.0.1).</item>
///   <item><c>MOCK_EG_USE_TLS</c> — <c>true</c> to enable TLS with a self-signed cert (default: true).</item>
///  </list>
/// The chosen port is printed to stdout as <c>MOCK_EG_LISTENING_ON=&lt;host&gt;:&lt;port&gt;</c>.
/// </summary>
internal static class Program
{
    private const string ExpectedMethod = "OAUTH2-JWT";

    internal static async Task Main()
    {
        var host = Environment.GetEnvironmentVariable("MOCK_EG_HOST") ?? "127.0.0.1";
        var portEnv = Environment.GetEnvironmentVariable("MOCK_EG_PORT");
        var useTlsEnv = Environment.GetEnvironmentVariable("MOCK_EG_USE_TLS");
        var useTls = string.IsNullOrEmpty(useTlsEnv) || bool.Parse(useTlsEnv);

        int port = int.TryParse(portEnv, out var p) ? p : GetFreeTcpPort();

        Log($"Starting mock Azure Event Grid broker on {host}:{port} (TLS={useTls}).");

        var optionsBuilder = new MqttServerOptionsBuilder()
            .WithPersistentSessions()
            .WithKeepAlive();

        if (useTls)
        {
            // Materialize a self-signed X509 certificate whose private key is
            // exportable and persisted — this is what MqttServer needs on Windows
            // to complete the TLS handshake. Ephemeral private keys created by
            // CertificateRequest.CreateSelfSigned aren't accessible during TLS.
            var pfxBytes = DevCertificateFactory.CreateSelfSignedPfxBytes(host);
            var cert = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

            optionsBuilder = optionsBuilder
                .WithEncryptedEndpoint()
                .WithEncryptedEndpointBoundIPAddress(IPAddress.Parse(host))
                .WithEncryptedEndpointPort(port)
                .WithoutDefaultEndpoint()
                .WithEncryptionCertificate(cert)
                .WithEncryptionSslProtocol(System.Security.Authentication.SslProtocols.None); // let OS choose
        }
        else
        {
            optionsBuilder = optionsBuilder
                .WithDefaultEndpoint()
                .WithDefaultEndpointBoundIPAddress(IPAddress.Parse(host))
                .WithDefaultEndpointPort(port);
        }

        var factory = new MqttServerFactory();
        using var server = factory.CreateMqttServer(optionsBuilder.Build());

        server.ValidatingConnectionAsync += HandleValidatingConnectionAsync;
        server.InterceptingInboundPacketAsync += HandleInboundPacketAsync;
        server.ClientConnectedAsync += args =>
        {
            Log($"Client connected: id='{args.ClientId}' endpoint={args.RemoteEndPoint}.");
            return Task.CompletedTask;
        };
        server.ClientDisconnectedAsync += args =>
        {
            Log($"Client disconnected: id='{args.ClientId}' type={args.DisconnectType}.");
            return Task.CompletedTask;
        };

        await server.StartAsync().ConfigureAwait(false);

        // Publish the effective endpoint so Aspire / test harnesses can discover it.
        Console.WriteLine($"MOCK_EG_LISTENING_ON={host}:{port}");
        Log("Mock broker started. Waiting for connections. Press Ctrl+C to stop.");

        var stopTcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopTcs.TrySetResult();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stopTcs.TrySetResult();

        await stopTcs.Task.ConfigureAwait(false);

        Log("Stopping mock broker...");
        await server.StopAsync().ConfigureAwait(false);
        Log("Mock broker stopped.");
    }

    private static Task HandleValidatingConnectionAsync(ValidatingConnectionEventArgs args)
    {
        if (!string.Equals(args.AuthenticationMethod, ExpectedMethod, StringComparison.Ordinal))
        {
            args.ReasonCode = MqttConnectReasonCode.BadAuthenticationMethod;
            args.ReasonString =
                $"Mock broker rejects CONNECT: expected AuthenticationMethod '{ExpectedMethod}', got '{args.AuthenticationMethod ?? "(null)"}'.";
            Log(args.ReasonString);
            return Task.CompletedTask;
        }

        if (args.AuthenticationData is null || args.AuthenticationData.Length == 0)
        {
            args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
            args.ReasonString = "Mock broker rejects CONNECT: AuthenticationData is empty.";
            Log(args.ReasonString);
            return Task.CompletedTask;
        }

        // The token must be a syntactically valid JWT. Signature is intentionally NOT verified.
        var tokenString = Encoding.UTF8.GetString(args.AuthenticationData);
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
            var sub = jwt.Subject ?? "(missing sub)";
            var exp = jwt.ValidTo == default ? "(no exp)" : jwt.ValidTo.ToString("O");
            Log($"Accepted CONNECT (method={ExpectedMethod}, client-id='{args.ClientId}', sub={sub}, exp={exp}).");
        }
        catch (Exception ex)
        {
            args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
            args.ReasonString = $"Mock broker rejects CONNECT: AuthenticationData is not a valid JWT ({ex.GetType().Name}: {ex.Message}).";
            Log(args.ReasonString);
        }

        return Task.CompletedTask;
    }

    private static Task HandleInboundPacketAsync(InterceptingPacketEventArgs args)
    {
        // MQTT v5 client-initiated re-authentication surfaces as an AUTH packet on
        // an already-connected session. Log the new token for observability. We
        // don't need to send an AUTH response — Azure Event Grid re-auth is a
        // one-shot notification from the client.
        if (args.Packet is MqttAuthPacket auth)
        {
            var tokenLen = auth.AuthenticationData?.Length ?? 0;
            var reason = auth.ReasonCode;
            var summary = $"Received AUTH (method={auth.AuthenticationMethod}, reason={reason}, data-bytes={tokenLen}, client-id='{args.ClientId}').";
            try
            {
                if (auth.AuthenticationData is { Length: > 0 })
                {
                    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(
                        Encoding.UTF8.GetString(auth.AuthenticationData));
                    summary += $" JWT sub={jwt.Subject ?? "(missing)"}, exp={jwt.ValidTo:O}.";
                }
            }
            catch (Exception ex)
            {
                summary += $" (JWT parse failed: {ex.Message})";
            }
            Log(summary);
        }

        return Task.CompletedTask;
    }

    private static int GetFreeTcpPort()
    {
        // Bind an ephemeral socket on the loopback and read the assigned port. The
        // socket is closed immediately; there's an inherent tiny race window before
        // MqttServer binds, but it's acceptable for a dev-only broker.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.UtcNow:O}] [mock-eg-broker] {message}");
    }
}
