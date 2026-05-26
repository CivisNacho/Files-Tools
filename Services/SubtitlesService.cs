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
public sealed record TranscriptionSegment(int Id, TimeSpan Start, TimeSpan End, string Text);

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
/// Built-in styled ASS subtitle presets.
/// </summary>
public static class StyledSubtitlePresets
{
    public static SubtitleStylePreset SocialImpact => CreateSocialImpact();

    public static SubtitleStylePreset CleanSans => CreateCleanSans();

    public static SubtitleStylePreset CaptionBox => CreateCaptionBox();

    public static SubtitleStylePreset BroadcastLowerThird => CreateBroadcastLowerThird();

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
            FontSize = 72,
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
            IntroScale = 1.08d,
            OutlineWidth = 6,
            ShadowDepth = 1.5,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 80,
            MarginRight = 80,
            MarginVertical = 90,
            MaxLines = 2,
            MaxCharsPerLine = 28
        };
    }

    public static SubtitleStylePreset CreateCleanSans()
    {
        return new SubtitleStylePreset
        {
            Name = "CleanSans",
            AssStyleName = "CleanSans",
            ScriptTitle = "Styled subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Segoe UI",
            FontFamilyFallbacks = ["Segoe UI", "Arial", "Helvetica", "Noto Sans"],
            FontSize = 62,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.None,
            FillColor = SubtitleColor.White,
            OutlineColor = SubtitleColor.Black,
            ShadowColor = new SubtitleColor(120, 0, 0, 0),
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.Fade,
            EntryFadeMilliseconds = 100,
            ExitFadeMilliseconds = 100,
            IntroScale = 1d,
            OutlineWidth = 4,
            ShadowDepth = 1,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 80,
            MarginRight = 80,
            MarginVertical = 96,
            MaxLines = 2,
            MaxCharsPerLine = 34
        };
    }

    public static SubtitleStylePreset CreateCaptionBox()
    {
        return new SubtitleStylePreset
        {
            Name = "CaptionBox",
            AssStyleName = "CaptionBox",
            ScriptTitle = "Styled subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Arial",
            FontFamilyFallbacks = ["Arial", "Helvetica", "Segoe UI"],
            FontSize = 58,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.None,
            FillColor = SubtitleColor.White,
            OutlineColor = SubtitleColor.Black,
            ShadowColor = new SubtitleColor(180, 0, 0, 0),
            UseBackgroundBox = true,
            PresentationAnimation = SubtitlePresentationAnimation.Fade,
            EntryFadeMilliseconds = 140,
            ExitFadeMilliseconds = 140,
            IntroScale = 1d,
            OutlineWidth = 2.5,
            ShadowDepth = 0.5,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 88,
            MarginRight = 88,
            MarginVertical = 114,
            MaxLines = 2,
            MaxCharsPerLine = 34
        };
    }

    public static SubtitleStylePreset CreateBroadcastLowerThird()
    {
        return new SubtitleStylePreset
        {
            Name = "BroadcastLowerThird",
            AssStyleName = "BroadcastLowerThird",
            ScriptTitle = "Styled subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Segoe UI Semibold",
            FontFamilyFallbacks = ["Segoe UI Semibold", "Segoe UI", "Arial"],
            FontSize = 52,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.Uppercase,
            FillColor = SubtitleColor.White,
            OutlineColor = SubtitleColor.Black,
            ShadowColor = new SubtitleColor(200, 24, 32, 56),
            UseBackgroundBox = true,
            PresentationAnimation = SubtitlePresentationAnimation.Pop,
            EntryFadeMilliseconds = 100,
            ExitFadeMilliseconds = 120,
            IntroScale = 1.1d,
            OutlineWidth = 3.5,
            ShadowDepth = 1,
            Alignment = SubtitleVisualAlignment.BottomLeft,
            MarginLeft = 96,
            MarginRight = 96,
            MarginVertical = 82,
            MaxLines = 2,
            MaxCharsPerLine = 30
        };
    }
}

/// <summary>
/// Built-in karaoke subtitle presets.
/// </summary>
public static class KaraokeSubtitlePresets
{
    public static SubtitleStylePreset NeonKaraoke => CreateNeonKaraoke();

    public static SubtitleStylePreset Punch => CreatePunch();

    public static SubtitleStylePreset CreateNeonKaraoke()
    {
        return new SubtitleStylePreset
        {
            Name = "NeonKaraoke",
            AssStyleName = "NeonKaraoke",
            ScriptTitle = "Karaoke subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Segoe UI Semibold",
            FontFamilyFallbacks = ["Segoe UI Semibold", "Segoe UI", "Arial"],
            FontSize = 64,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.None,
            FillColor = SubtitleColor.White,
            OutlineColor = SubtitleColor.Black,
            ShadowColor = new SubtitleColor(180, 0, 0, 0),
            KaraokeHighlightColor = new SubtitleColor(0, 255, 220, 20),
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.FadePop,
            EntryFadeMilliseconds = 80,
            ExitFadeMilliseconds = 80,
            IntroScale = 1.12d,
            OutlineWidth = 7,
            ShadowDepth = 1,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 80,
            MarginRight = 80,
            MarginVertical = 92,
            MaxLines = 2,
            MaxCharsPerLine = 30
        };
    }

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
            FontSize = 72,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.None,
            FillColor = SubtitleColor.White,
            OutlineColor = new SubtitleColor(0, 64, 64, 64),
            ShadowColor = new SubtitleColor(200, 0, 0, 0),
            KaraokeHighlightColor = new SubtitleColor(0, 255, 130, 0),
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.None,
            EntryFadeMilliseconds = 0,
            ExitFadeMilliseconds = 0,
            IntroScale = 1d,
            OutlineWidth = 12,
            ShadowDepth = 2,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 80,
            MarginRight = 80,
            MarginVertical = 92,
            MaxLines = 2,
            MaxCharsPerLine = 28
        };
    }

    public static SubtitleStylePreset Bubbly => CreateBubbly();

    public static SubtitleStylePreset CreateBubbly()
    {
        return new SubtitleStylePreset
        {
            Name = "Bubbly",
            AssStyleName = "Bubbly",
            ScriptTitle = "Karaoke subtitles",
            PlayResX = 1920,
            PlayResY = 1080,
            WrapStyle = 0,
            ScaledBorderAndShadow = true,
            PrimaryFontFamily = "Bahnschrift",
            FontFamilyFallbacks = ["Bahnschrift", "Segoe UI", "Arial"],
            FontSize = 68,
            Bold = true,
            Italic = false,
            TextTransform = SubtitleTextTransform.Lowercase,
            FillColor = new SubtitleColor(0, 255, 180, 200),
            OutlineColor = SubtitleColor.White,
            ShadowColor = new SubtitleColor(120, 0, 0, 0),
            KaraokeHighlightColor = new SubtitleColor(0, 255, 230, 80),
            UseBackgroundBox = false,
            PresentationAnimation = SubtitlePresentationAnimation.DropIn,
            EntryFadeMilliseconds = 400,
            ExitFadeMilliseconds = 100,
            IntroScale = 1d,
            OutlineWidth = 12,
            ShadowDepth = 0,
            Alignment = SubtitleVisualAlignment.BottomLeft,
            MarginLeft = 80,
            MarginRight = 80,
            MarginVertical = 92,
            MaxLines = 2,
            MaxCharsPerLine = 30
        };
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
    StyledSubtitleDraft ApplyStylePreset(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null);

    /// <summary>
    /// Renders a processed subtitle draft to styled ASS text.
    /// </summary>
    string RenderStyledAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null);

    /// <summary>
    /// Renders a reviewed transcription draft to karaoke ASS text.
    /// </summary>
    string RenderKaraokeAss(TranscriptionDraft draft, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null);

    /// <summary>
    /// Renders a processed subtitle draft to karaoke ASS text while preserving reviewed cue boundaries.
    /// </summary>
    string RenderKaraokeAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null);
}

/// <summary>
/// Generates plain SRT subtitles and advanced editable drafts using the audio transcription service.
/// </summary>
public sealed class SubtitlesService : ISubtitlesService
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
        ValidateOutputPath(outputSubtitlePath);

        var finalOutputPath = EnsureSubtitleExtension(outputSubtitlePath);
        var outputDirectory = Path.GetDirectoryName(finalOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var progressState = new ProgressState();
        var segments = await _audioTranscriptionService.TranscribeToSegmentsAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 0d, "Writing subtitle file");

        var srt = BuildSrt(segments);
        await File.WriteAllTextAsync(finalOutputPath, srt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);

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
        ValidateOutputPath(outputSubtitlePath);

        var finalOutputPath = EnsureAssExtension(outputSubtitlePath);
        var outputDirectory = Path.GetDirectoryName(finalOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var effectiveOptions = NormalizeOptions(options);
        var progressState = new ProgressState();
        var segments = await _audioTranscriptionService.TranscribeToSegmentsAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);

        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 0d, "Building karaoke subtitle file");
        var words = BuildWordsFromSegments(segments);
        var cues = BuildKaraokeCues(words, effectiveOptions);
        var ass = BuildKaraokeAss(cues, CreateDefaultKaraokePreset(preset, placement));
        await File.WriteAllTextAsync(finalOutputPath, ass, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);

        Report(progress, progressState, AudioTranscriptionStage.WritingSubtitles, 1d, "Karaoke subtitle file ready");
        Report(progress, progressState, AudioTranscriptionStage.Completed, 1d, "Subtitle generation complete");
        return finalOutputPath;
    }

    /// <summary>
    /// Transcribes supported audio or video input into a styled ASS subtitle file.
    /// </summary>
    public async Task<string> GenerateStyledAssAsync(string inputPath, string outputSubtitlePath, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, IProgress<AudioTranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateOutputPath(outputSubtitlePath);

        var finalOutputPath = EnsureAssExtension(outputSubtitlePath);
        var outputDirectory = Path.GetDirectoryName(finalOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var transcriptionDraft = await GenerateAdvancedTranscriptionDraftAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var draft = BuildSubtitleDraftFromTranscription(transcriptionDraft, Array.Empty<TranscriptionSegmentCorrection>(), options);
        var ass = RenderStyledAss(draft, preset, placement);
        await File.WriteAllTextAsync(finalOutputPath, ass, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
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
            .Select(segment => new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text))
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
                segments[segmentIndex] = existing with { Text = correction.Text };
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
        return new SubtitleDraft(ToImmutableCues(cues), effectiveOptions, issues);
    }

    /// <inheritdoc />
    public StyledSubtitleDraft ApplyStylePreset(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var effectivePreset = ApplyPlacementToPreset(NormalizePreset(preset), placement);
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
    public string RenderStyledAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var styled = ApplyStylePreset(draft, preset, placement);
        return BuildStyledAss(styled);
    }

    /// <inheritdoc />
    public string RenderKaraokeAss(TranscriptionDraft draft, SubtitlePostprocessingOptions? options = null, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var effectiveOptions = NormalizeOptions(options);
        var segments = draft.Segments
            .Select(segment => new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text))
            .ToArray();
        var words = BuildWordsFromSegments(segments);
        var cues = BuildKaraokeCues(words, effectiveOptions);
        return BuildKaraokeAss(cues, CreateDefaultKaraokePreset(preset, placement));
    }

    /// <inheritdoc />
    public string RenderKaraokeAss(SubtitleDraft draft, SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var cues = BuildKaraokeCuesFromSubtitleDraft(draft.Cues);
        return BuildKaraokeAss(cues, CreateDefaultKaraokePreset(preset, placement));
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

    internal static string BuildStyledAss(StyledSubtitleDraft styledDraft)
    {
        ArgumentNullException.ThrowIfNull(styledDraft);

        var preset = styledDraft.Preset;
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.Append("Title: ").AppendLine(preset.ScriptTitle);
        builder.AppendLine("ScriptType: v4.00+");
        builder.Append("PlayResX: ").AppendLine(preset.PlayResX.ToString());
        builder.Append("PlayResY: ").AppendLine(preset.PlayResY.ToString());
        builder.Append("WrapStyle: ").AppendLine(preset.WrapStyle.ToString());
        builder.Append("ScaledBorderAndShadow: ").AppendLine(preset.ScaledBorderAndShadow ? "yes" : "no");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.Append("Style: ")
            .Append(preset.AssStyleName).Append(',')
            .Append(preset.PrimaryFontFamily).Append(',')
            .Append(preset.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(ToAssColor(preset.FillColor)).Append(',')
            .Append(ToAssColor(preset.FillColor)).Append(',')
            .Append(ToAssColor(preset.OutlineColor)).Append(',')
            .Append(ToAssColor(preset.ShadowColor)).Append(',')
            .Append(preset.Bold ? "-1" : "0").Append(',')
            .Append(preset.Italic ? "-1" : "0").Append(",0,0,100,100,0,0,1,")
            .Append(preset.OutlineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(preset.ShadowDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(GetAssAlignmentCode(preset.Alignment)).Append(',')
            .Append(preset.MarginLeft).Append(',')
            .Append(preset.MarginRight).Append(',')
            .Append(preset.MarginVertical).Append(',')
            .AppendLine(preset.UseBackgroundBox ? "3" : "1");
        builder.AppendLine();

        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var cue in styledDraft.Cues)
        {
            builder.Append("Dialogue: 0,")
                .Append(FormatAssTimestamp(cue.Start)).Append(',')
                .Append(FormatAssTimestamp(cue.End)).Append(',')
                .Append(preset.AssStyleName).Append(", ,0,0,0,,")
                .Append(BuildAssCueOverrides(preset, cue.Start, cue.End))
                .AppendLine(cue.Text.Replace("\r", string.Empty).Replace("\n", "\\N"));
        }

        return builder.ToString();
    }

    private static string BuildKaraokeAss(IReadOnlyList<KaraokeCue> cues, KaraokeRenderPreset preset)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(preset);

        if (preset.TextTransform != SubtitleTextTransform.None)
        {
            foreach (var cue in cues)
            {
                foreach (var word in cue.Words)
                {
                    word.Text = preset.TextTransform switch
                    {
                        SubtitleTextTransform.Uppercase => word.Text.ToUpperInvariant(),
                        SubtitleTextTransform.Lowercase => word.Text.ToLowerInvariant(),
                        _ => word.Text
                    };
                }
            }
        }

        var isDropIn = preset.PresentationAnimation == SubtitlePresentationAnimation.DropIn;

        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.Append("Title: ").AppendLine(preset.ScriptTitle);
        builder.AppendLine("ScriptType: v4.00+");
        builder.Append("PlayResX: ").AppendLine(preset.PlayResX.ToString());
        builder.Append("PlayResY: ").AppendLine(preset.PlayResY.ToString());
        builder.Append("WrapStyle: ").AppendLine(preset.WrapStyle.ToString());
        builder.Append("ScaledBorderAndShadow: ").AppendLine(preset.ScaledBorderAndShadow ? "yes" : "no");
        builder.AppendLine();

        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.Append("Style: ")
            .Append(preset.StyleName).Append(',')
            .Append(preset.FontFamily).Append(',')
            .Append(preset.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(ToAssColor(isDropIn ? preset.BaseColor : preset.HighlightColor)).Append(',')
            .Append(isDropIn ? "&HFF000000&" : ToAssColor(preset.BaseColor)).Append(',')
            .Append(ToAssColor(preset.OutlineColor)).Append(',')
            .Append(ToAssColor(preset.ShadowColor)).Append(',')
            .Append(preset.Bold ? "-1" : "0").Append(',')
            .Append(preset.Italic ? "-1" : "0").Append(',')
            .Append("0,0,100,100,0,0,1,")
            .Append(preset.OutlineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(preset.ShadowDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(GetAssAlignmentCode(preset.Alignment)).Append(',')
            .Append(preset.MarginLeft).Append(',')
            .Append(preset.MarginRight).Append(',')
            .Append(preset.MarginVertical).Append(',')
            .AppendLine(preset.UseBackgroundBox ? "3" : "1");
        builder.AppendLine();

        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var cue in cues)
        {
            if (cue.Words.Count == 0)
            {
                continue;
            }

            if (isDropIn)
            {
                RenderDropInKaraokeEvents(builder, cue, preset);
            }
            else
            {
                builder.AppendLine(BuildAssDialogueLine(0, cue.Start, cue.End, preset.StyleName, BuildAssCueOverrides(preset, cue.Start, cue.End) + RenderKaraokeCueText(cue, preset)));
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<KaraokeCue> BuildKaraokeCues(IReadOnlyList<AudioTranscriptionWord> words, SubtitlePostprocessingOptions options)
    {
        var cues = BuildProvisionalKaraokeCues(words, options);
        RemoveInvalidKaraokeCues(cues);
        FixKaraokeCueOverlaps(cues);
        MergeTinyKaraokeFragments(cues, options);
        SplitOversizedKaraokeCues(cues, options);
        ReflowKaraokeLines(cues, options);
        AdjustKaraokeCueTimingForReadability(cues, options);
        ClampKaraokeCueGaps(cues, options);
        ReindexKaraokeCues(cues);
        return cues;
    }

    private static IReadOnlyList<KaraokeCue> BuildKaraokeCuesFromSubtitleDraft(IReadOnlyList<SubtitleCue> cues)
    {
        var karaokeCues = new List<KaraokeCue>();
        foreach (var cue in cues.OrderBy(cue => cue.Start).ThenBy(cue => cue.Id))
        {
            var words = BuildCueWordsFromSubtitleCue(cue);
            if (words.Count == 0)
            {
                continue;
            }

            karaokeCues.Add(new KaraokeCue(
                karaokeCues.Count + 1,
                words[0].Start,
                Max(words[^1].End, words[0].Start + MinimumPositiveDuration),
                words));
        }

        return karaokeCues;
    }

    private static List<KaraokeCueWord> BuildCueWordsFromSubtitleCue(SubtitleCue cue)
    {
        var normalizedText = cue.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalizedText.Split('\n');
        var weightedTokens = new List<(string Text, bool BreakBefore, int Weight)>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var tokens = SplitWords(lines[lineIndex]);
            for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                weightedTokens.Add((tokens[tokenIndex], lineIndex > 0 && tokenIndex == 0, CountTokenWeight(tokens[tokenIndex])));
            }
        }

        if (weightedTokens.Count == 0)
        {
            return [];
        }

        var cueStart = cue.Start < TimeSpan.Zero ? TimeSpan.Zero : cue.Start;
        var cueEnd = cue.End > cueStart ? cue.End : cueStart + MinimumPositiveDuration;
        var totalTicks = Math.Max(MinimumPositiveDuration.Ticks, (cueEnd - cueStart).Ticks);
        var totalWeight = Math.Max(1, weightedTokens.Sum(token => token.Weight));
        long consumedTicks = 0;
        var consumedWeight = 0;
        var output = new List<KaraokeCueWord>(weightedTokens.Count);

        for (var index = 0; index < weightedTokens.Count; index++)
        {
            var token = weightedTokens[index];
            var wordStart = cueStart + TimeSpan.FromTicks(consumedTicks);
            consumedWeight += token.Weight;

            long wordEndTicks;
            if (index == weightedTokens.Count - 1)
            {
                wordEndTicks = totalTicks;
            }
            else
            {
                wordEndTicks = (long)Math.Round(totalTicks * (consumedWeight / (double)totalWeight));
                wordEndTicks = Math.Clamp(wordEndTicks, consumedTicks + MinimumPositiveDuration.Ticks, totalTicks);
            }

            var word = new KaraokeCueWord(token.Text, wordStart, cueStart + TimeSpan.FromTicks(wordEndTicks))
            {
                BreakBefore = token.BreakBefore
            };
            output.Add(word);
            consumedTicks = wordEndTicks;
        }

        return output;
    }

    private static List<KaraokeCue> BuildProvisionalKaraokeCues(IReadOnlyList<AudioTranscriptionWord> words, SubtitlePostprocessingOptions options)
    {
        var cues = new List<KaraokeCue>();
        KaraokeCue? currentCue = null;

        foreach (var word in words)
        {
            var normalizedWord = NormalizeSegmentText(word.Text);
            if (normalizedWord.Length == 0)
            {
                continue;
            }

            var wordStart = word.Start < TimeSpan.Zero ? TimeSpan.Zero : word.Start;
            var wordEnd = word.End > wordStart ? word.End : wordStart + MinimumPositiveDuration;
            var cueWord = new KaraokeCueWord(normalizedWord, wordStart, wordEnd);

            if (currentCue is null)
            {
                currentCue = new KaraokeCue(cues.Count + 1, wordStart, wordEnd, [cueWord]);
                continue;
            }

            var candidateText = NormalizeMergedText(GetKaraokeCueText(currentCue), normalizedWord);
            var gap = wordStart > currentCue.End ? wordStart - currentCue.End : TimeSpan.Zero;
            var candidateDuration = wordEnd - currentCue.Start;
            var softCharacterLimit = Math.Max(options.MaxCharsPerLine * options.MaxLines, options.MaxCharsPerLine + 8);
            var shouldBreak =
                gap >= options.IntentionalPauseAtOrAbove ||
                (EndsSentence(GetKaraokeCueText(currentCue)) && currentCue.End - currentCue.Start >= options.MinimumDuration) ||
                (gap >= TimeSpan.FromMilliseconds(250) && currentCue.End - currentCue.Start >= options.MinimumDuration) ||
                (candidateDuration > options.IdealDurationMax && currentCue.End - currentCue.Start >= options.MinimumDuration) ||
                candidateText.Length > softCharacterLimit;

            if (shouldBreak)
            {
                cues.Add(currentCue);
                currentCue = new KaraokeCue(cues.Count + 1, wordStart, wordEnd, [cueWord]);
                continue;
            }

            currentCue.End = wordEnd;
            currentCue.Words.Add(cueWord);
        }

        if (currentCue is not null)
        {
            cues.Add(currentCue);
        }

        return cues;
    }

    private static void RemoveInvalidKaraokeCues(List<KaraokeCue> cues)
    {
        for (var index = cues.Count - 1; index >= 0; index--)
        {
            var cue = cues[index];
            cue.Words.RemoveAll(word => NormalizeSegmentText(word.Text).Length == 0);
            if (cue.Words.Count == 0)
            {
                cues.RemoveAt(index);
                continue;
            }

            cue.Start = Max(TimeSpan.Zero, cue.Words[0].Start);
            cue.End = Max(cue.Words[^1].End, cue.Start + MinimumPositiveDuration);
        }

        SortKaraokeCues(cues);
    }

    private static void FixKaraokeCueOverlaps(List<KaraokeCue> cues)
    {
        SortKaraokeCues(cues);
        for (var index = 1; index < cues.Count; index++)
        {
            var previous = cues[index - 1];
            var current = cues[index];
            if (current.Start >= previous.End)
            {
                continue;
            }

            var boundary = current.Start;
            if (boundary > previous.Start)
            {
                previous.End = boundary;
            }
            else
            {
                current.Start = previous.End;
            }
        }
    }

    private static void MergeTinyKaraokeFragments(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        var index = 0;
        while (index < cues.Count)
        {
            if (!IsTinyKaraokeCue(cues[index], options))
            {
                index++;
                continue;
            }

            var mergeTarget = ChooseKaraokeMergeTarget(cues, index, options);
            if (mergeTarget is null)
            {
                index++;
                continue;
            }

            if (mergeTarget.Value < index)
            {
                var target = cues[mergeTarget.Value];
                var current = cues[index];
                target.End = Max(target.End, current.End);
                target.Words.AddRange(current.Words);
                cues.RemoveAt(index);
                index = Math.Max(mergeTarget.Value, 0);
            }
            else
            {
                var current = cues[index];
                var target = cues[mergeTarget.Value];
                target.Start = Min(target.Start, current.Start);
                target.Words.InsertRange(0, current.Words);
                cues.RemoveAt(index);
            }
        }
    }

    private static bool IsTinyKaraokeCue(KaraokeCue cue, SubtitlePostprocessingOptions options)
    {
        var duration = GetKaraokeCueDuration(cue);
        if (duration < options.MinimumDuration)
        {
            return true;
        }

        return duration < options.IdealDurationMin && CalculateCps(GetKaraokeCueText(cue), duration) > options.AcceptableCpsMax;
    }

    private static int? ChooseKaraokeMergeTarget(List<KaraokeCue> cues, int index, SubtitlePostprocessingOptions options)
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

    private static void SplitOversizedKaraokeCues(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        var index = 0;
        while (index < cues.Count)
        {
            var cue = cues[index];
            if (!ShouldSplitKaraokeCue(cue, options) || !TrySplitKaraokeCue(cue, options, out var first, out var second))
            {
                index++;
                continue;
            }

            cues[index] = first;
            cues.Insert(index + 1, second);
        }
    }

    private static bool ShouldSplitKaraokeCue(KaraokeCue cue, SubtitlePostprocessingOptions options)
    {
        var duration = GetKaraokeCueDuration(cue);
        if (duration > options.MaximumDuration)
        {
            return true;
        }

        if (options.MaxWordsPerSection is int maxWordsPerSection && CountWords(GetKaraokeCueText(cue)) > maxWordsPerSection)
        {
            return true;
        }

        if (CalculateCps(GetKaraokeCueText(cue), duration) > options.AcceptableCpsMax)
        {
            return true;
        }

        var layout = EvaluateLayout(GetKaraokeCueText(cue), options.MaxCharsPerLine);
        return layout.LineCount > options.MaxLines || layout.MaxLineLength > options.MaxCharsPerLine;
    }

    private static bool TrySplitKaraokeCue(KaraokeCue cue, SubtitlePostprocessingOptions options, out KaraokeCue first, out KaraokeCue second)
    {
        first = null!;
        second = null!;

        if (cue.Words.Count < 2)
        {
            return false;
        }

        var totalWeight = cue.Words.Sum(word => CountTokenWeight(word.Text));
        var runningWeight = 0;
        SplitCandidate? best = null;
        var bestScore = (Priority: int.MaxValue, WordOverflow: int.MaxValue, Distance: int.MaxValue, Overflow: int.MaxValue);

        for (var index = 1; index < cue.Words.Count; index++)
        {
            runningWeight += CountTokenWeight(cue.Words[index - 1].Text);
            var leftText = JoinKaraokeWords(cue.Words.Take(index));
            var rightText = JoinKaraokeWords(cue.Words.Skip(index));
            if (leftText.Length == 0 || rightText.Length == 0)
            {
                continue;
            }

            var leftLayout = EvaluateLayout(leftText, options.MaxCharsPerLine);
            var rightLayout = EvaluateLayout(rightText, options.MaxCharsPerLine);
            var overflow = Math.Max(0, leftLayout.MaxLineLength - options.MaxCharsPerLine) +
                Math.Max(0, rightLayout.MaxLineLength - options.MaxCharsPerLine);
            var priority = GetSplitPriority(TrimClosingPunctuation(cue.Words[index - 1].Text));
            var wordOverflow = options.MaxWordsPerSection is int maxWordsPerSection
                ? Math.Max(0, CountWords(leftText) - maxWordsPerSection) + Math.Max(0, CountWords(rightText) - maxWordsPerSection)
                : 0;
            var distance = Math.Abs(runningWeight - (totalWeight - runningWeight));
            var score = (priority, wordOverflow, distance, overflow);
            if (score.CompareTo(bestScore) < 0)
            {
                best = new SplitCandidate(index, priority);
                bestScore = score;
            }
        }

        if (best is null)
        {
            return false;
        }

        var leftWords = cue.Words.Take(best.Index).Select(word => word.Clone()).ToList();
        var rightWords = cue.Words.Skip(best.Index).Select(word => word.Clone()).ToList();
        if (leftWords.Count == 0 || rightWords.Count == 0)
        {
            return false;
        }

        var boundary = rightWords[0].Start;
        first = new KaraokeCue(cue.Id, leftWords[0].Start, Max(leftWords[^1].End, boundary), leftWords);
        second = new KaraokeCue(cue.Id, boundary, Max(rightWords[^1].End, boundary + MinimumPositiveDuration), rightWords);
        return true;
    }

    private static void ReflowKaraokeLines(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        foreach (var cue in cues)
        {
            foreach (var word in cue.Words)
            {
                word.BreakBefore = false;
            }

            if (options.MaxLines <= 1 || cue.Words.Count <= 1)
            {
                continue;
            }

            if (options.MaxLines == 2)
            {
                var breakIndex = GetBestKaraokeLineBreakIndex(cue.Words, options.MaxCharsPerLine);
                if (breakIndex > 0 && breakIndex < cue.Words.Count)
                {
                    cue.Words[breakIndex].BreakBefore = true;
                }

                continue;
            }

            var currentLineLength = cue.Words[0].Text.Length;
            var currentLineWordCount = 1;
            for (var index = 1; index < cue.Words.Count; index++)
            {
                var candidateLength = currentLineLength + 1 + cue.Words[index].Text.Length;
                var remainingWords = cue.Words.Count - index;
                var usedLines = cue.Words.Count(word => word.BreakBefore) + 1;
                var remainingLines = Math.Max(1, options.MaxLines - usedLines);
                if (candidateLength > options.MaxCharsPerLine && remainingWords >= remainingLines)
                {
                    cue.Words[index].BreakBefore = true;
                    currentLineLength = cue.Words[index].Text.Length;
                    currentLineWordCount = 1;
                    continue;
                }

                currentLineLength = candidateLength;
                currentLineWordCount++;
            }
        }
    }

    private static int GetBestKaraokeLineBreakIndex(IReadOnlyList<KaraokeCueWord> words, int maxCharsPerLine)
    {
        if (words.Count < 2)
        {
            return -1;
        }

        var fullText = JoinKaraokeWords(words);
        if (fullText.Length <= maxCharsPerLine)
        {
            return -1;
        }

        var bestIndex = -1;
        var bestScore = (Fits: int.MaxValue, Overflow: int.MaxValue, SecondLineWordPenalty: int.MaxValue, Balance: int.MaxValue);
        for (var index = 1; index < words.Count; index++)
        {
            var left = JoinKaraokeWords(words.Take(index));
            var right = JoinKaraokeWords(words.Skip(index));
            var overflow = Math.Max(0, left.Length - maxCharsPerLine) + Math.Max(0, right.Length - maxCharsPerLine);
            var fits = overflow == 0 ? 0 : 1;
            var secondLinePenalty = words.Count - index == 1 ? 1 : 0;
            var balance = Math.Abs(left.Length - right.Length);
            var score = (fits, overflow, secondLinePenalty, balance);
            if (score.CompareTo(bestScore) < 0)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static void AdjustKaraokeCueTimingForReadability(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
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

            var duration = GetKaraokeCueDuration(current);
            var desiredDurationSeconds = Math.Max(options.MinimumDuration.TotalSeconds, CountTextWeight(GetKaraokeCueText(current)) / options.GoodCpsMax);
            desiredDurationSeconds = Math.Max(desiredDurationSeconds, duration.TotalSeconds);
            desiredDurationSeconds = Math.Min(desiredDurationSeconds, options.MaximumDuration.TotalSeconds);
            var desiredDuration = TimeSpan.FromSeconds(desiredDurationSeconds);
            if (desiredDuration <= duration)
            {
                continue;
            }

            var leaveGap = gap > options.CloseGapBelow ? options.CloseGapBelow : TimeSpan.Zero;
            var availableExtension = gap - leaveGap;
            if (availableExtension > TimeSpan.Zero)
            {
                current.End += Min(desiredDuration - duration, availableExtension);
            }
        }
    }

    private static void ClampKaraokeCueGaps(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        SortKaraokeCues(cues);
        for (var index = 0; index < cues.Count - 1; index++)
        {
            var current = cues[index];
            var next = cues[index + 1];
            if (current.End > next.Start)
            {
                current.End = next.Start;
            }

            var gap = next.Start - current.End;
            if (gap > TimeSpan.Zero && gap < options.CloseGapBelow)
            {
                current.End = next.Start;
            }
        }
    }

    private static void ReindexKaraokeCues(List<KaraokeCue> cues)
    {
        for (var index = 0; index < cues.Count; index++)
        {
            cues[index].Id = index + 1;
        }
    }

    private static void SortKaraokeCues(List<KaraokeCue> cues)
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

    private static TimeSpan GetKaraokeCueDuration(KaraokeCue cue)
    {
        return cue.End > cue.Start ? cue.End - cue.Start : MinimumPositiveDuration;
    }

    private static string GetKaraokeCueText(KaraokeCue cue)
    {
        return JoinKaraokeWords(cue.Words);
    }

    private static string JoinKaraokeWords(IEnumerable<KaraokeCueWord> words)
    {
        return string.Join(" ", words.Select(word => NormalizeSegmentText(word.Text)).Where(text => text.Length > 0));
    }

    private static KaraokeRenderPreset CreateDefaultKaraokePreset(SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null)
    {
        var basePreset = ApplyPlacementToPreset(NormalizePreset(preset ?? KaraokeSubtitlePresets.NeonKaraoke), placement);
        return new KaraokeRenderPreset
        {
            ScriptTitle = "Karaoke subtitles",
            StyleName = basePreset.AssStyleName,
            PlayResX = basePreset.PlayResX,
            PlayResY = basePreset.PlayResY,
            WrapStyle = basePreset.WrapStyle,
            ScaledBorderAndShadow = basePreset.ScaledBorderAndShadow,
            FontFamily = basePreset.PrimaryFontFamily,
            FontSize = basePreset.FontSize,
            Bold = basePreset.Bold,
            Italic = basePreset.Italic,
            OutlineWidth = basePreset.OutlineWidth,
            ShadowDepth = basePreset.ShadowDepth,
            Alignment = basePreset.Alignment,
            MarginLeft = basePreset.MarginLeft,
            MarginRight = basePreset.MarginRight,
            MarginVertical = basePreset.MarginVertical,
            PositionX = basePreset.PositionX,
            PositionY = basePreset.PositionY,
            UseBackgroundBox = basePreset.UseBackgroundBox,
            PresentationAnimation = basePreset.PresentationAnimation,
            EntryFadeMilliseconds = basePreset.EntryFadeMilliseconds,
            ExitFadeMilliseconds = basePreset.ExitFadeMilliseconds,
            IntroScale = basePreset.IntroScale,
            TextTransform = basePreset.TextTransform,
            BaseColor = basePreset.FillColor,
            HighlightColor = basePreset.KaraokeHighlightColor,
            OutlineColor = basePreset.OutlineColor,
            ShadowColor = basePreset.ShadowColor
        };
    }

    private static string BuildAssDialogueLine(int layer, TimeSpan start, TimeSpan end, string styleName, string text)
    {
        return $"Dialogue: {layer},{FormatAssTimestamp(start)},{FormatAssTimestamp(end > start ? end : start + MinimumPositiveDuration)},{styleName},,0,0,0,,{text}";
    }

    private static string RenderKaraokeCueText(KaraokeCue cue, KaraokeRenderPreset preset)
    {
        var builder = new StringBuilder();
        var useInstantFill = preset.PresentationAnimation == SubtitlePresentationAnimation.None;

        for (var index = 0; index < cue.Words.Count; index++)
        {
            var word = cue.Words[index];
            var durationCentiseconds = Math.Max(1, (int)Math.Round((word.End - word.Start).TotalMilliseconds / 10d, MidpointRounding.AwayFromZero));
            var prefix = word.BreakBefore ? @"\N" : index > 0 ? " " : string.Empty;

            if (useInstantFill)
            {
                builder.Append(@"{\k")
                    .Append(durationCentiseconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('}');
            }
            else
            {
                builder.Append(@"{\kf")
                    .Append(durationCentiseconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('}');
            }

            builder.Append(prefix)
                .Append(EscapeAssText(word.Text));
        }

        return builder.ToString();
    }

    private static void RenderDropInKaraokeEvents(StringBuilder builder, KaraokeCue cue, KaraokeRenderPreset preset)
    {
        // DropIn uses standard \kf karaoke tags with SecondaryColour set to fully transparent
        // in the style definition. Words gradually fill from transparent to PrimaryColour as
        // karaoke progresses, creating the "words appearing one by one" effect.
        // Position and fade-out are handled by BuildAssCueOverrides (same as other karaoke modes).

        // Build position override — reuse the same logic as normal karaoke.
        var posOverride = BuildAssPositionOverride(preset.Alignment, preset.PositionX, preset.PositionY);

        // Build exit fade only (no entry fade — \kf handles per-word appearance).
        var exitFade = Math.Clamp(preset.ExitFadeMilliseconds, 0, 5000);
        var overrideTags = new StringBuilder();
        if (posOverride.Length > 0)
            overrideTags.Append(posOverride);
        if (exitFade > 0)
            overrideTags.Append(FormattableString.Invariant($@"\fad(0,{exitFade})"));

        var overridePrefix = overrideTags.Length > 0 ? $"{{{overrideTags}}}" : string.Empty;

        // Build karaoke text with \kf tags (gradual fill per word).
        var textBuilder = new StringBuilder();
        for (var index = 0; index < cue.Words.Count; index++)
        {
            var word = cue.Words[index];
            var durationCentiseconds = Math.Max(1, (int)Math.Round((word.End - word.Start).TotalMilliseconds / 10d, MidpointRounding.AwayFromZero));
            var prefix = word.BreakBefore ? @"\N" : index > 0 ? " " : string.Empty;

            textBuilder.Append(@"{\kf")
                .Append(durationCentiseconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('}');
            textBuilder.Append(prefix);
            textBuilder.Append(EscapeAssText(word.Text));
        }

        builder.AppendLine(BuildAssDialogueLine(0, cue.Start, cue.End, preset.StyleName, overridePrefix + textBuilder.ToString()));
    }

    private static string FormatAssTimestamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var centiseconds = value.Milliseconds / 10;
        return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{centiseconds:00}";
    }

    private static string EscapeAssText(string value)
    {
        return value
            .Replace("\\", @"\\", StringComparison.Ordinal)
            .Replace("{", "(", StringComparison.Ordinal)
            .Replace("}", ")", StringComparison.Ordinal);
    }

    private static string ToAssColor(SubtitleColor color)
    {
        return $"&H{color.Alpha:X2}{color.Blue:X2}{color.Green:X2}{color.Red:X2}&";
    }

    private static string BuildAssCueOverrides(SubtitleStylePreset preset, TimeSpan start, TimeSpan end)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var tags = new List<string>(2);
        var positionOverride = BuildAssPositionOverride(preset.Alignment, preset.PositionX, preset.PositionY);
        if (positionOverride.Length > 0)
        {
            tags.Add(positionOverride);
        }

        var animationOverride = BuildAssAnimationOverride(preset, start, end);
        if (animationOverride.Length > 0)
        {
            tags.Add(animationOverride);
        }

        return tags.Count == 0 ? string.Empty : $"{{{string.Join(string.Empty, tags)}}}";
    }

    private static string BuildAssCueOverrides(KaraokeRenderPreset preset, TimeSpan start, TimeSpan end)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var tags = new List<string>(2);
        var positionOverride = BuildAssPositionOverride(preset.Alignment, preset.PositionX, preset.PositionY);
        if (positionOverride.Length > 0)
        {
            tags.Add(positionOverride);
        }

        var animationOverride = BuildAssAnimationOverride(preset.PresentationAnimation, preset.EntryFadeMilliseconds, preset.ExitFadeMilliseconds, preset.IntroScale, start, end);
        if (animationOverride.Length > 0)
        {
            tags.Add(animationOverride);
        }

        return tags.Count == 0 ? string.Empty : $"{{{string.Join(string.Empty, tags)}}}";
    }

    private static string BuildAssPositionOverride(SubtitleVisualAlignment alignment, int? positionX, int? positionY)
    {
        if (!positionX.HasValue || !positionY.HasValue)
        {
            return string.Empty;
        }

        return FormattableString.Invariant($@"\an{GetAssAlignmentCode(alignment)}\pos({positionX.Value},{positionY.Value})");
    }

    private static string BuildAssAnimationOverride(SubtitleStylePreset preset, TimeSpan start, TimeSpan end)
    {
        return BuildAssAnimationOverride(preset.PresentationAnimation, preset.EntryFadeMilliseconds, preset.ExitFadeMilliseconds, preset.IntroScale, start, end);
    }

    private static string BuildAssAnimationOverride(SubtitlePresentationAnimation presentationAnimation, int entryFadeMilliseconds, int exitFadeMilliseconds, double introScale, TimeSpan start, TimeSpan end)
    {
        var builder = new StringBuilder();

        var entryFade = Math.Clamp(entryFadeMilliseconds, 0, 5000);
        var exitFade = Math.Clamp(exitFadeMilliseconds, 0, 5000);
        if (entryFade > 0 || exitFade > 0)
        {
            builder.Append(@"\fad(")
                .Append(entryFade)
                .Append(',')
                .Append(exitFade)
                .Append(')');
        }

        var wantsScaleAnimation = presentationAnimation is SubtitlePresentationAnimation.Pop or SubtitlePresentationAnimation.FadePop;
        introScale = introScale > 0 ? introScale : 1d;
        if (wantsScaleAnimation && Math.Abs(introScale - 1d) > 0.01d)
        {
            var scalePercent = Math.Max(1, (int)Math.Round(introScale * 100d, MidpointRounding.AwayFromZero));
            builder.Append(@"\fscx")
                .Append(scalePercent)
                .Append(@"\fscy")
                .Append(scalePercent)
                .Append(@"\t(0,160,\fscx100\fscy100)");
        }
        else if (wantsScaleAnimation)
        {
            builder.Append(@"\fscx108\fscy108\t(0,160,\fscx100\fscy100)");
        }

        if (builder.Length > 0 && start >= end)
        {
            return string.Empty;
        }

        return builder.ToString();
    }

    private static int GetAssAlignmentCode(SubtitleVisualAlignment alignment)
    {
        return alignment switch
        {
            SubtitleVisualAlignment.BottomLeft => 1,
            SubtitleVisualAlignment.BottomRight => 3,
            SubtitleVisualAlignment.MiddleLeft => 4,
            SubtitleVisualAlignment.Center => 5,
            SubtitleVisualAlignment.MiddleRight => 6,
            SubtitleVisualAlignment.TopLeft => 7,
            SubtitleVisualAlignment.TopCenter => 8,
            SubtitleVisualAlignment.TopRight => 9,
            _ => 2
        };
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

            normalizedSegments.Add(new TranscriptionSegment(segmentId++, start, end, text));
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
        return new SubtitleDraft(ToImmutableCues(cues), options, issues);
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

    private static SubtitleStylePreset NormalizePreset(SubtitleStylePreset? preset)
    {
        var source = preset ?? StyledSubtitlePresets.SocialImpact;
        var fontFallbacks = source.FontFamilyFallbacks is { Count: > 0 }
            ? source.FontFamilyFallbacks.ToArray()
            : [source.PrimaryFontFamily];

        return new SubtitleStylePreset
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Custom" : source.Name.Trim(),
            AssStyleName = string.IsNullOrWhiteSpace(source.AssStyleName) ? "Default" : source.AssStyleName.Trim(),
            ScriptTitle = string.IsNullOrWhiteSpace(source.ScriptTitle) ? "Styled subtitles" : source.ScriptTitle.Trim(),
            PlayResX = Math.Max(1, source.PlayResX),
            PlayResY = Math.Max(1, source.PlayResY),
            WrapStyle = Math.Clamp(source.WrapStyle, 0, 3),
            ScaledBorderAndShadow = source.ScaledBorderAndShadow,
            PrimaryFontFamily = string.IsNullOrWhiteSpace(source.PrimaryFontFamily) ? "Arial Black" : source.PrimaryFontFamily.Trim(),
            FontFamilyFallbacks = fontFallbacks,
            FontSize = source.FontSize > 0 ? source.FontSize : 72,
            Bold = source.Bold,
            Italic = source.Italic,
            TextTransform = source.TextTransform,
            FillColor = source.FillColor ?? SubtitleColor.White,
            OutlineColor = source.OutlineColor ?? SubtitleColor.Black,
            ShadowColor = source.ShadowColor ?? SubtitleColor.Black,
            KaraokeHighlightColor = source.KaraokeHighlightColor,
            UseBackgroundBox = source.UseBackgroundBox,
            PresentationAnimation = source.PresentationAnimation,
            EntryFadeMilliseconds = Math.Max(0, source.EntryFadeMilliseconds),
            ExitFadeMilliseconds = Math.Max(0, source.ExitFadeMilliseconds),
            IntroScale = source.IntroScale > 0 ? source.IntroScale : 1d,
            OutlineWidth = Math.Max(0, source.OutlineWidth),
            ShadowDepth = Math.Max(0, source.ShadowDepth),
            Alignment = source.Alignment,
            MarginLeft = Math.Max(0, source.MarginLeft),
            MarginRight = Math.Max(0, source.MarginRight),
            MarginVertical = Math.Max(0, source.MarginVertical),
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            MaxLines = Math.Max(1, source.MaxLines),
            MaxCharsPerLine = Math.Max(1, source.MaxCharsPerLine)
        };
    }

    private static SubtitleStylePreset ApplyPlacementToPreset(SubtitleStylePreset preset, SubtitlePlacementOptions? placement)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (placement is null)
        {
            return preset;
        }

        var normalizedX = Math.Clamp(placement.NormalizedX, 0d, 1d);
        var normalizedY = Math.Clamp(placement.NormalizedY, 0d, 1d);
        var width = Math.Max(1, preset.PlayResX);
        var height = Math.Max(1, preset.PlayResY);
        var safeMarginX = Math.Max(24, (int)Math.Round(width * 0.04d, MidpointRounding.AwayFromZero));
        var safeMarginY = Math.Max(24, (int)Math.Round(height * 0.04d, MidpointRounding.AwayFromZero));
        var alignment = ResolveVisualAlignment(normalizedX, normalizedY);

        return new SubtitleStylePreset
        {
            Name = preset.Name,
            AssStyleName = preset.AssStyleName,
            ScriptTitle = preset.ScriptTitle,
            PlayResX = preset.PlayResX,
            PlayResY = preset.PlayResY,
            WrapStyle = preset.WrapStyle,
            ScaledBorderAndShadow = preset.ScaledBorderAndShadow,
            PrimaryFontFamily = preset.PrimaryFontFamily,
            FontFamilyFallbacks = preset.FontFamilyFallbacks,
            FontSize = preset.FontSize,
            Bold = preset.Bold,
            Italic = preset.Italic,
            TextTransform = preset.TextTransform,
            FillColor = preset.FillColor,
            OutlineColor = preset.OutlineColor,
            ShadowColor = preset.ShadowColor,
            KaraokeHighlightColor = preset.KaraokeHighlightColor,
            UseBackgroundBox = preset.UseBackgroundBox,
            PresentationAnimation = preset.PresentationAnimation,
            EntryFadeMilliseconds = preset.EntryFadeMilliseconds,
            ExitFadeMilliseconds = preset.ExitFadeMilliseconds,
            IntroScale = preset.IntroScale,
            OutlineWidth = preset.OutlineWidth,
            ShadowDepth = preset.ShadowDepth,
            Alignment = alignment,
            MarginLeft = alignment is SubtitleVisualAlignment.BottomLeft or SubtitleVisualAlignment.MiddleLeft or SubtitleVisualAlignment.TopLeft
                ? Math.Max(safeMarginX, (int)Math.Round(normalizedX * width, MidpointRounding.AwayFromZero))
                : preset.MarginLeft,
            MarginRight = alignment is SubtitleVisualAlignment.BottomRight or SubtitleVisualAlignment.MiddleRight or SubtitleVisualAlignment.TopRight
                ? Math.Max(safeMarginX, (int)Math.Round((1d - normalizedX) * width, MidpointRounding.AwayFromZero))
                : preset.MarginRight,
            MarginVertical = alignment switch
            {
                SubtitleVisualAlignment.TopLeft or SubtitleVisualAlignment.TopCenter or SubtitleVisualAlignment.TopRight
                    => Math.Max(safeMarginY, (int)Math.Round(normalizedY * height, MidpointRounding.AwayFromZero)),
                SubtitleVisualAlignment.MiddleLeft or SubtitleVisualAlignment.Center or SubtitleVisualAlignment.MiddleRight
                    => 0,
                _ => Math.Max(safeMarginY, (int)Math.Round((1d - normalizedY) * height, MidpointRounding.AwayFromZero))
            },
            PositionX = Math.Clamp((int)Math.Round(normalizedX * width, MidpointRounding.AwayFromZero), 0, width),
            PositionY = Math.Clamp((int)Math.Round(normalizedY * height, MidpointRounding.AwayFromZero), 0, height),
            MaxLines = preset.MaxLines,
            MaxCharsPerLine = preset.MaxCharsPerLine
        };
    }

    private static SubtitleVisualAlignment ResolveVisualAlignment(double normalizedX, double normalizedY)
    {
        var horizontalBand = normalizedX switch
        {
            < 0.33d => -1,
            > 0.67d => 1,
            _ => 0
        };

        var verticalBand = normalizedY switch
        {
            < 0.33d => 1,
            > 0.67d => -1,
            _ => 0
        };

        return (verticalBand, horizontalBand) switch
        {
            (1, -1) => SubtitleVisualAlignment.TopLeft,
            (1, 0) => SubtitleVisualAlignment.TopCenter,
            (1, 1) => SubtitleVisualAlignment.TopRight,
            (0, -1) => SubtitleVisualAlignment.MiddleLeft,
            (0, 0) => SubtitleVisualAlignment.Center,
            (0, 1) => SubtitleVisualAlignment.MiddleRight,
            (-1, -1) => SubtitleVisualAlignment.BottomLeft,
            (-1, 0) => SubtitleVisualAlignment.BottomCenter,
            (-1, 1) => SubtitleVisualAlignment.BottomRight,
            _ => SubtitleVisualAlignment.BottomCenter
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

    private static void ValidateOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(path));
        }
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
                : ComputeEta(state.StartedAtUtc.Value, overallPercent)
        });
    }

    private static TimeSpan? ComputeEta(DateTimeOffset startedAtUtc, double overallPercent)
    {
        if (overallPercent < 0.02d)
        {
            return null;
        }

        if (overallPercent >= 1d)
        {
            return TimeSpan.Zero;
        }

        var elapsed = DateTimeOffset.UtcNow - startedAtUtc;
        if (elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var totalTicksEstimate = elapsed.Ticks / overallPercent;
        var remainingTicks = Math.Max(0d, totalTicksEstimate - elapsed.Ticks);
        return TimeSpan.FromTicks((long)remainingTicks);
    }

    private sealed class ProgressState
    {
        public DateTimeOffset? StartedAtUtc { get; set; }
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

    private sealed class KaraokeCue
    {
        public KaraokeCue(int id, TimeSpan start, TimeSpan end, List<KaraokeCueWord> words)
        {
            Id = id;
            Start = start;
            End = end;
            Words = words ?? throw new ArgumentNullException(nameof(words));
        }

        public int Id { get; set; }

        public TimeSpan Start { get; set; }

        public TimeSpan End { get; set; }

        public List<KaraokeCueWord> Words { get; }
    }

    private sealed class KaraokeCueWord
    {
        public KaraokeCueWord(string text, TimeSpan start, TimeSpan end)
        {
            Text = text ?? string.Empty;
            Start = start;
            End = end;
        }

        public string Text { get; set; }

        public TimeSpan Start { get; set; }

        public TimeSpan End { get; set; }

        public bool BreakBefore { get; set; }

        public KaraokeCueWord Clone()
        {
            return new KaraokeCueWord(Text, Start, End)
            {
                BreakBefore = BreakBefore
            };
        }
    }

    private sealed class KaraokeRenderPreset
    {
        public required string ScriptTitle { get; init; }

        public required string StyleName { get; init; }

        public int PlayResX { get; init; }

        public int PlayResY { get; init; }

        public int WrapStyle { get; init; }

        public bool ScaledBorderAndShadow { get; init; }

        public required string FontFamily { get; init; }

        public double FontSize { get; init; }

        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public double OutlineWidth { get; init; }

        public double ShadowDepth { get; init; }

        public SubtitleVisualAlignment Alignment { get; init; }

        public int MarginLeft { get; init; }

        public int MarginRight { get; init; }

        public int MarginVertical { get; init; }

        public int? PositionX { get; init; }

        public int? PositionY { get; init; }

        public bool UseBackgroundBox { get; init; }

        public SubtitlePresentationAnimation PresentationAnimation { get; init; }

        public int EntryFadeMilliseconds { get; init; }

        public int ExitFadeMilliseconds { get; init; }

        public double IntroScale { get; init; } = 1d;

        public SubtitleTextTransform TextTransform { get; init; }

        public required SubtitleColor BaseColor { get; init; }

        public required SubtitleColor HighlightColor { get; init; }

        public required SubtitleColor OutlineColor { get; init; }

        public required SubtitleColor ShadowColor { get; init; }
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
