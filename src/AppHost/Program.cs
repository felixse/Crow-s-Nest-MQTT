using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Add EMQX MQTT v5 broker as a Docker container with dynamic ports
var mqttBroker = builder.AddContainer("mqtt", "emqx/emqx", "latest")
    .WithEndpoint(targetPort: 1883, name: "mqtt", scheme: "mqtt")
    .WithEndpoint(targetPort: 8083, name: "ws", scheme: "ws")
    .WithEndpoint(targetPort: 8883, name: "mqtts", scheme: "tcp")
    .WithHttpEndpoint(targetPort: 18083, name: "dashboard")
    .WithEnvironment("EMQX_AUTHORIZATION__NO_MATCH", "allow")
    .WithEnvironment("EMQX_AUTHORIZATION__SOURCES", "[]") // Disable file-based ACL to allow subscribe to #
    .WithEnvironment("EMQX_LISTENERS__TCP__DEFAULT__BIND", "0.0.0.0:1883")
    .WithEnvironment("EMQX_LISTENERS__WS__DEFAULT__BIND", "0.0.0.0:8083")
    .WithEnvironment("EMQX_LISTENERS__SSL__DEFAULT__BIND", "0.0.0.0:8883")
    .WithEnvironment("EMQX_MQTT__MAX_PACKET_SIZE", "10MB");

// Get the MQTT endpoints for passing to the client applications
var mqttEndpoint = mqttBroker.GetEndpoint("mqtt");
var wsEndpoint = mqttBroker.GetEndpoint("ws");
var mqttsEndpoint = mqttBroker.GetEndpoint("mqtts");

// Shared settings for all CrowsNestMqtt instances
var sharedEnvVars = (IResourceBuilder<ProjectResource> rb) => rb
    .WithEnvironment("CROWSNEST__AUTH_MODE", "anonymous")
    .WithEnvironment("CROWSNEST__CLIENT_ID", "")
    .WithEnvironment("CROWSNEST__KEEP_ALIVE_SECONDS", "0")
    .WithEnvironment("CROWSNEST__CLEAN_SESSION", "true")
    .WithEnvironment("CROWSNEST__SESSION_EXPIRY_SECONDS", "0")
    .WithEnvironment("CROWSNEST__SUBSCRIPTION_QOS", "1")
    .WithEnvironment("CROWSNEST__TOPIC_BUFFER_LIMITS", """[{"TopicFilter":"#","MaxSizeBytes":11048576}]""");

// Add CrowsNestMqtt as a project that auto-connects to the MQTT broker via TCP
var tcpInstance = builder.AddProject<Projects.CrowsNestMqtt_App>("crows-nest-mqtt")
    .WithReference(mqttEndpoint)
    .WaitFor(mqttBroker)
    .WithEnvironment("CROWSNEST__HOSTNAME", mqttEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("CROWSNEST__PORT", mqttEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("CROWSNEST__USE_TLS", "false");
sharedEnvVars(tcpInstance);

// Add a second CrowsNestMqtt instance that connects via WebSocket
var wsInstance = builder.AddProject<Projects.CrowsNestMqtt_App>("crows-nest-mqtt-ws")
    .WithReference(wsEndpoint)
    .WaitFor(mqttBroker)
    .WithEnvironment("CROWSNEST__HOSTNAME", wsEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("CROWSNEST__PORT", wsEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("CROWSNEST__USE_TLS", "false")
    .WithEnvironment("CROWSNEST__TRANSPORT", "WebSocket")
    .WithEnvironment("CROWSNEST__WEBSOCKET_PATH", "/mqtt");
sharedEnvVars(wsInstance);

// Add a third CrowsNestMqtt instance that connects via TLS
var tlsInstance = builder.AddProject<Projects.CrowsNestMqtt_App>("crows-nest-mqtt-tls")
    .WithReference(mqttsEndpoint)
    .WaitFor(mqttBroker)
    .WithEnvironment("CROWSNEST__HOSTNAME", mqttsEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("CROWSNEST__PORT", mqttsEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("CROWSNEST__USE_TLS", "true");
sharedEnvVars(tlsInstance);

// Add delayed test data sender that publishes sample data after broker is ready
var toolsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tools"));
builder.AddExecutable("test-data-sender", "pwsh", toolsDir, "-File", "SendTestDataDelayed.ps1")
    .WithReference(mqttEndpoint)
    .WaitFor(mqttBroker)
    .WithEnvironment("MQTT_HOST", mqttEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("MQTT_PORT", mqttEndpoint.Property(EndpointProperty.Port));

builder.Build().Run();
