# Requires -Version 7
# Delayed wrapper for SendTestData.ps1 — used by Aspire AppHost to send test data
# after the broker has had time to fully initialize and clients have connected.

param(
    [int]$DelaySeconds = 30
)

$scriptDir = $PSScriptRoot

# Read broker connection info from environment variables (set by Aspire)
$broker = $env:MQTT_HOST
$port = $env:MQTT_PORT

# Fallback: try Aspire service reference format
if (-not $broker) {
    $aspireEndpoint = $env:services__mqtt__mqtt__0
    if ($aspireEndpoint) {
        $uri = [System.Uri]::new($aspireEndpoint)
        $broker = $uri.Host
        $port = $uri.Port
    }
}

if (-not $broker -or -not $port) {
    Write-Error "MQTT_HOST and MQTT_PORT environment variables are not set. Cannot send test data."
    exit 1
}

# Resolve TLS for the Aspire-injected endpoint:
#   * Honor MQTT_USE_TLS if the AppHost set it (e.g. "false" for plain TCP, "true" for the TLS listener).
#   * Otherwise default to plain TCP — Aspire's default `mqtt` endpoint points at EMQX's :1883 listener,
#     and attempting TLS against it produces "Received an unexpected EOF or 0 bytes from the transport stream".
if ($env:MQTT_USE_TLS) {
    $useTls = [System.Convert]::ToBoolean($env:MQTT_USE_TLS)
} else {
    $useTls = $false
}

Write-Host "Waiting $DelaySeconds seconds for broker and clients to be ready..."
Start-Sleep -Seconds $DelaySeconds

Write-Host "Sending test data to $broker`:$port (TLS: $useTls) ..."
& "$scriptDir\SendTestData.ps1" -Broker $broker -BrokerPort ([int]$port) -UseTls $useTls

Write-Host "Test data sent successfully."
