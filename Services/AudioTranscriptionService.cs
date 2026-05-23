using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace Files_Tools.Services;

/// <summary>
/// Defines minimal local Whisper-backed transcription operations.
/// </summary>
public interface IAudioTranscriptionService
{
    /// <summary>
    /// Returns whether the Whisper base model is installed locally.
    /// </summary>
    bool IsInstalled();

    /// <summary>
    /// Installs the Whisper base model when missing.
    /// </summary>
    Task InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs the Whisper base model when missing and reports progress.
    /// </summary>
    Task InstallAsync(IProgress<AudioTranscriptionInstallProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into timestamped segments.
    /// </summary>
    Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeToSegmentsAsync(string inputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into timestamped segments and reports progress.
    /// </summary>
    Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeToSegmentsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into timestamped words.
    /// </summary>
    Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into timestamped words and reports progress.
    /// </summary>
    Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into a detailed result with raw tokens and cleaned words.
    /// </summary>
    Task<AudioTranscriptionDetailedResult> TranscribeToDetailedResultAsync(string inputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into a detailed result with raw tokens and cleaned words and reports progress.
    /// </summary>
    Task<AudioTranscriptionDetailedResult> TranscribeToDetailedResultAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into plain text.
    /// </summary>
    Task<string> TranscribeToTextAsync(string inputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into plain text and reports progress.
    /// </summary>
    Task<string> TranscribeToTextAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into timestamped plain text.
    /// </summary>
    Task<string> TranscribeToTimestampedTextAsync(string inputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into timestamped plain text and reports progress.
    /// </summary>
    Task<string> TranscribeToTimestampedTextAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base exception for transcription service failures.
/// </summary>
public class AudioTranscriptionException : InvalidOperationException
{
    /// <summary>
    /// Creates a new transcription exception.
    /// </summary>
    public AudioTranscriptionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a transcription request requires a missing Whisper model.
/// </summary>
public sealed class AudioTranscriptionNotInstalledException : AudioTranscriptionException
{
    /// <summary>
    /// Creates a new missing-installation exception.
    /// </summary>
    public AudioTranscriptionNotInstalledException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Progress payload for Whisper model installation.
/// </summary>
public sealed class AudioTranscriptionInstallProgress
{
    public required string Stage { get; init; }

    public required double FractionComplete { get; init; }
}

/// <summary>
/// Single Whisper transcription segment with start and end timestamps.
/// </summary>
public sealed record AudioTranscriptionSegment(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Single word-level Whisper transcription unit with start and end timestamps.
/// </summary>
public sealed record AudioTranscriptionWord(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Describes where cleaned word timing originated from.
/// </summary>
public enum AudioTranscriptionTimingSource
{
    RawTokenAlignment,
    WhisperWordTiming,
    SegmentFallback
}

/// <summary>
/// Raw Whisper token with normalized timing and probability payload.
/// </summary>
public sealed record AudioTranscriptionToken(
    int SegmentIndex,
    int TokenIndex,
    int TokenId,
    int TimestampId,
    string Text,
    TimeSpan Start,
    TimeSpan End,
    TimeSpan? DtwTimestamp,
    float Probability,
    float ProbabilityLog,
    float TimestampProbability,
    float TimestampProbabilitySum,
    float VoiceLength,
    bool IsSpecial);

/// <summary>
/// Detailed Whisper segment with raw token payload.
/// </summary>
public sealed class AudioTranscriptionDetailedSegment
{
    public AudioTranscriptionDetailedSegment(
        int index,
        TimeSpan start,
        TimeSpan end,
        string text,
        float probability,
        float minProbability,
        float maxProbability,
        float noSpeechProbability,
        string language,
        IReadOnlyList<AudioTranscriptionToken> tokens)
    {
        Index = index;
        Start = start;
        End = end;
        Text = text ?? string.Empty;
        Probability = probability;
        MinProbability = minProbability;
        MaxProbability = maxProbability;
        NoSpeechProbability = noSpeechProbability;
        Language = language ?? string.Empty;
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public int Index { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public string Text { get; }

    public float Probability { get; }

    public float MinProbability { get; }

    public float MaxProbability { get; }

    public float NoSpeechProbability { get; }

    public string Language { get; }

    public IReadOnlyList<AudioTranscriptionToken> Tokens { get; }
}

/// <summary>
/// Cleaned word unit aligned to a source segment.
/// </summary>
public sealed record AudioTranscriptionAlignedWord(
    int SegmentIndex,
    int WordIndex,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    AudioTranscriptionTimingSource TimingSource);

/// <summary>
/// Detailed transcription result used by subtitle-oriented flows.
/// </summary>
public sealed class AudioTranscriptionDetailedResult
{
    public AudioTranscriptionDetailedResult(
        IReadOnlyList<AudioTranscriptionDetailedSegment> segments,
        IReadOnlyList<AudioTranscriptionAlignedWord> words)
    {
        Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        Words = words ?? throw new ArgumentNullException(nameof(words));
    }

    public IReadOnlyList<AudioTranscriptionDetailedSegment> Segments { get; }

    public IReadOnlyList<AudioTranscriptionAlignedWord> Words { get; }
}

/// <summary>
/// High-level stages for transcription work.
/// </summary>
public enum AudioTranscriptionStage
{
    PreparingAudio,
    Transcribing,
    WritingSubtitles,
    Completed
}

/// <summary>
/// Progress payload for transcription and subtitle generation.
/// </summary>
public sealed class AudioTranscriptionProgress
{
    public AudioTranscriptionStage Stage { get; init; }

    public double OverallPercent { get; init; }

    public double StagePercent { get; init; }

    public string StageDescription { get; init; } = string.Empty;

    public TimeSpan? EstimatedRemainingTime { get; init; }
}

/// <summary>
/// Minimal Whisper-backed transcription service.
/// </summary>
public sealed class AudioTranscriptionService : IAudioTranscriptionService
{
    private const string BaseModelFileName = "ggml-medium.bin";
    private static readonly string[] SupportedVideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v", ".gif"];

    private readonly string _modelPath;
    private readonly IWhisperModelInstaller _modelInstaller;
    private readonly IWhisperTranscriber _transcriber;
    private readonly IMediaPreparationService _mediaPreparationService;

    /// <summary>
    /// Creates the service with default local media preparation and Whisper adapters.
    /// </summary>
    public AudioTranscriptionService()
        : this(
            ResolveDefaultModelPath(),
            new WhisperModelInstaller(),
            new WhisperNetTranscriber(),
            new MediaPreparationService(new AudioProcessingService(), new VideoProcessingService()))
    {
    }

    internal AudioTranscriptionService(
        string modelPath,
        IWhisperModelInstaller modelInstaller,
        IWhisperTranscriber transcriber,
        IMediaPreparationService mediaPreparationService)
    {
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _modelInstaller = modelInstaller ?? throw new ArgumentNullException(nameof(modelInstaller));
        _transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        _mediaPreparationService = mediaPreparationService ?? throw new ArgumentNullException(nameof(mediaPreparationService));
    }

    /// <inheritdoc />
    public bool IsInstalled()
    {
        return File.Exists(_modelPath);
    }

    /// <inheritdoc />
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        await InstallAsync(progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InstallAsync(IProgress<AudioTranscriptionInstallProgress>? progress, CancellationToken cancellationToken = default)
    {
        if (IsInstalled())
        {
            progress?.Report(new AudioTranscriptionInstallProgress
            {
                Stage = "Transcription feature already downloaded.",
                FractionComplete = 1d
            });
            return;
        }

        var modelDirectory = Path.GetDirectoryName(_modelPath);
        if (string.IsNullOrWhiteSpace(modelDirectory))
        {
            throw new AudioTranscriptionException("Unable to resolve the Whisper model directory.");
        }

        progress?.Report(new AudioTranscriptionInstallProgress
        {
            Stage = "Preparing model download...",
            FractionComplete = 0d
        });

        Directory.CreateDirectory(modelDirectory);
        var copyProgress = progress is null
            ? null
            : new ThrottledProgress<double>(value =>
            {
                var fraction = Math.Clamp(value, 0d, 1d);
                progress.Report(new AudioTranscriptionInstallProgress
                {
                    Stage = fraction >= 1d ? "Transcription feature downloaded." : "Downloading transcription feature...",
                    FractionComplete = fraction
                });
            }, throttleMilliseconds: 200);

        await _modelInstaller.InstallBaseModelAsync(_modelPath, copyProgress, cancellationToken).ConfigureAwait(false);
        progress?.Report(new AudioTranscriptionInstallProgress
        {
            Stage = "Transcription feature downloaded.",
            FractionComplete = 1d
        });
    }

    /// <inheritdoc />
    public async Task<string> TranscribeToTextAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        return await TranscribeToTextAsync(inputPath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeToSegmentsAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        return await TranscribeToSegmentsAsync(inputPath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeToSegmentsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        EnsureInstalled();

        var progressState = new ProgressState();
        var result = await TranscribeResultCoreAsync(inputPath, progress, progressState, TranscriptionGranularity.Segments, cancellationToken).ConfigureAwait(false);
        return result.Segments;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        return await TranscribeToWordsAsync(inputPath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        var detailed = await TranscribeToDetailedResultAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        return detailed.Words
            .Select(word => new AudioTranscriptionWord(word.Start, word.End, word.Text))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<AudioTranscriptionDetailedResult> TranscribeToDetailedResultAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        return await TranscribeToDetailedResultAsync(inputPath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AudioTranscriptionDetailedResult> TranscribeToDetailedResultAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        EnsureInstalled();

        var progressState = new ProgressState();
        var result = await TranscribeResultCoreAsync(inputPath, progress, progressState, TranscriptionGranularity.Detailed, cancellationToken).ConfigureAwait(false);
        return result.DetailedResult ?? new AudioTranscriptionDetailedResult(Array.Empty<AudioTranscriptionDetailedSegment>(), Array.Empty<AudioTranscriptionAlignedWord>());
    }

    /// <inheritdoc />
    public async Task<string> TranscribeToTextAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        var progressState = new ProgressState();
        var result = await TranscribeResultInternalAsync(inputPath, progress, progressState, TranscriptionGranularity.Segments, cancellationToken).ConfigureAwait(false);
        var text = string.Join(" ", result.Segments.Select(segment => segment.Text.Trim()).Where(text => text.Length > 0)).Trim();
        Report(progress, progressState, AudioTranscriptionStage.Completed, 1d, "Transcription complete");
        return text;
    }

    /// <inheritdoc />
    public async Task<string> TranscribeToTimestampedTextAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        return await TranscribeToTimestampedTextAsync(inputPath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> TranscribeToTimestampedTextAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        var progressState = new ProgressState();
        var result = await TranscribeResultInternalAsync(inputPath, progress, progressState, TranscriptionGranularity.Segments, cancellationToken).ConfigureAwait(false);
        var lines = result.Segments
            .Select(segment => new
            {
                Text = segment.Text.Trim(),
                Timestamp = segment.Start
            })
            .Where(line => line.Text.Length > 0)
            .Select(line => $"[{FormatTranscriptTimestamp(line.Timestamp)}] {line.Text}");

        Report(progress, progressState, AudioTranscriptionStage.Completed, 1d, "Transcription complete");
        return string.Join(Environment.NewLine, lines);
    }

    internal async Task<AudioTranscriptionResult> TranscribeResultInternalAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, ProgressState progressState, TranscriptionGranularity granularity, CancellationToken cancellationToken)
    {
        ValidateInputPath(inputPath);
        EnsureInstalled();
        return await TranscribeResultCoreAsync(inputPath, progress, progressState, granularity, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AudioTranscriptionResult> TranscribeResultCoreAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, ProgressState progressState, TranscriptionGranularity granularity, CancellationToken cancellationToken)
    {
        PreparedAudio? preparedAudio = null;

        try
        {
            var preparationProgress = progress is null
                ? null
                : new CallbackProgress<double>(value => Report(progress, progressState, AudioTranscriptionStage.PreparingAudio, value, "Preparing audio for transcription"));
            preparedAudio = await _mediaPreparationService.PrepareAsync(inputPath, preparationProgress, cancellationToken).ConfigureAwait(false);

            var transcriptionProgress = progress is null
                ? null
                : new CallbackProgress<double>(value => Report(progress, progressState, AudioTranscriptionStage.Transcribing, value, "Transcribing audio"));
            return await _transcriber.TranscribeAsync(_modelPath, preparedAudio.AudioPath, granularity, transcriptionProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (AudioTranscriptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AudioTranscriptionException("Failed to transcribe the input media.", ex);
        }
        finally
        {
            if (preparedAudio is not null)
            {
                preparedAudio.Dispose();
            }
        }
    }

    private void EnsureInstalled()
    {
        if (!IsInstalled())
        {
            throw new AudioTranscriptionNotInstalledException("Whisper base model is not installed.");
        }
    }

    private static string FormatTranscriptTimestamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    private static string ResolveDefaultModelPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Media Tools",
            "Whisper");

        return Path.Combine(root, BaseModelFileName);
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

    internal sealed class ProgressState
    {
        public DateTimeOffset? StartedAtUtc { get; set; }
    }

    internal enum TranscriptionGranularity
    {
        Segments,
        Detailed
    }

    internal sealed class AudioTranscriptionResult
    {
        public AudioTranscriptionResult(IReadOnlyList<AudioTranscriptionSegment> segments, AudioTranscriptionDetailedResult? detailedResult)
        {
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
            DetailedResult = detailedResult;
        }

        public IReadOnlyList<AudioTranscriptionSegment> Segments { get; }

        public AudioTranscriptionDetailedResult? DetailedResult { get; }
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Report(T value)
        {
            _callback(value);
        }
    }

    private sealed class ThrottledProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        private readonly int _throttleMilliseconds;
        private DateTimeOffset _lastReportTime = DateTimeOffset.MinValue;

        public ThrottledProgress(Action<T> callback, int throttleMilliseconds = 200)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _throttleMilliseconds = throttleMilliseconds;
        }

        public void Report(T value)
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - _lastReportTime).TotalMilliseconds < _throttleMilliseconds)
            {
                return;
            }

            _lastReportTime = now;
            _callback(value);
        }
    }

    internal sealed class PreparedAudio : IDisposable
    {
        public PreparedAudio(string audioPath, string workingDirectory)
        {
            AudioPath = audioPath;
            WorkingDirectory = workingDirectory;
        }

        public string AudioPath { get; }

        public string WorkingDirectory { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(WorkingDirectory))
                {
                    Directory.Delete(WorkingDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    internal interface IWhisperModelInstaller
    {
        Task InstallBaseModelAsync(string modelPath, IProgress<double>? progress, CancellationToken cancellationToken);
    }

    internal interface IWhisperTranscriber
    {
        Task<AudioTranscriptionResult> TranscribeAsync(string modelPath, string audioPath, TranscriptionGranularity granularity, IProgress<double>? progress, CancellationToken cancellationToken);
    }

    internal interface IMediaPreparationService
    {
        Task<PreparedAudio> PrepareAsync(string inputPath, IProgress<double>? progress, CancellationToken cancellationToken);
    }

    private sealed class WhisperModelInstaller : IWhisperModelInstaller
    {
        private const double UnknownLengthTargetBytes = 1500d * 1024d * 1024d;

        public async Task InstallBaseModelAsync(string modelPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (File.Exists(modelPath))
            {
                progress?.Report(1d);
                return;
            }

            await using var sourceStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Medium, QuantizationType.NoQuantization, cancellationToken).ConfigureAwait(false);
            var totalLength = sourceStream.CanSeek ? sourceStream.Length : -1L;
            await using var targetStream = File.Create(modelPath);
            var buffer = new byte[81920];
            long copied = 0;

            while (true)
            {
                var read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await targetStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;

                if (totalLength > 0)
                {
                    progress?.Report((double)copied / totalLength);
                }
                else
                {
                    // Fallback for non-seekable streams: provide smooth, bounded progress
                    // so the UI does not jump from 0% to 100% at the end.
                    var estimated = copied / (copied + UnknownLengthTargetBytes);
                    var bounded = Math.Clamp(estimated, 0d, 0.97d);
                    progress?.Report(bounded);
                }
            }

            progress?.Report(1d);
        }
    }

    internal sealed class WhisperNetTranscriber : IWhisperTranscriber
    {
        private const float DefaultTokenTimestampThreshold = 0.01f;
        private const long WhisperTimestampUnitMilliseconds = 10L;
        private const long MinimumDurationTicks = TimeSpan.TicksPerMillisecond;

        public async Task<AudioTranscriptionResult> TranscribeAsync(string modelPath, string audioPath, TranscriptionGranularity granularity, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (granularity == TranscriptionGranularity.Segments)
            {
                var segments = await TranscribeSegmentsOnlyAsync(modelPath, audioPath, progress, cancellationToken).ConfigureAwait(false);
                return new AudioTranscriptionResult(segments, detailedResult: null);
            }

            var detailed = await TranscribeDetailedAsync(modelPath, audioPath, progress, cancellationToken).ConfigureAwait(false);
            var projectedSegments = detailed.Segments
                .Select(segment => new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text))
                .ToArray();
            return new AudioTranscriptionResult(projectedSegments, detailed);
        }

        private static async Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeSegmentsOnlyAsync(string modelPath, string audioPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = CreateProcessor(whisperFactory, WhisperPassMode.Segments, progress);
            await using var audioStream = File.OpenRead(audioPath);
            var segments = new List<AudioTranscriptionSegment>();

            await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
            {
                try
                {
                    segments.Add(new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text ?? string.Empty));
                }
                finally
                {
                    processor.Return(segment);
                }
            }

            return segments;
        }

        private static async Task<AudioTranscriptionDetailedResult> TranscribeDetailedAsync(string modelPath, string audioPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            var segments = await TranscribeDetailedSegmentsAsync(modelPath, audioPath, WhisperPassMode.RawTokens, progress, cancellationToken).ConfigureAwait(false);
            var tokenAlignedWords = CleanupAlignedWords(
                segments,
                AlignWordsFromTokens(segments),
                AudioTranscriptionTimingSource.RawTokenAlignment);
            return new AudioTranscriptionDetailedResult(segments, tokenAlignedWords);
        }

        private static async Task<IReadOnlyList<AudioTranscriptionDetailedSegment>> TranscribeDetailedSegmentsAsync(string modelPath, string audioPath, WhisperPassMode passMode, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using var whisperFactory = CreateWhisperFactory(modelPath);
            using var processor = CreateProcessor(whisperFactory, passMode, progress);
            await using var audioStream = File.OpenRead(audioPath);
            var segments = new List<AudioTranscriptionDetailedSegment>();
            var segmentIndex = 0;

            await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
            {
                try
                {
                    var text = segment.Text ?? string.Empty;
                    var tokens = (segment.Tokens ?? Array.Empty<WhisperToken>())
                        .Select((token, tokenIndex) => ToToken(segmentIndex, tokenIndex, token))
                        .ToArray();

                    segments.Add(new AudioTranscriptionDetailedSegment(
                        segmentIndex,
                        segment.Start,
                        segment.End,
                        text,
                        segment.Probability,
                        segment.MinProbability,
                        segment.MaxProbability,
                        segment.NoSpeechProbability,
                        segment.Language ?? string.Empty,
                        tokens));
                    segmentIndex++;
                }
                finally
                {
                    processor.Return(segment);
                }
            }

            return segments;
        }

        private static AudioTranscriptionToken ToToken(int segmentIndex, int tokenIndex, WhisperToken token)
        {
            var text = token.Text ?? string.Empty;
            return new AudioTranscriptionToken(
                segmentIndex,
                tokenIndex,
                token.Id,
                token.TimestampId,
                text,
                ConvertTokenTime(token.Start),
                ConvertTokenTime(token.End),
                token.DtwTimestamp > 0 ? ConvertTokenTime(token.DtwTimestamp) : null,
                token.Probability,
                token.ProbabilityLog,
                token.TimestampProbability,
                token.TimestampProbabilitySum,
                token.VoiceLen,
                IsSpecialTokenText(text));
        }

        private static WhisperFactory CreateWhisperFactory(string modelPath)
        {
            return WhisperFactory.FromPath(modelPath);
        }

        private static WhisperProcessor CreateProcessor(WhisperFactory whisperFactory, WhisperPassMode passMode, IProgress<double>? progress)
        {
            var builder = whisperFactory.CreateBuilder()
                .WithLanguageDetection()
                .WithProgressHandler(value => progress?.Report(Math.Clamp(value / 100d, 0d, 1d)));

            if (passMode == WhisperPassMode.RawTokens)
            {
                builder.WithTokenTimestamps()
                    .WithTokenTimestampsThreshold(DefaultTokenTimestampThreshold);
            }

            return builder.Build();
        }

        internal static (TimeSpan Start, TimeSpan End) GetTokenTiming(AudioTranscriptionToken token)
        {
            if (token.DtwTimestamp.HasValue)
            {
                return (token.DtwTimestamp.Value, token.DtwTimestamp.Value + (token.End - token.Start));
            }

            return (token.Start, token.End);
        }

        internal static IReadOnlyList<WorkingAlignedWord> AlignWordsFromTokens(IReadOnlyList<AudioTranscriptionDetailedSegment> segments)
        {
            var words = new List<WorkingAlignedWord>();
            var sequence = 0;

            foreach (var segment in segments)
            {
                WordBuilder? current = null;
                var pendingPrefixTokens = new List<AudioTranscriptionToken>();

                void FlushCurrent()
                {
                    if (current is null)
                    {
                        return;
                    }

                    var text = NormalizeTokenFragment(current.Text.ToString());
                    if (text.Length > 0)
                    {
                        words.Add(new WorkingAlignedWord(sequence++, segment.Index, current.Start, current.End, text));
                    }

                    current = null;
                }

                foreach (var token in segment.Tokens.OrderBy(token => token.TokenIndex))
                {
                    if (token.IsSpecial)
                    {
                        continue;
                    }

                    var rawText = token.Text ?? string.Empty;
                    if (!ContainsVisibleCharacters(rawText))
                    {
                        FlushCurrent();
                        pendingPrefixTokens.Clear();
                        continue;
                    }

                    var fragment = NormalizeTokenFragment(rawText);
                    if (fragment.Length == 0)
                    {
                        continue;
                    }

                    var (tokenStart, tokenEnd) = GetTokenTiming(token);

                    if (IsPunctuationOnlyToken(fragment))
                    {
                        if (current is not null)
                        {
                            current.Append(fragment, tokenStart, tokenEnd);
                        }
                        else
                        {
                            pendingPrefixTokens.Add(token);
                        }

                        continue;
                    }

                    if (current is not null && StartsWithWhitespace(rawText))
                    {
                        FlushCurrent();
                    }

                    if (current is null)
                    {
                        current = new WordBuilder();
                        foreach (var prefixToken in pendingPrefixTokens)
                        {
                            var prefixText = NormalizeTokenFragment(prefixToken.Text);
                            if (prefixText.Length > 0)
                            {
                                var (prefixStart, prefixEnd) = GetTokenTiming(prefixToken);
                                current.Append(prefixText, prefixStart, prefixEnd);
                            }
                        }

                        pendingPrefixTokens.Clear();
                    }

                    current.Append(fragment, tokenStart, tokenEnd);
                }

                FlushCurrent();
            }

            return words;
        }

        private static IReadOnlyList<WorkingAlignedWord> AlignFallbackWordsToSegments(IReadOnlyList<AudioTranscriptionDetailedSegment> segments, IReadOnlyList<AudioTranscriptionWord> words)
        {
            var aligned = new List<WorkingAlignedWord>();
            var sequence = 0;

            foreach (var word in words)
            {
                var text = NormalizeTokenFragment(word.Text);
                if (text.Length == 0)
                {
                    continue;
                }

                var segmentIndex = FindSegmentIndexForWord(segments, word);
                aligned.Add(new WorkingAlignedWord(sequence++, segmentIndex, word.Start, word.End, text));
            }

            return aligned;
        }

        private static int FindSegmentIndexForWord(IReadOnlyList<AudioTranscriptionDetailedSegment> segments, AudioTranscriptionWord word)
        {
            if (segments.Count == 0)
            {
                return 0;
            }

            var midpointTicks = word.Start.Ticks + Math.Max(0L, word.End.Ticks - word.Start.Ticks) / 2L;
            var midpoint = new TimeSpan(midpointTicks);
            var bestIndex = 0;
            var bestDistance = long.MaxValue;

            foreach (var segment in segments)
            {
                if (midpoint >= segment.Start && midpoint <= segment.End)
                {
                    return segment.Index;
                }

                var distance = midpoint < segment.Start
                    ? segment.Start.Ticks - midpoint.Ticks
                    : midpoint.Ticks - segment.End.Ticks;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = segment.Index;
                }
            }

            return bestIndex;
        }

        private static IReadOnlyList<WorkingAlignedWord> BuildSegmentFallbackWords(IReadOnlyList<AudioTranscriptionDetailedSegment> segments)
        {
            var words = new List<WorkingAlignedWord>();
            var sequence = 0;

            foreach (var segment in segments)
            {
                foreach (var word in BuildFallbackWords(new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text)))
                {
                    var text = NormalizeTokenFragment(word.Text);
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    words.Add(new WorkingAlignedWord(sequence++, segment.Index, word.Start, word.End, text));
                }
            }

            return words;
        }

        internal static IReadOnlyList<AudioTranscriptionAlignedWord> CleanupAlignedWords(
            IReadOnlyList<AudioTranscriptionDetailedSegment> segments,
            IReadOnlyList<WorkingAlignedWord> candidateWords,
            AudioTranscriptionTimingSource timingSource)
        {
            var wordsBySegment = candidateWords
                .GroupBy(word => word.SegmentIndex)
                .ToDictionary(group => group.Key, group => group.OrderBy(word => word.Sequence).ToList());
            var output = new List<AudioTranscriptionAlignedWord>();
            var wordIndex = 0;

            foreach (var segment in segments)
            {
                if (!wordsBySegment.TryGetValue(segment.Index, out var segmentWords) || segmentWords.Count == 0)
                {
                    continue;
                }

                RepairSegmentWordTimings(segment, segmentWords);

                foreach (var word in segmentWords)
                {
                    var text = NormalizeTokenFragment(word.Text);
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    output.Add(new AudioTranscriptionAlignedWord(
                        segment.Index,
                        wordIndex++,
                        word.Start,
                        word.End,
                        text,
                        timingSource));
                }
            }

            return output;
        }

        private static void RepairSegmentWordTimings(AudioTranscriptionDetailedSegment segment, List<WorkingAlignedWord> words)
        {
            words.RemoveAll(word => NormalizeTokenFragment(word.Text).Length == 0);
            if (words.Count == 0)
            {
                return;
            }

            foreach (var word in words)
            {
                word.Start = Max(segment.Start, word.Start);
                word.End = Min(segment.End, word.End);
            }

            if (NeedsRedistribution(segment, words))
            {
                RedistributeSegmentWordTimingsWithConfidence(segment, words, segment.Tokens);
                return;
            }

            var cursor = segment.Start;
            for (var index = 0; index < words.Count; index++)
            {
                var remainingWords = words.Count - index - 1;
                var latestEnd = segment.End - TimeSpan.FromTicks(MinimumDurationTicks * remainingWords);
                var start = Max(words[index].Start, cursor);
                if (start > latestEnd - TimeSpan.FromTicks(MinimumDurationTicks))
                {
                    start = latestEnd - TimeSpan.FromTicks(MinimumDurationTicks);
                }

                if (start < segment.Start)
                {
                    start = segment.Start;
                }

                var end = Max(words[index].End, start + TimeSpan.FromTicks(MinimumDurationTicks));
                if (end > latestEnd)
                {
                    end = latestEnd;
                }

                if (end <= start)
                {
                    end = Min(segment.End, start + TimeSpan.FromTicks(MinimumDurationTicks));
                    if (end <= start)
                    {
                        RedistributeSegmentWordTimingsWithConfidence(segment, words, segment.Tokens);
                        return;
                    }
                }

                words[index].Start = start;
                words[index].End = end;
                cursor = end;
            }

            if (words[^1].End < words[^1].Start)
            {
                RedistributeSegmentWordTimingsWithConfidence(segment, words, segment.Tokens);
            }
        }

        private static bool NeedsRedistribution(AudioTranscriptionDetailedSegment segment, IReadOnlyList<WorkingAlignedWord> words)
        {
            var cursor = segment.Start;
            foreach (var word in words)
            {
                if (word.End <= word.Start)
                {
                    return true;
                }

                if (word.Start < cursor)
                {
                    return true;
                }

                if (word.Start < segment.Start || word.End > segment.End)
                {
                    return true;
                }

                cursor = word.End;
            }

            return false;
        }

        private static void RedistributeSegmentWordTimings(AudioTranscriptionDetailedSegment segment, List<WorkingAlignedWord> words)
        {
            var segmentDurationTicks = Math.Max(
                MinimumDurationTicks * words.Count,
                (segment.End > segment.Start ? segment.End - segment.Start : TimeSpan.FromMilliseconds(1)).Ticks);
            var totalWeight = words.Sum(word => Math.Max(1, CountTextWeight(word.Text)));
            var cursor = segment.Start;

            for (var index = 0; index < words.Count; index++)
            {
                var remainingWords = words.Count - index;
                var remainingTicks = segment.End.Ticks - cursor.Ticks;
                if (remainingTicks < MinimumDurationTicks * remainingWords)
                {
                    remainingTicks = MinimumDurationTicks * remainingWords;
                }

                var weight = Math.Max(1, CountTextWeight(words[index].Text));
                var allocatedTicks = index == words.Count - 1
                    ? Math.Max(MinimumDurationTicks, remainingTicks)
                    : Math.Max(
                        MinimumDurationTicks,
                        (long)Math.Round(segmentDurationTicks * (double)weight / Math.Max(1, totalWeight)));
                var maxTicksForCurrent = Math.Max(MinimumDurationTicks, remainingTicks - (remainingWords - 1) * MinimumDurationTicks);
                allocatedTicks = Math.Min(allocatedTicks, maxTicksForCurrent);

                var end = index == words.Count - 1
                    ? segment.End
                    : cursor + TimeSpan.FromTicks(allocatedTicks);
                if (end <= cursor)
                {
                    end = cursor + TimeSpan.FromTicks(MinimumDurationTicks);
                }

                words[index].Start = cursor;
                words[index].End = Min(segment.End, end);
                cursor = words[index].End;
            }

            if (words.Count > 0)
            {
                words[^1].End = segment.End > words[^1].Start
                    ? segment.End
                    : words[^1].Start + TimeSpan.FromTicks(MinimumDurationTicks);
            }
        }

        private static void RedistributeSegmentWordTimingsWithConfidence(AudioTranscriptionDetailedSegment segment, List<WorkingAlignedWord> words, IReadOnlyList<AudioTranscriptionToken> tokens)
        {
            var segmentDurationTicks = Math.Max(
                MinimumDurationTicks * words.Count,
                (segment.End > segment.Start ? segment.End - segment.Start : TimeSpan.FromMilliseconds(1)).Ticks);

            var tokensByTimeRange = tokens
                .Where(t => !t.IsSpecial && t.Start < t.End)
                .ToList();

            var wordWeights = new List<double>();
            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var wordStart = word.Start;
                var wordEnd = word.End;

                var overlappingTokens = tokensByTimeRange
                    .Where(t => t.End > wordStart && t.Start < wordEnd)
                    .ToList();

                if (overlappingTokens.Count > 0)
                {
                    var avgProbability = overlappingTokens.Average(t => t.Probability);
                    var avgTimestampProb = overlappingTokens.Average(t => t.TimestampProbability);
                    var confidence = Math.Max(0.1d, avgProbability * 0.7d + avgTimestampProb * 0.3d);
                    wordWeights.Add(confidence);
                }
                else
                {
                    wordWeights.Add(1d);
                }
            }

            var totalWeight = wordWeights.Sum();
            var cursor = segment.Start;

            for (var index = 0; index < words.Count; index++)
            {
                var remainingWords = words.Count - index;
                var remainingTicks = segment.End.Ticks - cursor.Ticks;
                if (remainingTicks < MinimumDurationTicks * remainingWords)
                {
                    remainingTicks = MinimumDurationTicks * remainingWords;
                }

                var weight = wordWeights[index];
                var allocatedTicks = index == words.Count - 1
                    ? Math.Max(MinimumDurationTicks, remainingTicks)
                    : Math.Max(
                        MinimumDurationTicks,
                        (long)Math.Round(segmentDurationTicks * weight / Math.Max(0.1d, totalWeight)));
                var maxTicksForCurrent = Math.Max(MinimumDurationTicks, remainingTicks - (remainingWords - 1) * MinimumDurationTicks);
                allocatedTicks = Math.Min(allocatedTicks, maxTicksForCurrent);

                var end = index == words.Count - 1
                    ? segment.End
                    : cursor + TimeSpan.FromTicks(allocatedTicks);
                if (end <= cursor)
                {
                    end = cursor + TimeSpan.FromTicks(MinimumDurationTicks);
                }

                words[index].Start = cursor;
                words[index].End = Min(segment.End, end);
                cursor = words[index].End;
            }

            if (words.Count > 0)
            {
                words[^1].End = segment.End > words[^1].Start
                    ? segment.End
                    : words[^1].Start + TimeSpan.FromTicks(MinimumDurationTicks);
            }
        }

        private static bool HasUsableWordsForSegments(IReadOnlyList<AudioTranscriptionDetailedSegment> segments, IReadOnlyList<AudioTranscriptionAlignedWord> words)
        {
            var wordCountsBySegment = words
                .GroupBy(word => word.SegmentIndex)
                .ToDictionary(group => group.Key, group => group.Count());

            foreach (var segment in segments)
            {
                if (NormalizeTokenFragment(segment.Text).Length == 0)
                {
                    continue;
                }

                if (!wordCountsBySegment.TryGetValue(segment.Index, out var count) || count == 0)
                {
                    return false;
                }
            }

            return words.Count > 0 || segments.All(segment => NormalizeTokenFragment(segment.Text).Length == 0);
        }

        private static bool IsSpecialTokenText(string text)
        {
            return text.Contains("<|", StringComparison.Ordinal);
        }

        private static TimeSpan ConvertTokenTime(long value)
        {
            if (value <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromMilliseconds(value * WhisperTimestampUnitMilliseconds);
        }

        private static bool ContainsVisibleCharacters(string value)
        {
            return value.Any(character => !char.IsWhiteSpace(character));
        }

        private static bool StartsWithWhitespace(string value)
        {
            return value.Length > 0 && char.IsWhiteSpace(value[0]);
        }

        private static string NormalizeTokenFragment(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var buffer = new char[text.Length];
            var index = 0;
            var previousWhitespace = false;
            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (previousWhitespace)
                    {
                        continue;
                    }

                    buffer[index++] = ' ';
                    previousWhitespace = true;
                }
                else
                {
                    buffer[index++] = character;
                    previousWhitespace = false;
                }
            }

            return new string(buffer, 0, index).Trim();
        }

        private static bool IsPunctuationOnlyToken(string text)
        {
            var visible = false;
            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                visible = true;
                if (char.IsLetterOrDigit(character))
                {
                    return false;
                }
            }

            return visible;
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

        private static TimeSpan Min(TimeSpan left, TimeSpan right)
        {
            return left <= right ? left : right;
        }

        private static TimeSpan Max(TimeSpan left, TimeSpan right)
        {
            return left >= right ? left : right;
        }

        private static IEnumerable<AudioTranscriptionWord> BuildFallbackWords(AudioTranscriptionSegment segment)
        {
            var trimmedText = NormalizeTokenFragment(segment.Text);
            if (trimmedText.Length == 0)
            {
                yield break;
            }

            var words = trimmedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                yield break;
            }

            var totalTicks = Math.Max(2L, (segment.End > segment.Start ? segment.End - segment.Start : TimeSpan.FromMilliseconds(1)).Ticks);
            var totalWeight = words.Sum(word => Math.Max(1, word.Length));
            var cursor = segment.Start;

            for (var index = 0; index < words.Length; index++)
            {
                var remainingTicks = segment.End.Ticks - cursor.Ticks;
                var remainingWords = words.Length - index;
                var weight = Math.Max(1, words[index].Length);
                var allocatedTicks = remainingWords == 1
                    ? Math.Max(1L, remainingTicks)
                    : Math.Max(1L, (long)Math.Round(totalTicks * (double)weight / totalWeight));

                if (allocatedTicks >= remainingTicks)
                {
                    allocatedTicks = Math.Max(1L, remainingTicks - (remainingWords - 1));
                }

                var end = index == words.Length - 1
                    ? segment.End
                    : cursor + TimeSpan.FromTicks(allocatedTicks);

                if (end <= cursor)
                {
                    end = cursor + TimeSpan.FromMilliseconds(1);
                }

                yield return new AudioTranscriptionWord(cursor, end, words[index]);
                cursor = end;
            }
        }

        private enum WhisperPassMode
        {
            Segments,
            RawTokens,
            Words
        }

        internal sealed class WorkingAlignedWord
        {
            public WorkingAlignedWord(int sequence, int segmentIndex, TimeSpan start, TimeSpan end, string text)
            {
                Sequence = sequence;
                SegmentIndex = segmentIndex;
                Start = start;
                End = end;
                Text = text ?? string.Empty;
            }

            public int Sequence { get; }

            public int SegmentIndex { get; }

            public TimeSpan Start { get; set; }

            public TimeSpan End { get; set; }

            public string Text { get; set; }
        }

        private sealed class WordBuilder
        {
            public StringBuilder Text { get; } = new();

            public TimeSpan Start { get; private set; }

            public TimeSpan End { get; private set; }

            public bool HasTiming { get; private set; }

            public void Append(string text, TimeSpan start, TimeSpan end)
            {
                Text.Append(text);
                if (!HasTiming)
                {
                    Start = start;
                    End = end;
                    HasTiming = true;
                    return;
                }

                if (start < Start)
                {
                    Start = start;
                }

                if (end > End)
                {
                    End = end;
                }
            }
        }
    }

    private sealed class MediaPreparationService : IMediaPreparationService
    {
        private readonly IAudioProcessingService _audioProcessingService;
        private readonly IVideoProcessingService _videoProcessingService;

        public MediaPreparationService(IAudioProcessingService audioProcessingService, IVideoProcessingService videoProcessingService)
        {
            _audioProcessingService = audioProcessingService ?? throw new ArgumentNullException(nameof(audioProcessingService));
            _videoProcessingService = videoProcessingService ?? throw new ArgumentNullException(nameof(videoProcessingService));
        }

        public async Task<PreparedAudio> PrepareAsync(string inputPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), "files-tools-whisper", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            try
            {
                var preparedAudioPath = Path.Combine(workingDirectory, "prepared.wav");
                progress?.Report(0d);

                if (IsVideoPath(inputPath))
                {
                    var extractedAudioPath = Path.Combine(workingDirectory, "extracted.wav");
                    var denoisedAudioPath = Path.Combine(workingDirectory, "denoised.wav");
                    await _videoProcessingService.ExtractAudioAsync(inputPath, extractedAudioPath, cancellationToken).ConfigureAwait(false);
                    progress?.Report(0.2d);
                    await DenoiseVideoAudioForTranscriptionAsync(extractedAudioPath, denoisedAudioPath, progress, 0.2d, 0.55d, cancellationToken).ConfigureAwait(false);
                    await ConvertToWhisperWaveAsync(denoisedAudioPath, preparedAudioPath, progress, 0.75d, 0.25d, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await ConvertToWhisperWaveAsync(inputPath, preparedAudioPath, progress, 0d, 1d, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(1d);
                return new PreparedAudio(preparedAudioPath, workingDirectory);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(workingDirectory))
                    {
                        Directory.Delete(workingDirectory, recursive: true);
                    }
                }
                catch
                {
                    // Best effort cleanup only.
                }

                throw;
            }
        }

        private async Task ConvertToWhisperWaveAsync(string inputPath, string outputPath, IProgress<double>? progress, double progressOffset, double progressScale, CancellationToken cancellationToken)
        {
            var convertProgress = progress is null
                ? null
                : new CallbackProgress<AudioProcessProgress>(update =>
                {
                    var value = Math.Clamp(progressOffset + (update.OverallPercent * progressScale), 0d, 1d);
                    progress.Report(value);
                });

            await _audioProcessingService.ConvertAsync(inputPath, outputPath, new AudioConversionOptions
            {
                OutputFormat = "wav",
                SampleRate = 16000,
                Channels = 1
            }, convertProgress, cancellationToken).ConfigureAwait(false);
        }

        private async Task DenoiseVideoAudioForTranscriptionAsync(string inputPath, string outputPath, IProgress<double>? progress, double progressOffset, double progressScale, CancellationToken cancellationToken)
        {
            var denoiseProgress = progress is null
                ? null
                : new CallbackProgress<AudioProcessProgress>(update =>
                {
                    var value = Math.Clamp(progressOffset + (update.OverallPercent * progressScale), 0d, 1d);
                    progress.Report(value);
                });

            await _audioProcessingService.ProcessPodcastAudioAsync(inputPath, outputPath, new AudioPodcastProcessingOptions
            {
                EnableDtlnDenoise = true,
                DtlnDenoiseMode = AudioDenoiseMode.Mono,
                DtlnDenoiseAmount = 100,
                DtlnDenoisePasses = 1,
                EnableCompressor = false,
                EnableDeEsser = false,
                HighPassFrequencyHz = 60,
                TargetLufs = -16,
                LimiterLimit = 0.98,
                OutputCodec = "pcm_s16le",
                BitrateKbps = null,
                SampleRate = 16000,
                Channels = 1,
                PreserveMetadata = false
            }, denoiseProgress, cancellationToken).ConfigureAwait(false);
        }

        private static bool IsVideoPath(string inputPath)
        {
            var extension = Path.GetExtension(inputPath);
            return SupportedVideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
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
            AudioTranscriptionStage.PreparingAudio => stagePercent * 0.2d,
            AudioTranscriptionStage.Transcribing => 0.2d + (stagePercent * 0.75d),
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
}
