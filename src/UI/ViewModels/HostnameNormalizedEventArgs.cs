namespace CrowsNestMqtt.UI.ViewModels;

using System;
using System.Collections.Generic;

/// <summary>
/// Event payload for <see cref="SettingsViewModel.HostnameNormalized"/>.
/// Fires when the hostname setter transformed user input (stripped a scheme,
/// extracted a port, rewrote the Event Grid HTTP suffix, etc.).
/// </summary>
public sealed class HostnameNormalizedEventArgs : EventArgs
{
    public HostnameNormalizedEventArgs(string original, string cleaned, int? extractedPort, IReadOnlyList<string> notes)
    {
        Original = original;
        Cleaned = cleaned;
        ExtractedPort = extractedPort;
        Notes = notes;
    }

    /// <summary>The raw value the user assigned (before normalization).</summary>
    public string Original { get; }

    /// <summary>The cleaned hostname now stored on the ViewModel.</summary>
    public string Cleaned { get; }

    /// <summary>Port extracted from the input, or null when none was present.</summary>
    public int? ExtractedPort { get; }

    /// <summary>Human-readable descriptions of every transformation applied.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Compose a single-sentence status-bar summary.</summary>
    public string ToStatusMessage()
    {
        var parts = new List<string> { $"Corrected hostname to '{Cleaned}'" };
        if (ExtractedPort is int p)
        {
            parts.Add($"(port {p} extracted from input)");
        }
        return string.Join(' ', parts) + ".";
    }
}
