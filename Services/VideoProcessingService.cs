using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services;

/// <summary>
/// Defines video and muxing operations backed by FFmpeg.
/// </summary>
public interface IVideoProcessingService
{
    /// <summary>
    /// Uses FFprobe metadata to estimate how a processing request will behave before executing FFmpeg.
    /// </summary>
    Task<VideoProcessingEstimate> EstimateProcessAsync(string inputPath, ProcessVideoOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a full video processing pipeline in a single FFmpeg invocation when possible.
    /// </summary>
    Task ProcessVideoAsync(string inputPath, string outputPath, ProcessVideoOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a full video processing pipeline and reports FFmpeg progress while it runs.
    /// </summary>
    Task ProcessVideoAsync(string inputPath, string outputPath, ProcessVideoOptions options, IProgress<VideoProcessingProgress> progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to repair a problematic media file by remuxing or re-encoding it into a clean output.
    /// </summary>
    Task RepairAsync(string inputPath, string outputPath, RepairOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the output container while preserving compatible codecs when possible.
    /// </summary>
    Task ChangeContainerAsync(string inputPath, string outputPath, VideoContainerFormat format, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resizes a video to the requested dimensions using the selected fit mode.
    /// </summary>
    Task ResizeAsync(string inputPath, string outputPath, VideoResizeOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-encodes a video using a compression preset.
    /// </summary>
    Task CompressAsync(string inputPath, string outputPath, VideoCompressionOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes video and/or audio codec selections.
    /// </summary>
    Task ChangeCodecAsync(string inputPath, string outputPath, CodecChangeOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trims a video between the requested timestamps.
    /// </summary>
    Task TrimAsync(string inputPath, string outputPath, TrimOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies fixed rotation and mirroring operations.
    /// </summary>
    Task RotateOrMirrorAsync(string inputPath, string outputPath, TransformOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes container and stream metadata from the output.
    /// </summary>
    Task RemoveMetadataAsync(string inputPath, string outputPath, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Muxes video with an external audio source.
    /// </summary>
    Task CombineWithAudioAsync(string inputPath, string outputPath, MuxAudioOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Muxes or burns subtitles into the output video.
    /// </summary>
    Task CombineWithSubtitlesAsync(string inputPath, string outputPath, MuxSubtitleOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the primary audio stream from a video into a standalone audio file inferred from the output extension.
    /// </summary>
    Task ExtractAudioAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default, IProgress<VideoProcessingProgress>? progress = null);
}

/// <summary>
/// Supported output containers for the video service.
/// </summary>
public enum VideoContainerFormat
{
    Mp4,
    Webm,
    Gif,
    Mkv,
    Mov,
    Avi
}

/// <summary>
/// Supported video codec selections.
/// </summary>
public enum VideoCodec
{
    H264,
    H265,
    Av1,
    Vp9,
    Vp8,
    Gif,
    Mpeg4
}

/// <summary>
/// Supported audio codec selections.
/// </summary>
public enum AudioCodec
{
    Aac,
    Opus,
    Vorbis,
    Mp3,
    Ac3,
    Flac,
    PcmS16Le
}

/// <summary>
/// Resize behavior used to fit into the requested output dimensions.
/// </summary>
public enum ResizeMode
{
    Stretch,
    CropToFill,
    PadToFit
}

/// <summary>
/// Compression quality presets mapped to codec-specific CRF defaults.
/// </summary>
public enum CompressionPreset
{
    VeryHigh,
    High,
    Balanced,
    SmallSize
}

/// <summary>
/// Subtitle application mode.
/// </summary>
public enum SubtitleMode
{
    SoftMux,
    BurnIn
}

/// <summary>
/// Normalized subtitle placement selected from the editor preview.
/// </summary>
public sealed class SubtitlePlacementOptions
{
    /// <summary>
    /// Horizontal subtitle anchor point normalized from 0 to 1.
    /// </summary>
    public double NormalizedX { get; init; } = 0.5d;

    /// <summary>
    /// Vertical subtitle anchor point normalized from 0 to 1.
    /// </summary>
    public double NormalizedY { get; init; } = 0.88d;
}

/// <summary>
/// Repair strategy used to recover broken timestamps, stale indexes, or damaged container metadata.
/// </summary>
public enum RepairMode
{
    Remux,
    Reencode
}

/// <summary>
/// Combined processing pipeline options.
/// </summary>
public sealed class ProcessVideoOptions
{
    /// <summary>
    /// Optional container/output settings.
    /// </summary>
    public VideoOutputOptions Output { get; init; } = new();

    /// <summary>
    /// Optional resize request.
    /// </summary>
    public VideoResizeOptions? Resize { get; init; }

    /// <summary>
    /// Optional compression preset.
    /// </summary>
    public VideoCompressionOptions? Compression { get; init; }

    /// <summary>
    /// Optional codec change request.
    /// </summary>
    public CodecChangeOptions? CodecChange { get; init; }

    /// <summary>
    /// Optional trim request.
    /// </summary>
    public TrimOptions? Trim { get; init; }

    /// <summary>
    /// Optional fixed transform request.
    /// </summary>
    public TransformOptions? Transform { get; init; }

    /// <summary>
    /// Removes metadata from the output container and streams.
    /// </summary>
    public bool RemoveMetadata { get; init; }

    /// <summary>
    /// Optional audio mux request.
    /// </summary>
    public MuxAudioOptions? AudioMux { get; init; }

    /// <summary>
    /// Optional audio-adjust request applied to final output audio.
    /// </summary>
    public AudioAdjustOptions? AudioAdjust { get; init; }

    /// <summary>
    /// Removes audio from the final video output when true.
    /// </summary>
    public bool RemoveAudio { get; init; }

    /// <summary>
    /// Optional subtitle mux/burn request.
    /// </summary>
    public MuxSubtitleOptions? SubtitleMux { get; init; }

    /// <summary>
    /// Optional audio denoise request. This is orchestrated as a post-processing stage by callers.
    /// </summary>
    public AudioDenoiseRequestOptions? AudioDenoise { get; init; }

    /// <summary>
    /// Optional repair strategy for damaged media inputs.
    /// </summary>
    public RepairOptions? Repair { get; init; }

    /// <summary>
    /// Returns true when any operation requires pixel re-encoding.
    /// </summary>
    public bool RequiresVideoFiltering =>
        Resize is not null ||
        (Transform is not null && !Transform.IsIdentity) ||
        SubtitleMux?.Mode == SubtitleMode.BurnIn ||
        Repair?.Mode == RepairMode.Reencode;

    /// <summary>
    /// Returns true when any audio filter must be applied to the final output audio stream.
    /// </summary>
    public bool RequiresAudioFiltering =>
        AudioAdjust is not null && !AudioAdjust.IsIdentity;
}

/// <summary>
/// High-level denoise request options captured from UI. Execution is delegated to <see cref="IVideoAudioDenoiseService"/>.
/// </summary>
public sealed class AudioDenoiseRequestOptions
{
    /// <summary>
    /// Enables denoise when true.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Denoise mode selection.
    /// </summary>
    public AudioDenoiseMode Mode { get; init; } = AudioDenoiseMode.Mono;

    /// <summary>
    /// Denoise strength from 0 to 100.
    /// </summary>
    public int Strength { get; init; } = 50;
}

/// <summary>
/// Container and stream defaults for the final output.
/// </summary>
public sealed class VideoOutputOptions
{
    /// <summary>
    /// Requested container. If null, container is inferred from <paramref name="outputPath"/> or preserved from input.
    /// </summary>
    public VideoContainerFormat? Format { get; init; }

    /// <summary>
    /// Optional explicit video codec to force on output.
    /// </summary>
    public VideoCodec? VideoCodec { get; init; }

    /// <summary>
    /// Optional explicit audio codec to force on output.
    /// </summary>
    public AudioCodec? AudioCodec { get; init; }
}

/// <summary>
/// Resize options for exact-dimension output.
/// </summary>
public sealed class VideoResizeOptions
{
    /// <summary>
    /// Target width in pixels. Must be positive.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Target height in pixels. Must be positive.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Resize fit mode.
    /// </summary>
    public ResizeMode Mode { get; init; } = ResizeMode.PadToFit;

    /// <summary>
    /// Padding color used only for <see cref="ResizeMode.PadToFit"/>.
    /// </summary>
    public string PadColor { get; init; } = "black";
}

/// <summary>
/// Compression options based on codec-specific CRF presets.
/// </summary>
public sealed class VideoCompressionOptions
{
    /// <summary>
    /// Compression preset.
    /// </summary>
    public CompressionPreset Preset { get; init; } = CompressionPreset.Balanced;

    /// <summary>
    /// Optional preferred video codec for compression.
    /// </summary>
    public VideoCodec? VideoCodec { get; init; }
}

/// <summary>
/// Explicit codec change request.
/// </summary>
public sealed class CodecChangeOptions
{
    /// <summary>
    /// Optional new video codec. Null preserves the current video codec when possible.
    /// </summary>
    public VideoCodec? VideoCodec { get; init; }

    /// <summary>
    /// Optional new audio codec. Null preserves the current audio codec when possible.
    /// </summary>
    public AudioCodec? AudioCodec { get; init; }
}

/// <summary>
/// Trim options using absolute start and end timestamps.
/// </summary>
public sealed class TrimOptions
{
    /// <summary>
    /// Trim start position.
    /// </summary>
    public TimeSpan Start { get; init; }

    /// <summary>
    /// Trim end position. Must be greater than <see cref="Start"/>.
    /// </summary>
    public TimeSpan End { get; init; }
}

/// <summary>
/// Rotation and mirroring options.
/// </summary>
public sealed class TransformOptions
{
    /// <summary>
    /// Rotation angle in degrees. Allowed values: 0, 90, 180, 270.
    /// </summary>
    public int RotationDegrees { get; init; }

    /// <summary>
    /// Mirrors around the vertical axis.
    /// </summary>
    public bool MirrorHorizontal { get; init; }

    /// <summary>
    /// Mirrors around the horizontal axis.
    /// </summary>
    public bool MirrorVertical { get; init; }

    /// <summary>
    /// Returns true when no transform is configured.
    /// </summary>
    public bool IsIdentity => RotationDegrees == 0 && !MirrorHorizontal && !MirrorVertical;
}

/// <summary>
/// Audio mux options.
/// </summary>
public sealed class MuxAudioOptions
{
    /// <summary>
    /// Path to an external audio file.
    /// </summary>
    public string AudioPath { get; init; } = string.Empty;

    /// <summary>
    /// Optional target audio codec for the muxed stream.
    /// </summary>
    public AudioCodec? AudioCodec { get; init; }

    /// <summary>
    /// Replaces existing audio streams when true.
    /// </summary>
    public bool ReplaceExistingAudio { get; init; } = true;

    /// <summary>
    /// Stops output when the shortest mapped stream ends.
    /// </summary>
    public bool UseShortestDuration { get; init; } = true;

    /// <summary>
    /// Marks the muxed audio stream as default when true.
    /// </summary>
    public bool SetAsDefault { get; init; } = true;
}

/// <summary>
/// Audio adjustment options applied to the final output audio stream.
/// </summary>
public sealed class AudioAdjustOptions
{
    /// <summary>
    /// Final output volume percentage. `100` preserves original loudness, `0` mutes, and `200` doubles volume.
    /// </summary>
    public int VolumePercent { get; init; } = 100;

    /// <summary>
    /// Applies a simple loudness normalization pass when true.
    /// </summary>
    public bool NormalizeLoudness { get; init; }

    /// <summary>
    /// Shifts audio in milliseconds relative to video. Positive delays audio; negative advances it.
    /// </summary>
    public int SyncOffsetMilliseconds { get; init; }

    /// <summary>
    /// Returns true when the adjustment leaves the audio unchanged.
    /// </summary>
    public bool IsIdentity =>
        VolumePercent == 100 &&
        !NormalizeLoudness &&
        SyncOffsetMilliseconds == 0;
}

/// <summary>
/// Subtitle mux or burn options.
/// </summary>
public sealed class MuxSubtitleOptions
{
    /// <summary>
    /// Path to the subtitle file.
    /// </summary>
    public string SubtitlePath { get; init; } = string.Empty;

    /// <summary>
    /// Subtitle application mode.
    /// </summary>
    public SubtitleMode Mode { get; init; } = SubtitleMode.SoftMux;

    /// <summary>
    /// Optional subtitle language tag for soft muxed output.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Optional subtitle track title for soft muxed output.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Marks the subtitle stream as default when true.
    /// </summary>
    public bool SetAsDefault { get; init; } = false;

    /// <summary>
    /// Optional subtitle placement chosen in the editor preview.
    /// </summary>
    public SubtitlePlacementOptions? Placement { get; init; }
}

/// <summary>
/// Repair options for damaged or partially broken media files.
/// </summary>
public sealed class RepairOptions
{
    /// <summary>
    /// Repair strategy. Remux preserves streams when possible. Reencode rewrites streams using safe defaults.
    /// </summary>
    public RepairMode Mode { get; init; } = RepairMode.Remux;

    /// <summary>
    /// Regenerates timestamps during repair.
    /// </summary>
    public bool RegeneratePresentationTimestamps { get; init; } = true;

    /// <summary>
    /// Ignores recoverable demux/decode issues.
    /// </summary>
    public bool IgnoreRecoverableErrors { get; init; } = true;

    /// <summary>
    /// Drops subtitle and other non-essential streams while repairing.
    /// </summary>
    public bool DropNonEssentialStreams { get; init; } = true;

    /// <summary>
    /// Removes metadata while repairing.
    /// </summary>
    public bool RemoveMetadata { get; init; } = true;
}

/// <summary>
/// FFprobe-backed estimate describing expected output shape and cost.
/// </summary>
public sealed class VideoProcessingEstimate
{
    /// <summary>
    /// Resolved output container.
    /// </summary>
    public required VideoContainerFormat OutputFormat { get; init; }

    /// <summary>
    /// Estimated output duration.
    /// </summary>
    public TimeSpan EstimatedDuration { get; init; }

    /// <summary>
    /// Estimated output width.
    /// </summary>
    public int EstimatedWidth { get; init; }

    /// <summary>
    /// Estimated output height.
    /// </summary>
    public int EstimatedHeight { get; init; }

    /// <summary>
    /// Estimated output size in bytes. Null when probe bitrate information is insufficient.
    /// </summary>
    public long? EstimatedOutputSizeBytes { get; init; }

    /// <summary>
    /// Indicates that the video stream is expected to be re-encoded.
    /// </summary>
    public bool RequiresVideoReencode { get; init; }

    /// <summary>
    /// Indicates that the audio stream is expected to be re-encoded.
    /// </summary>
    public bool RequiresAudioReencode { get; init; }

    /// <summary>
    /// Estimated output video codec.
    /// </summary>
    public required VideoCodec OutputVideoCodec { get; init; }

    /// <summary>
    /// Estimated output audio codec.
    /// </summary>
    public AudioCodec? OutputAudioCodec { get; init; }

    /// <summary>
    /// Human-readable notes describing estimate assumptions.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Represents a live FFmpeg progress snapshot for a running video job.
/// </summary>
public sealed class VideoProcessingProgress
{
    public double FractionComplete { get; init; }

    public TimeSpan ProcessedDuration { get; init; }

    public TimeSpan? TotalDuration { get; init; }

    public TimeSpan? EstimatedTimeRemaining { get; init; }

    public bool IsCompleted { get; init; }
}

/// <summary>
/// Rich FFmpeg failure information used for debugging service operations.
/// </summary>
public sealed class VideoProcessingException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception for a failed FFmpeg or FFprobe invocation.
    /// </summary>
    public VideoProcessingException(
        string message,
        string binaryPath,
        string commandLine,
        int? exitCode,
        string standardOutput,
        string standardError,
        string? probeJson,
        Exception? innerException = null)
        : base(message, innerException)
    {
        BinaryPath = binaryPath;
        CommandLine = commandLine;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        ProbeJson = probeJson;
    }

    /// <summary>
    /// Executed binary path.
    /// </summary>
    public string BinaryPath { get; }

    /// <summary>
    /// Fully formatted command line used for the failed process.
    /// </summary>
    public string CommandLine { get; }

    /// <summary>
    /// Process exit code when available.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>
    /// Captured standard output.
    /// </summary>
    public string StandardOutput { get; }

    /// <summary>
    /// Captured standard error.
    /// </summary>
    public string StandardError { get; }

    /// <summary>
    /// Probed media JSON captured before the failing operation, when available.
    /// </summary>
    public string? ProbeJson { get; }
}

/// <summary>
/// FFmpeg-backed implementation for file-based video processing.
/// </summary>
public sealed class VideoProcessingService : IVideoProcessingService
{
    private static readonly ConcurrentDictionary<string, string> VerifiedExecutableCache = new(StringComparer.OrdinalIgnoreCase);

    private const string FfprobeJsonArgs = "-v error -print_format json -show_streams -show_format";
    private static readonly object VideoEncoderPlanCacheLock = new();
    private static readonly Dictionary<string, VideoEncoderPlan> VideoEncoderPlanCache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<VideoProcessingEstimate> EstimateProcessAsync(string inputPath, ProcessVideoOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Output);

        ValidateInputPath(inputPath);
        ValidateOptions(options);

        var ffprobeCandidates = ResolveExecutableCandidates("ffprobe");
        var (inputInfo, _) = await ProbeAsync(ffprobeCandidates, inputPath, cancellationToken).ConfigureAwait(false);
        var outputFormat = ResolveOutputFormat(inputPath, inputPath, options.Output.Format);

        ValidateOptionsAgainstMedia(options, outputFormat, inputInfo);

        var streamPlan = ResolveStreamPlan(options, outputFormat, inputInfo);
        var (estimatedWidth, estimatedHeight) = EstimateOutputDimensions(inputInfo, options);
        var estimatedDuration = EstimateOutputDuration(inputInfo, options);
        var estimatedOutputSizeBytes = EstimateOutputSizeBytes(inputInfo, options, streamPlan, estimatedWidth, estimatedHeight, estimatedDuration);

        return new VideoProcessingEstimate
        {
            OutputFormat = outputFormat,
            EstimatedDuration = estimatedDuration,
            EstimatedWidth = estimatedWidth,
            EstimatedHeight = estimatedHeight,
            EstimatedOutputSizeBytes = estimatedOutputSizeBytes,
            RequiresVideoReencode = streamPlan.VideoNeedsEncoding,
            RequiresAudioReencode = streamPlan.AudioNeedsEncoding,
            OutputVideoCodec = streamPlan.VideoCodec,
            OutputAudioCodec = streamPlan.AudioCodec,
            Notes = BuildEstimateNotes(options, streamPlan, inputInfo, estimatedOutputSizeBytes)
        };
    }

    /// <inheritdoc />
    public Task ProcessVideoAsync(string inputPath, string outputPath, ProcessVideoOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Output);

        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateOptions(options);

        return ProcessVideoCoreAsync(inputPath, outputPath, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task ProcessVideoAsync(string inputPath, string outputPath, ProcessVideoOptions options, IProgress<VideoProcessingProgress> progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return ProcessVideoCoreAsync(inputPath, outputPath, options, cancellationToken, progress);
    }

    /// <inheritdoc />
    public Task RepairAsync(string inputPath, string outputPath, RepairOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            Repair = options,
            RemoveMetadata = options.RemoveMetadata,
            Output = new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ChangeContainerAsync(string inputPath, string outputPath, VideoContainerFormat format, CancellationToken cancellationToken = default)
    {
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            Output = new VideoOutputOptions
            {
                Format = format
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ResizeAsync(string inputPath, string outputPath, VideoResizeOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            Resize = options,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task CompressAsync(string inputPath, string outputPath, VideoCompressionOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            Compression = options,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ChangeCodecAsync(string inputPath, string outputPath, CodecChangeOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            CodecChange = options,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task TrimAsync(string inputPath, string outputPath, TrimOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            Trim = options,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RotateOrMirrorAsync(string inputPath, string outputPath, TransformOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            Transform = options,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveMetadataAsync(string inputPath, string outputPath, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            RemoveMetadata = true,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task CombineWithAudioAsync(string inputPath, string outputPath, MuxAudioOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            AudioMux = options,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task CombineWithSubtitlesAsync(string inputPath, string outputPath, MuxSubtitleOptions options, VideoOutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ProcessVideoAsync(inputPath, outputPath, new ProcessVideoOptions
        {
            SubtitleMux = options,
            Output = outputOptions ?? new VideoOutputOptions()
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ExtractAudioAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default, IProgress<VideoProcessingProgress>? progress = null)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);

        var ffmpegCandidates = ResolveExecutableCandidates("ffmpeg");
        var ffprobeCandidates = ResolveExecutableCandidates("ffprobe");
        var (inputInfo, probeJson) = await ProbeAsync(ffprobeCandidates, inputPath, cancellationToken).ConfigureAwait(false);

        if (inputInfo.PrimaryAudioStream is null)
        {
            throw new InvalidOperationException("Input file does not contain an audio stream to extract.");
        }

        var extractionTarget = ResolveAudioExtractionTarget(outputPath);
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-progress",
            "pipe:2",
            "-nostats",
            "-i",
            Path.GetFullPath(inputPath),
            "-vn",
            "-map",
            "0:a:0"
        };

        if (string.Equals(inputInfo.PrimaryAudioStream.CodecName, extractionTarget.ProbeCodecName, StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-c:a");
            args.Add("copy");
        }
        else
        {
            args.Add("-c:a");
            args.Add(extractionTarget.EncoderName);
        }

        args.Add(Path.GetFullPath(outputPath));

        var progressObserver = CreateProgressObserver(inputInfo.Duration ?? TimeSpan.Zero, progress);
        await RunProcessWithFallbackAsync(ffmpegCandidates, args, cancellationToken, probeJson, progressObserver).ConfigureAwait(false);
    }

    private static async Task ProcessVideoCoreAsync(string inputPath, string outputPath, ProcessVideoOptions options, CancellationToken cancellationToken, IProgress<VideoProcessingProgress>? progress = null)
    {
        var ffmpegCandidates = ResolveExecutableCandidates("ffmpeg");
        var ffprobeCandidates = ResolveExecutableCandidates("ffprobe");

        string? probeJson = null;
        MediaInfo? inputInfo = null;

        try
        {
            (inputInfo, probeJson) = await ProbeAsync(ffprobeCandidates, inputPath, cancellationToken).ConfigureAwait(false);

            var outputFormat = ResolveOutputFormat(inputPath, outputPath, options.Output.Format);
            var finalOutputPath = EnsureOutputExtension(outputPath, outputFormat);

            ValidateOptionsAgainstMedia(options, outputFormat, inputInfo);

            var streamPlan = ResolveStreamPlan(options, outputFormat, inputInfo);
            var videoEncoderPlan = await ResolveVideoEncoderPlanAsync(ffmpegCandidates, streamPlan, cancellationToken).ConfigureAwait(false);
            var ffmpegArgs = BuildFfmpegArguments(inputPath, finalOutputPath, outputFormat, options, inputInfo, streamPlan, videoEncoderPlan);
            var progressObserver = CreateProgressObserver(EstimateOutputDuration(inputInfo, options), progress);

            await RunProcessWithFallbackAsync(ffmpegCandidates, ffmpegArgs, cancellationToken, probeJson, progressObserver).ConfigureAwait(false);
        }
        catch (VideoProcessingException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new VideoProcessingException(
                "Video processing failed before FFmpeg completed.",
                ffmpegCandidates[0],
                ffmpegCandidates[0],
                null,
                string.Empty,
                ex.Message,
                probeJson,
                ex);
        }
    }

    private static List<string> BuildFfmpegArguments(string inputPath, string outputPath, VideoContainerFormat outputFormat, ProcessVideoOptions options, MediaInfo inputInfo, StreamPlan streamPlan, VideoEncoderPlan videoEncoderPlan)
    {
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-progress",
            "pipe:2",
            "-nostats"
        };

        ApplyRepairInputArguments(args, options.Repair);

        if (options.Trim is not null)
        {
            args.Add("-ss");
            args.Add(ToFfmpegTimestamp(options.Trim.Start));
        }

        args.Add("-i");
        args.Add(Path.GetFullPath(inputPath));

        if (options.AudioMux is not null)
        {
            args.Add("-i");
            args.Add(Path.GetFullPath(options.AudioMux.AudioPath));
        }

        var shouldBurnInAssFallback = ShouldBurnInAssSubtitleFallback(options, outputFormat);

        if (options.SubtitleMux is { Mode: SubtitleMode.SoftMux } && !shouldBurnInAssFallback)
        {
            args.Add("-i");
            args.Add(Path.GetFullPath(options.SubtitleMux.SubtitlePath));
        }

        if (options.Trim is not null)
        {
            var duration = options.Trim.End - options.Trim.Start;
            args.Add("-t");
            args.Add(ToFfmpegTimestamp(duration));
        }

        var videoFilterChain = BuildVideoFilter(options, outputFormat, inputInfo);
        var audioFilterChain = BuildAudioFilter(options);
        var audioStreamIndex = options.AudioMux is not null ? 1 : -1;
        var subtitleStreamIndex = options.SubtitleMux is { Mode: SubtitleMode.SoftMux } && !shouldBurnInAssFallback
            ? (options.AudioMux is not null ? 2 : 1)
            : -1;

        AddMappingArguments(args, options, inputInfo, audioStreamIndex, subtitleStreamIndex, shouldBurnInAssFallback);

        if (!string.IsNullOrWhiteSpace(videoFilterChain))
        {
            args.Add("-vf");
            args.Add(videoFilterChain);
        }

        if (!string.IsNullOrWhiteSpace(audioFilterChain))
        {
            args.Add("-af");
            args.Add(audioFilterChain);
        }

        ApplyVideoCodecArguments(args, streamPlan, videoEncoderPlan);
        ApplyAudioCodecArguments(args, streamPlan, options);
        ApplySubtitleCodecArguments(args, streamPlan, options, outputFormat);
        ApplyMetadataArguments(args, options, inputInfo);

        if (options.AudioMux is not null && options.AudioMux.UseShortestDuration)
        {
            args.Add("-shortest");
        }

        args.Add(Path.GetFullPath(outputPath));

        return args;
    }

    private static void AddMappingArguments(List<string> args, ProcessVideoOptions options, MediaInfo inputInfo, int audioMuxInputIndex, int subtitleInputIndex, bool shouldBurnInAssFallback)
    {
        args.Add("-map");
        args.Add("0:v:0");

        if (options.AudioMux is not null)
        {
            if (!options.AudioMux.ReplaceExistingAudio && inputInfo.AudioStreams.Count > 0)
            {
                args.Add("-map");
                args.Add("0:a?");
            }

            args.Add("-map");
            args.Add($"{audioMuxInputIndex}:a:0");
        }
        else if (!options.RemoveAudio && inputInfo.AudioStreams.Count > 0)
        {
            args.Add("-map");
            args.Add("0:a?");
        }

        if (options.SubtitleMux is { Mode: SubtitleMode.SoftMux } && !shouldBurnInAssFallback)
        {
            args.Add("-map");
            args.Add($"{subtitleInputIndex}:s:0");
        }
        else if (!ShouldDropSubtitleStreams(options) && inputInfo.SubtitleStreams.Count > 0)
        {
            args.Add("-map");
            args.Add("0:s?");
        }
    }

    private static string? BuildVideoFilter(ProcessVideoOptions options, VideoContainerFormat outputFormat, MediaInfo inputInfo)
    {
        var filters = new List<string>();

        var shouldBurnInAssFallback = ShouldBurnInAssSubtitleFallback(options, outputFormat);
        if (options.SubtitleMux is { } subtitleOptions && (subtitleOptions.Mode == SubtitleMode.BurnIn || shouldBurnInAssFallback))
        {
            var (estimatedWidth, estimatedHeight) = EstimateOutputDimensions(inputInfo, options);
            filters.Add(BuildSubtitleFilter(subtitleOptions, estimatedWidth, estimatedHeight));
        }

        if (options.Transform is not null && !options.Transform.IsIdentity)
        {
            filters.AddRange(BuildTransformFilters(options.Transform));
        }

        if (options.Resize is not null)
        {
            filters.Add(BuildResizeFilter(options.Resize));
        }

        return filters.Count == 0
            ? null
            : string.Join(",", filters);
    }

    private static string? BuildAudioFilter(ProcessVideoOptions options)
    {
        var filters = new List<string>();

        if (options.AudioAdjust is not null && !options.AudioAdjust.IsIdentity)
        {
            if (options.AudioAdjust.NormalizeLoudness)
            {
                filters.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
            }

            if (options.AudioAdjust.VolumePercent != 100)
            {
                filters.Add(FormattableString.Invariant($"volume={options.AudioAdjust.VolumePercent / 100d:0.##}"));
            }

            if (options.AudioAdjust.SyncOffsetMilliseconds > 0)
            {
                filters.Add(FormattableString.Invariant($"adelay={options.AudioAdjust.SyncOffsetMilliseconds}:all=true"));
            }
            else if (options.AudioAdjust.SyncOffsetMilliseconds < 0)
            {
                filters.Add(FormattableString.Invariant($"atrim=start={Math.Abs(options.AudioAdjust.SyncOffsetMilliseconds) / 1000d:0.###},asetpts=PTS-STARTPTS"));
            }
        }

        return filters.Count == 0
            ? null
            : string.Join(",", filters);
    }

    private static string BuildSubtitleFilter(MuxSubtitleOptions subtitleOptions, int width, int height)
    {
        var builder = new StringBuilder();
        builder.Append("subtitles='");
        builder.Append(EscapeSubtitleFilterPath(subtitleOptions.SubtitlePath));
        builder.Append('\'');

        var forceStyle = BuildSubtitleForceStyle(subtitleOptions.Placement, width, height);
        if (!string.IsNullOrWhiteSpace(forceStyle))
        {
            builder.Append(":force_style='");
            builder.Append(forceStyle);
            builder.Append('\'');
        }

        return builder.ToString();
    }

    private static string? BuildSubtitleForceStyle(SubtitlePlacementOptions? placement, int width, int height)
    {
        if (placement is null || width <= 0 || height <= 0)
        {
            return null;
        }

        var normalizedX = Math.Clamp(placement.NormalizedX, 0d, 1d);
        var normalizedY = Math.Clamp(placement.NormalizedY, 0d, 1d);
        var alignment = ResolveAssAlignment(normalizedX, normalizedY);
        var marginVertical = ResolveAssVerticalMargin(normalizedY, height);
        var marginLeft = ResolveAssHorizontalMargin(normalizedX, width, isLeftAligned: alignment is 1 or 4 or 7);
        var marginRight = ResolveAssHorizontalMargin(normalizedX, width, isLeftAligned: false, isRightAligned: alignment is 3 or 6 or 9);

        return FormattableString.Invariant(
            $"Alignment={alignment},MarginV={marginVertical},MarginL={marginLeft},MarginR={marginRight}");
    }

    private static int ResolveAssAlignment(double normalizedX, double normalizedY)
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
            (1, -1) => 7,
            (1, 0) => 8,
            (1, 1) => 9,
            (0, -1) => 4,
            (0, 0) => 5,
            (0, 1) => 6,
            (-1, -1) => 1,
            (-1, 0) => 2,
            (-1, 1) => 3,
            _ => 2
        };
    }

    private static int ResolveAssVerticalMargin(double normalizedY, int height)
    {
        var safeMargin = Math.Max(24, (int)Math.Round(height * 0.04d, MidpointRounding.AwayFromZero));
        if (normalizedY < 0.33d)
        {
            return Math.Max(safeMargin, (int)Math.Round(height * normalizedY, MidpointRounding.AwayFromZero));
        }

        if (normalizedY > 0.67d)
        {
            return Math.Max(safeMargin, (int)Math.Round(height * (1d - normalizedY), MidpointRounding.AwayFromZero));
        }

        return 0;
    }

    private static int ResolveAssHorizontalMargin(double normalizedX, int width, bool isLeftAligned, bool isRightAligned = false)
    {
        var safeMargin = Math.Max(24, (int)Math.Round(width * 0.04d, MidpointRounding.AwayFromZero));
        if (isLeftAligned)
        {
            return Math.Max(safeMargin, (int)Math.Round(width * normalizedX, MidpointRounding.AwayFromZero));
        }

        if (isRightAligned)
        {
            return Math.Max(safeMargin, (int)Math.Round(width * (1d - normalizedX), MidpointRounding.AwayFromZero));
        }

        return 0;
    }

    private static IEnumerable<string> BuildTransformFilters(TransformOptions options)
    {
        if (options.RotationDegrees != 0)
        {
            yield return options.RotationDegrees switch
            {
                90 => "transpose=clock",
                180 => "hflip,vflip",
                270 => "transpose=cclock",
                _ => throw new ArgumentOutOfRangeException(nameof(options.RotationDegrees), "Rotation angle must be 0, 90, 180 or 270 degrees.")
            };
        }

        if (options.MirrorHorizontal)
        {
            yield return "hflip";
        }

        if (options.MirrorVertical)
        {
            yield return "vflip";
        }
    }

    private static string BuildResizeFilter(VideoResizeOptions options)
    {
        return options.Mode switch
        {
            ResizeMode.Stretch => FormattableString.Invariant($"scale={options.Width}:{options.Height}"),
            ResizeMode.CropToFill => FormattableString.Invariant($"scale={options.Width}:{options.Height}:force_original_aspect_ratio=increase,crop={options.Width}:{options.Height}"),
            ResizeMode.PadToFit => FormattableString.Invariant($"scale={options.Width}:{options.Height}:force_original_aspect_ratio=decrease,pad={options.Width}:{options.Height}:(ow-iw)/2:(oh-ih)/2:color={options.PadColor}"),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Mode), options.Mode, "Unsupported resize mode.")
        };
    }

    private static StreamPlan ResolveStreamPlan(ProcessVideoOptions options, VideoContainerFormat outputFormat, MediaInfo inputInfo)
    {
        var containerDefaultVideo = GetDefaultVideoCodec(outputFormat);
        var containerDefaultAudio = GetDefaultAudioCodec(outputFormat);

        var inputVideoCodec = MapVideoCodec(inputInfo.PrimaryVideoStream?.CodecName);
        var inputAudioCodec = MapAudioCodec(inputInfo.PrimaryAudioStream?.CodecName);

        var requestedVideoCodec =
            options.Output.VideoCodec ??
            options.CodecChange?.VideoCodec ??
            options.Compression?.VideoCodec;

        var requestedAudioCodec =
            options.RemoveAudio && options.AudioMux is null
                ? null
                : options.Output.AudioCodec ??
                  options.CodecChange?.AudioCodec ??
                  options.AudioMux?.AudioCodec;

        var requiresVideoEncode =
            options.RequiresVideoFiltering ||
            ShouldBurnInAssSubtitleFallback(options, outputFormat) ||
            options.Trim is not null ||
            options.Compression is not null ||
            requestedVideoCodec.HasValue;

        var requiresAudioEncode =
            requestedAudioCodec.HasValue ||
            (options.AudioMux is not null && options.AudioMux.AudioCodec.HasValue) ||
            options.RequiresAudioFiltering;

        var desiredVideoCodec = requestedVideoCodec ??
            (IsContainerCompatible(outputFormat, inputVideoCodec, null)
                ? inputVideoCodec ?? containerDefaultVideo
                : containerDefaultVideo);

        var desiredAudioCodec = options.RemoveAudio && options.AudioMux is null
            ? null
            : requestedAudioCodec ??
              (options.AudioMux is not null
                  ? (containerDefaultAudio ?? inputAudioCodec)
                  : (IsContainerCompatible(outputFormat, null, inputAudioCodec)
                      ? inputAudioCodec
                      : containerDefaultAudio));

        if (options.Repair?.Mode == RepairMode.Reencode)
        {
            requiresVideoEncode = true;
            requiresAudioEncode = desiredAudioCodec.HasValue;
        }

        if (desiredVideoCodec == VideoCodec.Gif)
        {
            requiresVideoEncode = true;
            desiredAudioCodec = null;
        }

        if (!IsContainerCompatible(outputFormat, desiredVideoCodec, desiredAudioCodec))
        {
            throw new NotSupportedException($"Container {outputFormat} does not support video codec {desiredVideoCodec} with audio codec {desiredAudioCodec}.");
        }

        var allowFullStreamCopy =
            !requiresVideoEncode &&
            !requiresAudioEncode &&
            options.SubtitleMux is null &&
            !options.RemoveMetadata &&
            options.AudioMux is null &&
            options.Trim is null &&
            options.Repair is null &&
            !options.RemoveAudio;

        if (allowFullStreamCopy)
        {
            return new StreamPlan
            {
                CopyAllStreams = true,
                VideoCodec = desiredVideoCodec,
                AudioCodec = desiredAudioCodec,
                SubtitleCodec = null,
                VideoNeedsEncoding = false,
                AudioNeedsEncoding = false,
                SubtitleNeedsEncoding = inputInfo.SubtitleStreams.Count > 0 && !ShouldDropSubtitleStreams(options),
                VideoCrf = null
            };
        }

        return new StreamPlan
        {
            CopyAllStreams = false,
            VideoCodec = desiredVideoCodec,
            AudioCodec = desiredAudioCodec,
            SubtitleCodec = ResolveSubtitleCodec(options.SubtitleMux, outputFormat),
            VideoNeedsEncoding = requiresVideoEncode || !string.Equals(inputInfo.PrimaryVideoStream?.CodecName, GetProbeCodecName(desiredVideoCodec), StringComparison.OrdinalIgnoreCase),
            AudioNeedsEncoding = desiredAudioCodec.HasValue && (requiresAudioEncode || options.AudioMux is not null || !string.Equals(inputInfo.PrimaryAudioStream?.CodecName, GetProbeCodecName(desiredAudioCodec.Value), StringComparison.OrdinalIgnoreCase)),
            SubtitleNeedsEncoding = options.SubtitleMux is { Mode: SubtitleMode.SoftMux } || (inputInfo.SubtitleStreams.Count > 0 && !ShouldDropSubtitleStreams(options)),
            VideoCrf = options.Compression is not null ? GetCrf(options.Compression.Preset, desiredVideoCodec) : null
        };
    }

    private static void ApplyVideoCodecArguments(List<string> args, StreamPlan plan, VideoEncoderPlan encoderPlan)
    {
        if (plan.CopyAllStreams)
        {
            args.Add("-c");
            args.Add("copy");
            return;
        }

        if (!plan.VideoNeedsEncoding)
        {
            args.Add("-c:v");
            args.Add("copy");
            return;
        }

        args.Add("-c:v");
        args.Add(encoderPlan.EncoderName);

        if (!encoderPlan.IsHardwareAccelerated && plan.VideoCodec != VideoCodec.Gif && plan.VideoCrf.HasValue)
        {
            args.Add("-crf");
            args.Add(plan.VideoCrf.Value.ToString(CultureInfo.InvariantCulture));
        }

        switch (plan.VideoCodec)
        {
            case VideoCodec.H264:
            case VideoCodec.H265:
            case VideoCodec.Av1:
            case VideoCodec.Vp8:
            case VideoCodec.Vp9:
                args.Add("-pix_fmt");
                args.Add("yuv420p");
                break;
        }
    }

    private static void ApplyAudioCodecArguments(List<string> args, StreamPlan plan, ProcessVideoOptions options)
    {
        if (plan.CopyAllStreams)
        {
            return;
        }

        if (!plan.AudioCodec.HasValue)
        {
            args.Add("-an");
            return;
        }

        if (!plan.AudioNeedsEncoding && options.AudioMux is null)
        {
            args.Add("-c:a");
            args.Add("copy");
        }
        else
        {
            args.Add("-c:a");
            args.Add(GetAudioEncoder(plan.AudioCodec.Value));
        }

        if (options.AudioMux is not null && options.AudioMux.SetAsDefault)
        {
            args.Add("-disposition:a:0");
            args.Add("default");
        }
    }

    private static void ApplySubtitleCodecArguments(List<string> args, StreamPlan plan, ProcessVideoOptions options, VideoContainerFormat outputFormat)
    {
        if (plan.CopyAllStreams)
        {
            return;
        }

        if (options.SubtitleMux is null)
        {
            if (ShouldDropSubtitleStreams(options))
            {
                args.Add("-sn");
            }
            else if (plan.SubtitleNeedsEncoding)
            {
                args.Add("-c:s");
                args.Add("copy");
            }

            return;
        }

        if (options.SubtitleMux.Mode == SubtitleMode.BurnIn || ShouldBurnInAssSubtitleFallback(options, outputFormat))
        {
            args.Add("-sn");
            return;
        }

        if (plan.SubtitleCodec is null)
        {
            throw new NotSupportedException($"Soft subtitle muxing is not supported for output container {outputFormat}.");
        }

        args.Add("-c:s");
        args.Add(plan.SubtitleCodec);

        if (!string.IsNullOrWhiteSpace(options.SubtitleMux.Language))
        {
            args.Add("-metadata:s:s:0");
            args.Add($"language={options.SubtitleMux.Language}");
        }

        if (!string.IsNullOrWhiteSpace(options.SubtitleMux.Title))
        {
            args.Add("-metadata:s:s:0");
            args.Add($"title={options.SubtitleMux.Title}");
        }

        if (options.SubtitleMux.SetAsDefault)
        {
            args.Add("-disposition:s:0");
            args.Add("default");
        }
    }

    private static void ApplyMetadataArguments(List<string> args, ProcessVideoOptions options, MediaInfo inputInfo)
    {
        if (options.RemoveMetadata)
        {
            args.Add("-map_metadata");
            args.Add("-1");
            args.Add("-map_chapters");
            args.Add("-1");
        }
    }

    private static async Task<VideoEncoderPlan> ResolveVideoEncoderPlanAsync(IReadOnlyList<string> ffmpegCandidates, StreamPlan streamPlan, CancellationToken cancellationToken)
    {
        var cacheKey = BuildVideoEncoderCacheKey(ffmpegCandidates, streamPlan);
        lock (VideoEncoderPlanCacheLock)
        {
            if (VideoEncoderPlanCache.TryGetValue(cacheKey, out var cachedPlan))
            {
                return cachedPlan;
            }
        }

        VideoEncoderPlan resolvedPlan;
        if (!CanUseHardwareEncoding(streamPlan))
        {
            resolvedPlan = CreateSoftwareVideoEncoderPlan(streamPlan.VideoCodec);
        }
        else
        {
            resolvedPlan = await DetectWorkingHardwareEncoderPlanAsync(ffmpegCandidates, streamPlan.VideoCodec, cancellationToken).ConfigureAwait(false)
                ?? CreateSoftwareVideoEncoderPlan(streamPlan.VideoCodec);
        }

        lock (VideoEncoderPlanCacheLock)
        {
            VideoEncoderPlanCache[cacheKey] = resolvedPlan;
        }

        return resolvedPlan;
    }

    private static async Task<VideoEncoderPlan?> DetectWorkingHardwareEncoderPlanAsync(IReadOnlyList<string> ffmpegCandidates, VideoCodec codec, CancellationToken cancellationToken)
    {
        foreach (var encoderName in GetHardwareEncoderCandidates(codec))
        {
            if (await IsVideoEncoderUsableAsync(ffmpegCandidates, encoderName, cancellationToken).ConfigureAwait(false))
            {
                return new VideoEncoderPlan(encoderName, IsHardwareAccelerated: true);
            }
        }

        return null;
    }

    private static async Task<bool> IsVideoEncoderUsableAsync(IReadOnlyList<string> ffmpegCandidates, string encoderName, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            "color=c=black:s=64x64:d=0.1",
            "-frames:v",
            "1",
            "-an",
            "-c:v",
            encoderName,
            "-f",
            "null",
            "-"
        };

        var result = await TryRunProcessWithFallbackAsync(ffmpegCandidates, args, cancellationToken).ConfigureAwait(false);
        return result?.ExitCode == 0;
    }

    private static bool CanUseHardwareEncoding(StreamPlan streamPlan)
    {
        return streamPlan.VideoNeedsEncoding &&
               !streamPlan.CopyAllStreams &&
               !streamPlan.VideoCrf.HasValue &&
               streamPlan.VideoCodec is VideoCodec.H264 or VideoCodec.H265 or VideoCodec.Av1;
    }

    internal static VideoEncoderPlan CreateVideoEncoderPlan(VideoCodec codec, IReadOnlySet<string> workingHardwareEncoders, bool preferHardwareEncoding)
    {
        if (preferHardwareEncoding)
        {
            foreach (var encoderName in GetHardwareEncoderCandidates(codec))
            {
                if (workingHardwareEncoders.Contains(encoderName))
                {
                    return new VideoEncoderPlan(encoderName, IsHardwareAccelerated: true);
                }
            }
        }

        return CreateSoftwareVideoEncoderPlan(codec);
    }

    internal static IReadOnlyList<string> GetHardwareEncoderCandidates(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => ["h264_nvenc", "h264_amf", "h264_qsv"],
            VideoCodec.H265 => ["hevc_nvenc", "hevc_amf", "hevc_qsv"],
            VideoCodec.Av1 => ["av1_nvenc", "av1_amf", "av1_qsv"],
            _ => []
        };
    }

    private static VideoEncoderPlan CreateSoftwareVideoEncoderPlan(VideoCodec codec) => new(GetVideoEncoder(codec), IsHardwareAccelerated: false);

    private static string BuildVideoEncoderCacheKey(IReadOnlyList<string> ffmpegCandidates, StreamPlan streamPlan)
    {
        return $"{string.Join("|", ffmpegCandidates)}::{streamPlan.VideoCodec}::{streamPlan.VideoNeedsEncoding}::{streamPlan.CopyAllStreams}::{streamPlan.VideoCrf.HasValue}";
    }

    private static void ValidateOptions(ProcessVideoOptions options)
    {
        if (options.Resize is not null)
        {
            if (options.Resize.Width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.Resize.Width), "Width must be greater than 0.");
            }

            if (options.Resize.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.Resize.Height), "Height must be greater than 0.");
            }
        }

        if (options.Transform is not null &&
            options.Transform.RotationDegrees is not 0 and not 90 and not 180 and not 270)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Transform.RotationDegrees), "Rotation angle must be 0, 90, 180 or 270 degrees.");
        }

        if (options.AudioAdjust is not null &&
            (options.AudioAdjust.VolumePercent < 0 || options.AudioAdjust.VolumePercent > 200))
        {
            throw new ArgumentOutOfRangeException(nameof(options.AudioAdjust.VolumePercent), "Audio volume percent must be between 0 and 200.");
        }

        if (options.AudioAdjust is not null &&
            Math.Abs(options.AudioAdjust.SyncOffsetMilliseconds) > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(options.AudioAdjust.SyncOffsetMilliseconds), "Audio sync offset must be between -5000 and 5000 milliseconds.");
        }

        if (options.Trim is not null && options.Trim.End <= options.Trim.Start)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Trim.End), "Trim end time must be greater than start time.");
        }

        if (options.AudioMux is not null)
        {
            ValidateInputPath(options.AudioMux.AudioPath);
        }

        if (options.SubtitleMux is not null)
        {
            ValidateInputPath(options.SubtitleMux.SubtitlePath);
        }

        if (options.Repair is not null && options.SubtitleMux is not null)
        {
            throw new InvalidOperationException("Repair mode cannot be combined with subtitle muxing in the same request.");
        }

        if (options.RemoveAudio && options.AudioMux is not null)
        {
            throw new InvalidOperationException("Remove audio cannot be combined with external audio muxing.");
        }
    }

    private static void ValidateOptionsAgainstMedia(ProcessVideoOptions options, VideoContainerFormat outputFormat, MediaInfo inputInfo)
    {
        if (inputInfo.PrimaryVideoStream is null)
        {
            throw new InvalidOperationException("Input file does not contain a video stream.");
        }

        if (options.Trim is not null &&
            inputInfo.Duration.HasValue &&
            options.Trim.End > inputInfo.Duration.Value.Add(TimeSpan.FromMilliseconds(1)))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Trim.End), "Trim end time exceeds the input duration.");
        }

        if (options.RemoveAudio && options.AudioAdjust is not null && !options.AudioAdjust.IsIdentity)
        {
            throw new InvalidOperationException("Audio adjustments cannot be applied when audio is removed from the final output.");
        }

        if (options.SubtitleMux is { Mode: SubtitleMode.BurnIn })
        {
            var extension = Path.GetExtension(options.SubtitleMux.SubtitlePath);
            if (!SupportedBurnInSubtitleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Burn-in subtitles require a text subtitle file such as .srt, .ass, .ssa or .vtt.");
            }
        }

        if (options.SubtitleMux is { Mode: SubtitleMode.SoftMux } &&
            ResolveSubtitleCodec(options.SubtitleMux, outputFormat) is null)
        {
            throw new NotSupportedException($"Soft subtitle muxing is not supported for {outputFormat} with subtitle file '{options.SubtitleMux.SubtitlePath}'.");
        }

        var targetVideoCodec =
            options.Output.VideoCodec ??
            options.CodecChange?.VideoCodec ??
            options.Compression?.VideoCodec ??
            MapVideoCodec(inputInfo.PrimaryVideoStream.CodecName) ??
            GetDefaultVideoCodec(outputFormat);

        var targetAudioCodec =
            options.Output.AudioCodec ??
            options.CodecChange?.AudioCodec ??
            options.AudioMux?.AudioCodec ??
            MapAudioCodec(inputInfo.PrimaryAudioStream?.CodecName) ??
            GetDefaultAudioCodec(outputFormat);

        if (!IsContainerCompatible(outputFormat, targetVideoCodec, targetAudioCodec))
        {
            throw new NotSupportedException($"Container {outputFormat} does not support video codec {targetVideoCodec} with audio codec {targetAudioCodec}.");
        }
    }

    private static void ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Input file was not found.", path);
        }
    }

    private static void ValidateOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(path));
        }
    }

    private static async Task<(MediaInfo Info, string Json)> ProbeAsync(IReadOnlyList<string> ffprobeCandidates, string inputPath, CancellationToken cancellationToken)
    {
        var args = new List<string>(SplitArguments(FfprobeJsonArgs))
        {
            Path.GetFullPath(inputPath)
        };

        var result = await RunProcessWithFallbackAsync(ffprobeCandidates, args, cancellationToken, probeJson: null).ConfigureAwait(false);

        try
        {
            var info = ParseMediaInfo(result.StandardOutput);
            return (info, result.StandardOutput);
        }
        catch (Exception ex)
        {
            throw new VideoProcessingException(
                "FFprobe returned output that could not be parsed.",
                ffprobeCandidates[0],
                FormatCommandLine(ffprobeCandidates[0], args),
                result.ExitCode,
                result.StandardOutput,
                result.StandardError,
                result.StandardOutput,
                ex);
        }
    }

    private static MediaInfo ParseMediaInfo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = new List<MediaStreamInfo>();

        if (root.TryGetProperty("streams", out var streamsElement))
        {
            foreach (var stream in streamsElement.EnumerateArray())
            {
                var width = TryGetInt(stream, "width");
                var height = TryGetInt(stream, "height");
                streams.Add(new MediaStreamInfo
                {
                    Index = TryGetInt(stream, "index") ?? 0,
                    CodecType = TryGetString(stream, "codec_type"),
                    CodecName = TryGetString(stream, "codec_name"),
                    Width = width,
                    Height = height,
                    BitRate = TryGetLong(stream, "bit_rate"),
                    Tags = ReadTags(stream)
                });
            }
        }

        TimeSpan? duration = null;
        Dictionary<string, string> formatTags = new(StringComparer.OrdinalIgnoreCase);
        long? formatBitRate = null;

        if (root.TryGetProperty("format", out var formatElement))
        {
            var durationString = TryGetString(formatElement, "duration");
            if (durationString is not null &&
                double.TryParse(durationString, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }

            formatTags = ReadTags(formatElement);
            formatBitRate = TryGetLong(formatElement, "bit_rate");
        }

        return new MediaInfo
        {
            Streams = streams,
            Duration = duration,
            FormatBitRate = formatBitRate,
            FormatTags = formatTags
        };
    }

    private static Dictionary<string, string> ReadTags(JsonElement element)
    {
        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("tags", out var tagsElement) || tagsElement.ValueKind != JsonValueKind.Object)
        {
            return tags;
        }

        foreach (var property in tagsElement.EnumerateObject())
        {
            tags[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return tags;
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return null;
    }

    private static long? TryGetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return null;
    }

    private static async Task<ProcessResult> RunProcessWithFallbackAsync(IReadOnlyList<string> binaryCandidates, IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? probeJson, Action<string>? standardErrorLineObserver = null)
    {
        VideoProcessingException? lastException = null;

        foreach (var candidate in binaryCandidates)
        {
            try
            {
                return await RunProcessAsync(candidate, arguments, cancellationToken, probeJson, standardErrorLineObserver).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (VideoProcessingException ex) when (CanFallbackToPath(candidate, ex, binaryCandidates))
            {
                lastException = ex;
            }
        }

        throw lastException ?? new InvalidOperationException("No FFmpeg executable candidates were available.");
    }

    private static async Task<ProcessResult?> TryRunProcessWithFallbackAsync(IReadOnlyList<string> binaryCandidates, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        foreach (var candidate in binaryCandidates)
        {
            try
            {
                return await RunProcessAsync(candidate, arguments, cancellationToken, probeJson: null).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (VideoProcessingException)
            {
                // Silently continue to next candidate during testing
            }
        }

        return null;
    }

    private static async Task<ProcessResult> RunProcessAsync(string binaryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? probeJson, Action<string>? standardErrorLineObserver = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(binaryPath) ?? AppContext.BaseDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start process '{binaryPath}'.");
            }
        }
        catch (Exception ex)
        {
            throw new VideoProcessingException(
                "Failed to start FFmpeg/FFprobe process.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                null,
                string.Empty,
                ex.Message,
                probeJson,
                ex);
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort kill on cancellation.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is string line)
            {
                stderrBuilder.AppendLine(line);
                standardErrorLineObserver?.Invoke(line);
            }
        }, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);
        var stderr = stderrBuilder.ToString();

        if (process.ExitCode != 0)
        {
            throw new VideoProcessingException(
                "FFmpeg/FFprobe exited with a non-zero code.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                process.ExitCode,
                stdout,
                stderr,
                probeJson);
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static Action<string>? CreateProgressObserver(TimeSpan totalDuration, IProgress<VideoProcessingProgress>? progress)
    {
        if (progress is null || totalDuration <= TimeSpan.Zero)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var lastProcessed = TimeSpan.Zero;

        progress.Report(new VideoProcessingProgress
        {
            FractionComplete = 0,
            ProcessedDuration = TimeSpan.Zero,
            TotalDuration = totalDuration,
            EstimatedTimeRemaining = null,
            IsCompleted = false
        });

        return line =>
        {
            if (line.StartsWith("out_time=", StringComparison.Ordinal))
            {
                lastProcessed = ParseProgressTimestamp(line["out_time=".Length..]);
                return;
            }

            if (line.StartsWith("out_time_ms=", StringComparison.Ordinal) &&
                long.TryParse(line["out_time_ms=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var outTimeMs))
            {
                lastProcessed = TimeSpan.FromMilliseconds(outTimeMs / 1000d);
                return;
            }

            if (line.StartsWith("progress=", StringComparison.Ordinal))
            {
                var isCompleted = string.Equals(line["progress=".Length..], "end", StringComparison.Ordinal);
                var clampedProcessed = lastProcessed > totalDuration ? totalDuration : lastProcessed;
                var fraction = totalDuration.TotalMilliseconds <= 0
                    ? 0d
                    : Math.Clamp(clampedProcessed.TotalMilliseconds / totalDuration.TotalMilliseconds, 0d, 1d);

                TimeSpan? eta = null;
                if (!isCompleted && fraction > 0d)
                {
                    var remainingMilliseconds = stopwatch.Elapsed.TotalMilliseconds * ((1d - fraction) / fraction);
                    eta = TimeSpan.FromMilliseconds(Math.Max(0d, remainingMilliseconds));
                }

                progress.Report(new VideoProcessingProgress
                {
                    FractionComplete = isCompleted ? 1d : fraction,
                    ProcessedDuration = isCompleted ? totalDuration : clampedProcessed,
                    TotalDuration = totalDuration,
                    EstimatedTimeRemaining = isCompleted ? TimeSpan.Zero : eta,
                    IsCompleted = isCompleted
                });
            }
        };
    }

    private static TimeSpan ParseProgressTimestamp(string value)
    {
        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture, out var precise))
        {
            return precise;
        }

        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var centiseconds))
        {
            return centiseconds;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return TimeSpan.Zero;
    }

    private static IReadOnlyList<string> ResolveExecutableCandidates(string executableNameWithoutExtension)
    {
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? executableNameWithoutExtension + ".exe"
            : executableNameWithoutExtension;

        var rid = GetCurrentRid();
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", rid, executableName);
        var candidates = new List<string>();

        if (File.Exists(bundledPath))
        {
            candidates.Add(bundledPath);
        }

        candidates.Add(executableName);

        if (VerifiedExecutableCache.TryGetValue(executableName, out var cachedExecutable))
        {
            return [cachedExecutable];
        }

        foreach (var candidate in candidates)
        {
            if (!CanLaunchExecutable(candidate))
            {
                continue;
            }

            VerifiedExecutableCache[executableName] = candidate;
            return [candidate];
        }

        return candidates;
    }

    private static bool CanLaunchExecutable(string binaryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.IsPathRooted(binaryPath)
                ? (Path.GetDirectoryName(binaryPath) ?? AppContext.BaseDirectory)
                : AppContext.BaseDirectory
        };

        startInfo.ArgumentList.Add("-version");

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return false;
            }

            process.WaitForExit(5000);
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort only.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => throw new PlatformNotSupportedException($"Unsupported Windows architecture '{RuntimeInformation.ProcessArchitecture}'.")
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw new PlatformNotSupportedException($"Unsupported macOS architecture '{RuntimeInformation.ProcessArchitecture}'.")
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => throw new PlatformNotSupportedException($"Unsupported Linux architecture '{RuntimeInformation.ProcessArchitecture}'.")
            };
        }

        throw new PlatformNotSupportedException("Current operating system is not supported.");
    }

    private static VideoContainerFormat ResolveOutputFormat(string inputPath, string outputPath, VideoContainerFormat? requestedFormat)
    {
        if (requestedFormat.HasValue)
        {
            return requestedFormat.Value;
        }

        var outputExtension = Path.GetExtension(outputPath);
        if (!string.IsNullOrWhiteSpace(outputExtension) && TryMapContainer(outputExtension, out var format))
        {
            return format;
        }

        var inputExtension = Path.GetExtension(inputPath);
        if (TryMapContainer(inputExtension, out format))
        {
            return format;
        }

        throw new NotSupportedException($"Unable to infer output container from '{outputPath}' or '{inputPath}'.");
    }

    private static bool TryMapContainer(string? extension, out VideoContainerFormat format)
    {
        switch (extension?.ToLowerInvariant())
        {
            case ".mp4":
                format = VideoContainerFormat.Mp4;
                return true;
            case ".webm":
                format = VideoContainerFormat.Webm;
                return true;
            case ".gif":
                format = VideoContainerFormat.Gif;
                return true;
            case ".mkv":
                format = VideoContainerFormat.Mkv;
                return true;
            case ".mov":
                format = VideoContainerFormat.Mov;
                return true;
            case ".avi":
                format = VideoContainerFormat.Avi;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static string EnsureOutputExtension(string outputPath, VideoContainerFormat format)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var extension = format switch
        {
            VideoContainerFormat.Mp4 => ".mp4",
            VideoContainerFormat.Webm => ".webm",
            VideoContainerFormat.Gif => ".gif",
            VideoContainerFormat.Mkv => ".mkv",
            VideoContainerFormat.Mov => ".mov",
            VideoContainerFormat.Avi => ".avi",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported output container.")
        };

        return Path.ChangeExtension(outputPath, extension);
    }

    private static AudioExtractionTarget ResolveAudioExtractionTarget(string outputPath)
    {
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        return extension switch
        {
            ".mp3" => new AudioExtractionTarget("libmp3lame", "mp3"),
            ".wav" => new AudioExtractionTarget("pcm_s16le", "pcm_s16le"),
            ".flac" => new AudioExtractionTarget("flac", "flac"),
            ".aac" => new AudioExtractionTarget("aac", "aac"),
            ".m4a" => new AudioExtractionTarget("aac", "aac"),
            ".opus" => new AudioExtractionTarget("libopus", "opus"),
            ".ogg" => new AudioExtractionTarget("libvorbis", "vorbis"),
            _ => throw new NotSupportedException($"Audio extraction does not support output extension '{extension}'.")
        };
    }

    private static bool IsContainerCompatible(VideoContainerFormat format, VideoCodec? videoCodec, AudioCodec? audioCodec)
    {
        if (format == VideoContainerFormat.Gif)
        {
            return videoCodec == VideoCodec.Gif && audioCodec is null;
        }

        if (videoCodec.HasValue && !GetSupportedVideoCodecs(format).Contains(videoCodec.Value))
        {
            return false;
        }

        if (audioCodec.HasValue && !GetSupportedAudioCodecs(format).Contains(audioCodec.Value))
        {
            return false;
        }

        return true;
    }

    private static HashSet<VideoCodec> GetSupportedVideoCodecs(VideoContainerFormat format)
    {
        return format switch
        {
            VideoContainerFormat.Mp4 => new HashSet<VideoCodec> { VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1, VideoCodec.Mpeg4 },
            VideoContainerFormat.Webm => new HashSet<VideoCodec> { VideoCodec.Vp8, VideoCodec.Vp9, VideoCodec.Av1 },
            VideoContainerFormat.Gif => new HashSet<VideoCodec> { VideoCodec.Gif },
            VideoContainerFormat.Mkv => new HashSet<VideoCodec> { VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1, VideoCodec.Vp8, VideoCodec.Vp9, VideoCodec.Mpeg4 },
            VideoContainerFormat.Mov => new HashSet<VideoCodec> { VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1, VideoCodec.Mpeg4 },
            VideoContainerFormat.Avi => new HashSet<VideoCodec> { VideoCodec.H264, VideoCodec.Mpeg4 },
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static HashSet<AudioCodec> GetSupportedAudioCodecs(VideoContainerFormat format)
    {
        return format switch
        {
            VideoContainerFormat.Mp4 => new HashSet<AudioCodec> { AudioCodec.Aac, AudioCodec.Mp3, AudioCodec.Ac3, AudioCodec.Flac },
            VideoContainerFormat.Webm => new HashSet<AudioCodec> { AudioCodec.Opus, AudioCodec.Vorbis },
            VideoContainerFormat.Gif => new HashSet<AudioCodec>(),
            VideoContainerFormat.Mkv => new HashSet<AudioCodec> { AudioCodec.Aac, AudioCodec.Opus, AudioCodec.Vorbis, AudioCodec.Mp3, AudioCodec.Ac3, AudioCodec.Flac, AudioCodec.PcmS16Le },
            VideoContainerFormat.Mov => new HashSet<AudioCodec> { AudioCodec.Aac, AudioCodec.Mp3, AudioCodec.Ac3, AudioCodec.Flac, AudioCodec.PcmS16Le },
            VideoContainerFormat.Avi => new HashSet<AudioCodec> { AudioCodec.Mp3, AudioCodec.Ac3, AudioCodec.PcmS16Le },
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static VideoCodec GetDefaultVideoCodec(VideoContainerFormat format)
    {
        return format switch
        {
            VideoContainerFormat.Mp4 => VideoCodec.H264,
            VideoContainerFormat.Webm => VideoCodec.Vp9,
            VideoContainerFormat.Gif => VideoCodec.Gif,
            VideoContainerFormat.Mkv => VideoCodec.H264,
            VideoContainerFormat.Mov => VideoCodec.H264,
            VideoContainerFormat.Avi => VideoCodec.Mpeg4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static AudioCodec? GetDefaultAudioCodec(VideoContainerFormat format)
    {
        return format switch
        {
            VideoContainerFormat.Mp4 => AudioCodec.Aac,
            VideoContainerFormat.Webm => AudioCodec.Opus,
            VideoContainerFormat.Gif => null,
            VideoContainerFormat.Mkv => AudioCodec.Aac,
            VideoContainerFormat.Mov => AudioCodec.Aac,
            VideoContainerFormat.Avi => AudioCodec.Mp3,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static string? ResolveSubtitleCodec(MuxSubtitleOptions? options, VideoContainerFormat outputFormat)
    {
        if (options is null || options.Mode == SubtitleMode.BurnIn)
        {
            return null;
        }

        return outputFormat switch
        {
            VideoContainerFormat.Mp4 or VideoContainerFormat.Mov => "mov_text",
            VideoContainerFormat.Mkv => Path.GetExtension(options.SubtitlePath).Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
                                         Path.GetExtension(options.SubtitlePath).Equals(".ssa", StringComparison.OrdinalIgnoreCase)
                ? "ass"
                : "srt",
            VideoContainerFormat.Webm => "webvtt",
            _ => null
        };
    }

    private static bool ShouldBurnInAssSubtitleFallback(ProcessVideoOptions options, VideoContainerFormat outputFormat)
    {
        if (options.SubtitleMux is not { Mode: SubtitleMode.SoftMux } subtitleOptions)
        {
            return false;
        }

        if (outputFormat is not (VideoContainerFormat.Mp4 or VideoContainerFormat.Mov))
        {
            return false;
        }

        var extension = Path.GetExtension(subtitleOptions.SubtitlePath);
        return extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);
    }

    private static VideoCodec? MapVideoCodec(string? codecName)
    {
        return codecName?.ToLowerInvariant() switch
        {
            "h264" => VideoCodec.H264,
            "hevc" => VideoCodec.H265,
            "av1" => VideoCodec.Av1,
            "vp9" => VideoCodec.Vp9,
            "vp8" => VideoCodec.Vp8,
            "gif" => VideoCodec.Gif,
            "mpeg4" => VideoCodec.Mpeg4,
            _ => null
        };
    }

    private static AudioCodec? MapAudioCodec(string? codecName)
    {
        return codecName?.ToLowerInvariant() switch
        {
            "aac" => AudioCodec.Aac,
            "opus" => AudioCodec.Opus,
            "vorbis" => AudioCodec.Vorbis,
            "mp3" => AudioCodec.Mp3,
            "ac3" => AudioCodec.Ac3,
            "flac" => AudioCodec.Flac,
            "pcm_s16le" => AudioCodec.PcmS16Le,
            _ => null
        };
    }

    private static string GetVideoEncoder(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "libx264",
            VideoCodec.H265 => "libx265",
            VideoCodec.Av1 => "libsvtav1",
            VideoCodec.Vp9 => "libvpx-vp9",
            VideoCodec.Vp8 => "libvpx",
            VideoCodec.Gif => "gif",
            VideoCodec.Mpeg4 => "mpeg4",
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported video codec.")
        };
    }

    private static string GetAudioEncoder(AudioCodec codec)
    {
        return codec switch
        {
            AudioCodec.Aac => "aac",
            AudioCodec.Opus => "libopus",
            AudioCodec.Vorbis => "libvorbis",
            AudioCodec.Mp3 => "libmp3lame",
            AudioCodec.Ac3 => "ac3",
            AudioCodec.Flac => "flac",
            AudioCodec.PcmS16Le => "pcm_s16le",
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported audio codec.")
        };
    }

    private static string GetProbeCodecName(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "h264",
            VideoCodec.H265 => "hevc",
            VideoCodec.Av1 => "av1",
            VideoCodec.Vp9 => "vp9",
            VideoCodec.Vp8 => "vp8",
            VideoCodec.Gif => "gif",
            VideoCodec.Mpeg4 => "mpeg4",
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
        };
    }

    private static string GetProbeCodecName(AudioCodec codec)
    {
        return codec switch
        {
            AudioCodec.Aac => "aac",
            AudioCodec.Opus => "opus",
            AudioCodec.Vorbis => "vorbis",
            AudioCodec.Mp3 => "mp3",
            AudioCodec.Ac3 => "ac3",
            AudioCodec.Flac => "flac",
            AudioCodec.PcmS16Le => "pcm_s16le",
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
        };
    }

    private static int GetCrf(CompressionPreset preset, VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => preset switch
            {
                CompressionPreset.VeryHigh => 18,
                CompressionPreset.High => 21,
                CompressionPreset.Balanced => 23,
                CompressionPreset.SmallSize => 27,
                _ => 23
            },
            VideoCodec.H265 => preset switch
            {
                CompressionPreset.VeryHigh => 20,
                CompressionPreset.High => 24,
                CompressionPreset.Balanced => 28,
                CompressionPreset.SmallSize => 32,
                _ => 28
            },
            VideoCodec.Av1 => preset switch
            {
                CompressionPreset.VeryHigh => 22,
                CompressionPreset.High => 28,
                CompressionPreset.Balanced => 34,
                CompressionPreset.SmallSize => 40,
                _ => 34
            },
            VideoCodec.Vp9 => preset switch
            {
                CompressionPreset.VeryHigh => 28,
                CompressionPreset.High => 32,
                CompressionPreset.Balanced => 36,
                CompressionPreset.SmallSize => 40,
                _ => 36
            },
            VideoCodec.Vp8 => preset switch
            {
                CompressionPreset.VeryHigh => 10,
                CompressionPreset.High => 18,
                CompressionPreset.Balanced => 24,
                CompressionPreset.SmallSize => 30,
                _ => 24
            },
            VideoCodec.Mpeg4 => preset switch
            {
                CompressionPreset.VeryHigh => 4,
                CompressionPreset.High => 6,
                CompressionPreset.Balanced => 8,
                CompressionPreset.SmallSize => 10,
                _ => 8
            },
            VideoCodec.Gif => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
        };
    }

    private static IEnumerable<string> SplitArguments(string args)
    {
        return args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ToFfmpegTimestamp(TimeSpan value)
    {
        return value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string EscapeSubtitleFilterPath(string path)
    {
        var normalized = Path.GetFullPath(path).Replace("\\", "/");
        normalized = normalized.Replace(":", "\\:").Replace("'", "\\'");
        return normalized;
    }

    private static string FormatCommandLine(string binaryPath, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder(binaryPath);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteArgument(argument));
        }

        return builder.ToString();
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void ApplyRepairInputArguments(List<string> args, RepairOptions? repair)
    {
        if (repair is null)
        {
            return;
        }

        if (repair.IgnoreRecoverableErrors)
        {
            args.Add("-err_detect");
            args.Add("ignore_err");
            args.Add("-fflags");
            args.Add("+discardcorrupt");
        }

        if (repair.RegeneratePresentationTimestamps)
        {
            args.Add("-fflags");
            args.Add("+genpts");
        }
    }

    private static bool ShouldDropSubtitleStreams(ProcessVideoOptions options)
    {
        return options.Repair?.DropNonEssentialStreams == true;
    }

    private static (int Width, int Height) EstimateOutputDimensions(MediaInfo inputInfo, ProcessVideoOptions options)
    {
        var width = inputInfo.PrimaryVideoStream?.Width ?? 0;
        var height = inputInfo.PrimaryVideoStream?.Height ?? 0;

        if (options.Transform is not null && options.Transform.RotationDegrees is 90 or 270)
        {
            (width, height) = (height, width);
        }

        if (options.Resize is not null)
        {
            width = options.Resize.Width;
            height = options.Resize.Height;
        }

        return (width, height);
    }

    private static TimeSpan EstimateOutputDuration(MediaInfo inputInfo, ProcessVideoOptions options)
    {
        var duration = inputInfo.Duration ?? TimeSpan.Zero;

        if (options.Trim is not null)
        {
            duration = options.Trim.End - options.Trim.Start;
        }

        return duration;
    }

    private static long? EstimateOutputSizeBytes(MediaInfo inputInfo, ProcessVideoOptions options, StreamPlan streamPlan, int estimatedWidth, int estimatedHeight, TimeSpan estimatedDuration)
    {
        if (estimatedDuration <= TimeSpan.Zero)
        {
            return 0;
        }

        var sourceVideoBitrate = inputInfo.PrimaryVideoStream?.BitRate;
        var sourceAudioBitrate = inputInfo.PrimaryAudioStream?.BitRate;
        var sourceTotalBitrate = inputInfo.FormatBitRate;

        if (!sourceVideoBitrate.HasValue && sourceTotalBitrate.HasValue && sourceAudioBitrate.HasValue)
        {
            sourceVideoBitrate = Math.Max(0, sourceTotalBitrate.Value - sourceAudioBitrate.Value);
        }

        long? estimatedVideoBitrate = streamPlan.VideoNeedsEncoding
            ? EstimateEncodedVideoBitrate(inputInfo, options, streamPlan, estimatedWidth, estimatedHeight)
            : sourceVideoBitrate;

        long? estimatedAudioBitrate = streamPlan.AudioCodec.HasValue
            ? (streamPlan.AudioNeedsEncoding ? EstimateEncodedAudioBitrate(streamPlan.AudioCodec.Value) : sourceAudioBitrate)
            : 0;

        if (!estimatedVideoBitrate.HasValue && !estimatedAudioBitrate.HasValue)
        {
            return sourceTotalBitrate.HasValue
                ? (long)Math.Round(sourceTotalBitrate.Value / 8d * estimatedDuration.TotalSeconds, MidpointRounding.AwayFromZero)
                : null;
        }

        var totalBitrate = (estimatedVideoBitrate ?? 0) + (estimatedAudioBitrate ?? 0);
        return (long)Math.Round(totalBitrate / 8d * estimatedDuration.TotalSeconds, MidpointRounding.AwayFromZero);
    }

    private static long? EstimateEncodedVideoBitrate(MediaInfo inputInfo, ProcessVideoOptions options, StreamPlan streamPlan, int estimatedWidth, int estimatedHeight)
    {
        var sourceBitrate = inputInfo.PrimaryVideoStream?.BitRate ?? inputInfo.FormatBitRate;
        if (!sourceBitrate.HasValue)
        {
            return null;
        }

        var sourceWidth = Math.Max(1, inputInfo.PrimaryVideoStream?.Width ?? estimatedWidth);
        var sourceHeight = Math.Max(1, inputInfo.PrimaryVideoStream?.Height ?? estimatedHeight);
        var pixelRatio = (double)(estimatedWidth * Math.Max(1, estimatedHeight)) / (sourceWidth * sourceHeight);

        var presetFactor = options.Compression?.Preset switch
        {
            CompressionPreset.VeryHigh => 0.85,
            CompressionPreset.High => 0.65,
            CompressionPreset.Balanced => 0.50,
            CompressionPreset.SmallSize => 0.32,
            null => 1.0,
            _ => 1.0
        };

        var codecFactor = streamPlan.VideoCodec switch
        {
            VideoCodec.H264 => 1.00,
            VideoCodec.H265 => 0.72,
            VideoCodec.Av1 => 0.58,
            VideoCodec.Vp9 => 0.70,
            VideoCodec.Vp8 => 0.90,
            VideoCodec.Mpeg4 => 1.20,
            VideoCodec.Gif => 1.50,
            _ => 1.0
        };

        var repairPenalty = options.Repair?.Mode == RepairMode.Reencode ? 0.95 : 1.0;
        var estimated = sourceBitrate.Value * pixelRatio * presetFactor * codecFactor * repairPenalty;
        return Math.Max(32_000, (long)Math.Round(estimated, MidpointRounding.AwayFromZero));
    }

    private static long EstimateEncodedAudioBitrate(AudioCodec codec)
    {
        return codec switch
        {
            AudioCodec.Aac => 128_000,
            AudioCodec.Opus => 112_000,
            AudioCodec.Vorbis => 128_000,
            AudioCodec.Mp3 => 160_000,
            AudioCodec.Ac3 => 192_000,
            AudioCodec.Flac => 900_000,
            AudioCodec.PcmS16Le => 1_536_000,
            _ => 128_000
        };
    }

    private static IReadOnlyList<string> BuildEstimateNotes(ProcessVideoOptions options, StreamPlan streamPlan, MediaInfo inputInfo, long? estimatedOutputSizeBytes)
    {
        var notes = new List<string>();

        if (streamPlan.CopyAllStreams)
        {
            notes.Add("Estimate assumes stream copy without re-encoding.");
        }

        if (options.Compression is not null)
        {
            notes.Add("Estimated size is heuristic and derived from FFprobe bitrate metadata plus codec/preset scaling.");
        }

        if (options.Repair is not null)
        {
            notes.Add($"Repair mode '{options.Repair.Mode}' enables recovery flags and may drop corrupt or non-essential streams.");
        }

        if (!estimatedOutputSizeBytes.HasValue)
        {
            notes.Add("Estimated size is unavailable because FFprobe bitrate data was incomplete.");
        }

        if (inputInfo.PrimaryVideoStream?.Width is null || inputInfo.PrimaryVideoStream?.Height is null)
        {
            notes.Add("Input dimensions were not fully available from FFprobe.");
        }

        return notes;
    }

    private static readonly string[] SupportedBurnInSubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt"];

    private static bool CanFallbackToPath(string candidate, VideoProcessingException exception, IReadOnlyList<string> candidates)
    {
        if (candidates.Count <= 1)
        {
            return false;
        }

        if (!Path.IsPathRooted(candidate))
        {
            return false;
        }

        return exception.ExitCode is -1073741515 or unchecked((int)0xC0000135);
    }

    private sealed record AudioExtractionTarget(string EncoderName, string ProbeCodecName);

    internal sealed record VideoEncoderPlan(string EncoderName, bool IsHardwareAccelerated);

    private sealed class StreamPlan
    {
        public bool CopyAllStreams { get; init; }

        public required VideoCodec VideoCodec { get; init; }

        public AudioCodec? AudioCodec { get; init; }

        public string? SubtitleCodec { get; init; }

        public bool VideoNeedsEncoding { get; init; }

        public bool AudioNeedsEncoding { get; init; }

        public bool SubtitleNeedsEncoding { get; init; }

        public int? VideoCrf { get; init; }
    }

    private sealed class MediaInfo
    {
        public List<MediaStreamInfo> Streams { get; init; } = [];

        public TimeSpan? Duration { get; init; }

        public long? FormatBitRate { get; init; }

        public Dictionary<string, string> FormatTags { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public MediaStreamInfo? PrimaryVideoStream => Streams.FirstOrDefault(stream => string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase));

        public MediaStreamInfo? PrimaryAudioStream => Streams.FirstOrDefault(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<MediaStreamInfo> AudioStreams => Streams.Where(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase)).ToArray();

        public IReadOnlyList<MediaStreamInfo> SubtitleStreams => Streams.Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private sealed class MediaStreamInfo
    {
        public int Index { get; init; }

        public string? CodecType { get; init; }

        public string? CodecName { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }

        public long? BitRate { get; init; }

        public Dictionary<string, string> Tags { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
