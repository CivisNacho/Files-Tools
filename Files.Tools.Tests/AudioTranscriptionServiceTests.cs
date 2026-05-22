using Files_Tools.Services;

namespace Files.Tools.Tests;

[TestClass]
public class AudioTranscriptionServiceTests
{
    private string _tempRoot = null!;
    private string _modelPath = null!;
    private FakeWhisperModelInstaller _installer = null!;
    private FakeWhisperTranscriber _transcriber = null!;
    private FakeMediaPreparationService _mediaPreparationService = null!;
    private AudioTranscriptionService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-transcription-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _modelPath = Path.Combine(_tempRoot, "models", "ggml-base.bin");
        _installer = new FakeWhisperModelInstaller();
        _transcriber = new FakeWhisperTranscriber();
        _mediaPreparationService = new FakeMediaPreparationService();
        _service = new AudioTranscriptionService(_modelPath, _installer, _transcriber, _mediaPreparationService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    [TestMethod]
    public void IsInstalled_ReturnsFalse_WhenModelFileIsMissing()
    {
        Assert.IsFalse(_service.IsInstalled());
    }

    [TestMethod]
    public void IsInstalled_ReturnsTrue_WhenModelFileExists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
        File.WriteAllText(_modelPath, "model");

        Assert.IsTrue(_service.IsInstalled());
    }

    [TestMethod]
    public async Task InstallAsync_WritesModel_AndSkipsSecondInstall()
    {
        await _service.InstallAsync();
        await _service.InstallAsync();

        Assert.IsTrue(File.Exists(_modelPath));
        Assert.AreEqual(1, _installer.CallCount);
    }

    [TestMethod]
    public async Task InstallAsync_WithProgress_ReportsCompletion()
    {
        var progress = new CollectingProgress<AudioTranscriptionInstallProgress>();

        await _service.InstallAsync(progress);

        Assert.IsTrue(File.Exists(_modelPath));
        Assert.IsNotEmpty(progress.Items);
        Assert.AreEqual(1d, progress.Items[^1].FractionComplete, 0.0001d);
        StringAssert.Contains(progress.Items[^1].Stage, "downloaded", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task TranscribeToTextAsync_RejectsMissingInput()
    {
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(async () =>
            await _service.TranscribeToTextAsync(Path.Combine(_tempRoot, "missing.wav")));
    }

    [TestMethod]
    public async Task TranscribeToTextAsync_ThrowsWhenWhisperIsNotInstalled()
    {
        var input = CreateInputFile("input.wav");

        await Assert.ThrowsExactlyAsync<AudioTranscriptionNotInstalledException>(async () =>
            await _service.TranscribeToTextAsync(input));
    }

    [TestMethod]
    public async Task TranscribeToTextAsync_ReturnsCombinedTranscript()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "world")
        ];

        var text = await _service.TranscribeToTextAsync(input);

        Assert.AreEqual("Hello world", text);
        Assert.AreEqual(input, _mediaPreparationService.LastInputPath);
    }

    [TestMethod]
    public async Task TranscribeToTextAsync_WithProgress_ReportsStagesAndCompletion()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello")
        ];

        var progress = new CollectingProgress<AudioTranscriptionProgress>();

        var text = await _service.TranscribeToTextAsync(input, progress);

        Assert.AreEqual("Hello", text);
        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.PreparingAudio));
        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.Transcribing));
        Assert.AreEqual(AudioTranscriptionStage.Completed, progress.Items[^1].Stage);
        Assert.AreEqual(1d, progress.Items[^1].OverallPercent, 0.0001d);
        Assert.AreEqual(TimeSpan.Zero, progress.Items[^1].EstimatedRemainingTime);
    }

    [TestMethod]
    public async Task TranscribeToTimestampedTextAsync_ReturnsTimestampedTranscript()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), "Hello"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "world")
        ];

        var text = await _service.TranscribeToTimestampedTextAsync(input);

        StringAssert.Contains(text, "[00:00:00.250] Hello");
        StringAssert.Contains(text, "[00:00:02.000] world");
    }

    [TestMethod]
    public async Task TranscribeToSegmentsAsync_RejectsMissingInput()
    {
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(async () =>
            await _service.TranscribeToSegmentsAsync(Path.Combine(_tempRoot, "missing.wav")));
    }

    [TestMethod]
    public async Task TranscribeToSegmentsAsync_ThrowsWhenWhisperIsNotInstalled()
    {
        var input = CreateInputFile("input.wav");

        await Assert.ThrowsExactlyAsync<AudioTranscriptionNotInstalledException>(async () =>
            await _service.TranscribeToSegmentsAsync(input));
    }

    [TestMethod]
    public async Task TranscribeToSegmentsAsync_ReturnsTimestampedSegments()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromMilliseconds(950), "First line"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Second line")
        ];

        var segments = await _service.TranscribeToSegmentsAsync(input);

        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual(TimeSpan.Zero, segments[0].Start);
        Assert.AreEqual(TimeSpan.FromMilliseconds(950), segments[0].End);
        Assert.AreEqual("First line", segments[0].Text);
        Assert.AreEqual(TimeSpan.FromSeconds(2), segments[1].Start);
    }

    [TestMethod]
    public async Task TranscribeToSegmentsAsync_WithProgress_ReportsTranscriptionStages()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromMilliseconds(500), "First line")
        ];

        var progress = new CollectingProgress<AudioTranscriptionProgress>();

        await _service.TranscribeToSegmentsAsync(input, progress);

        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.PreparingAudio));
        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.Transcribing));
        Assert.IsFalse(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.WritingSubtitles));
    }

    [TestMethod]
    public async Task TranscribeToWordsAsync_ReturnsTimestampedWords()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.DetailedResult = new AudioTranscriptionDetailedResult(
        [
            new AudioTranscriptionDetailedSegment(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello world", 0.9f, 0.8f, 1f, 0.1f, "en", [])
        ],
        [
            new AudioTranscriptionAlignedWord(0, 0, TimeSpan.Zero, TimeSpan.FromMilliseconds(450), "Hello", AudioTranscriptionTimingSource.RawTokenAlignment),
            new AudioTranscriptionAlignedWord(0, 1, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), "world", AudioTranscriptionTimingSource.RawTokenAlignment)
        ]);

        var words = await _service.TranscribeToWordsAsync(input);

        Assert.AreEqual(2, words.Count);
        Assert.AreEqual("Hello", words[0].Text);
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), words[1].Start);
    }

    [TestMethod]
    public async Task TranscribeToWordsAsync_WithProgress_ReportsTranscriptionStages()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.DetailedResult = new AudioTranscriptionDetailedResult(
        [
            new AudioTranscriptionDetailedSegment(0, TimeSpan.Zero, TimeSpan.FromMilliseconds(500), "First", 0.9f, 0.8f, 1f, 0.1f, "en", [])
        ],
        [
            new AudioTranscriptionAlignedWord(0, 0, TimeSpan.Zero, TimeSpan.FromMilliseconds(500), "First", AudioTranscriptionTimingSource.WhisperWordTiming)
        ]);

        var progress = new CollectingProgress<AudioTranscriptionProgress>();

        await _service.TranscribeToWordsAsync(input, progress);

        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.PreparingAudio));
        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.Transcribing));
    }

    [TestMethod]
    public async Task TranscribeToDetailedResultAsync_ReturnsDetailedSegmentsTokensAndWords()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.DetailedResult = new AudioTranscriptionDetailedResult(
        [
            new AudioTranscriptionDetailedSegment(
                0,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                "Hello, world",
                0.93f,
                0.75f,
                0.99f,
                0.05f,
                "en",
                [
                    new AudioTranscriptionToken(0, 0, 11, 0, "Hello", TimeSpan.Zero, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(180), 0.9f, -0.1f, 0.8f, 0.81f, 1f, false),
                    new AudioTranscriptionToken(0, 1, 12, 0, ",", TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(450), null, 0.7f, -0.2f, 0.7f, 0.71f, 1f, false),
                    new AudioTranscriptionToken(0, 2, 13, 0, " world", TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(730), 0.92f, -0.08f, 0.83f, 0.84f, 1f, false)
                ])
        ],
        [
            new AudioTranscriptionAlignedWord(0, 0, TimeSpan.Zero, TimeSpan.FromMilliseconds(450), "Hello,", AudioTranscriptionTimingSource.RawTokenAlignment),
            new AudioTranscriptionAlignedWord(0, 1, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), "world", AudioTranscriptionTimingSource.RawTokenAlignment)
        ]);

        var result = await _service.TranscribeToDetailedResultAsync(input);

        Assert.AreEqual(1, result.Segments.Count);
        Assert.AreEqual(3, result.Segments[0].Tokens.Count);
        Assert.AreEqual("Hello,", result.Words[0].Text);
        Assert.AreEqual(AudioTranscriptionTimingSource.RawTokenAlignment, result.Words[0].TimingSource);
    }

    private string CreateInputFile(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]);
        return path;
    }

    private void CreateInstalledModel()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
        File.WriteAllText(_modelPath, "model");
    }

    private sealed class FakeWhisperModelInstaller : AudioTranscriptionService.IWhisperModelInstaller
    {
        public int CallCount { get; private set; }

        public Task InstallBaseModelAsync(string modelPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            CallCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            File.WriteAllText(modelPath, "model");
            progress?.Report(0.3d);
            progress?.Report(1d);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWhisperTranscriber : AudioTranscriptionService.IWhisperTranscriber
    {
        public IReadOnlyList<AudioTranscriptionSegment> Segments { get; set; } = [];
        public AudioTranscriptionDetailedResult? DetailedResult { get; set; }

        public Task<AudioTranscriptionService.AudioTranscriptionResult> TranscribeAsync(string modelPath, string audioPath, AudioTranscriptionService.TranscriptionGranularity granularity, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(0.25d);
            progress?.Report(1d);
            return Task.FromResult(granularity == AudioTranscriptionService.TranscriptionGranularity.Detailed
                ? new AudioTranscriptionService.AudioTranscriptionResult(Segments, DetailedResult ?? BuildDetailedResult())
                : new AudioTranscriptionService.AudioTranscriptionResult(Segments, detailedResult: null));
        }

        private AudioTranscriptionDetailedResult BuildDetailedResult()
        {
            var detailedSegments = Segments
                .Select((segment, index) => new AudioTranscriptionDetailedSegment(index, segment.Start, segment.End, segment.Text, 0.9f, 0.8f, 1f, 0.1f, "en", []))
                .ToArray();
            var detailedWords = Segments
                .SelectMany((segment, segmentIndex) =>
                    BuildWordsFromSegment(segment)
                        .Select((word, wordIndex) => new AudioTranscriptionAlignedWord(segmentIndex, wordIndex, word.Start, word.End, word.Text, AudioTranscriptionTimingSource.SegmentFallback)))
                .ToArray();
            return new AudioTranscriptionDetailedResult(detailedSegments, detailedWords);
        }

        private static IReadOnlyList<AudioTranscriptionWord> BuildWordsFromSegment(AudioTranscriptionSegment segment)
        {
            var text = segment.Text.Trim();
            if (text.Length == 0)
            {
                return [];
            }

            return [new AudioTranscriptionWord(segment.Start, segment.End, text)];
        }
    }

    private sealed class FakeMediaPreparationService : AudioTranscriptionService.IMediaPreparationService
    {
        public string? LastInputPath { get; private set; }

        public Task<AudioTranscriptionService.PreparedAudio> PrepareAsync(string inputPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            LastInputPath = inputPath;

            var workingDirectory = Path.Combine(Path.GetTempPath(), "files-tools-transcription-prepared", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
            var preparedPath = Path.Combine(workingDirectory, "prepared.wav");
            File.WriteAllBytes(preparedPath, [0x52, 0x49, 0x46, 0x46]);
            progress?.Report(0.5d);
            progress?.Report(1d);

            return Task.FromResult(new AudioTranscriptionService.PreparedAudio(preparedPath, workingDirectory));
        }
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value)
        {
            Items.Add(value);
        }
    }
}
