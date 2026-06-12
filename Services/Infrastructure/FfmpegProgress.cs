using System;
using System.Globalization;

namespace Files_Tools.Services.Infrastructure;

/// <summary>
/// Parses the ffmpeg progress-line format (<c>out_time=</c> / <c>out_time_ms=</c>) emitted when
/// ffmpeg is run with <c>-progress pipe:1</c> or <c>-progress pipe:2</c>.
/// </summary>
internal static class FfmpegProgress
{
    /// <summary>
    /// Attempts to parse a single ffmpeg progress key=value line and, if it carries time
    /// information, sets <paramref name="time"/> and returns <see langword="true"/>.
    /// </summary>
    /// <param name="line">A single line from the ffmpeg progress stream.</param>
    /// <param name="time">Parsed time when the method returns <see langword="true"/>.</param>
    public static bool TryParseTime(string line, out TimeSpan time)
    {
        if (line.StartsWith("out_time=", StringComparison.Ordinal))
        {
            time = ParseTimestamp(line["out_time=".Length..]);
            return true;
        }

        if (line.StartsWith("out_time_ms=", StringComparison.Ordinal) &&
            long.TryParse(line["out_time_ms=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var outTimeMs))
        {
            time = TimeSpan.FromMilliseconds(outTimeMs / 1000d);
            return true;
        }

        time = default;
        return false;
    }

    /// <summary>
    /// Parses an ffmpeg progress timestamp string in <c>hh:mm:ss.ffffff</c> or similar formats.
    /// Returns <see cref="TimeSpan.Zero"/> when the value cannot be parsed.
    /// </summary>
    public static TimeSpan ParseTimestamp(string value)
    {
        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture, out var precise))
        {
            return precise;
        }

        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var centiseconds))
        {
            return centiseconds;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;
    }
}
