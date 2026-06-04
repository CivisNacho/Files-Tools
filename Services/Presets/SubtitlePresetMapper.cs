using System;
using System.Linq;

namespace Files_Tools.Services.Presets;

/// <summary>
/// Maps a deserialized <see cref="SubtitlePresetDto"/> onto the domain types the renderer already
/// speaks: an immutable <see cref="SubtitleStylePreset"/> and a <see cref="SubtitleStyleCatalogEntry"/>.
/// This is the only place that bridges the JSON layer and the renderer, so the rest of the app is
/// unaware presets can come from disk.
/// </summary>
public static class SubtitlePresetMapper
{
    /// <summary>
    /// Builds an immutable <see cref="SubtitleStylePreset"/> from a preset DTO.
    /// </summary>
    /// <exception cref="ArgumentNullException">The DTO is null.</exception>
    /// <exception cref="InvalidOperationException">The DTO is missing a required field such as <c>id</c>.</exception>
    public static SubtitleStylePreset ToPreset(SubtitlePresetDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var id = NormalizeRequired(dto.Id, "id");

        return new SubtitleStylePreset
        {
            Name = id,
            AssStyleName = string.IsNullOrWhiteSpace(dto.AssStyleName) ? id : dto.AssStyleName!,
            ScriptTitle = dto.ScriptTitle,
            PlayResX = dto.PlayResX,
            PlayResY = dto.PlayResY,
            WrapStyle = dto.WrapStyle,
            ScaledBorderAndShadow = dto.ScaledBorderAndShadow,
            PrimaryFontFamily = dto.PrimaryFontFamily,
            FontFamilyFallbacks = dto.FontFamilyFallbacks.ToArray(),
            FontSize = dto.FontSize,
            Bold = dto.Bold,
            Italic = dto.Italic,
            TextTransform = dto.TextTransform,
            FillColor = dto.FillColor,
            OutlineColor = dto.OutlineColor,
            ShadowColor = dto.ShadowColor,
            KaraokeHighlightColor = dto.KaraokeHighlightColor,
            UseBackgroundBox = dto.UseBackgroundBox,
            OutlineWidth = dto.OutlineWidth,
            ShadowDepth = dto.ShadowDepth,
            Alignment = dto.Alignment,
            MarginLeft = dto.MarginLeft,
            MarginRight = dto.MarginRight,
            MarginVertical = dto.MarginVertical,
            PositionX = dto.PositionX,
            PositionY = dto.PositionY,
            MaxLines = dto.MaxLines,
            MaxCharsPerLine = dto.MaxCharsPerLine,
            MaxWordsPerChunk = dto.MaxWordsPerChunk,
            PresentationAnimation = dto.PresentationAnimation,
            EntryFadeMilliseconds = dto.EntryFadeMilliseconds,
            ExitFadeMilliseconds = dto.ExitFadeMilliseconds,
            IntroScale = dto.IntroScale,
            Effects = dto.Effects?
                .Select(effect => new SubtitleEffect(effect.Kind)
                {
                    DurationMs = effect.DurationMs,
                    Scale = effect.Scale
                })
                .ToArray()
        };
    }

    /// <summary>
    /// Builds a <see cref="SubtitleStyleCatalogEntry"/> from a preset DTO. The preset is built once
    /// up front (validating the DTO) and shared by the entry's factory, which is safe because
    /// <see cref="SubtitleStylePreset"/> is immutable.
    /// </summary>
    public static SubtitleStyleCatalogEntry ToCatalogEntry(SubtitlePresetDto dto)
    {
        var preset = ToPreset(dto);
        var displayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? preset.Name : dto.DisplayName!;
        return new SubtitleStyleCatalogEntry(preset.Name, displayName, dto.Kind, () => preset);
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Subtitle preset is missing required field '{fieldName}'.");
        }

        return value.Trim();
    }
}
