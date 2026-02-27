using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AlwaysOnTopTranscriber.Core.Utilities;

public static class FilenameSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Sanitize(string? input, string fallback = "session")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input.Trim())
        {
            if (invalidChars.Contains(ch) || char.IsControl(ch))
            {
                builder.Append('_');
                continue;
            }

            builder.Append(ch);
        }

        var cleaned = builder
            .ToString()
            .Replace(' ', '_')
            .Trim('_', '.', ' ');

        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = fallback;
        }

        if (ReservedNames.Contains(cleaned))
        {
            cleaned = $"{cleaned}_file";
        }

        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
