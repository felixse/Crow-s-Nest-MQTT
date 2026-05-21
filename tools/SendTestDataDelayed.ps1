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

Write-Host "Waiting $DelaySeconds seconds for broker and clients to be ready..."
Start-Sleep -Seconds $DelaySeconds

Write-Host "Sending test data to $broker`:$port ..."
& "$scriptDir\SendTestData.ps1" -Broker $broker -BrokerPort ([int]$port)

Write-Host "Test data sent successfully."
