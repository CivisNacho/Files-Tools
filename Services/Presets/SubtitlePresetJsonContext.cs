using System.Text.Json;
using System.Text.Json.Serialization;

namespace Files_Tools.Services.Presets;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for subtitle preset files. Using source
/// generation (rather than reflection-based serialization) keeps preset loading fast and trim/AOT
/// safe, which matters because the app publishes trimmed in non-Debug configurations.
///
/// Enum-typed properties serialize by name via per-property
/// <see cref="JsonStringEnumConverter"/> attributes on the DTO, so preset files are human-readable;
/// this makes enum member names part of the preset file contract.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SubtitlePresetDto))]
public partial class SubtitlePresetJsonContext : JsonSerializerContext
{
}
