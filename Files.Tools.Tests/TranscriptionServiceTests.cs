using Files_Tools.Services;

namespace Files.Tools.Tests;

[TestClass]
public class TranscriptionServiceTests
{
    private string _tempRoot = null!;
    private string _modelPath = null!;
    private FakeWhisperModelInstaller _installer = null!;
    private FakeWhisperTranscriber _transcriber = null!;
    private FakeMediaPreparationService _mediaPreparationService = null!;
    private TranscriptionService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-transcription-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _modelPath = Path.Combine(_tempRoot, "models", "ggml-base.bin");
        _installer = new FakeWhisperModelInstaller();
        _transcriber = new FakeWhisperTranscriber();
        _mediaPreparationService = new FakeMediaPreparationService();
        _service = new TranscriptionService(_modelPath, _installer, _transcriber, _mediaPreparationService);
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
    public async Task TranscribeToWordsAsync_DerivesWordsFromSegments()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello world")
        ];

        var words = await _service.TranscribeToWordsAsync(input);

        Assert.AreEqual(2, words.Count);
        Assert.AreEqual("Hello", words[0].Text);
        Assert.AreEqual("world", words[1].Text);
        Assert.AreEqual(TimeSpan.Zero, words[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(1), words[^1].End);
        Assert.IsTrue(words[1].Start >= words[0].End);
    }

    [TestMethod]
    public async Task TranscribeToWordsAsync_WithProgress_ReportsTranscriptionStages()
    {
        CreateInstalledModel();
        var input = CreateInputFile("input.wav");
        _transcriber.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromMilliseconds(500), "First")
        ];

        var progress = new CollectingProgress<AudioTranscriptionProgress>();

        await _service.TranscribeToWordsAsync(input, progress);

        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.PreparingAudio));
        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.Transcribing));
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

    private sealed class FakeWhisperModelInstaller : TranscriptionService.IWhisperModelInstaller
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

    private sealed class FakeWhisperTranscriber : TranscriptionService.IWhisperTranscriber
    {
        public IReadOnlyList<AudioTranscriptionSegment> Segments { get; set; } = [];

        public Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeAsync(string modelPath, string audioPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(0.25d);
            progress?.Report(1d);
            return Task.FromResult(Segments);
        }
    }

    private sealed class FakeMediaPreparationService : TranscriptionService.IMediaPreparationService
    {
        public string? LastInputPath { get; private set; }

        public Task<TranscriptionService.PreparedAudio> PrepareAsync(string inputPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            LastInputPath = inputPath;

            var workingDirectory = Path.Combine(Path.GetTempPath(), "files-tools-transcription-prepared", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
            var preparedPath = Path.Combine(workingDirectory, "prepared.wav");
            File.WriteAllBytes(preparedPath, [0x52, 0x49, 0x46, 0x46]);
            progress?.Report(0.5d);
            progress?.Report(1d);

            return Task.FromResult(new TranscriptionService.PreparedAudio(preparedPath, workingDirectory));
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
