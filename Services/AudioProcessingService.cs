using System;
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
using Files_Tools.Helpers;

namespace Files_Tools.Services;

/// <summary>
/// Defines standalone audio-file processing operations backed by local FFmpeg tooling.
/// </summary>
public interface IAudioProcessingService
{
    /// <summary>
    /// Converts an audio file to a requested container, codec, sample rate, channel count, or bitrate.
    /// </summary>
    Task<AudioProcessResult> ConvertAsync(string inputPath, string outputPath, AudioConversionOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compresses an audio file using a lossy or lossless codec.
    /// </summary>
    Task<AudioProcessResult> CompressAsync(string inputPath, string outputPath, AudioCompressionOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes an audio file using peak or LUFS loudness normalization.
    /// </summary>
    Task<AudioProcessResult> NormalizeAsync(string inputPath, string outputPath, AudioNormalizationOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an audio file into a podcast-oriented voice output with analysis, voice EQ, dynamics, limiting, and LUFS normalization.
    /// </summary>
    Task<AudioProcessResult> ProcessPodcastAudioAsync(string inputPath, string outputPath, AudioPodcastProcessingOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trims an audio file to an explicit time range.
    /// </summary>
    Task<AudioProcessResult> TrimAsync(string inputPath, string outputPath, AudioTrimOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes leading and/or trailing silence from an audio file.
    /// </summary>
    Task<AudioProcessResult> RemoveSilenceAsync(string inputPath, string outputPath, AudioSilenceRemovalOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an equalizer preset or custom equalizer bands to an audio file.
    /// </summary>
    Task<AudioProcessResult> ApplyEqualizerAsync(string inputPath, string outputPath, AudioEqualizerOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes metadata from an audio file.
    /// </summary>
    Task<AudioProcessResult> RemoveMetadataAsync(string inputPath, string outputPath, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// High-level stage for standalone audio processing progress.
/// </summary>
public enum AudioProcessStage
{
    /// <summary>
    /// Inspecting input or output audio metadata with FFprobe.
    /// </summary>
    Probing,

    /// <summary>
    /// Validating options and building an FFmpeg command.
    /// </summary>
    Preparing,

    /// <summary>
    /// Running FFmpeg filters or encoding work.
    /// </summary>
    Processing,

    /// <summary>
    /// Encoding the final output stream.
    /// </summary>
    Encoding,

    /// <summary>
    /// Reading final output metadata.
    /// </summary>
    Finalizing,

    /// <summary>
    /// Removing temporary files created by the service.
    /// </summary>
    CleaningTemporaryFiles,

    /// <summary>
    /// Processing completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Cancellation has been requested.
    /// </summary>
    Cancelling
}

/// <summary>
/// Live progress snapshot for an audio processing job.
/// </summary>
public sealed class AudioProcessProgress
{
    /// <summary>
    /// Current processing stage.
    /// </summary>
    public AudioProcessStage Stage { get; init; }

    /// <summary>
    /// Overall progress from 0.0 to 1.0.
    /// </summary>
    public double OverallPercent { get; init; }

    /// <summary>
    /// Progress within the current stage from 0.0 to 1.0.
    /// </summary>
    public double StagePercent { get; init; }

    /// <summary>
    /// User-facing description of the current operation.
    /// </summary>
    public string StageDescription { get; init; } = string.Empty;

    /// <summary>
    /// Processed media duration when FFmpeg reports timestamp progress.
    /// </summary>
    public TimeSpan? ProcessedDuration { get; init; }

    /// <summary>
    /// Total media duration when known.
    /// </summary>
    public TimeSpan? TotalDuration { get; init; }

    /// <summary>
    /// Estimated time remaining when enough progress data exists.
    /// </summary>
    public TimeSpan? EstimatedRemainingTime { get; init; }

    /// <summary>
    /// Indicates that FFmpeg or FFprobe is currently active.
    /// </summary>
    public bool IsFfmpegActive { get; init; }
}

/// <summary>
/// Metadata returned after an audio processing operation completes.
/// </summary>
public sealed class AudioProcessResult
{
    /// <summary>
    /// Absolute output file path.
    /// </summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>
    /// Duration of the output audio when available.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Output codec name reported by FFprobe.
    /// </summary>
    public string? OutputCodec { get; init; }

    /// <summary>
    /// Output container or format name reported by FFprobe.
    /// </summary>
    public string? OutputFormat { get; init; }

    /// <summary>
    /// Output sample rate in hertz when available.
    /// </summary>
    public int? OutputSampleRate { get; init; }

    /// <summary>
    /// Output channel count when available.
    /// </summary>
    public int? OutputChannels { get; init; }

    /// <summary>
    /// Output bitrate in kilobits per second when available.
    /// </summary>
    public int? OutputBitrateKbps { get; init; }

    /// <summary>
    /// Output file size in bytes when the file exists.
    /// </summary>
    public long? OutputSizeBytes { get; init; }

    /// <summary>
    /// Non-fatal notes about defaults, format choices, or v1 limitations.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional loudness and peak analysis captured before the main processing pass.
    /// </summary>
    public AudioAnalysisResult? Analysis { get; init; }
}

/// <summary>
/// Loudness and peak measurements captured during audio analysis.
/// </summary>
public sealed class AudioAnalysisResult
{
    /// <summary>
    /// Integrated LUFS reported by FFmpeg loudnorm, when available.
    /// </summary>
    public double? IntegratedLufs { get; init; }

    /// <summary>
    /// True peak in dBTP reported by FFmpeg loudnorm, when available.
    /// </summary>
    public double? TruePeakDb { get; init; }

    /// <summary>
    /// Mean volume in dB reported by FFmpeg volumedetect, when available.
    /// </summary>
    public double? MeanVolumeDb { get; init; }

    /// <summary>
    /// Maximum sample peak in dB reported by FFmpeg volumedetect, when available.
    /// </summary>
    public double? MaxVolumeDb { get; init; }
}

/// <summary>
/// Options for audio conversion.
/// </summary>
public sealed class AudioConversionOptions
{
    /// <summary>
    /// Optional output format/container name such as mp3, wav, flac, ipod, opus, or ogg.
    /// </summary>
    public string? OutputFormat { get; init; }

    /// <summary>
    /// Optional FFmpeg audio encoder name.
    /// </summary>
    public string? OutputCodec { get; init; }

    /// <summary>
    /// Optional target bitrate in kilobits per second.
    /// </summary>
    public int? BitrateKbps { get; init; }

    /// <summary>
    /// Optional target sample rate in hertz.
    /// </summary>
    public int? SampleRate { get; init; }

    /// <summary>
    /// Optional channel count. V1 supports null, 1, or 2.
    /// </summary>
    public int? Channels { get; init; }

    /// <summary>
    /// Preserves source metadata when true.
    /// </summary>
    public bool PreserveMetadata { get; init; } = true;
}

/// <summary>
/// Audio compression strategy.
/// </summary>
public enum AudioCompressionMode
{
    /// <summary>
    /// Encode with a lossy codec such as MP3, AAC, Opus, or Vorbis.
    /// </summary>
    Lossy,

    /// <summary>
    /// Encode with a lossless codec. V1 defaults to FLAC.
    /// </summary>
    Lossless
}

/// <summary>
/// Options for audio compression.
/// </summary>
public sealed class AudioCompressionOptions
{
    /// <summary>
    /// Compression strategy.
    /// </summary>
    public AudioCompressionMode Mode { get; init; } = AudioCompressionMode.Lossy;

    /// <summary>
    /// Optional FFmpeg output audio encoder.
    /// </summary>
    public string? OutputCodec { get; init; }

    /// <summary>
    /// Optional target bitrate in kilobits per second for lossy compression.
    /// </summary>
    public int? TargetBitrateKbps { get; init; }

    /// <summary>
    /// Optional target sample rate in hertz.
    /// </summary>
    public int? SampleRate { get; init; }

    /// <summary>
    /// Optional output channel count. V1 supports null, 1, or 2.
    /// </summary>
    public int? Channels { get; init; }

    /// <summary>
    /// Preserves source metadata when true.
    /// </summary>
    public bool PreserveMetadata { get; init; } = true;
}

/// <summary>
/// Audio normalization strategy.
/// </summary>
public enum AudioNormalizationMode
{
    /// <summary>
    /// Normalize peak level using FFmpeg volume filters.
    /// </summary>
    Peak,

    /// <summary>
    /// Normalize perceived loudness using FFmpeg loudnorm.
    /// </summary>
    Lufs
}

/// <summary>
/// Options for peak or LUFS normalization.
/// </summary>
public sealed class AudioNormalizationOptions
{
    /// <summary>
    /// Normalization strategy.
    /// </summary>
    public AudioNormalizationMode Mode { get; init; } = AudioNormalizationMode.Peak;

    /// <summary>
    /// Target peak level in dB. Defaults to -1.0 dB for peak normalization.
    /// </summary>
    public double? TargetPeakDb { get; init; }

    /// <summary>
    /// Target integrated LUFS. Defaults to -16 LUFS for LUFS normalization.
    /// </summary>
    public double? TargetLufs { get; init; }

    /// <summary>
    /// Adds a limiter to the normalization chain when true.
    /// </summary>
    public bool UseLimiter { get; init; } = true;

    /// <summary>
    /// Prevents clipping when true.
    /// </summary>
    public bool PreventClipping { get; init; } = true;
}

/// <summary>
/// Options for podcast-style spoken-word processing.
/// </summary>
public sealed class AudioPodcastProcessingOptions
{
    /// <summary>
    /// Enables DTLN denoise before voice shaping.
    /// </summary>
    public bool EnableDtlnDenoise { get; init; }

    /// <summary>
    /// Legacy alias for <see cref="EnableDtlnDenoise"/>. When true, DTLN denoise is enabled.
    /// </summary>
    [Obsolete("Use EnableDtlnDenoise. Podcast denoise is DTLN-backed; FFmpeg afftdn is no longer used.")]
    public bool EnableLightweightDenoise { get; init; }

    /// <summary>
    /// Legacy FFmpeg noise-floor option retained for source compatibility. DTLN denoise ignores this value.
    /// </summary>
    [Obsolete("DTLN denoise does not use a noise floor. Use DtlnDenoiseAmount to control blend strength.")]
    public double DenoiseNoiseFloorDb { get; init; } = -35;

    /// <summary>
    /// DTLN channel strategy used when denoise is enabled.
    /// </summary>
    public AudioDenoiseMode DtlnDenoiseMode { get; init; } = AudioDenoiseMode.Mono;

    /// <summary>
    /// DTLN blend amount from 0 to 100 when denoise is enabled.
    /// </summary>
    public int DtlnDenoiseAmount { get; init; } = 100;

    /// <summary>
    /// DTLN inference passes to apply when denoise is enabled. V1 supports 1 through 3.
    /// </summary>
    public int DtlnDenoisePasses { get; init; } = 1;

    /// <summary>
    /// High-pass cutoff in hertz used to remove rumble before voice EQ.
    /// </summary>
    public double HighPassFrequencyHz { get; init; } = 80;

    /// <summary>
    /// Enables EQ-based sibilance control after podcast voice EQ.
    /// </summary>
    public bool EnableDeEsser { get; init; } = true;

    /// <summary>
    /// Enables the spoken-word dynamic compressor.
    /// </summary>
    public bool EnableCompressor { get; init; } = true;

    /// <summary>
    /// Target integrated loudness in LUFS for final delivery.
    /// </summary>
    public double TargetLufs { get; init; } = -16;

    /// <summary>
    /// Limiter ceiling passed to FFmpeg alimiter. Must be greater than 0 and no more than 1.
    /// </summary>
    public double LimiterLimit { get; init; } = 0.98;

    /// <summary>
    /// Optional FFmpeg audio encoder. When omitted, the output extension decides the encoder.
    /// </summary>
    public string? OutputCodec { get; init; }

    /// <summary>
    /// Optional target bitrate in kilobits per second.
    /// </summary>
    public int? BitrateKbps { get; init; }

    /// <summary>
    /// Optional target sample rate in hertz.
    /// </summary>
    public int? SampleRate { get; init; }

    /// <summary>
    /// Optional output channel count. V1 supports null, 1, or 2.
    /// </summary>
    public int? Channels { get; init; }

    /// <summary>
    /// Preserves source metadata when true.
    /// </summary>
    public bool PreserveMetadata { get; init; } = true;
}

/// <summary>
/// Options for audio trimming.
/// </summary>
public sealed class AudioTrimOptions
{
    /// <summary>
    /// Optional start time to keep from.
    /// </summary>
    public TimeSpan? StartTime { get; init; }

    /// <summary>
    /// Optional end time to keep until.
    /// </summary>
    public TimeSpan? EndTime { get; init; }

    /// <summary>
    /// Optional duration to keep from the start time.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Re-encodes output when true. Stream copy is used when false.
    /// </summary>
    public bool ReEncode { get; init; } = true;
}

/// <summary>
/// Silence removal scope.
/// </summary>
public enum SilenceRemovalMode
{
    /// <summary>
    /// Remove silence from the beginning only.
    /// </summary>
    Leading,

    /// <summary>
    /// Remove silence from the end only.
    /// </summary>
    Trailing,

    /// <summary>
    /// Remove silence from the beginning and end.
    /// </summary>
    LeadingAndTrailing,

    /// <summary>
    /// Remove silence inside the file. Not supported in v1.
    /// </summary>
    Internal
}

/// <summary>
/// Options for silence removal.
/// </summary>
public sealed class AudioSilenceRemovalOptions
{
    /// <summary>
    /// Silence removal scope.
    /// </summary>
    public SilenceRemovalMode Mode { get; init; } = SilenceRemovalMode.LeadingAndTrailing;

    /// <summary>
    /// Silence threshold in dB.
    /// </summary>
    public double SilenceThresholdDb { get; init; } = -40;

    /// <summary>
    /// Minimum duration that must be considered silence.
    /// </summary>
    public TimeSpan MinimumSilenceDuration { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Reserved for future precise segment trimming. Not used by v1 FFmpeg silenceremove mode.
    /// </summary>
    public TimeSpan PaddingBeforeCut { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Reserved for future precise segment trimming. Not used by v1 FFmpeg silenceremove mode.
    /// </summary>
    public TimeSpan PaddingAfterCut { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Re-encodes output when true. Silence removal requires re-encoding in v1.
    /// </summary>
    public bool ReEncode { get; init; } = true;
}

/// <summary>
/// Equalizer preset.
/// </summary>
public enum EqualizerPreset
{
    /// <summary>No equalizer is applied.</summary>
    None,
    /// <summary>Conservative spoken-word enhancement.</summary>
    PodcastVoice,
    /// <summary>Light speech intelligibility boost.</summary>
    VoiceClarity,
    /// <summary>Slightly fuller spoken voice.</summary>
    WarmVoice,
    /// <summary>Brighter voice presence.</summary>
    BrightVoice,
    /// <summary>Reduce low-frequency content.</summary>
    ReduceBass,
    /// <summary>Reduce high-frequency content.</summary>
    ReduceTreble,
    /// <summary>Band-limited telephone effect.</summary>
    PhoneVoice,
    /// <summary>Radio-style band-limited effect.</summary>
    RadioVoice,
    /// <summary>Notch 50 Hz electrical hum.</summary>
    RemoveElectricalHum50Hz,
    /// <summary>Notch 60 Hz electrical hum.</summary>
    RemoveElectricalHum60Hz,
    /// <summary>Caller-defined equalizer bands.</summary>
    Custom
}

/// <summary>
/// Options for equalizer processing.
/// </summary>
public sealed class AudioEqualizerOptions
{
    /// <summary>
    /// Equalizer preset to apply.
    /// </summary>
    public EqualizerPreset Preset { get; init; } = EqualizerPreset.None;

    /// <summary>
    /// Caller-defined bands used when <see cref="Preset"/> is <see cref="EqualizerPreset.Custom"/>.
    /// </summary>
    public IReadOnlyList<EqualizerBand> CustomBands { get; init; } = Array.Empty<EqualizerBand>();

    /// <summary>
    /// Adds a limiter to the equalizer chain when true.
    /// </summary>
    public bool PreventClipping { get; init; } = true;
}

/// <summary>
/// Parametric equalizer band.
/// </summary>
public sealed class EqualizerBand
{
    /// <summary>
    /// Band center frequency in hertz.
    /// </summary>
    public double FrequencyHz { get; init; }

    /// <summary>
    /// Band gain in dB. V1 accepts -24 through 24.
    /// </summary>
    public double GainDb { get; init; }

    /// <summary>
    /// FFmpeg equalizer width value. Must be greater than 0.
    /// </summary>
    public double Width { get; init; }
}

/// <summary>
/// Thrown when audio processing options are invalid.
/// </summary>
public class AudioProcessingValidationException : ArgumentException
{
    /// <summary>
    /// Creates an exception describing invalid audio processing options.
    /// </summary>
    public AudioProcessingValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when input media does not contain a usable audio stream.
/// </summary>
public class AudioProcessingUnsupportedMediaException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception describing unsupported audio input.
    /// </summary>
    public AudioProcessingUnsupportedMediaException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when FFmpeg or FFprobe fails during audio processing.
/// </summary>
public sealed class AudioProcessingFfmpegException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception for a failed FFmpeg or FFprobe invocation.
    /// </summary>
    public AudioProcessingFfmpegException(string message, string binaryPath, string commandLine, int? exitCode, string standardOutput, string standardError, Exception? innerException = null)
        : base(message, innerException)
    {
        BinaryPath = binaryPath;
        CommandLine = commandLine;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>Executed binary path or executable name.</summary>
    public string BinaryPath { get; }

    /// <summary>Fully formatted command line.</summary>
    public string CommandLine { get; }

    /// <summary>Process exit code when available.</summary>
    public int? ExitCode { get; }

    /// <summary>Captured standard output.</summary>
    public string StandardOutput { get; }

    /// <summary>Captured standard error.</summary>
    public string StandardError { get; }
}

/// <summary>
/// Thrown when audio processing cannot read or write required files.
/// </summary>
public class AudioProcessingFileSystemException : IOException
{
    /// <summary>
    /// Creates an exception describing a file-system failure.
    /// </summary>
    public AudioProcessingFileSystemException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// FFmpeg-backed implementation for standalone audio-file processing.
/// </summary>
public sealed class AudioProcessingService : IAudioProcessingService
{
    private const string FfprobeJsonArgs = "-v error -print_format json -show_streams -show_format";
    private readonly Lazy<IVideoAudioDenoiseService> _dtlnDenoiseService;

    /// <summary>
    /// Creates an audio processing service that loads the default DTLN denoise service only when podcast denoise is requested.
    /// </summary>
    public AudioProcessingService()
        : this(new Lazy<IVideoAudioDenoiseService>(() => new VideoAudioDenoise()))
    {
    }

    /// <summary>
    /// Creates an audio processing service with an explicit DTLN denoise service for podcast denoise.
    /// </summary>
    public AudioProcessingService(IVideoAudioDenoiseService dtlnDenoiseService)
        : this(new Lazy<IVideoAudioDenoiseService>(() => dtlnDenoiseService))
    {
        ArgumentNullException.ThrowIfNull(dtlnDenoiseService);
    }

    private AudioProcessingService(Lazy<IVideoAudioDenoiseService> dtlnDenoiseService)
    {
        _dtlnDenoiseService = dtlnDenoiseService;
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> ConvertAsync(string inputPath, string outputPath, AudioConversionOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateCommonAudioOptions(options.BitrateKbps, options.SampleRate, options.Channels);

        var warnings = new List<string>();
        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var codec = ResolveCodec(outputPath, options.OutputCodec);
        var format = options.OutputFormat ?? ResolveFormat(outputPath);
        var args = CreateBaseFfmpegArgs(inputPath);
        ApplyMetadata(args, options.PreserveMetadata);
        ApplyAudioShape(args, codec, options.BitrateKbps, options.SampleRate, options.Channels);
        AddFormat(args, format);
        args.Add(Path.GetFullPath(outputPath));

        await RunFfmpegOperationAsync(args, input.Duration, "Converting audio", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, warnings, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> CompressAsync(string inputPath, string outputPath, AudioCompressionOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateCommonAudioOptions(options.TargetBitrateKbps, options.SampleRate, options.Channels);

        var warnings = new List<string>();
        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var codec = options.OutputCodec;
        if (string.IsNullOrWhiteSpace(codec))
        {
            codec = options.Mode == AudioCompressionMode.Lossless ? "flac" : ResolveCodec(outputPath, null);
            warnings.Add($"Output codec was inferred as {codec}.");
        }

        if (options.Mode == AudioCompressionMode.Lossless && !string.Equals(codec, "flac", StringComparison.OrdinalIgnoreCase))
        {
            throw new AudioProcessingValidationException("Lossless compression supports FLAC in v1.");
        }

        var args = CreateBaseFfmpegArgs(inputPath);
        ApplyMetadata(args, options.PreserveMetadata);
        ApplyAudioShape(args, codec, options.TargetBitrateKbps, options.SampleRate, options.Channels);
        args.Add(Path.GetFullPath(outputPath));

        await RunFfmpegOperationAsync(args, input.Duration, "Compressing audio", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, warnings, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> NormalizeAsync(string inputPath, string outputPath, AudioNormalizationOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateNormalizationOptions(options);

        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var filters = new List<string>
        {
            options.Mode == AudioNormalizationMode.Lufs
                ? FormattableString.Invariant($"loudnorm=I={options.TargetLufs ?? -16}:TP=-1.5:LRA=11")
                : FormattableString.Invariant($"volume={options.TargetPeakDb ?? -1}dB")
        };

        if (options.UseLimiter || options.PreventClipping)
        {
            filters.Add("alimiter=limit=0.98");
        }

        var args = CreateBaseFfmpegArgs(inputPath);
        args.Add("-af");
        args.Add(string.Join(",", filters));
        args.Add(Path.GetFullPath(outputPath));

        await RunFfmpegOperationAsync(args, input.Duration, "Normalizing audio", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, [], progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> ProcessPodcastAudioAsync(string inputPath, string outputPath, AudioPodcastProcessingOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidatePodcastOptions(options);

        var warnings = new List<string>();
        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var analysis = await AnalyzeAudioAsync(inputPath, options.TargetLufs, progress, cancellationToken).ConfigureAwait(false);
        var processingInputPath = inputPath;
        var temporaryDenoisedPath = string.Empty;
        var denoiseEnabled = IsDtlnDenoiseEnabled(options);
        try
        {
            if (denoiseEnabled)
            {
                Report(progress, AudioProcessStage.Preparing, 0.9, "Running DTLN denoise before podcast processing", null, input.Duration, true);
                temporaryDenoisedPath = Path.Combine(Path.GetTempPath(), "files-tools-audio", Guid.NewGuid().ToString("N"), "dtln-denoised.wav");
                Directory.CreateDirectory(Path.GetDirectoryName(temporaryDenoisedPath)!);
                await _dtlnDenoiseService.Value.DenoiseAudioAsync(
                    inputPath,
                    temporaryDenoisedPath,
                    new AudioDenoiseOptions
                    {
                        Mode = options.DtlnDenoiseMode,
                        DenoiseAmount = options.DtlnDenoiseAmount,
                        DenoisePasses = options.DtlnDenoisePasses,
                        OutputSampleRate = input.SampleRate,
                        NormalizePeak = true,
                        PreventClipping = true
                    },
                    null,
                    cancellationToken).ConfigureAwait(false);

                processingInputPath = temporaryDenoisedPath;
                warnings.Add("DTLN denoise was applied before podcast EQ, dynamics, limiting, and LUFS normalization.");
            }

        var filters = BuildPodcastFilterChain(options);
        var codec = ResolveCodec(outputPath, options.OutputCodec);
        var args = CreateBaseFfmpegArgs(processingInputPath);
        ApplyMetadata(args, options.PreserveMetadata);
        args.Add("-af");
        args.Add(string.Join(",", filters));
        ApplyAudioShape(args, codec, options.BitrateKbps, options.SampleRate, options.Channels);
        args.Add(Path.GetFullPath(outputPath));

        warnings.Add("Podcast processing changes tone and dynamics with EQ, compression, limiting, and LUFS normalization.");
        await RunFfmpegOperationAsync(args, input.Duration, "Processing podcast audio", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, warnings, progress, cancellationToken, analysis).ConfigureAwait(false);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryDenoisedPath))
            {
                DeleteTemporaryDirectory(Path.GetDirectoryName(temporaryDenoisedPath));
            }
        }
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> TrimAsync(string inputPath, string outputPath, AudioTrimOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateTrimOptions(options);

        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var args = new List<string> { "-y", "-hide_banner", "-progress", "pipe:2", "-nostats" };
        if (options.StartTime is TimeSpan start)
        {
            args.Add("-ss");
            args.Add(ToFfmpegTimestamp(start));
        }

        args.Add("-i");
        args.Add(Path.GetFullPath(inputPath));

        if (options.Duration is TimeSpan duration)
        {
            args.Add("-t");
            args.Add(ToFfmpegTimestamp(duration));
        }
        else if (options.EndTime is TimeSpan end)
        {
            var trimStart = options.StartTime ?? TimeSpan.Zero;
            args.Add("-t");
            args.Add(ToFfmpegTimestamp(end - trimStart));
        }

        args.Add("-vn");
        args.Add("-c:a");
        args.Add(options.ReEncode ? ResolveCodec(outputPath, null) : "copy");
        args.Add(Path.GetFullPath(outputPath));

        await RunFfmpegOperationAsync(args, input.Duration, "Trimming audio", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, [], progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> RemoveSilenceAsync(string inputPath, string outputPath, AudioSilenceRemovalOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateSilenceOptions(options);

        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var filter = BuildSilenceFilter(options);
        var args = CreateBaseFfmpegArgs(inputPath);
        args.Add("-af");
        args.Add(filter);
        args.Add("-c:a");
        args.Add(ResolveCodec(outputPath, null));
        args.Add(Path.GetFullPath(outputPath));

        await RunFfmpegOperationAsync(args, input.Duration, "Removing silence", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, options.PaddingBeforeCut > TimeSpan.Zero || options.PaddingAfterCut > TimeSpan.Zero
            ? ["Padding options are reserved for future precise silence removal and are not applied in v1."]
            : [], progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> ApplyEqualizerAsync(string inputPath, string outputPath, AudioEqualizerOptions options, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateEqualizerOptions(options);

        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var filter = BuildEqualizerFilter(options);
        var args = CreateBaseFfmpegArgs(inputPath);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            args.Add("-af");
            args.Add(filter);
        }

        args.Add(Path.GetFullPath(outputPath));

        await RunFfmpegOperationAsync(args, input.Duration, "Applying equalizer", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, [], progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AudioProcessResult> RemoveMetadataAsync(string inputPath, string outputPath, IProgress<AudioProcessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);

        var input = await ProbeInputAudioAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        var args = CreateBaseFfmpegArgs(inputPath);
        ApplyMetadata(args, preserveMetadata: false);
        args.Add("-vn");
        args.Add("-c:a");
        args.Add("copy");
        args.Add(Path.GetFullPath(outputPath));

        await RunFfmpegOperationAsync(args, input.Duration, "Removing metadata", progress, cancellationToken).ConfigureAwait(false);
        return await BuildResultAsync(outputPath, [], progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AudioProbeInfo> ProbeInputAudioAsync(string inputPath, IProgress<AudioProcessProgress>? progress, CancellationToken cancellationToken)
    {
        Report(progress, AudioProcessStage.Probing, 0, "Probing input audio", null, null, true);
        var info = await ProbeAudioAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (info.Channels is null || info.SampleRate is null)
        {
            throw new AudioProcessingUnsupportedMediaException("Input file does not contain a readable audio stream.");
        }

        Report(progress, AudioProcessStage.Probing, 1, "Input audio metadata ready", info.Duration, info.Duration, false);
        return info;
    }

    private static async Task<AudioProcessResult> BuildResultAsync(string outputPath, IReadOnlyList<string> warnings, IProgress<AudioProcessProgress>? progress, CancellationToken cancellationToken, AudioAnalysisResult? analysis = null)
    {
        Report(progress, AudioProcessStage.Finalizing, 0, "Reading output metadata", null, null, true);
        var probe = await ProbeAudioAsync(outputPath, cancellationToken).ConfigureAwait(false);
        Report(progress, AudioProcessStage.Completed, 1, "Audio processing completed", probe.Duration, probe.Duration, false);

        return new AudioProcessResult
        {
            OutputPath = Path.GetFullPath(outputPath),
            Duration = probe.Duration,
            OutputCodec = probe.CodecName,
            OutputFormat = probe.FormatName,
            OutputSampleRate = probe.SampleRate,
            OutputChannels = probe.Channels,
            OutputBitrateKbps = probe.BitrateKbps,
            OutputSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : null,
            Warnings = warnings,
            Analysis = analysis
        };
    }

    private static async Task<AudioProbeInfo> ProbeAudioAsync(string inputPath, CancellationToken cancellationToken)
    {
        var args = new List<string>(SplitArguments(FfprobeJsonArgs)) { Path.GetFullPath(inputPath) };
        var result = await RunProcessWithFallbackAsync(FfmpegLocator.ResolveExecutableCandidates("ffprobe"), args, cancellationToken, null).ConfigureAwait(false);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var stream = root.TryGetProperty("streams", out var streams)
            ? streams.EnumerateArray().FirstOrDefault(s => string.Equals(TryGetString(s, "codec_type"), "audio", StringComparison.OrdinalIgnoreCase))
            : default;

        if (stream.ValueKind == JsonValueKind.Undefined)
        {
            throw new AudioProcessingUnsupportedMediaException("Input file does not contain an audio stream.");
        }

        TimeSpan? duration = TryGetDuration(stream);
        string? formatName = null;
        if (root.TryGetProperty("format", out var format))
        {
            duration ??= TryGetDuration(format);
            formatName = TryGetString(format, "format_name");
        }

        return new AudioProbeInfo
        {
            CodecName = TryGetString(stream, "codec_name"),
            FormatName = formatName,
            SampleRate = TryGetInt(stream, "sample_rate"),
            Channels = TryGetInt(stream, "channels"),
            Duration = duration,
            BitrateKbps = TryGetLong(stream, "bit_rate") is long bitrate ? (int)Math.Max(1, bitrate / 1000) : null
        };
    }

    private static async Task<AudioAnalysisResult> AnalyzeAudioAsync(string inputPath, double targetLufs, IProgress<AudioProcessProgress>? progress, CancellationToken cancellationToken)
    {
        Report(progress, AudioProcessStage.Preparing, 0.25, "Analyzing loudness and peaks", null, null, true);
        var absoluteInput = Path.GetFullPath(inputPath);
        var volumeArgs = new List<string>
        {
            "-hide_banner",
            "-nostats",
            "-i",
            absoluteInput,
            "-af",
            "volumedetect",
            "-f",
            "null",
            "-"
        };

        var loudnessArgs = new List<string>
        {
            "-hide_banner",
            "-nostats",
            "-i",
            absoluteInput,
            "-af",
            FormattableString.Invariant($"loudnorm=I={targetLufs:0.###}:TP=-1.5:LRA=11:print_format=json"),
            "-f",
            "null",
            "-"
        };

        var volumeResult = await RunProcessWithFallbackAsync(FfmpegLocator.ResolveExecutableCandidates("ffmpeg"), volumeArgs, cancellationToken, null).ConfigureAwait(false);
        var loudnessResult = await RunProcessWithFallbackAsync(FfmpegLocator.ResolveExecutableCandidates("ffmpeg"), loudnessArgs, cancellationToken, null).ConfigureAwait(false);
        Report(progress, AudioProcessStage.Preparing, 0.75, "Audio analysis completed", null, null, false);

        var loudnormJson = ExtractLastJsonObject(loudnessResult.StandardError);
        double? integratedLufs = null;
        double? truePeak = null;
        if (loudnormJson is not null)
        {
            using var document = JsonDocument.Parse(loudnormJson);
            integratedLufs = TryGetDouble(document.RootElement, "input_i");
            truePeak = TryGetDouble(document.RootElement, "input_tp");
        }

        return new AudioAnalysisResult
        {
            IntegratedLufs = integratedLufs,
            TruePeakDb = truePeak,
            MeanVolumeDb = ParseVolumedetectValue(volumeResult.StandardError, "mean_volume:"),
            MaxVolumeDb = ParseVolumedetectValue(volumeResult.StandardError, "max_volume:")
        };
    }

    private static List<string> CreateBaseFfmpegArgs(string inputPath)
    {
        return ["-y", "-hide_banner", "-progress", "pipe:2", "-nostats", "-i", Path.GetFullPath(inputPath), "-vn"];
    }

    private static void ApplyAudioShape(List<string> args, string codec, int? bitrateKbps, int? sampleRate, int? channels)
    {
        args.Add("-c:a");
        args.Add(codec);
        if (bitrateKbps is int bitrate)
        {
            args.Add("-b:a");
            args.Add(FormattableString.Invariant($"{bitrate}k"));
        }

        if (sampleRate is int rate)
        {
            args.Add("-ar");
            args.Add(rate.ToString(CultureInfo.InvariantCulture));
        }

        if (channels is int channelCount)
        {
            args.Add("-ac");
            args.Add(channelCount.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddFormat(List<string> args, string? format)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            args.Add("-f");
            args.Add(format);
        }
    }

    private static void ApplyMetadata(List<string> args, bool preserveMetadata)
    {
        if (!preserveMetadata)
        {
            args.Add("-map_metadata");
            args.Add("-1");
        }
    }

    private static string BuildSilenceFilter(AudioSilenceRemovalOptions options)
    {
        var duration = Math.Max(0.001, options.MinimumSilenceDuration.TotalSeconds);
        var threshold = FormattableString.Invariant($"{options.SilenceThresholdDb}dB");
        return options.Mode switch
        {
            SilenceRemovalMode.Leading => FormattableString.Invariant($"silenceremove=start_periods=1:start_duration={duration:0.###}:start_threshold={threshold}"),
            SilenceRemovalMode.Trailing => FormattableString.Invariant($"areverse,silenceremove=start_periods=1:start_duration={duration:0.###}:start_threshold={threshold},areverse"),
            SilenceRemovalMode.LeadingAndTrailing => FormattableString.Invariant($"silenceremove=start_periods=1:start_duration={duration:0.###}:start_threshold={threshold},areverse,silenceremove=start_periods=1:start_duration={duration:0.###}:start_threshold={threshold},areverse"),
            _ => throw new NotSupportedException("Internal silence removal is not supported in v1.")
        };
    }

    private static string? BuildEqualizerFilter(AudioEqualizerOptions options)
    {
        var filters = options.Preset == EqualizerPreset.Custom
            ? options.CustomBands.Select(BuildBandFilter).ToList()
            : BuildPresetFilters(options.Preset).ToList();

        if (options.PreventClipping && filters.Count > 0)
        {
            filters.Add("alimiter=limit=0.98");
        }

        return filters.Count == 0 ? null : string.Join(",", filters);
    }

    private static IReadOnlyList<string> BuildPodcastFilterChain(AudioPodcastProcessingOptions options)
    {
        var filters = new List<string>();
        filters.Add(FormattableString.Invariant($"highpass=f={options.HighPassFrequencyHz:0.###}"));
        filters.AddRange(BuildPresetFilters(EqualizerPreset.PodcastVoice).Where(filter => !filter.StartsWith("highpass=", StringComparison.OrdinalIgnoreCase)));

        if (options.EnableDeEsser)
        {
            filters.Add("equalizer=f=6500:t=q:w=2:g=-2.5");
            filters.Add("equalizer=f=8500:t=q:w=1.5:g=-1.5");
        }

        if (options.EnableCompressor)
        {
            filters.Add("acompressor=threshold=-18dB:ratio=3:attack=5:release=80:makeup=2");
        }

        filters.Add(FormattableString.Invariant($"alimiter=limit={options.LimiterLimit:0.###}"));
        filters.Add(FormattableString.Invariant($"loudnorm=I={options.TargetLufs:0.###}:TP=-1.5:LRA=11"));
        filters.Add(FormattableString.Invariant($"alimiter=limit={options.LimiterLimit:0.###}"));
        return filters;
    }

    private static IEnumerable<string> BuildPresetFilters(EqualizerPreset preset)
    {
        return preset switch
        {
            EqualizerPreset.None => [],
            EqualizerPreset.PodcastVoice => ["highpass=f=80", "equalizer=f=250:t=q:w=1:g=-2", "equalizer=f=3000:t=q:w=1:g=2", "equalizer=f=7000:t=q:w=1:g=-1"],
            EqualizerPreset.VoiceClarity => ["highpass=f=90", "equalizer=f=3200:t=q:w=1:g=2"],
            EqualizerPreset.WarmVoice => ["highpass=f=70", "equalizer=f=180:t=q:w=1:g=1.5", "equalizer=f=3500:t=q:w=1:g=1"],
            EqualizerPreset.BrightVoice => ["highpass=f=90", "equalizer=f=4500:t=q:w=1:g=2.5"],
            EqualizerPreset.ReduceBass => ["highpass=f=120", "equalizer=f=180:t=q:w=1:g=-3"],
            EqualizerPreset.ReduceTreble => ["lowpass=f=8500", "equalizer=f=6000:t=q:w=1:g=-2"],
            EqualizerPreset.PhoneVoice => ["highpass=f=300", "lowpass=f=3400"],
            EqualizerPreset.RadioVoice => ["highpass=f=120", "lowpass=f=6500", "equalizer=f=2500:t=q:w=1:g=2"],
            EqualizerPreset.RemoveElectricalHum50Hz => ["equalizer=f=50:t=q:w=12:g=-18", "equalizer=f=100:t=q:w=12:g=-8"],
            EqualizerPreset.RemoveElectricalHum60Hz => ["equalizer=f=60:t=q:w=12:g=-18", "equalizer=f=120:t=q:w=12:g=-8"],
            EqualizerPreset.Custom => [],
            _ => throw new AudioProcessingValidationException("Unsupported equalizer preset.")
        };
    }

    private static string BuildBandFilter(EqualizerBand band)
    {
        return FormattableString.Invariant($"equalizer=f={band.FrequencyHz:0.###}:t=q:w={band.Width:0.###}:g={band.GainDb:0.###}");
    }

    private static void ValidateCommonAudioOptions(int? bitrateKbps, int? sampleRate, int? channels)
    {
        if (bitrateKbps is <= 0)
        {
            throw new AudioProcessingValidationException("Bitrate must be greater than 0.");
        }

        if (sampleRate is <= 0)
        {
            throw new AudioProcessingValidationException("Sample rate must be greater than 0.");
        }

        if (channels is not null and not 1 and not 2)
        {
            throw new AudioProcessingValidationException("Channel count must be 1, 2, or null in v1.");
        }
    }

    private static void ValidateNormalizationOptions(AudioNormalizationOptions options)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            throw new AudioProcessingValidationException("Normalization mode is invalid.");
        }

        if (options.TargetPeakDb is < -60 or > 0)
        {
            throw new AudioProcessingValidationException("Target peak must be between -60 and 0 dB.");
        }

        if (options.TargetLufs is < -70 or > 0)
        {
            throw new AudioProcessingValidationException("Target LUFS must be between -70 and 0.");
        }
    }

    private static void ValidatePodcastOptions(AudioPodcastProcessingOptions options)
    {
        ValidateCommonAudioOptions(options.BitrateKbps, options.SampleRate, options.Channels);

        if (options.HighPassFrequencyHz is <= 0 or > 1000)
        {
            throw new AudioProcessingValidationException("High-pass frequency must be greater than 0 and no more than 1000 Hz.");
        }

        if (options.TargetLufs is < -70 or > 0)
        {
            throw new AudioProcessingValidationException("Target LUFS must be between -70 and 0.");
        }

        if (options.LimiterLimit is <= 0 or > 1)
        {
            throw new AudioProcessingValidationException("Limiter limit must be greater than 0 and no more than 1.");
        }

        if (!Enum.IsDefined(options.DtlnDenoiseMode))
        {
            throw new AudioProcessingValidationException("DTLN denoise mode is invalid.");
        }

        if (options.DtlnDenoiseAmount is < 0 or > 100)
        {
            throw new AudioProcessingValidationException("DTLN denoise amount must be between 0 and 100.");
        }

        if (options.DtlnDenoisePasses is < 1 or > 3)
        {
            throw new AudioProcessingValidationException("DTLN denoise passes must be between 1 and 3.");
        }
    }

    private static bool IsDtlnDenoiseEnabled(AudioPodcastProcessingOptions options)
    {
#pragma warning disable CS0618
        return options.EnableDtlnDenoise || options.EnableLightweightDenoise;
#pragma warning restore CS0618
    }

    private static void DeleteTemporaryDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static void ValidateTrimOptions(AudioTrimOptions options)
    {
        if (options.StartTime is null && options.EndTime is null && options.Duration is null)
        {
            throw new AudioProcessingValidationException("Trim requires a start, end, or duration.");
        }

        if ((options.StartTime.HasValue && options.StartTime.Value < TimeSpan.Zero) ||
            (options.EndTime.HasValue && options.EndTime.Value < TimeSpan.Zero) ||
            (options.Duration.HasValue && options.Duration.Value <= TimeSpan.Zero))
        {
            throw new AudioProcessingValidationException("Trim times must be positive and duration must be greater than 0.");
        }

        if (options.EndTime is TimeSpan end && options.StartTime is TimeSpan start && end <= start)
        {
            throw new AudioProcessingValidationException("Trim end time must be greater than start time.");
        }
    }

    private static void ValidateSilenceOptions(AudioSilenceRemovalOptions options)
    {
        if (options.Mode == SilenceRemovalMode.Internal)
        {
            throw new NotSupportedException("Internal silence removal is not supported in v1.");
        }

        if (options.SilenceThresholdDb is < -100 or > 0)
        {
            throw new AudioProcessingValidationException("Silence threshold must be between -100 and 0 dB.");
        }

        if (options.MinimumSilenceDuration <= TimeSpan.Zero)
        {
            throw new AudioProcessingValidationException("Minimum silence duration must be greater than 0.");
        }
    }

    private static void ValidateEqualizerOptions(AudioEqualizerOptions options)
    {
        if (!Enum.IsDefined(options.Preset))
        {
            throw new AudioProcessingValidationException("Equalizer preset is invalid.");
        }

        if (options.Preset == EqualizerPreset.Custom && options.CustomBands.Count == 0)
        {
            throw new AudioProcessingValidationException("Custom equalizer requires at least one band.");
        }

        foreach (var band in options.CustomBands)
        {
            if (band.FrequencyHz <= 0 || band.Width <= 0)
            {
                throw new AudioProcessingValidationException("Custom EQ frequency and width must be greater than 0.");
            }

            if (band.GainDb is < -24 or > 24)
            {
                throw new AudioProcessingValidationException("Custom EQ gain must be between -24 and 24 dB.");
            }
        }
    }

    private static async Task RunFfmpegOperationAsync(List<string> args, TimeSpan? duration, string description, IProgress<AudioProcessProgress>? progress, CancellationToken cancellationToken)
    {
        Report(progress, AudioProcessStage.Preparing, 1, "Audio command prepared", null, duration, false);
        var observer = CreateProgressObserver(duration, description, progress);
        await RunProcessWithFallbackAsync(FfmpegLocator.ResolveExecutableCandidates("ffmpeg"), args, cancellationToken, observer).ConfigureAwait(false);
    }

    private static Action<string>? CreateProgressObserver(TimeSpan? totalDuration, string description, IProgress<AudioProcessProgress>? progress)
    {
        if (progress is null || totalDuration is null || totalDuration <= TimeSpan.Zero)
        {
            return null;
        }

        var etaEstimator = new EtaEstimator();
        var lastProcessed = TimeSpan.Zero;
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

            if (!line.StartsWith("progress=", StringComparison.Ordinal))
            {
                return;
            }

            var isCompleted = string.Equals(line["progress=".Length..], "end", StringComparison.Ordinal);
            var clamped = lastProcessed > totalDuration.Value ? totalDuration.Value : lastProcessed;
            var fraction = Math.Clamp(clamped.TotalMilliseconds / totalDuration.Value.TotalMilliseconds, 0d, 1d);
            var eta = etaEstimator.AddSample(isCompleted ? 1d : fraction);

            progress.Report(new AudioProcessProgress
            {
                Stage = isCompleted ? AudioProcessStage.Encoding : AudioProcessStage.Processing,
                OverallPercent = isCompleted ? 0.9 : 0.1 + (fraction * 0.8),
                StagePercent = isCompleted ? 1 : fraction,
                StageDescription = description,
                ProcessedDuration = isCompleted ? totalDuration : clamped,
                TotalDuration = totalDuration,
                EstimatedRemainingTime = isCompleted ? TimeSpan.Zero : eta,
                IsFfmpegActive = true
            });
        };
    }

    private static string ResolveCodec(string outputPath, string? requestedCodec)
    {
        if (!string.IsNullOrWhiteSpace(requestedCodec))
        {
            return requestedCodec;
        }

        return Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".mp3" => "libmp3lame",
            ".m4a" or ".aac" => "aac",
            ".opus" => "libopus",
            ".ogg" => "libvorbis",
            ".flac" => "flac",
            ".wav" => "pcm_s16le",
            _ => throw new AudioProcessingValidationException($"Unsupported output extension '{Path.GetExtension(outputPath)}'.")
        };
    }

    private static string ResolveFormat(string outputPath)
    {
        return Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".mp3" => "mp3",
            ".m4a" => "ipod",
            ".aac" => "adts",
            ".opus" => "opus",
            ".ogg" => "ogg",
            ".flac" => "flac",
            ".wav" => "wav",
            _ => throw new AudioProcessingValidationException($"Unsupported output extension '{Path.GetExtension(outputPath)}'.")
        };
    }

    private static void ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Input path cannot be null or whitespace.", nameof(path));
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

    private static void Report(IProgress<AudioProcessProgress>? progress, AudioProcessStage stage, double stagePercent, string description, TimeSpan? processed, TimeSpan? total, bool isFfmpegActive)
    {
        progress?.Report(new AudioProcessProgress
        {
            Stage = stage,
            OverallPercent = stage switch
            {
                AudioProcessStage.Probing => stagePercent * 0.05,
                AudioProcessStage.Preparing => 0.05 + (stagePercent * 0.05),
                AudioProcessStage.Finalizing => 0.9 + (stagePercent * 0.08),
                AudioProcessStage.Completed => 1,
                _ => stagePercent
            },
            StagePercent = Math.Clamp(stagePercent, 0, 1),
            StageDescription = description,
            ProcessedDuration = processed,
            TotalDuration = total,
            IsFfmpegActive = isFfmpegActive
        });
    }

    private static async Task<ProcessResult> RunProcessWithFallbackAsync(IReadOnlyList<string> binaryCandidates, IReadOnlyList<string> arguments, CancellationToken cancellationToken, Action<string>? standardErrorLineObserver)
    {
        AudioProcessingFfmpegException? lastException = null;
        foreach (var candidate in binaryCandidates)
        {
            try
            {
                return await RunProcessAsync(candidate, arguments, cancellationToken, standardErrorLineObserver).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AudioProcessingFfmpegException ex) when (CanFallbackToPath(candidate, ex, binaryCandidates))
            {
                lastException = ex;
            }
        }

        throw lastException ?? new AudioProcessingFfmpegException("No FFmpeg executable candidates were available.", "ffmpeg", string.Empty, null, string.Empty, string.Empty);
    }

    private static async Task<ProcessResult> RunProcessAsync(string binaryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken, Action<string>? standardErrorLineObserver)
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
            throw new AudioProcessingFfmpegException("Failed to start FFmpeg/FFprobe process.", binaryPath, FormatCommandLine(binaryPath, arguments), null, string.Empty, ex.Message, ex);
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
                // Best effort cancellation cleanup.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                stderrBuilder.AppendLine(line);
                standardErrorLineObserver?.Invoke(line);
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);
        var stderr = stderrBuilder.ToString();

        if (process.ExitCode != 0)
        {
            throw new AudioProcessingFfmpegException("FFmpeg/FFprobe exited with a non-zero code.", binaryPath, FormatCommandLine(binaryPath, arguments), process.ExitCode, stdout, stderr);
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static bool CanFallbackToPath(string candidate, AudioProcessingFfmpegException exception, IReadOnlyList<string> candidates)
    {
        return candidates.Count > 1 && Path.IsPathRooted(candidate) && exception.ExitCode is null;
    }

    private static string FormatCommandLine(string binaryPath, IReadOnlyList<string> arguments)
    {
        return string.Join(" ", new[] { Quote(binaryPath) }.Concat(arguments.Select(Quote)));
    }

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static IEnumerable<string> SplitArguments(string args)
    {
        return args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static TimeSpan? TryGetDuration(JsonElement element)
    {
        var durationString = TryGetString(element, "duration");
        return durationString is not null && double.TryParse(durationString, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;
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

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : null;
    }

    private static double? TryGetDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
        {
            return value;
        }

        return property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : null;
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

        return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : null;
    }

    private static TimeSpan ParseProgressTimestamp(string value)
    {
        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture, out var precise))
        {
            return precise;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;
    }

    private static string ToFfmpegTimestamp(TimeSpan value)
    {
        return value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string? ExtractLastJsonObject(string text)
    {
        var end = text.LastIndexOf('}');
        if (end < 0)
        {
            return null;
        }

        var start = text.LastIndexOf('{', end);
        return start < 0 ? null : text[start..(end + 1)];
    }

    private static double? ParseVolumedetectValue(string stderr, string marker)
    {
        foreach (var line in stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var markerIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var valueStart = markerIndex + marker.Length;
            var valueEnd = line.IndexOf(" dB", valueStart, StringComparison.OrdinalIgnoreCase);
            var valueText = valueEnd < 0 ? line[valueStart..] : line[valueStart..valueEnd];
            if (double.TryParse(valueText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private sealed class AudioProbeInfo
    {
        public string? CodecName { get; init; }

        public string? FormatName { get; init; }

        public int? SampleRate { get; init; }

        public int? Channels { get; init; }

        public TimeSpan? Duration { get; init; }

        public int? BitrateKbps { get; init; }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
