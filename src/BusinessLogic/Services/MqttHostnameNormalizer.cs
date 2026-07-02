namespace CrowsNestMqtt.BusinessLogic.Services;

using System;
using System.Globalization;

/// <summary>
/// Result of normalizing an MQTT-style hostname input.
/// </summary>
/// <param name="Hostname">The cleaned hostname (no scheme, no path, no port).</param>
/// <param name="Port">The port extracted from the input, or <c>null</c> when none was present.</param>
/// <param name="Notes">
/// Human-readable notes describing every non-trivial transformation applied
/// (e.g. "stripped 'https://' scheme"). Empty when the input was already clean.
/// </param>
public readonly record struct NormalizedMqttHostname(string Hostname, int? Port, IReadOnlyList<string> Notes)
{
    /// <summary>True when the normalizer actually changed the input.</summary>
    public bool WasChanged => Notes.Count > 0;
}

/// <summary>
/// Cleans user-supplied hostname strings so callers can accept the many shapes
/// people paste from browser address bars, Azure portal blades, and MQTT
/// tutorials, and always end up with a plain hostname the MQTT layer can use.
///
/// The normalizer:
///  <list type="bullet">
///   <item>strips URL schemes (<c>https://</c>, <c>http://</c>, <c>mqtt://</c>, <c>mqtts://</c>, <c>ws://</c>, <c>wss://</c>, <c>tcp://</c>);</item>
///   <item>strips anything after the first <c>/</c> (e.g. <c>/api/events</c>);</item>
///   <item>extracts a trailing <c>:&lt;port&gt;</c> and reports it separately;</item>
///   <item>rewrites <c>&lt;ns&gt;.&lt;region&gt;-N.eventgrid.azure.net</c> → <c>&lt;ns&gt;.&lt;region&gt;-N.ts.eventgrid.azure.net</c> (Azure Event Grid HTTP data-plane → MQTT topic-space suffix mapping);</item>
///   <item>trims whitespace.</item>
///  </list>
/// It never invents a hostname from thin air: null / empty inputs pass through
/// unchanged with no notes.
/// </summary>
public static class MqttHostnameNormalizer
{
    private static readonly string[] KnownSchemes =
    {
        "https://", "http://", "mqtts://", "mqtt://", "wss://", "ws://", "tcp://",
    };

    private const string EventGridHttpSuffix = ".eventgrid.azure.net";
    private const string EventGridMqttSuffix = ".ts.eventgrid.azure.net";

    /// <summary>
    /// Normalizes <paramref name="input"/>. Returns the original string in the
    /// <see cref="NormalizedMqttHostname.Hostname"/> slot when the value is
    /// null / whitespace so callers don't have to special-case that.
    /// </summary>
    public static NormalizedMqttHostname Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new NormalizedMqttHostname(input ?? string.Empty, null, Array.Empty<string>());
        }

        var notes = new List<string>();
        var work = input.Trim();
        if (work.Length != input.Length)
        {
            notes.Add("trimmed surrounding whitespace");
        }

        // 1. Strip a known URL scheme.
        foreach (var scheme in KnownSchemes)
        {
            if (work.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                work = work.Substring(scheme.Length);
                notes.Add($"stripped '{scheme}' scheme");
                break;
            }
        }

        // 2. Strip anything after the first '/' (path / query).
        var slashIndex = work.IndexOf('/');
        if (slashIndex >= 0)
        {
            var stripped = work.Substring(slashIndex);
            work = work.Substring(0, slashIndex);
            notes.Add($"stripped path '{stripped}'");
        }

        // 3. Strip a userinfo prefix if any (rare, but 'user@host:port' is legal in URIs).
        var atIndex = work.IndexOf('@');
        if (atIndex >= 0)
        {
            work = work.Substring(atIndex + 1);
            notes.Add("stripped userinfo prefix");
        }

        // 4. Extract a trailing ':port'.
        int? extractedPort = null;
        var colonIndex = work.LastIndexOf(':');
        if (colonIndex > 0
            && colonIndex < work.Length - 1
            && int.TryParse(
                work.AsSpan(colonIndex + 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedPort)
            && parsedPort is > 0 and <= 65535)
        {
            extractedPort = parsedPort;
            work = work.Substring(0, colonIndex);
            notes.Add($"extracted port {parsedPort}");
        }

        // 5. Azure Event Grid: rewrite the HTTP data-plane suffix to the MQTT
        //    topic-space suffix. The rewrite is guarded on NOT already having
        //    the .ts. infix to avoid duplication.
        if (work.EndsWith(EventGridHttpSuffix, StringComparison.OrdinalIgnoreCase)
            && !work.EndsWith(EventGridMqttSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = work.Substring(0, work.Length - EventGridHttpSuffix.Length);
            var rewritten = prefix + EventGridMqttSuffix;
            notes.Add($"rewrote Event Grid HTTP suffix to MQTT topic-space suffix ('{work}' → '{rewritten}')");
            work = rewritten;
        }

        return new NormalizedMqttHostname(work, extractedPort, notes);
    }
}
