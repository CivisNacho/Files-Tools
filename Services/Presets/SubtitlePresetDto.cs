using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Files_Tools.Services.Presets;

/// <summary>
/// JSON shape of a subtitle preset file under <c>Assets/Presets/</c> (built-in) or the user preset
/// folder. It is deserialized and then mapped onto the domain <see cref="SubtitleStylePreset"/> plus
/// a <see cref="SubtitleStyleCatalogEntry"/> by <see cref="SubtitlePresetMapper"/>.
///
/// Every default mirrors <see cref="SubtitleStylePreset"/>'s own defaults, so a minimal file that
/// only sets <c>id</c> and a handful of fields still deserializes into a sensible preset.
/// </summary>
public sealed class SubtitlePresetDto
{
    /// <summary>Schema version of this file, so the loader can migrate or reject old shapes later.</summary>
    public int SchemaVersion { get; set; } = 1;

    // ---- Catalog metadata ----

    /// <summary>Stable id used as the catalog key and the preset <see cref="SubtitleStylePreset.Name"/>.</summary>
    public string? Id { get; set; }

    /// <summary>Human-facing name shown in pickers. Falls back to <see cref="Id"/> when omitted.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Whether this style produces whole-line (Styled) or per-word (Karaoke) output.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubtitleStyleKind Kind { get; set; } = SubtitleStyleKind.Styled;

    // ---- Script / canvas ----

    /// <summary>ASS style name. Falls back to <see cref="Id"/> when omitted.</summary>
    public string? AssStyleName { get; set; }

    public string ScriptTitle { get; set; } = "Styled subtitles";

    public int PlayResX { get; set; } = 1920;

    public int PlayResY { get; set; } = 1080;

    public int WrapStyle { get; set; }

    public bool ScaledBorderAndShadow { get; set; } = true;

    // ---- Typography ----

    public string PrimaryFontFamily { get; set; } = "Segoe UI";

    public List<string> FontFamilyFallbacks { get; set; } = new();

    public double FontSize { get; set; } = 72;

    public bool Bold { get; set; } = true;

    public bool Italic { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubtitleTextTransform TextTransform { get; set; } = SubtitleTextTransform.Uppercase;

    // ---- Colors (#AARRGGBB; see SubtitleColorJsonConverter for ASS alpha semantics) ----

    [JsonConverter(typeof(SubtitleColorJsonConverter))]
    public SubtitleColor FillColor { get; set; } = SubtitleColor.White;

    [JsonConverter(typeof(SubtitleColorJsonConverter))]
    public SubtitleColor OutlineColor { get; set; } = SubtitleColor.Black;

    [JsonConverter(typeof(SubtitleColorJsonConverter))]
    public SubtitleColor ShadowColor { get; set; } = SubtitleColor.Black;

    [JsonConverter(typeof(SubtitleColorJsonConverter))]
    public SubtitleColor KaraokeHighlightColor { get; set; } = new(0, 255, 110, 0);

    // ---- Box / stroke ----

    public bool UseBackgroundBox { get; set; }

    public double OutlineWidth { get; set; } = 5;

    public double ShadowDepth { get; set; }

    // ---- Layout ----

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubtitleVisualAlignment Alignment { get; set; } = SubtitleVisualAlignment.BottomCenter;

    public int MarginLeft { get; set; } = 80;

    public int MarginRight { get; set; } = 80;

    public int MarginVertical { get; set; } = 90;

    public int? PositionX { get; set; }

    public int? PositionY { get; set; }

    public int MaxLines { get; set; } = 2;

    public int MaxCharsPerLine { get; set; } = 28;

    public int? MaxWordsPerChunk { get; set; }

    // ---- Animation ----

    /// <summary>
    /// Composable effects. When set, these drive rendering directly (preferred). When null, the
    /// renderer derives an equivalent list from the legacy fields below, so old files keep working.
    /// </summary>
    public List<SubtitleEffectDto>? Effects { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubtitlePresentationAnimation PresentationAnimation { get; set; } = SubtitlePresentationAnimation.None;

    public int EntryFadeMilliseconds { get; set; }

    public int ExitFadeMilliseconds { get; set; }

    public double IntroScale { get; set; } = 1d;
}

/// <summary>
/// JSON shape of a single composable animation effect, mirroring <see cref="SubtitleEffect"/>.
/// Unused parameters keep their defaults.
/// </summary>
public sealed class SubtitleEffectDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SubtitleEffectKind Kind { get; set; }

    /// <summary>Duration in milliseconds, used by fade effects.</summary>
    public int DurationMs { get; set; }

    /// <summary>Starting scale factor (e.g. 1.12 = 112%), used by pop effects.</summary>
    public double Scale { get; set; } = 1d;
}
