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
    /// Returns whether the Whisper model is installed locally.
    /// </summary>
    bool IsInstalled();

    /// <summary>
    /// Installs the Whisper model when missing.
    /// </summary>
    Task InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs the Whisper model when missing and reports progress.
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
    /// Transcribes supported audio or video input into words whose timings are derived from segment envelopes.
    /// </summary>
    Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes supported audio or video input into words whose timings are derived from segment envelopes and reports progress.
    /// </summary>
    Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default);

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
public sealed record AudioTranscriptionSegment(TimeSpan Start, TimeSpan End, string Text)
{
    /// <summary>
    /// Real word-level timings produced from Whisper token timestamps, when available.
    /// Null when the transcriber did not produce usable word timing, in which case callers
    /// fall back to synthesizing word timing from the segment envelope.
    /// </summary>
    public IReadOnlyList<AudioTranscriptionWord>? Words { get; init; }
}

/// <summary>
/// Single word-level transcription unit with start and end timestamps.
/// </summary>
public sealed record AudioTranscriptionWord(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// High-level stages for transcription work.
/// </summary>
public enum AudioTranscriptionStage
{
    PreparingAudio,
    Transcribing,
    Aligning,
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
    private static readonly TimeSpan MinimumWordDuration = TimeSpan.FromMilliseconds(1);
    private static readonly string[] SupportedVideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v", ".gif"];

    private readonly string _modelPath;
    private readonly IWhisperModelInstaller _modelInstaller;
    private readonly IWhisperTranscriber _transcriber;
    private readonly IMediaPreparationService _mediaPreparationService;
    private readonly IWordAligner? _wordAligner;

    /// <summary>
    /// Creates the service with default local media preparation and Whisper adapters.
    /// </summary>
    public AudioTranscriptionService()
        : this(
            ResolveDefaultModelPath(),
            new WhisperModelInstaller(),
            new WhisperNetTranscriber(),
            new MediaPreparationService(new AudioProcessingService(), new VideoProcessingService()),
            new Wav2Vec2AlignmentService())
    {
    }

    internal AudioTranscriptionService(
        string modelPath,
        IWhisperModelInstaller modelInstaller,
        IWhisperTranscriber transcriber,
        IMediaPreparationService mediaPreparationService,
        IWordAligner? wordAligner = null)
    {
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _modelInstaller = modelInstaller ?? throw new ArgumentNullException(nameof(modelInstaller));
        _transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
        _mediaPreparationService = mediaPreparationService ?? throw new ArgumentNullException(nameof(mediaPreparationService));
        _wordAligner = wordAligner;
    }

    /// <inheritdoc />
    public bool IsInstalled()
    {
        // The transcription feature needs both the Whisper model and the forced-alignment model.
        return File.Exists(_modelPath) && (_wordAligner?.IsInstalled() ?? true);
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

        // Whisper (speech-to-text) downloads first and maps to the first 80% of overall progress;
        // the forced-alignment model fills the remaining 20%. The Whisper model is far larger, so
        // this split keeps the bar moving roughly in line with bytes downloaded.
        const double WhisperShare = 0.8d;

        if (!File.Exists(_modelPath))
        {
            var copyProgress = progress is null
                ? null
                : new ThrottledProgress<double>(value =>
                {
                    var fraction = Math.Clamp(value, 0d, 1d) * WhisperShare;
                    progress.Report(new AudioTranscriptionInstallProgress
                    {
                        Stage = "Downloading transcription model...",
                        FractionComplete = fraction
                    });
                }, throttleMilliseconds: 200);

            await _modelInstaller.InstallBaseModelAsync(_modelPath, copyProgress, cancellationToken).ConfigureAwait(false);
        }

        if (_wordAligner is not null && !_wordAligner.IsInstalled())
        {
            var alignerProgress = progress is null
                ? null
                : new ThrottledProgress<double>(value =>
                {
                    var fraction = WhisperShare + (Math.Clamp(value, 0d, 1d) * (1d - WhisperShare));
                    progress.Report(new AudioTranscriptionInstallProgress
                    {
                        Stage = "Downloading alignment model...",
                        FractionComplete = fraction
                    });
                }, throttleMilliseconds: 200);

            await _wordAligner.InstallAsync(alignerProgress, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new AudioTranscriptionInstallProgress
        {
            Stage = "Transcription feature downloaded.",
            FractionComplete = 1d
        });
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
        return await TranscribeSegmentsCoreAsync(inputPath, progress, progressState, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        return await TranscribeToWordsAsync(inputPath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        var segments = await TranscribeToSegmentsAsync(inputPath, progress, cancellationToken).ConfigureAwait(false);
        return BuildWordsFromSegments(segments);
    }

    /// <inheritdoc />
    public async Task<string> TranscribeToTextAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        return await TranscribeToTextAsync(inputPath, progress: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> TranscribeToTextAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        EnsureInstalled();

        var progressState = new ProgressState();
        var segments = await TranscribeSegmentsCoreAsync(inputPath, progress, progressState, cancellationToken).ConfigureAwait(false);
        var text = string.Join(" ", segments.Select(segment => segment.Text.Trim()).Where(text => text.Length > 0)).Trim();
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
        ValidateInputPath(inputPath);
        EnsureInstalled();

        var progressState = new ProgressState();
        var segments = await TranscribeSegmentsCoreAsync(inputPath, progress, progressState, cancellationToken).ConfigureAwait(false);
        var lines = segments
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

    private async Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeSegmentsCoreAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, ProgressState progressState, CancellationToken cancellationToken)
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
            var segments = await _transcriber.TranscribeAsync(_modelPath, preparedAudio.AudioPath, transcriptionProgress, cancellationToken).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[Transcription] Whisper produced {segments.Count} segments from '{System.IO.Path.GetFileName(inputPath)}'.");

            // Refine word timing with forced alignment. The Whisper model/session is fully released
            // by TranscribeAsync above before this runs, so only one acoustic model is resident at a
            // time. Alignment is best-effort: on any failure we keep Whisper's token timings.
            var alignerInstalled = _wordAligner is not null && _wordAligner.IsInstalled();
            System.Diagnostics.Debug.WriteLine($"[Transcription] Word aligner {(alignerInstalled ? "available -> refining word timings" : "unavailable -> using Whisper token timings")}.");
            if (alignerInstalled)
            {
                var alignmentProgress = progress is null
                    ? null
                    : new CallbackProgress<double>(value => Report(progress, progressState, AudioTranscriptionStage.Aligning, value, "Aligning word timings"));
                try
                {
                    segments = await Task.Run(
                        () => _wordAligner!.Align(preparedAudio.AudioPath, segments, alignmentProgress, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Keep the unaligned segments; karaoke falls back to Whisper/synthesized timing.
                    System.Diagnostics.Debug.WriteLine($"[Transcription] Alignment failed ({ex.GetType().Name}: {ex.Message}); falling back to Whisper token timings.");
                }
            }

            return segments;
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
            preparedAudio?.Dispose();
        }
    }

    private void EnsureInstalled()
    {
        if (!IsInstalled())
        {
            throw new AudioTranscriptionNotInstalledException("Whisper base model is not installed.");
        }
    }

    private static IReadOnlyList<AudioTranscriptionWord> BuildWordsFromSegments(IReadOnlyList<AudioTranscriptionSegment> segments)
    {
        var output = new List<AudioTranscriptionWord>();
        foreach (var segment in segments)
        {
            if (segment.Words is { Count: > 0 } realWords)
            {
                foreach (var word in realWords)
                {
                    var wordText = word.Text?.Trim();
                    if (!string.IsNullOrEmpty(wordText))
                    {
                        output.Add(string.Equals(wordText, word.Text, StringComparison.Ordinal) ? word : word with { Text = wordText });
                    }
                }

                continue;
            }

            var trimmed = segment.Text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            var start = segment.Start < TimeSpan.Zero ? TimeSpan.Zero : segment.Start;
            var end = segment.End > start ? segment.End : start + MinimumWordDuration;
            var totalTicks = Math.Max(MinimumWordDuration.Ticks * tokens.Length, (end - start).Ticks);
            var totalWeight = tokens.Sum(token => Math.Max(1, token.Length));
            long consumedTicks = 0;
            var consumedWeight = 0;

            for (var index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                var wordStart = start + TimeSpan.FromTicks(consumedTicks);
                consumedWeight += Math.Max(1, token.Length);

                long wordEndTicks;
                if (index == tokens.Length - 1)
                {
                    wordEndTicks = totalTicks;
                }
                else
                {
                    wordEndTicks = (long)Math.Round(totalTicks * (consumedWeight / (double)totalWeight));
                    wordEndTicks = Math.Clamp(wordEndTicks, consumedTicks + MinimumWordDuration.Ticks, totalTicks);
                }

                var wordEnd = start + TimeSpan.FromTicks(wordEndTicks);
                output.Add(new AudioTranscriptionWord(wordStart, wordEnd, token));
                consumedTicks = wordEndTicks;
            }
        }

        return output;
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
        Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeAsync(string modelPath, string audioPath, IProgress<double>? progress, CancellationToken cancellationToken);
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

    private sealed class WhisperNetTranscriber : IWhisperTranscriber
    {
        public async Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeAsync(string modelPath, string audioPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguageDetection()
                .WithTokenTimestamps()
                .WithProgressHandler(value => progress?.Report(Math.Clamp(value / 100d, 0d, 1d)))
                .Build();
            await using var audioStream = File.OpenRead(audioPath);
            var segments = new List<AudioTranscriptionSegment>();

            await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
            {
                try
                {
                    var words = BuildWordsFromTokens(segment);
                    segments.Add(new AudioTranscriptionSegment(segment.Start, segment.End, segment.Text ?? string.Empty)
                    {
                        Words = words
                    });
                }
                finally
                {
                    processor.Return(segment);
                }
            }

            return segments;
        }

        // Whisper token timestamps come in centiseconds (the same units the segment t0/t1
        // are scaled from). Reconstruct whole words by concatenating sub-word token pieces:
        // a piece that begins with whitespace starts a new word.
        private static IReadOnlyList<AudioTranscriptionWord>? BuildWordsFromTokens(SegmentData segment)
        {
            var tokens = segment.Tokens;
            if (tokens is null || tokens.Length == 0)
            {
                return null;
            }

            var words = new List<AudioTranscriptionWord>();
            var builder = new StringBuilder();
            var wordStart = TimeSpan.Zero;
            var wordEnd = TimeSpan.Zero;
            var hasContent = false;
            var sawTiming = false;

            void Flush()
            {
                if (!hasContent)
                {
                    return;
                }

                var text = builder.ToString().Trim();
                if (text.Length > 0)
                {
                    words.Add(new AudioTranscriptionWord(wordStart, wordEnd, text));
                }

                builder.Clear();
                hasContent = false;
            }

            foreach (var token in tokens)
            {
                var raw = token.Text;
                if (string.IsNullOrEmpty(raw) || raw.StartsWith("[_", StringComparison.Ordinal))
                {
                    // Skip empty pieces and special tokens such as [_BEG_] or [_TT_750].
                    continue;
                }

                if (char.IsWhiteSpace(raw[0]) && hasContent)
                {
                    Flush();
                }

                var pieceStart = TimeSpan.FromMilliseconds(Math.Max(0L, token.Start) * 10d);
                var pieceEnd = TimeSpan.FromMilliseconds(Math.Max(0L, token.End) * 10d);
                if (token.End > 0L)
                {
                    sawTiming = true;
                }

                if (!hasContent)
                {
                    wordStart = pieceStart;
                    hasContent = true;
                }

                wordEnd = pieceEnd > wordStart ? pieceEnd : wordStart;
                builder.Append(raw);
            }

            Flush();

            if (words.Count == 0 || !sawTiming)
            {
                // No usable timing (e.g. token timestamps unavailable): let the caller synthesize.
                return null;
            }

            return SanitizeWordTimings(words, segment.Start, segment.End);
        }

        private static IReadOnlyList<AudioTranscriptionWord> SanitizeWordTimings(List<AudioTranscriptionWord> words, TimeSpan segmentStart, TimeSpan segmentEnd)
        {
            var lower = segmentStart < TimeSpan.Zero ? TimeSpan.Zero : segmentStart;
            var upper = segmentEnd > lower ? segmentEnd : lower + MinimumWordDuration;
            var result = new List<AudioTranscriptionWord>(words.Count);
            var cursor = lower;

            foreach (var word in words)
            {
                var start = word.Start;
                if (start < cursor)
                {
                    start = cursor;
                }

                if (start > upper)
                {
                    start = upper;
                }

                var end = word.End;
                if (end <= start)
                {
                    end = start + MinimumWordDuration;
                }

                if (end > upper && upper > start)
                {
                    end = upper;
                }

                result.Add(new AudioTranscriptionWord(start, end, word.Text));
                cursor = end;
            }

            return result;
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
            AudioTranscriptionStage.PreparingAudio => stagePercent * 0.15d,
            AudioTranscriptionStage.Transcribing => 0.15d + (stagePercent * 0.6d),
            AudioTranscriptionStage.Aligning => 0.75d + (stagePercent * 0.2d),
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
