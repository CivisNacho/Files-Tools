using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services;

/// <summary>
/// Raw transcription draft produced before subtitle postprocessing.
/// </summary>
public sealed class TranscriptionDraft
{
    public TranscriptionDraft(IReadOnlyList<TranscriptionSegment> segments)
    {
        Segments = segments ?? throw new ArgumentNullException(nameof(segments));
    }

    public IReadOnlyList<TranscriptionSegment> Segments { get; }
}

/// <summary>
/// Single editable transcription segment from Whisper.
/// </summary>
public sealed record TranscriptionSegment(int Id, TimeSpan Start, TimeSpan End, string Text)
{
    /// <summary>
    /// Real word-level timings carried from transcription, when available. Used to drive
    /// accurate karaoke highlighting; ignored once the segment text is edited.
    /// </summary>
    public IReadOnlyList<AudioTranscriptionWord>? Words { get; init; }
}

/// <summary>
/// User-authored text corrections to apply to a transcription segment.
/// </summary>
public sealed record TranscriptionSegmentCorrection(int SegmentId, string? Text);

/// <summary>
/// Structured subtitle draft produced by the advanced postprocessing pipeline.
/// </summary>
public sealed class SubtitleDraft
{
    public SubtitleDraft(IReadOnlyList<SubtitleCue> cues, SubtitlePostprocessingOptions options, IReadOnlyList<SubtitleValidationIssue> issues)
    {
        Cues = cues ?? throw new ArgumentNullException(nameof(cues));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    public IReadOnlyList<SubtitleCue> Cues { get; }

    public SubtitlePostprocessingOptions Options { get; }

    public IReadOnlyList<SubtitleValidationIssue> Issues { get; }

    /// <summary>
    /// Real word-level timings from the source transcription, preserved so karaoke rendering
    /// can map each cue back to accurate per-word timing. Null when no word timing is available.
    /// </summary>
    public IReadOnlyList<AudioTranscriptionWord>? SourceWords { get; init; }
}

/// <summary>
/// Styled subtitle draft ready for output-format rendering such as ASS.
/// </summary>
public sealed class StyledSubtitleDraft
{
    public StyledSubtitleDraft(IReadOnlyList<SubtitleCue> cues, SubtitleStylePreset preset, IReadOnlyList<SubtitleValidationIssue> issues)
    {
        Cues = cues ?? throw new ArgumentNullException(nameof(cues));
        Preset = preset ?? throw new ArgumentNullException(nameof(preset));
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    public IReadOnlyList<SubtitleCue> Cues { get; }

    public SubtitleStylePreset Preset { get; }

    public IReadOnlyList<SubtitleValidationIssue> Issues { get; }
}

/// <summary>
/// Single editable subtitle cue.
/// </summary>
public sealed record SubtitleCue(int Id, TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// User-authored edits to apply to a generated subtitle cue.
/// </summary>
public sealed record SubtitleSegmentCorrection(int CueId, string? Text, TimeSpan? Start, TimeSpan? End);

/// <summary>
/// Readability and timing thresholds used during subtitle postprocessing.
/// </summary>
public sealed class SubtitlePostprocessingOptions
{
    public TimeSpan MinimumDuration { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan IdealDurationMin { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan IdealDurationMax { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromSeconds(6.5);

    public int? MaxWordsPerSection { get; init; }

    public int MaxCharsPerLine { get; init; } = 32;

    public int MaxLines { get; init; } = 2;

    public double GoodCpsMax { get; init; } = 17;

    public double AcceptableCpsMax { get; init; } = 21;

    public TimeSpan CloseGapBelow { get; init; } = TimeSpan.FromMilliseconds(80);

    public TimeSpan IntentionalPauseAtOrAbove { get; init; } = TimeSpan.FromMilliseconds(700);
}

/// <summary>
/// Text transform used when rendering a subtitle preset.
/// </summary>
public enum SubtitleTextTransform
{
    None,
    Uppercase,
    Lowercase
}

/// <summary>
/// Visual alignment for subtitle rendering.
/// </summary>
public enum SubtitleVisualAlignment
{
    BottomLeft,
    BottomCenter,
    BottomRight,
    MiddleLeft,
    Center,
    MiddleRight,
    TopLeft,
    TopCenter,
    TopRight
}

/// <summary>
/// Simple BGRA color payload aligned with ASS color needs.
/// </summary>
public sealed record SubtitleColor(byte Alpha, byte Red, byte Green, byte Blue)
{
    public static SubtitleColor White { get; } = new(0, 255, 255, 255);

    public static SubtitleColor Black { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Visual preset metadata for styled subtitles.
/// </summary>
public sealed class SubtitleStylePreset
{
    public required string Name { get; init; }

    public required string AssStyleName { get; init; }

    public required string ScriptTitle { get; init; }

    public int PlayResX { get; init; } = 1920;

    public int PlayResY { get; init; } = 1080;

    public int WrapStyle { get; init; } = 0;

    public bool ScaledBorderAndShadow { get; init; } = true;

    public required string PrimaryFontFamily { get; init; }

    public IReadOnlyList<string> FontFamilyFallbacks { get; init; } = Array.Empty<string>();

    public double FontSize { get; init; } = 72;

    public bool Bold { get; init; } = true;

    public bool Italic { get; init; }

    public SubtitleTextTransform TextTransform { get; init; } = SubtitleTextTransform.Uppercase;

    public SubtitleColor FillColor { get; init; } = SubtitleColor.White;

    public SubtitleColor OutlineColor { get; init; } = SubtitleColor.Black;

    public SubtitleColor ShadowColor { get; init; } = SubtitleColor.Black;

    public SubtitleColor KaraokeHighlightColor { get; init; } = new(0, 255, 110, 0);

    public bool UseBackgroundBox { get; init; }

    public SubtitlePresentationAnimation PresentationAnimation { get; init; } = SubtitlePresentationAnimation.None;

    public int EntryFadeMilliseconds { get; init; }

    public int ExitFadeMilliseconds { get; init; }

    public double IntroScale { get; init; } = 1d;

    /// <summary>
    /// Composable animation effects. When set, these drive rendering directly; when null the
    /// renderer derives an equivalent effect list from <see cref="PresentationAnimation"/> and the
    /// fade/scale fields, so existing presets keep working unchanged.
    /// </summary>
    public IReadOnlyList<SubtitleEffect>? Effects { get; init; }

    public double OutlineWidth { get; init; } = 5;

    public double ShadowDepth { get; init; }

    public SubtitleVisualAlignment Alignment { get; init; } = SubtitleVisualAlignment.BottomCenter;

    public int MarginLeft { get; init; } = 80;

    public int MarginRight { get; init; } = 80;

    public int MarginVertical { get; init; } = 90;

    public int? PositionX { get; init; }

    public int? PositionY { get; init; }

    public int MaxLines { get; init; } = 2;

    public int MaxCharsPerLine { get; init; } = 28;

    /// <summary>
    /// When set, karaoke rendering shows at most this many words on screen at once (the
    /// autosubtitles-style "chunked" look) and emits one dialogue event per word so the active
    /// word can be emphasised individually. Null keeps the classic full-line karaoke rendering.
    /// </summary>
    public int? MaxWordsPerChunk { get; init; }
}

/// <summary>
/// Small animation profile used when rendering ASS subtitle cues.
/// </summary>
public enum SubtitlePresentationAnimation
{
    None,
    Fade,
    Pop,
    FadePop,
    DropIn
}

/// <summary>
/// A single composable subtitle animation effect. New visual styles are expressed as a list of
/// these, so adding a look means describing effects rather than editing the renderer.
/// </summary>
public enum SubtitleEffectKind
{
    /// <summary>Fade the line in over <see cref="SubtitleEffect.DurationMs"/>.</summary>
    EntryFade,

    /// <summary>Fade the line out over <see cref="SubtitleEffect.DurationMs"/>.</summary>
    ExitFade,

    /// <summary>Scale the whole line down to 100% from <see cref="SubtitleEffect.Scale"/> on entry.</summary>
    EntryPop,

    /// <summary>Karaoke fill: each word sweeps from base to highlight colour (<c>\kf</c>).</summary>
    KaraokeColorSweep,

    /// <summary>Karaoke fill: each word switches to the highlight colour instantly (<c>\k</c>).</summary>
    KaraokeColorInstant,

    /// <summary>Karaoke fill: words appear one by one from transparent (drop-in).</summary>
    DropIn,

    /// <summary>
    /// Chunked karaoke: the active word scales from <see cref="SubtitleEffect.Scale"/> down to 100%
    /// as it becomes active. Rendered as one dialogue event per word.
    /// </summary>
    ActiveWordPop,

    /// <summary>
    /// Entry glow: the whole line starts blurred and sharpens over <see cref="SubtitleEffect.DurationMs"/>.
    /// Blur radius is set by <see cref="SubtitleEffect.Scale"/> (e.g. 8 = heavy glow, 4 = soft).
    /// </summary>
    KaraokeGlowBurst,

    /// <summary>
    /// Entry outline flash: the outline colour starts at the karaoke highlight colour and fades to
    /// the base outline colour over <see cref="SubtitleEffect.DurationMs"/>. Creates a vivid border
    /// pulse each time a new cue appears.
    /// </summary>
    KaraokeOutlineFlash
}

/// <summary>
/// A composable animation effect with its parameters. Unused parameters keep their defaults.
/// </summary>
public sealed record SubtitleEffect(SubtitleEffectKind Kind)
{
    /// <summary>Duration in milliseconds, used by fade effects.</summary>
    public int DurationMs { get; init; }

    /// <summary>Starting scale factor (e.g. 1.12 = 112%), used by pop effects.</summary>
    public double Scale { get; init; } = 1d;
}

/// <summary>
/// Convenience factories for <see cref="SubtitleEffect"/> values.
/// </summary>
public static class SubtitleEffects
{
    public static SubtitleEffect EntryFade(int durationMs) => new(SubtitleEffectKind.EntryFade) { DurationMs = durationMs };

    public static SubtitleEffect ExitFade(int durationMs) => new(SubtitleEffectKind.ExitFade) { DurationMs = durationMs };

    public static SubtitleEffect EntryPop(double scale) => new(SubtitleEffectKind.EntryPop) { Scale = scale };

    public static SubtitleEffect KaraokeColorSweep() => new(SubtitleEffectKind.KaraokeColorSweep);

    public static SubtitleEffect KaraokeColorInstant() => new(SubtitleEffectKind.KaraokeColorInstant);

    public static SubtitleEffect DropIn() => new(SubtitleEffectKind.DropIn);

    public static SubtitleEffect ActiveWordPop(double scale) => new(SubtitleEffectKind.ActiveWordPop) { Scale = scale };

    /// <summary>
    /// Entry glow: the line starts blurred (<paramref name="blurRadius"/>) and sharpens over
    /// <paramref name="durationMs"/> milliseconds.
    /// </summary>
    public static SubtitleEffect KaraokeGlowBurst(int durationMs = 240, double blurRadius = 8d) =>
        new(SubtitleEffectKind.KaraokeGlowBurst) { DurationMs = durationMs, Scale = blurRadius };

    /// <summary>
    /// Entry outline flash: the outline starts at the karaoke highlight colour and fades to the
    /// base outline colour over <paramref name="durationMs"/> milliseconds.
    /// </summary>
    public static SubtitleEffect KaraokeOutlineFlash(int durationMs = 280) =>
        new(SubtitleEffectKind.KaraokeOutlineFlash) { DurationMs = durationMs };
}

/// <summary>
/// Karaoke fill behaviour resolved from a preset's effects.
/// </summary>
internal enum KaraokeFill
{
    Sweep,
    Instant,
    DropIn
}

/// <summary>
/// Built-in styled ASS subtitle preset. A single base style is exposed; the page layer lets the
/// user customise font, size, outline, margin, weight, transform, fill colour, and outline colour
/// on top of it — no additional presets are needed in the catalog.
/// </summary>
public static class StyledSubtitlePresets
{
    public static SubtitleStylePreset SocialImpact => CreateSocialImpact();

    public static SubtitleStylePreset CreateSocialImpact()
    {
        return new SubtitleStylePreset
        {
            Name = "SocialImpact",
            AssStyleName = "SocialImpact",
            ScriptTitle = "Styled subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Impact",
            FontFamilyFallbacks = ["Impact", "Anton", "Bebas Neue", "Arial Black"],
            FontSize = 86,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.Uppercase,
            FillColor = SubtitleColor.White,
            OutlineColor = SubtitleColor.Black,
            ShadowColor = new SubtitleColor(100, 0, 0, 0),
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.FadePop,
            EntryFadeMilliseconds = 120,
            ExitFadeMilliseconds = 120,
            IntroScale = 1.12d,
            OutlineWidth = 8,
            ShadowDepth = 2,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 100,
            MarginRight = 100,
            MarginVertical = 120,
            MaxLines = 2,
            MaxCharsPerLine = 26
        };
    }

}

/// <summary>
/// Built-in karaoke subtitle presets.
/// </summary>
public static class KaraokeSubtitlePresets
{
    public static SubtitleStylePreset Punch => CreatePunch();

    public static SubtitleStylePreset CreatePunch()
    {
        return new SubtitleStylePreset
        {
            Name = "Punch",
            AssStyleName = "Punch",
            ScriptTitle = "Karaoke subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Arial Black",
            FontFamilyFallbacks = ["Arial Black", "Impact", "Arial"],
            FontSize = 84,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.None,
            FillColor = SubtitleColor.White,
            OutlineColor = new SubtitleColor(0, 64, 64, 64),
            ShadowColor = new SubtitleColor(200, 0, 0, 0),
            KaraokeHighlightColor = new SubtitleColor(0, 255, 130, 0),
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.None,
            // Add a per-cue entry pop while keeping the instant per-word fill (\k, not \kf):
            // KaraokeColorInstant pins the fill mode, EntryPop supplies the scale punch.
            Effects =
            [
                SubtitleEffects.EntryPop(1.12d),
                SubtitleEffects.KaraokeColorInstant()
            ],
            EntryFadeMilliseconds = 0,
            ExitFadeMilliseconds = 0,
            IntroScale = 1d,
            OutlineWidth = 12,
            ShadowDepth = 2,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 100,
            MarginRight = 100,
            MarginVertical = 120,
            MaxLines = 2,
            MaxCharsPerLine = 26
        };
    }

    public static SubtitleStylePreset GlowKaraoke => CreateGlowKaraoke();

    /// <summary>
    /// Soft entry glow + per-word colour sweep. Inspired by classic Aegisub colour-transition
    /// templates: the whole line blurs in sharp on entry while each word sweeps from white to
    /// electric cyan, giving a neon-glow feel without per-character positioning.
    /// </summary>
    public static SubtitleStylePreset CreateGlowKaraoke()
    {
        return new SubtitleStylePreset
        {
            Name = "GlowKaraoke",
            AssStyleName = "GlowKaraoke",
            ScriptTitle = "Karaoke subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Segoe UI Semibold",
            FontFamilyFallbacks = ["Segoe UI Semibold", "Segoe UI", "Arial"],
            FontSize = 68,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.None,
            // White = "not yet sung"; electric cyan = "currently sweeping through".
            FillColor = SubtitleColor.White,
            OutlineColor = new SubtitleColor(0, 20, 40, 90),   // dark navy for contrast
            ShadowColor = new SubtitleColor(160, 0, 0, 0),
            KaraokeHighlightColor = new SubtitleColor(0, 0, 210, 255), // electric cyan
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.None,
            Effects =
            [
                SubtitleEffects.EntryFade(80),
                SubtitleEffects.ExitFade(80),
                SubtitleEffects.KaraokeGlowBurst(240, 8d),
                SubtitleEffects.KaraokeColorSweep()
            ],
            EntryFadeMilliseconds = 80,
            ExitFadeMilliseconds = 80,
            IntroScale = 1d,
            OutlineWidth = 10,
            ShadowDepth = 1,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 100,
            MarginRight = 100,
            MarginVertical = 120,
            MaxLines = 2,
            MaxCharsPerLine = 28
        };
    }

    public static SubtitleStylePreset WordPop => CreateWordPop();

    /// <summary>
    /// Autosubtitles-style chunked karaoke: a few big, centered words on screen at a time, with the
    /// active word popping in a vivid highlight colour. Rendered one event per word.
    /// </summary>
    public static SubtitleStylePreset CreateWordPop()
    {
        return new SubtitleStylePreset
        {
            Name = "WordPop",
            AssStyleName = "WordPop",
            ScriptTitle = "Karaoke subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 2,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Montserrat",
            FontFamilyFallbacks = ["Montserrat", "Arial Black", "Segoe UI Black", "Arial"],
            FontSize = 92,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.Uppercase,
            FillColor = SubtitleColor.White,
            OutlineColor = SubtitleColor.Black,
            ShadowColor = new SubtitleColor(160, 0, 0, 0),
            KaraokeHighlightColor = new SubtitleColor(0, 255, 216, 0),
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.None,
            Effects =
            [
                SubtitleEffects.EntryFade(60),
                SubtitleEffects.ActiveWordPop(1.18d)
            ],
            EntryFadeMilliseconds = 60,
            ExitFadeMilliseconds = 0,
            IntroScale = 1d,
            OutlineWidth = 8,
            ShadowDepth = 1.5,
            Alignment = SubtitleVisualAlignment.Center,
            MarginLeft = 120,
            MarginRight = 120,
            MarginVertical = 0,
            MaxLines = 1,
            MaxCharsPerLine = 22,
            MaxWordsPerChunk = 3
        };
    }
}

/// <summary>
/// Whether a catalog entry produces styled (whole-line) or karaoke (per-word) subtitles.
/// </summary>
public enum SubtitleStyleKind
{
    Styled,
    Karaoke
}

/// <summary>
/// A single selectable subtitle style, with the metadata a UI needs to list it and a factory
/// that builds its <see cref="SubtitleStylePreset"/>.
/// </summary>
public sealed record SubtitleStyleCatalogEntry(string Id, string DisplayName, SubtitleStyleKind Kind, Func<SubtitleStylePreset> Factory);

/// <summary>
/// Single registry of every built-in subtitle style. Adding a new look means adding one entry
/// here (and a factory) — the renderer needs no changes. A UI can enumerate this to populate a
/// picker instead of hard-coding names.
/// </summary>
public static class SubtitleStyleCatalog
{
    private static readonly SubtitleStyleCatalogEntry[] BuiltInEntries =
    [
        // Styled presets are intentionally absent from the catalog: the page layer exposes a single
        // customisable styled option (based on SocialImpact) rather than a preset picker, so there
        // is nothing to enumerate here. The StyledSubtitlePresets factory methods still exist and
        // are called directly wherever a base style is needed.
        new("Punch", "Punch", SubtitleStyleKind.Karaoke, KaraokeSubtitlePresets.CreatePunch),
        new("WordPop", "Word Pop", SubtitleStyleKind.Karaoke, KaraokeSubtitlePresets.CreateWordPop),
        new("GlowKaraoke", "Glow", SubtitleStyleKind.Karaoke, KaraokeSubtitlePresets.CreateGlowKaraoke)
    ];

    private static readonly object SyncRoot = new();

    // Presets registered at runtime (typically loaded from JSON by SubtitlePresetLoader). Keyed by
    // id; a registered entry overrides a built-in with the same id, keeping the built-in's position.
    private static readonly Dictionary<string, SubtitleStyleCatalogEntry> RegisteredById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> RegisteredOrder = new();

    /// <summary>
    /// Registers additional styles (e.g. loaded from JSON preset files), merging them over the
    /// built-ins by id. Registering an id that matches a built-in replaces it in place; new ids are
    /// appended after the built-ins in registration order. Safe to call more than once.
    /// </summary>
    public static void RegisterPresets(IEnumerable<SubtitleStyleCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (SyncRoot)
        {
            foreach (var entry in entries)
            {
                if (entry is null)
                {
                    continue;
                }

                if (!RegisteredById.ContainsKey(entry.Id))
                {
                    RegisteredOrder.Add(entry.Id);
                }

                RegisteredById[entry.Id] = entry;
            }
        }
    }

    /// <summary>Removes all runtime-registered presets, leaving only the built-ins. Intended for tests.</summary>
    public static void ResetRegistrations()
    {
        lock (SyncRoot)
        {
            RegisteredById.Clear();
            RegisteredOrder.Clear();
        }
    }

    /// <summary>All registered styles, in display order: built-ins first, then user-only presets.</summary>
    public static IReadOnlyList<SubtitleStyleCatalogEntry> Entries
    {
        get
        {
            lock (SyncRoot)
            {
                if (RegisteredById.Count == 0)
                {
                    return BuiltInEntries;
                }

                var merged = new List<SubtitleStyleCatalogEntry>(BuiltInEntries.Length + RegisteredOrder.Count);
                var emittedBuiltInOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Built-ins keep their order; replaced in place when an override shares their id.
                foreach (var builtIn in BuiltInEntries)
                {
                    if (RegisteredById.TryGetValue(builtIn.Id, out var overrideEntry))
                    {
                        merged.Add(overrideEntry);
                        emittedBuiltInOverrides.Add(builtIn.Id);
                    }
                    else
                    {
                        merged.Add(builtIn);
                    }
                }

                // Append registered presets that did not override a built-in, in registration order.
                foreach (var id in RegisteredOrder)
                {
                    if (!emittedBuiltInOverrides.Contains(id))
                    {
                        merged.Add(RegisteredById[id]);
                    }
                }

                return merged;
            }
        }
    }

    /// <summary>Registered styles of a given kind, in display order.</summary>
    public static IEnumerable<SubtitleStyleCatalogEntry> ByKind(SubtitleStyleKind kind)
    {
        return Entries.Where(entry => entry.Kind == kind);
    }

    /// <summary>Finds an entry by id, or null when it is not registered.</summary>
    public static SubtitleStyleCatalogEntry? Find(string id)
    {
        return Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Builds a preset by id, throwing when the id is unknown.</summary>
    public static SubtitleStylePreset Create(string id)
    {
        var entry = Find(id) ?? throw new ArgumentException($"Unknown subtitle style id '{id}'.", nameof(id));
        return entry.Factory();
    }
}

/// <summary>
/// Validation severity for postprocessed subtitle cues.
/// </summary>
public enum SubtitleValidationSeverity
{
    Warning,
    Error
}

/// <summary>
/// Validation issue raised while checking a subtitle draft.
/// </summary>
public sealed record SubtitleValidationIssue(int CueId, SubtitleValidationSeverity Severity, string Code, string Message);

/// <summary>
/// Actual target video resolution for subtitle rendering. When supplied to the chunked karaoke
/// renderer, the ASS is written in this coordinate space and the chunked "viral" word style is sized
/// to fit the real frame width, so it adapts to any aspect ratio instead of overflowing a non-16:9
/// frame. Ignored by the other styles, which keep their fixed design-space layout.
/// </summary>
public sealed record SubtitleRenderTarget(int Width, int Height);

/// <summary>
/// Generates subtitle files from timestamped audio transcription segments.
/// </summary>
public interface ISubtitlesService
{
    /// <summary>
    /// Transcribes supported audio or video input into an SRT subtitle file.
    /// </summary>
    Task<string> GenerateSrtAsync(string inputPath, string outputSubtitlePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into an SRT subtitle file and reports progress.
    /// </summary>
    Task<string> GenerateSrtAsync(string inputPath, string outputSubtitlePath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into a raw transcription draft for user review before subtitle postprocessing.
    /// </summary>
    Task<TranscriptionDraft> GenerateAdvancedTranscriptionDraftAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into a structured subtitle draft using the advanced postprocessing pipeline.
    /// </summary>
    Task<SubtitleDraft> GenerateAdvancedDraftAsync(string inputPath, SubtitlePostprocessingOptions? options = null, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into a karaoke-style ASS subtitle file.
    /// </summary>
    Task<string> GenerateKaraokeAssAsync(string inputPath, string outputSubtitlePath, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into a styled ASS subtitle file.
    /// </summary>
    Task<string> GenerateStyledAssAsync(string inputPath, string outputSubtitlePath, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a processed subtitle draft from a reviewed transcription draft.
    /// </summary>
    SubtitleDraft BuildSubtitleDraftFromTranscription(TranscriptionDraft draft, IReadOnlyList<TranscriptionSegmentCorrection> corrections, SubtitlePostprocessingOptions? options = null);

    /// <summary>
    /// Applies user corrections to an existing subtitle draft.
    /// </summary>
    SubtitleDraft ApplyCorrections(SubtitleDraft draft, IReadOnlyList<SubtitleSegmentCorrection> corrections, SubtitlePostprocessingOptions? options = null);

    /// <summary>
    /// Applies a visual subtitle preset to a processed subtitle draft.
    /// </summary>
    StyledSubtitleDraft ApplyStylePreset(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null);

    /// <summary>
    /// Renders a processed subtitle draft to styled ASS text.
    /// </summary>
    string RenderStyledAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null);

    /// <summary>
    /// Renders a reviewed transcription draft to karaoke ASS text.
    /// </summary>
    string RenderKaraokeAss(TranscriptionDraft draft, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null);

    /// <summary>
    /// Renders a processed subtitle draft to karaoke ASS text while preserving reviewed cue boundaries.
    /// </summary>
    string RenderKaraokeAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null);

    /// <summary>
    /// Rewrites a preset into the given target frame's coordinate space (the same font/margin scaling
    /// the renderers apply), so a live preview can size text exactly like the burned output — including
    /// the chunked karaoke fit-to-frame clamp. Returns the preset unchanged when target is null/equal.
    /// </summary>
    SubtitleStylePreset ApplyRenderTarget(SubtitleStylePreset preset, SubtitleRenderTarget? target);
}

/// <summary>
/// Generates plain SRT subtitles and advanced editable drafts using the audio transcription service.
/// </summary>
public sealed partial class SubtitlesService : ISubtitlesService
{
    private static readonly TimeSpan MinimumPositiveDuration = TimeSpan.FromMilliseconds(1);

    private readonly IAudioTranscriptionService _audioTranscriptionService;

    public SubtitlesService()
        : this(new AudioTranscriptionService())
    {
    }

    internal SubtitlesService(IAudioTranscriptionService audioTranscriptionService)
    {
        _audioTranscriptionService = audioTranscriptionService ?? throw new ArgumentNullException(nameof(audioTranscriptionService));
    }

    /// <inheritdoc />
    public async Task<string> GenerateSrtAsync(string inputPath, string outputSubtitlePath, CancellationToken cancellationToken = default)
    {
        return await GenerateSrtAsync(inputPath, outputSubtitlePath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GenerateSrtAsync(string inputPath, string outputSubtitlePath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        var finalOutputPath = PrepareOutputPath(outputSubtitlePath, EnsureSubtitleExtension);

        var progressState = new ProgressState();
        var segments = await _audioTranscriptionService.TranscribeToSegmentsAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 0d, "Writing subtitle file");

        await WriteSubtitleFileAsync(finalOutputPath, BuildSrt(segments), cancellationToken).ConfigureAwait(false);

        Report(progress, progressState, AudioTranscriptionStage.Completed, 1d, "Subtitle generation complete");
        return finalOutputPath;
    }

    /// <inheritdoc />
    public async Task<TranscriptionDraft> GenerateAdvancedTranscriptionDraftAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var progressState = new ProgressState();
        var segments = await _audioTranscriptionService.TranscribeToSegmentsAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);

        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 0d, "Preparing transcription review");
        var draft = BuildTranscriptionDraft(segments);
        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 1d, "Transcription review ready");
        Report(progress, progressState, AudioTranscriptionStage.Completed, 1d, "Subtitle generation complete");
        return draft;
    }

    /// <inheritdoc />
    public async Task<SubtitleDraft> GenerateAdvancedDraftAsync(string inputPath, SubtitlePostprocessingOptions? options = null, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var effectiveOptions = NormalizeOptions(options);
        var transcriptionDraft = await GenerateAdvancedTranscriptionDraftAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        return BuildSubtitleDraftFromTranscription(transcriptionDraft, Array.Empty<TranscriptionSegmentCorrection>(), effectiveOptions);
    }

    /// <inheritdoc />
    public async Task<string> GenerateKaraokeAssAsync(string inputPath, string outputSubtitlePath, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var finalOutputPath = PrepareOutputPath(outputSubtitlePath, EnsureAssExtension);

        var effectiveOptions = NormalizeOptions(options);
        var progressState = new ProgressState();
        var segments = await _audioTranscriptionService.TranscribeToSegmentsAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);

        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 0d, "Building karaoke subtitle file");
        var words = BuildWordsFromSegments(segments);
        var cues = BuildKaraokeCues(words, effectiveOptions);
        var ass = BuildKaraokeAss(cues, CreateDefaultKaraokePreset(preset, placement));
        await WriteSubtitleFileAsync(finalOutputPath, ass, cancellationToken).ConfigureAwait(false);

        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 1d, "Karaoke subtitle file ready");
        Report(progress, progressState, AudioTranscriptionStage.Completed, 1d, "Subtitle generation complete");
        return finalOutputPath;
    }

    /// <summary>
    /// Transcribes supported audio or video input into a styled ASS subtitle file.
    /// </summary>
    public async Task<string> GenerateStyledAssAsync(string inputPath, string outputSubtitlePath, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var finalOutputPath = PrepareOutputPath(outputSubtitlePath, EnsureAssExtension);

        var transcriptionDraft = await GenerateAdvancedTranscriptionDraftAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var draft = BuildSubtitleDraftFromTranscription(transcriptionDraft, Array.Empty<TranscriptionSegmentCorrection>(), options);
        var ass = RenderStyledAss(draft, preset, placement);
        await WriteSubtitleFileAsync(finalOutputPath, ass, cancellationToken).ConfigureAwait(false);
        return finalOutputPath;
    }

    /// <inheritdoc />
    public SubtitleDraft BuildSubtitleDraftFromTranscription(TranscriptionDraft draft, IReadOnlyList<TranscriptionSegmentCorrection> corrections, SubtitlePostprocessingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(corrections);

        var effectiveOptions = NormalizeOptions(options);
        var segmentIndexById = draft.Segments
            .Select((segment, index) => (segment.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        var segments = draft.Segments
            .Select(segment => new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text)
            {
                Words = WordsMatchText(segment.Words, segment.Text) ? segment.Words : null
            })
            .ToList();

        foreach (var correction in corrections)
        {
            if (!segmentIndexById.TryGetValue(correction.SegmentId, out var segmentIndex))
            {
                throw new ArgumentException($"Correction references unknown transcription segment id {correction.SegmentId}.", nameof(corrections));
            }

            if (correction.Text is not null)
            {
                var existing = segments[segmentIndex];
                // The edited text no longer matches the original word timing, so drop it and
                // let the pipeline synthesize word timing from the cue envelope instead.
                segments[segmentIndex] = existing with { Text = correction.Text, Words = null };
            }
        }

        return BuildAdvancedDraft(segments, effectiveOptions);
    }

    /// <inheritdoc />
    public SubtitleDraft ApplyCorrections(SubtitleDraft draft, IReadOnlyList<SubtitleSegmentCorrection> corrections, SubtitlePostprocessingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(corrections);

        var effectiveOptions = NormalizeOptions(options ?? draft.Options);
        var cues = draft.Cues
            .Select(cue => new WorkingCue(cue.Id, cue.Start, cue.End, cue.Text))
            .ToList();

        foreach (var correction in corrections)
        {
            var cue = cues.FirstOrDefault(item => item.Id == correction.CueId);
            if (cue is null)
            {
                throw new ArgumentException($"Correction references unknown cue id {correction.CueId}.", nameof(corrections));
            }

            if (correction.Text is not null)
            {
                cue.Text = correction.Text;
            }

            if (correction.Start is TimeSpan start)
            {
                cue.Start = start;
            }

            if (correction.End is TimeSpan end)
            {
                cue.End = end;
            }
        }

        NormalizeCueTexts(cues);
        RepairCueTimestamps(cues);
        SortCues(cues);
        ClampGapsAndOverlaps(cues, effectiveOptions);
        ReflowLines(cues, effectiveOptions);
        ReindexCues(cues);

        var issues = ValidateCues(cues, effectiveOptions);
        return new SubtitleDraft(ToImmutableCues(cues), effectiveOptions, issues)
        {
            SourceWords = draft.SourceWords
        };
    }

    /// <inheritdoc />
    public SubtitleStylePreset ApplyRenderTarget(SubtitleStylePreset preset, SubtitleRenderTarget? target)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyTargetResolutionToPreset(NormalizePreset(preset), target);
    }

    /// <inheritdoc />
    public StyledSubtitleDraft ApplyStylePreset(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var effectivePreset = ApplyTargetResolutionToPreset(ApplyPlacementToPreset(NormalizePreset(preset), placement), target);
        var cues = draft.Cues
            .Select(cue => new WorkingCue(cue.Id, cue.Start, cue.End, cue.Text))
            .ToList();

        ApplyTextTransform(cues, effectivePreset);
        ReflowLinesForPreset(cues, effectivePreset);
        ReindexCues(cues);

        var issues = ValidateStyledCues(cues, effectivePreset);
        return new StyledSubtitleDraft(ToImmutableCues(cues), effectivePreset, issues);
    }

    /// <inheritdoc />
    public string RenderStyledAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var styled = ApplyStylePreset(draft, preset, placement, target);
        return BuildStyledAss(styled);
    }

    /// <inheritdoc />
    public string RenderKaraokeAss(TranscriptionDraft draft, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var effectiveOptions = NormalizeOptions(options);
        var segments = draft.Segments
            .Select(segment => new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text)
            {
                Words = segment.Words
            })
            .ToArray();
        var words = BuildWordsFromSegments(segments);
        var cues = BuildKaraokeCues(words, effectiveOptions);
        return BuildKaraokeAss(cues, CreateDefaultKaraokePreset(preset, placement, target));
    }

    /// <inheritdoc />
    public string RenderKaraokeAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var cues = BuildKaraokeCuesFromSubtitleDraft(draft.Cues, draft.SourceWords);
        return BuildKaraokeAss(cues, CreateDefaultKaraokePreset(preset, placement, target));
    }

    internal static string BuildSrt(IReadOnlyList<AudioTranscriptionSegment> segments)
    {
        var builder = new StringBuilder();
        var cueIndex = 1;

        foreach (var segment in segments)
        {
            var text = segment.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            builder.Append(cueIndex.ToString());
            builder.AppendLine();
            builder.Append(FormatSubtitleTimestamp(segment.Start));
            builder.Append(" --> ");
            builder.Append(FormatSubtitleTimestamp(segment.End > segment.Start ? segment.End : segment.Start + MinimumPositiveDuration));
            builder.AppendLine();
            builder.AppendLine(text);
            builder.AppendLine();
            cueIndex++;
        }

        return builder.ToString();
    }

    internal static string EnsureSubtitleExtension(string outputPath)
    {
        return string.Equals(Path.GetExtension(outputPath), ".srt", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(outputPath)
            : Path.GetFullPath(Path.ChangeExtension(outputPath, ".srt"));
    }

    internal static string EnsureAssExtension(string outputPath)
    {
        return string.Equals(Path.GetExtension(outputPath), ".ass", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(outputPath)
            : Path.GetFullPath(Path.ChangeExtension(outputPath, ".ass"));
    }

    private static TranscriptionDraft BuildTranscriptionDraft(IReadOnlyList<AudioTranscriptionSegment> segments)
    {
        var normalizedSegments = new List<TranscriptionSegment>();
        var segmentId = 1;

        foreach (var segment in segments)
        {
            var start = segment.Start < TimeSpan.Zero ? TimeSpan.Zero : segment.Start;
            var end = segment.End > start ? segment.End : start + MinimumPositiveDuration;
            var text = NormalizeSegmentText(segment.Text);
            if (text.Length == 0)
            {
                continue;
            }

            normalizedSegments.Add(new TranscriptionSegment(segmentId++, start, end, text)
            {
                Words = segment.Words
            });
        }

        return new TranscriptionDraft(normalizedSegments);
    }

    private static SubtitleDraft BuildAdvancedDraft(IReadOnlyList<AudioTranscriptionSegment> segments, SubtitlePostprocessingOptions options)
    {
        var cues = BuildProvisionalCues(segments, options);

        RemoveEmptyAndRepairInvalidSegments(cues);
        FixOverlappingTimestamps(cues);
        MergeTinyFragments(cues, options);
        SplitOversizedSegments(cues, options);
        ReflowLines(cues, options);
        AdjustTimingForReadability(cues, options);
        ClampGapsAndOverlaps(cues, options);
        ReindexCues(cues);

        var issues = ValidateCues(cues, options);
        return new SubtitleDraft(ToImmutableCues(cues), options, issues)
        {
            SourceWords = ExtractRealWords(segments)
        };
    }

    /// <summary>
    /// Flattens the real word-level timings carried by the segments, or returns null when none
    /// are present so callers fall back to synthesizing word timing.
    /// </summary>
    private static IReadOnlyList<AudioTranscriptionWord>? ExtractRealWords(IReadOnlyList<AudioTranscriptionSegment> segments)
    {
        List<AudioTranscriptionWord>? words = null;
        foreach (var segment in segments)
        {
            if (segment.Words is not { Count: > 0 } realWords)
            {
                continue;
            }

            words ??= [];
            foreach (var word in realWords)
            {
                var text = NormalizeSegmentText(word.Text);
                if (text.Length == 0)
                {
                    continue;
                }

                var start = word.Start < TimeSpan.Zero ? TimeSpan.Zero : word.Start;
                var end = word.End > start ? word.End : start + MinimumPositiveDuration;
                words.Add(new AudioTranscriptionWord(start, end, text));
            }
        }

        return words is { Count: > 0 } ? words : null;
    }

    /// <summary>
    /// Returns true when the supplied word timings reconstruct exactly to the segment text,
    /// so it is safe to keep them for accurate highlighting.
    /// </summary>
    private static bool WordsMatchText(IReadOnlyList<AudioTranscriptionWord>? words, string text)
    {
        if (words is not { Count: > 0 })
        {
            return false;
        }

        var joined = JoinWords(words.Select(word => word.Text));
        return string.Equals(joined, NormalizeSegmentText(text), StringComparison.Ordinal);
    }

    private static string JoinWords(IEnumerable<string> words)
    {
        return string.Join(" ", words.Select(NormalizeSegmentText).Where(text => text.Length > 0));
    }

    private static List<WorkingCue> BuildProvisionalCues(IReadOnlyList<AudioTranscriptionSegment> segments, SubtitlePostprocessingOptions options)
    {
        var provisionalCues = new List<WorkingCue>();
        foreach (var segment in segments)
        {
            var normalizedText = NormalizeSegmentText(segment.Text);
            if (normalizedText.Length == 0)
            {
                continue;
            }

            var start = segment.Start < TimeSpan.Zero ? TimeSpan.Zero : segment.Start;
            var end = segment.End > start ? segment.End : start + MinimumPositiveDuration;
            provisionalCues.Add(new WorkingCue(provisionalCues.Count + 1, start, end, normalizedText));
        }

        return provisionalCues;
    }

    private static IReadOnlyList<AudioTranscriptionWord> BuildWordsFromSegments(IReadOnlyList<AudioTranscriptionSegment> segments)
    {
        var output = new List<AudioTranscriptionWord>();

        foreach (var segment in segments)
        {
            if (WordsMatchText(segment.Words, segment.Text))
            {
                foreach (var word in segment.Words!)
                {
                    var realText = NormalizeSegmentText(word.Text);
                    if (realText.Length == 0)
                    {
                        continue;
                    }

                    var realStart = word.Start < TimeSpan.Zero ? TimeSpan.Zero : word.Start;
                    var realEnd = word.End > realStart ? word.End : realStart + MinimumPositiveDuration;
                    output.Add(new AudioTranscriptionWord(realStart, realEnd, realText));
                }

                continue;
            }

            var tokens = SplitWords(segment.Text);
            if (tokens.Count == 0)
            {
                continue;
            }

            var start = segment.Start < TimeSpan.Zero ? TimeSpan.Zero : segment.Start;
            var end = segment.End > start ? segment.End : start + MinimumPositiveDuration;
            var totalTicks = Math.Max(MinimumPositiveDuration.Ticks, (end - start).Ticks);
            var totalWeight = Math.Max(1, tokens.Sum(CountTokenWeight));
            long consumedTicks = 0;
            var consumedWeight = 0;

            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                var tokenWeight = CountTokenWeight(token);
                var wordStart = start + TimeSpan.FromTicks(consumedTicks);
                consumedWeight += tokenWeight;

                long wordEndTicks;
                if (index == tokens.Count - 1)
                {
                    wordEndTicks = totalTicks;
                }
                else
                {
                    wordEndTicks = (long)Math.Round(totalTicks * (consumedWeight / (double)totalWeight));
                    wordEndTicks = Math.Clamp(wordEndTicks, consumedTicks + MinimumPositiveDuration.Ticks, totalTicks);
                }

                var wordEnd = start + TimeSpan.FromTicks(wordEndTicks);
                output.Add(new AudioTranscriptionWord(wordStart, wordEnd, token));

                consumedTicks = wordEndTicks;
            }
        }

        return output;
    }

    private static SubtitlePostprocessingOptions NormalizeOptions(SubtitlePostprocessingOptions? options)
    {
        var source = options ?? new SubtitlePostprocessingOptions();
        var minimumDuration = Max(source.MinimumDuration, MinimumPositiveDuration);
        var idealDurationMin = Max(source.IdealDurationMin, minimumDuration);
        var idealDurationMax = Max(source.IdealDurationMax, idealDurationMin);
        var maximumDuration = Max(source.MaximumDuration, idealDurationMax);
        int? maxWordsPerSection = source.MaxWordsPerSection is int configuredMaxWords
            ? Math.Max(1, configuredMaxWords)
            : null;
        var goodCpsMax = source.GoodCpsMax > 0 ? source.GoodCpsMax : 17;
        var acceptableCpsMax = source.AcceptableCpsMax >= goodCpsMax ? source.AcceptableCpsMax : goodCpsMax;
        var closeGapBelow = source.CloseGapBelow >= TimeSpan.Zero ? source.CloseGapBelow : TimeSpan.Zero;
        var intentionalPause = source.IntentionalPauseAtOrAbove >= closeGapBelow ? source.IntentionalPauseAtOrAbove : closeGapBelow;

        return new SubtitlePostprocessingOptions
        {
            MinimumDuration = minimumDuration,
            IdealDurationMin = idealDurationMin,
            IdealDurationMax = idealDurationMax,
            MaximumDuration = maximumDuration,
            MaxWordsPerSection = maxWordsPerSection,
            MaxCharsPerLine = Math.Max(1, source.MaxCharsPerLine),
            MaxLines = Math.Max(1, source.MaxLines),
            GoodCpsMax = goodCpsMax,
            AcceptableCpsMax = acceptableCpsMax,
            CloseGapBelow = closeGapBelow,
            IntentionalPauseAtOrAbove = intentionalPause
        };
    }

    private static void NormalizeCueTexts(List<WorkingCue> cues)
    {
        foreach (var cue in cues)
        {
            cue.Text = NormalizeSegmentText(cue.Text);
        }
    }

    private static void RemoveEmptyAndRepairInvalidSegments(List<WorkingCue> cues)
    {
        for (var index = cues.Count - 1; index >= 0; index--)
        {
            var cue = cues[index];
            cue.Start = Max(cue.Start, TimeSpan.Zero);
            cue.End = Max(cue.End, TimeSpan.Zero);

            if (cue.Text.Length == 0)
            {
                cues.RemoveAt(index);
                continue;
            }

            if (cue.End <= cue.Start)
            {
                cue.End = cue.Start + MinimumPositiveDuration;
            }
        }

        SortCues(cues);
    }

    private static void RepairCueTimestamps(List<WorkingCue> cues)
    {
        foreach (var cue in cues)
        {
            cue.Start = Max(cue.Start, TimeSpan.Zero);
            cue.End = Max(cue.End, cue.Start + MinimumPositiveDuration);
        }
    }

    private static void FixOverlappingTimestamps(List<WorkingCue> cues)
    {
        SortCues(cues);

        for (var index = 1; index < cues.Count; index++)
        {
            var previous = cues[index - 1];
            var current = cues[index];

            if (current.Start >= previous.End)
            {
                continue;
            }

            var trimmedPreviousEnd = current.Start;
            if (trimmedPreviousEnd > previous.Start)
            {
                previous.End = trimmedPreviousEnd;
            }
            else
            {
                current.Start = previous.End;
                if (current.End <= current.Start)
                {
                    current.End = current.Start + MinimumPositiveDuration;
                }
            }
        }
    }

    private static void MergeTinyFragments(List<WorkingCue> cues, SubtitlePostprocessingOptions options)
    {
        var index = 0;
        while (index < cues.Count)
        {
            if (!IsTinyFragment(cues[index], options))
            {
                index++;
                continue;
            }

            var mergeTargetIndex = ChooseMergeTargetIndex(cues, index, options);
            if (mergeTargetIndex is null)
            {
                index++;
                continue;
            }

            if (mergeTargetIndex.Value < index)
            {
                var target = cues[mergeTargetIndex.Value];
                var current = cues[index];
                target.End = Max(target.End, current.End);
                target.Text = NormalizeMergedText(target.Text, current.Text);
                cues.RemoveAt(index);
                index = Math.Max(mergeTargetIndex.Value, 0);
            }
            else
            {
                var current = cues[index];
                var target = cues[mergeTargetIndex.Value];
                target.Start = Min(target.Start, current.Start);
                target.Text = NormalizeMergedText(current.Text, target.Text);
                cues.RemoveAt(index);
            }
        }
    }

    private static bool IsTinyFragment(WorkingCue cue, SubtitlePostprocessingOptions options)
    {
        var duration = GetDuration(cue);
        if (duration < options.MinimumDuration)
        {
            return true;
        }

        return duration < options.IdealDurationMin && CalculateCps(cue.Text, duration) > options.AcceptableCpsMax;
    }

    private static int? ChooseMergeTargetIndex(List<WorkingCue> cues, int index, SubtitlePostprocessingOptions options)
    {
        var current = cues[index];
        var previousIndex = index > 0 ? index - 1 : (int?)null;
        var nextIndex = index < cues.Count - 1 ? index + 1 : (int?)null;

        TimeSpan? previousGap = null;
        if (previousIndex is not null)
        {
            previousGap = Max(current.Start - cues[previousIndex.Value].End, TimeSpan.Zero);
            if (previousGap >= options.IntentionalPauseAtOrAbove)
            {
                previousIndex = null;
                previousGap = null;
            }
        }

        TimeSpan? nextGap = null;
        if (nextIndex is not null)
        {
            nextGap = Max(cues[nextIndex.Value].Start - current.End, TimeSpan.Zero);
            if (nextGap >= options.IntentionalPauseAtOrAbove)
            {
                nextIndex = null;
                nextGap = null;
            }
        }

        if (previousIndex is null && nextIndex is null)
        {
            return null;
        }

        if (previousIndex is null)
        {
            return nextIndex;
        }

        if (nextIndex is null)
        {
            return previousIndex;
        }

        return previousGap <= nextGap ? previousIndex : nextIndex;
    }

    private static void SplitOversizedSegments(List<WorkingCue> cues, SubtitlePostprocessingOptions options)
    {
        var index = 0;
        while (index < cues.Count)
        {
            var cue = cues[index];
            if (!ShouldSplitCue(cue, options) || !TrySplitCue(cue, options, out var first, out var second))
            {
                index++;
                continue;
            }

            cues[index] = first;
            cues.Insert(index + 1, second);
        }
    }

    private static bool ShouldSplitCue(WorkingCue cue, SubtitlePostprocessingOptions options)
    {
        var duration = GetDuration(cue);
        if (duration > options.MaximumDuration)
        {
            return true;
        }

        if (options.MaxWordsPerSection is int maxWordsPerSection && CountWords(cue.Text) > maxWordsPerSection)
        {
            return true;
        }

        if (CalculateCps(cue.Text, duration) > options.AcceptableCpsMax)
        {
            return true;
        }

        var layout = EvaluateLayout(cue.Text, options.MaxCharsPerLine);
        return layout.LineCount > options.MaxLines || layout.MaxLineLength > options.MaxCharsPerLine;
    }

    private static bool TrySplitCue(WorkingCue cue, SubtitlePostprocessingOptions options, out WorkingCue first, out WorkingCue second)
    {
        first = null!;
        second = null!;

        var singleLineText = NormalizeSegmentText(cue.Text);
        var words = SplitWords(singleLineText);
        if (words.Count < 2)
        {
            return false;
        }

        var candidates = GetSplitCandidates(words);
        if (candidates.Count == 0)
        {
            return false;
        }

        var totalWeight = words.Sum(CountTokenWeight);
        var runningWeight = 0;
        SplitCandidate? best = null;
        var bestScore = (Priority: int.MaxValue, WordOverflow: int.MaxValue, Distance: int.MaxValue, Overflow: int.MaxValue);

        for (var i = 0; i < candidates.Count; i++)
        {
            runningWeight += CountTokenWeight(words[i]);
            var candidate = candidates[i];
            var left = string.Join(" ", words.Take(candidate.Index));
            var right = string.Join(" ", words.Skip(candidate.Index));
            if (left.Length == 0 || right.Length == 0)
            {
                continue;
            }

            var leftLayout = EvaluateLayout(left, options.MaxCharsPerLine);
            var rightLayout = EvaluateLayout(right, options.MaxCharsPerLine);
            var overflow = Math.Max(0, leftLayout.MaxLineLength - options.MaxCharsPerLine) +
                Math.Max(0, rightLayout.MaxLineLength - options.MaxCharsPerLine);
            var wordOverflow = options.MaxWordsPerSection is int maxWordsPerSection
                ? Math.Max(0, CountWords(left) - maxWordsPerSection) + Math.Max(0, CountWords(right) - maxWordsPerSection)
                : 0;
            var distance = Math.Abs((runningWeight) - (totalWeight - runningWeight));
            var score = (candidate.Priority, wordOverflow, distance, overflow);
            if (score.CompareTo(bestScore) < 0)
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (best is null)
        {
            return false;
        }

        var partOne = string.Join(" ", words.Take(best.Index));
        var partTwo = string.Join(" ", words.Skip(best.Index));
        if (partOne.Length == 0 || partTwo.Length == 0)
        {
            return false;
        }

        var firstWeight = Math.Max(1, CountTextWeight(partOne));
        var secondWeight = Math.Max(1, CountTextWeight(partTwo));
        var totalDurationTicks = Math.Max(2L, GetDuration(cue).Ticks);
        var firstDurationTicks = (long)Math.Round(totalDurationTicks * (double)firstWeight / (firstWeight + secondWeight));
        firstDurationTicks = Math.Clamp(firstDurationTicks, MinimumPositiveDuration.Ticks, totalDurationTicks - MinimumPositiveDuration.Ticks);

        var boundary = cue.Start + TimeSpan.FromTicks(firstDurationTicks);
        first = new WorkingCue(cue.Id, cue.Start, boundary, partOne);
        second = new WorkingCue(cue.Id, boundary, cue.End, partTwo);
        return true;
    }

    private static List<SplitCandidate> GetSplitCandidates(IReadOnlyList<string> words)
    {
        var candidates = new List<SplitCandidate>();
        for (var index = 1; index < words.Count; index++)
        {
            var previousWord = TrimClosingPunctuation(words[index - 1]);
            var priority = GetSplitPriority(previousWord);
            candidates.Add(new SplitCandidate(index, priority));
        }

        return candidates;
    }

    private static int GetSplitPriority(string word)
    {
        if (word.EndsWith(".", StringComparison.Ordinal) ||
            word.EndsWith("!", StringComparison.Ordinal) ||
            word.EndsWith("?", StringComparison.Ordinal))
        {
            return 0;
        }

        if (word.EndsWith(",", StringComparison.Ordinal) ||
            word.EndsWith(";", StringComparison.Ordinal) ||
            word.EndsWith(":", StringComparison.Ordinal))
        {
            return 1;
        }

        return 2;
    }

    private static void ReflowLines(List<WorkingCue> cues, SubtitlePostprocessingOptions options)
    {
        foreach (var cue in cues)
        {
            cue.Text = ApplyLineReflow(cue.Text, options);
        }
    }

    private static void ApplyTextTransform(List<WorkingCue> cues, SubtitleStylePreset preset)
    {
        foreach (var cue in cues)
        {
            cue.Text = preset.TextTransform switch
            {
                SubtitleTextTransform.Uppercase => cue.Text.ToUpperInvariant(),
                SubtitleTextTransform.Lowercase => cue.Text.ToLowerInvariant(),
                _ => cue.Text
            };
        }
    }

    private static void ReflowLinesForPreset(List<WorkingCue> cues, SubtitleStylePreset preset)
    {
        foreach (var cue in cues)
        {
            cue.Text = ApplyPresetLineReflow(cue.Text, preset);
        }
    }

    private static string ApplyLineReflow(string text, SubtitlePostprocessingOptions options)
    {
        var singleLineText = NormalizeSegmentText(text);
        var layout = EvaluateLayout(singleLineText, options.MaxCharsPerLine);
        return layout.SecondLine is null
            ? layout.FirstLine
            : $"{layout.FirstLine}\n{layout.SecondLine}";
    }

    private static string ApplyPresetLineReflow(string text, SubtitleStylePreset preset)
    {
        var singleLineText = NormalizeSegmentText(text);
        var layout = EvaluateLayoutForMaxLines(singleLineText, preset.MaxCharsPerLine, preset.MaxLines);
        return layout.Lines.Count <= 1
            ? layout.Lines[0]
            : string.Join("\n", layout.Lines);
    }

    private static LayoutEvaluation EvaluateLayout(string text, int maxCharsPerLine)
    {
        var singleLineText = NormalizeSegmentText(text);
        var words = SplitWords(singleLineText);
        if (words.Count == 0)
        {
            return new LayoutEvaluation(string.Empty, null);
        }

        if (singleLineText.Length <= maxCharsPerLine || words.Count == 1)
        {
            return new LayoutEvaluation(singleLineText, null);
        }

        LayoutEvaluation? best = null;
        var bestScore = (Fits: int.MaxValue, Overflow: int.MaxValue, SecondLineWordPenalty: int.MaxValue, Balance: int.MaxValue);

        for (var index = 1; index < words.Count; index++)
        {
            var firstLine = string.Join(" ", words.Take(index));
            var secondLine = string.Join(" ", words.Skip(index));
            if (firstLine.Length == 0 || secondLine.Length == 0)
            {
                continue;
            }

            var overflow = Math.Max(0, firstLine.Length - maxCharsPerLine) + Math.Max(0, secondLine.Length - maxCharsPerLine);
            var fits = overflow == 0 ? 0 : 1;
            var secondLineWordPenalty = CountWords(secondLine) == 1 ? 1 : 0;
            var balance = Math.Abs(firstLine.Length - secondLine.Length);
            var score = (fits, overflow, secondLineWordPenalty, balance);
            if (score.CompareTo(bestScore) < 0)
            {
                best = new LayoutEvaluation(firstLine, secondLine);
                bestScore = score;
            }
        }

        return best ?? new LayoutEvaluation(singleLineText, null);
    }

    private static MultiLineLayoutEvaluation EvaluateLayoutForMaxLines(string text, int maxCharsPerLine, int maxLines)
    {
        var normalizedText = NormalizeSegmentText(text);
        var words = SplitWords(normalizedText);
        if (words.Count == 0)
        {
            return new MultiLineLayoutEvaluation([string.Empty]);
        }

        if (maxLines <= 1 || words.Count == 1)
        {
            return new MultiLineLayoutEvaluation([normalizedText]);
        }

        if (maxLines == 2)
        {
            var twoLineLayout = EvaluateLayout(normalizedText, maxCharsPerLine);
            return new MultiLineLayoutEvaluation(twoLineLayout.SecondLine is null
                ? [twoLineLayout.FirstLine]
                : [twoLineLayout.FirstLine, twoLineLayout.SecondLine]);
        }

        var lines = new List<string>();
        var currentLine = words[0];
        for (var index = 1; index < words.Count; index++)
        {
            var candidate = $"{currentLine} {words[index]}";
            var remainingWords = words.Count - index - 1;
            var remainingLines = Math.Max(1, maxLines - lines.Count - 1);
            if (candidate.Length <= maxCharsPerLine || remainingWords >= remainingLines)
            {
                currentLine = candidate;
                continue;
            }

            lines.Add(currentLine);
            currentLine = words[index];
        }

        lines.Add(currentLine);
        return new MultiLineLayoutEvaluation(lines);
    }

    private static void AdjustTimingForReadability(List<WorkingCue> cues, SubtitlePostprocessingOptions options)
    {
        for (var index = 0; index < cues.Count - 1; index++)
        {
            var current = cues[index];
            var next = cues[index + 1];
            var gap = next.Start - current.End;

            if (gap <= TimeSpan.Zero || gap >= options.IntentionalPauseAtOrAbove)
            {
                continue;
            }

            var duration = GetDuration(current);
            var desiredDurationSeconds = Math.Max(
                options.MinimumDuration.TotalSeconds,
                CountTextWeight(current.Text) / options.GoodCpsMax);
            desiredDurationSeconds = Math.Max(desiredDurationSeconds, duration.TotalSeconds);
            desiredDurationSeconds = Math.Min(desiredDurationSeconds, options.MaximumDuration.TotalSeconds);

            var desiredDuration = TimeSpan.FromSeconds(desiredDurationSeconds);
            if (desiredDuration <= duration)
            {
                continue;
            }

            var leaveGap = gap > options.CloseGapBelow ? options.CloseGapBelow : TimeSpan.Zero;
            var availableExtension = gap - leaveGap;
            if (availableExtension <= TimeSpan.Zero)
            {
                continue;
            }

            var extension = Min(desiredDuration - duration, availableExtension);
            if (extension > TimeSpan.Zero)
            {
                current.End += extension;
            }
        }
    }

    private static void ClampGapsAndOverlaps(List<WorkingCue> cues, SubtitlePostprocessingOptions options)
    {
        SortCues(cues);

        for (var index = 0; index < cues.Count - 1; index++)
        {
            var current = cues[index];
            var next = cues[index + 1];

            if (current.End > next.Start)
            {
                if (next.Start > current.Start)
                {
                    current.End = next.Start;
                }
                else
                {
                    next.Start = current.End;
                    if (next.End <= next.Start)
                    {
                        next.End = next.Start + MinimumPositiveDuration;
                    }
                }
            }

            var gap = next.Start - current.End;
            if (gap > TimeSpan.Zero && gap < options.CloseGapBelow)
            {
                current.End = next.Start;
            }
        }
    }

    private static void ReindexCues(List<WorkingCue> cues)
    {
        for (var index = 0; index < cues.Count; index++)
        {
            cues[index].Id = index + 1;
        }
    }

    private static IReadOnlyList<SubtitleValidationIssue> ValidateCues(List<WorkingCue> cues, SubtitlePostprocessingOptions options)
    {
        var issues = new List<SubtitleValidationIssue>();

        foreach (var cue in cues)
        {
            var duration = GetDuration(cue);
            if (cue.Text.Length == 0)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "empty-text", "Cue text is empty."));
            }

            if (cue.End <= cue.Start)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Error, "invalid-timestamps", "Cue end time must be after the start time."));
            }

            if (duration < options.MinimumDuration)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "short-duration", $"Cue duration {duration.TotalSeconds:0.###} s is below the minimum of {options.MinimumDuration.TotalSeconds:0.###} s."));
            }

            if (duration > options.MaximumDuration)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "long-duration", $"Cue duration {duration.TotalSeconds:0.###} s exceeds the maximum of {options.MaximumDuration.TotalSeconds:0.###} s."));
            }

            var cps = CalculateCps(cue.Text, duration);
            if (cps > options.AcceptableCpsMax)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "fast-cps", $"Cue reads at {cps:0.##} CPS which exceeds the acceptable limit of {options.AcceptableCpsMax:0.##}."));
            }

            var lines = cue.Text.Split('\n');
            if (lines.Length > options.MaxLines)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "too-many-lines", $"Cue uses {lines.Length} lines which exceeds the limit of {options.MaxLines}."));
            }

            var maxLineLength = lines.Length == 0 ? 0 : lines.Max(line => line.Length);
            if (maxLineLength > options.MaxCharsPerLine)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "line-too-long", $"Cue line length {maxLineLength} exceeds the limit of {options.MaxCharsPerLine} characters."));
            }
        }

        return issues;
    }

    private static IReadOnlyList<SubtitleValidationIssue> ValidateStyledCues(List<WorkingCue> cues, SubtitleStylePreset preset)
    {
        var issues = new List<SubtitleValidationIssue>();
        foreach (var cue in cues)
        {
            var lines = cue.Text.Split('\n');
            if (lines.Length > preset.MaxLines)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "style-too-many-lines", $"Styled cue uses {lines.Length} lines which exceeds the preset limit of {preset.MaxLines}."));
            }

            var maxLineLength = lines.Length == 0 ? 0 : lines.Max(line => line.Length);
            if (maxLineLength > preset.MaxCharsPerLine)
            {
                issues.Add(new SubtitleValidationIssue(cue.Id, SubtitleValidationSeverity.Warning, "style-line-too-long", $"Styled cue line length {maxLineLength} exceeds the preset limit of {preset.MaxCharsPerLine} characters."));
            }
        }

        return issues;
    }

    private static IReadOnlyList<SubtitleCue> ToImmutableCues(List<WorkingCue> cues)
    {
        return cues
            .Select(cue => new SubtitleCue(cue.Id, cue.Start, cue.End, cue.Text))
            .ToArray();
    }

    private static void SortCues(List<WorkingCue> cues)
    {
        cues.Sort(static (left, right) =>
        {
            var startComparison = left.Start.CompareTo(right.Start);
            if (startComparison != 0)
            {
                return startComparison;
            }

            var endComparison = left.End.CompareTo(right.End);
            if (endComparison != 0)
            {
                return endComparison;
            }

            return left.Id.CompareTo(right.Id);
        });
    }

    private static string NormalizeMergedText(string left, string right)
    {
        return NormalizeSegmentText($"{RemoveLineBreaks(left)} {RemoveLineBreaks(right)}");
    }

    private static string NormalizeSegmentText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var previousWasWhitespace = false;
        foreach (var character in text)
        {
            var normalizedCharacter = character is '\r' or '\n' or '\t' ? ' ' : character;
            if (char.IsWhiteSpace(normalizedCharacter))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                builder.Append(' ');
                previousWasWhitespace = true;
            }
            else
            {
                builder.Append(normalizedCharacter);
                previousWasWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }

    private static string RemoveLineBreaks(string text)
    {
        return text.Replace('\n', ' ').Replace('\r', ' ');
    }

    private static List<string> SplitWords(string text)
    {
        return NormalizeSegmentText(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static int CountWords(string text)
    {
        return SplitWords(text).Count;
    }

    private static int CountTextWeight(string text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountTokenWeight(string token)
    {
        return Math.Max(1, CountTextWeight(token));
    }

    private static double CalculateCps(string text, TimeSpan duration)
    {
        var seconds = Math.Max(duration.TotalSeconds, MinimumPositiveDuration.TotalSeconds);
        return CountTextWeight(text) / seconds;
    }

    private static TimeSpan GetDuration(WorkingCue cue)
    {
        return cue.End > cue.Start ? cue.End - cue.Start : MinimumPositiveDuration;
    }

    private static string TrimClosingPunctuation(string value)
    {
        return value.TrimEnd('"', '\'', ')', ']', '}');
    }

    private static bool EndsSentence(string value)
    {
        var trimmed = value.TrimEnd();
        return trimmed.EndsWith(".", StringComparison.Ordinal) ||
            trimmed.EndsWith("!", StringComparison.Ordinal) ||
            trimmed.EndsWith("?", StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates the requested output path, normalizes its extension, and ensures the parent
    /// directory exists. Shared by every "generate file" entry point.
    /// </summary>
    private static string PrepareOutputPath(string outputPath, Func<string, string> ensureExtension)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(outputPath));
        }

        var finalOutputPath = ensureExtension(outputPath);
        var outputDirectory = Path.GetDirectoryName(finalOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        return finalOutputPath;
    }

    private static Task WriteSubtitleFileAsync(string path, string contents, CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    private static string FormatSubtitleTimestamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right)
    {
        return left <= right ? left : right;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static void Report(IProgress<AudioTranscriptionProgress>? progress, ProgressState state, AudioTranscriptionStage stage, double stagePercent, string description)
    {
        if (progress is null)
        {
            return;
        }

        state.StartedAtUtc ??= DateTimeOffset.UtcNow;
        stagePercent = Math.Clamp(stagePercent, 0d, 1d);

        var overallPercent = stage switch
        {
            AudioTranscriptionStage.WritingSubtitles => 0.95d + (stagePercent * 0.04d),
            AudioTranscriptionStage.Completed => 1d,
            _ => stagePercent
        };

        progress.Report(new AudioTranscriptionProgress
        {
            Stage = stage,
            OverallPercent = overallPercent,
            StagePercent = stagePercent,
            StageDescription = description,
            EstimatedRemainingTime = stage == AudioTranscriptionStage.Completed
                ? TimeSpan.Zero
                : state.Eta.AddSample(overallPercent)
        });
    }

    private sealed class ProgressState
    {
        public DateTimeOffset? StartedAtUtc { get; set; }
        public Helpers.EtaEstimator Eta { get; } = new();
    }

    private sealed class WorkingCue
    {
        public WorkingCue(int id, TimeSpan start, TimeSpan end, string text)
        {
            Id = id;
            Start = start;
            End = end;
            Text = text ?? string.Empty;
        }

        public int Id { get; set; }

        public TimeSpan Start { get; set; }

        public TimeSpan End { get; set; }

        public string Text { get; set; }
    }

    private sealed record SplitCandidate(int Index, int Priority);

    private sealed class LayoutEvaluation
    {
        public LayoutEvaluation(string firstLine, string? secondLine)
        {
            FirstLine = firstLine;
            SecondLine = secondLine;
        }

        public string FirstLine { get; }

        public string? SecondLine { get; }

        public int LineCount => SecondLine is null ? 1 : 2;

        public int MaxLineLength => SecondLine is null ? FirstLine.Length : Math.Max(FirstLine.Length, SecondLine.Length);
    }

    private sealed class MultiLineLayoutEvaluation
    {
        public MultiLineLayoutEvaluation(IReadOnlyList<string> lines)
        {
            Lines = lines;
        }

        public IReadOnlyList<string> Lines { get; }
    }
}
