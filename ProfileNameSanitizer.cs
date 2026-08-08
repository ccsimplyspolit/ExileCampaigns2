using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExileCampaigns;

/// <summary>Pure, Windows-safe profile filename normalization.</summary>
public static class ProfileNameSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "_default";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        var value = sb.ToString().TrimEnd('.', ' ');
        if (value.Length > 80) value = value[..80].TrimEnd('.', ' ');
        if (value.Length == 0) return "_default";

        var stem = value;
        var dot = stem.IndexOf('.');
        if (dot >= 0) stem = stem[..dot];
        return ReservedNames.Contains(stem) ? "_" + value : value;
    }
}
