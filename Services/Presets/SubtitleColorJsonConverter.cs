using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Files_Tools.Services.Presets;

/// <summary>
/// (De)serializes <see cref="SubtitleColor"/> as an <c>"#AARRGGBB"</c> string.
///
/// Alpha follows the ASS / domain convention used throughout <see cref="SubtitlesService"/>:
/// <c>00</c> = fully opaque, <c>FF</c> = fully transparent. This is the single place that
/// documents and enforces that convention for preset files.
/// </summary>
public sealed class SubtitleColorJsonConverter : JsonConverter<SubtitleColor>
{
    public override SubtitleColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Subtitle color must be a string.");
        var hex = value.StartsWith('#') ? value[1..] : value;
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            throw new JsonException($"Subtitle color '{value}' must be in #AARRGGBB form.");
        }

        return new SubtitleColor(
            (byte)((packed >> 24) & 0xFF),
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF));
    }

    public override void Write(Utf8JsonWriter writer, SubtitleColor value, JsonSerializerOptions options)
    {
        writer.WriteStringValue($"#{value.Alpha:X2}{value.Red:X2}{value.Green:X2}{value.Blue:X2}");
    }
}
