using Files_Tools.Services;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace Files.Tools.Tests;

[TestClass]
public class AudioProcessingServiceTests
{
    private string _tempRoot = null!;
    private AudioProcessingService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-audio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _service = new AudioProcessingService();
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
    public async Task ConvertAsync_RejectsMissingInput()
    {
        var output = Path.Combine(_tempRoot, "output.mp3");

        await AssertThrowsAsync<FileNotFoundException>(async () =>
            await _service.ConvertAsync(Path.Combine(_tempRoot, "missing.wav"), output, new AudioConversionOptions()));
    }

    [TestMethod]
    public async Task ConvertAsync_WavToMp3_ProducesPlayableOutput()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "output.mp3");

        var result = await _service.ConvertAsync(input, output, new AudioConversionOptions
        {
            BitrateKbps = 128,
            SampleRate = 44100,
            Channels = 1
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("mp3", result.OutputCodec);
        Assert.AreEqual(44100, result.OutputSampleRate);
        Assert.AreEqual(1, result.OutputChannels);
        Assert.IsGreaterThan(0L, result.OutputSizeBytes ?? 0);
    }

    [TestMethod]
    public async Task ConvertAsync_WavToFlac_ProducesPlayableOutput()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "output.flac");

        var result = await _service.ConvertAsync(input, output, new AudioConversionOptions());

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("flac", result.OutputCodec);
    }

    [TestMethod]
    public async Task CompressAsync_RejectsInvalidBitrate()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "output.mp3");

        await AssertThrowsAsync<AudioProcessingValidationException>(async () =>
            await _service.CompressAsync(input, output, new AudioCompressionOptions
            {
                TargetBitrateKbps = 0
            }));
    }

    [TestMethod]
    public async Task CompressAsync_Lossless_DefaultsToFlac()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "output.flac");

        var result = await _service.CompressAsync(input, output, new AudioCompressionOptions
        {
            Mode = AudioCompressionMode.Lossless
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("flac", result.OutputCodec);
    }

    [TestMethod]
    public async Task NormalizeAsync_Peak_ProducesOutput()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "normalized.wav");

        var result = await _service.NormalizeAsync(input, output, new AudioNormalizationOptions
        {
            Mode = AudioNormalizationMode.Peak,
            TargetPeakDb = -3
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual(1, result.OutputChannels);
    }

    [TestMethod]
    public async Task NormalizeAsync_Lufs_ProducesOutput()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "lufs.wav");

        var result = await _service.NormalizeAsync(input, output, new AudioNormalizationOptions
        {
            Mode = AudioNormalizationMode.Lufs,
            TargetLufs = -16
        });

        Assert.IsTrue(File.Exists(output));
        Assert.IsNotNull(result.Duration);
    }

    [TestMethod]
    public async Task ProcessPodcastAudioAsync_ProducesPodcastLikeOutputWithAnalysis()
    {
        var input = CreateTone("input.wav", durationSeconds: 2);
        var output = Path.Combine(_tempRoot, "podcast.mp3");
        var reports = new List<AudioProcessProgress>();

        var result = await _service.ProcessPodcastAudioAsync(input, output, new AudioPodcastProcessingOptions
        {
            BitrateKbps = 128,
            Channels = 1
        }, new Progress<AudioProcessProgress>(reports.Add));

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("mp3", result.OutputCodec);
        Assert.AreEqual(1, result.OutputChannels);
        Assert.IsNotNull(result.Analysis);
        Assert.IsNotNull(result.Analysis!.MaxVolumeDb);
        Assert.IsGreaterThan(0, result.Warnings.Count);
        Assert.IsTrue(reports.Any(report => report.Stage == AudioProcessStage.Probing));
        Assert.IsTrue(reports.Any(report => report.Stage == AudioProcessStage.Completed));
    }

    [TestMethod]
    public async Task ProcessPodcastAudioAsync_WithDtlnDenoise_RoutesThroughDtlnService()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "podcast-denoise.wav");
        var denoiseService = new PassthroughVideoAudioDenoiseService();
        var service = new AudioProcessingService(denoiseService);

        var result = await service.ProcessPodcastAudioAsync(input, output, new AudioPodcastProcessingOptions
        {
            EnableDtlnDenoise = true,
            DtlnDenoiseMode = AudioDenoiseMode.Mono,
            DtlnDenoiseAmount = 85,
            DtlnDenoisePasses = 2
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("pcm_s16le", result.OutputCodec);
        Assert.AreEqual(1, denoiseService.AudioDenoiseCalls);
        Assert.AreEqual(85, denoiseService.LastOptions?.DenoiseAmount);
        Assert.AreEqual(2, denoiseService.LastOptions?.DenoisePasses);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("DTLN", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ProcessPodcastAudioAsync_RejectsInvalidTargetLufs()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "invalid.wav");

        await AssertThrowsAsync<AudioProcessingValidationException>(async () =>
            await _service.ProcessPodcastAudioAsync(input, output, new AudioPodcastProcessingOptions
            {
                TargetLufs = 3
            }));
    }

    [TestMethod]
    public async Task TrimAsync_RejectsInvalidRange()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "trimmed.wav");

        await AssertThrowsAsync<AudioProcessingValidationException>(async () =>
            await _service.TrimAsync(input, output, new AudioTrimOptions
            {
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromMilliseconds(500)
            }));
    }

    [TestMethod]
    public async Task TrimAsync_CreatesShorterOutput()
    {
        var input = CreateTone("input.wav", durationSeconds: 2);
        var output = Path.Combine(_tempRoot, "trimmed.wav");

        var result = await _service.TrimAsync(input, output, new AudioTrimOptions
        {
            StartTime = TimeSpan.FromMilliseconds(200),
            Duration = TimeSpan.FromMilliseconds(600)
        });

        Assert.IsTrue(File.Exists(output));
        Assert.IsTrue(result.Duration < TimeSpan.FromSeconds(1.2));
    }

    [TestMethod]
    public async Task RemoveSilenceAsync_LeadingAndTrailing_ProducesOutput()
    {
        var input = CreateToneWithSilence("silence.wav");
        var output = Path.Combine(_tempRoot, "nosilence.wav");

        var result = await _service.RemoveSilenceAsync(input, output, new AudioSilenceRemovalOptions());

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("pcm_s16le", result.OutputCodec);
        Assert.IsGreaterThan(0L, result.OutputSizeBytes ?? 0);
    }

    [TestMethod]
    public async Task RemoveSilenceAsync_Internal_IsNotSupported()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "internal.wav");

        await AssertThrowsAsync<NotSupportedException>(async () =>
            await _service.RemoveSilenceAsync(input, output, new AudioSilenceRemovalOptions
            {
                Mode = SilenceRemovalMode.Internal
            }));
    }

    [TestMethod]
    public async Task ApplyEqualizerAsync_PodcastVoice_ProducesOutput()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "podcast.wav");

        var result = await _service.ApplyEqualizerAsync(input, output, new AudioEqualizerOptions
        {
            Preset = EqualizerPreset.PodcastVoice
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("pcm_s16le", result.OutputCodec);
    }

    [TestMethod]
    public async Task ApplyEqualizerAsync_CustomBand_ProducesOutput()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "custom.wav");

        var result = await _service.ApplyEqualizerAsync(input, output, new AudioEqualizerOptions
        {
            Preset = EqualizerPreset.Custom,
            CustomBands =
            [
                new EqualizerBand
                {
                    FrequencyHz = 1000,
                    GainDb = 2,
                    Width = 1
                }
            ]
        });

        Assert.IsTrue(File.Exists(output));
        Assert.IsNotNull(result.Duration);
    }

    [TestMethod]
    public async Task ConvertAsync_ReportsProgress()
    {
        var input = CreateTone("input.wav");
        var output = Path.Combine(_tempRoot, "progress.mp3");
        var reports = new List<AudioProcessProgress>();

        await _service.ConvertAsync(input, output, new AudioConversionOptions(), new Progress<AudioProcessProgress>(reports.Add));

        Assert.IsTrue(reports.Any(report => report.Stage == AudioProcessStage.Probing));
        Assert.IsTrue(reports.Any(report => report.Stage == AudioProcessStage.Completed));
    }

    [TestMethod]
    public async Task ConvertAsync_CanBeCancelled()
    {
        var input = CreateTone("input.wav", durationSeconds: 5);
        var output = Path.Combine(_tempRoot, "cancelled.mp3");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await AssertThrowsAsync<OperationCanceledException>(async () =>
            await _service.ConvertAsync(input, output, new AudioConversionOptions(), cancellationToken: cts.Token));
    }

    private string CreateTone(string name, int sampleRate = 44100, int durationSeconds = 1)
    {
        var path = Path.Combine(_tempRoot, name);
        var samples = Enumerable.Range(0, sampleRate * durationSeconds)
            .Select(index => (short)(Math.Sin(index * 2 * Math.PI * 440 / sampleRate) * short.MaxValue * 0.25))
            .ToArray();
        WritePcm16Wave(path, samples, sampleRate, channels: 1);
        return path;
    }

    private string CreateToneWithSilence(string name)
    {
        var sampleRate = 44100;
        var samples = new short[sampleRate * 2];
        for (var i = sampleRate / 2; i < sampleRate + sampleRate / 2; i++)
        {
            samples[i] = (short)(Math.Sin(i * 2 * Math.PI * 440 / sampleRate) * short.MaxValue * 0.25);
        }

        var path = Path.Combine(_tempRoot, name);
        WritePcm16Wave(path, samples, sampleRate, channels: 1);
        return path;
    }

    private static void WritePcm16Wave(string path, short[] samples, int sampleRate, int channels)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        var dataLength = samples.Length * 2;

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        Span<byte> buffer = stackalloc byte[2];
        foreach (var sample in samples)
        {
            BinaryPrimitives.WriteInt16LittleEndian(buffer, sample);
            writer.Write(buffer);
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}, but no exception was thrown.");
    }

    private sealed class PassthroughVideoAudioDenoiseService : IVideoAudioDenoiseService
    {
        public int AudioDenoiseCalls { get; private set; }

        public AudioDenoiseOptions? LastOptions { get; private set; }

        public Task<DenoiseResult> DenoiseAudioAsync(string inputAudioPath, string outputAudioPath, AudioDenoiseOptions options, IProgress<DenoiseProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            AudioDenoiseCalls++;
            LastOptions = options;
            Directory.CreateDirectory(Path.GetDirectoryName(outputAudioPath)!);
            File.Copy(inputAudioPath, outputAudioPath, overwrite: true);
            return Task.FromResult(new DenoiseResult
            {
                OutputPath = outputAudioPath,
                Mode = options.Mode,
                ModelSampleRate = options.ModelSampleRate,
                OutputSampleRate = options.OutputSampleRate ?? 44100,
                OutputChannels = options.Mode == AudioDenoiseMode.Mono ? 1 : 2,
                Warnings = Array.Empty<string>()
            });
        }

        public Task<DenoiseResult> DenoiseVideoAudioAsync(string inputVideoPath, string outputVideoPath, VideoAudioDenoiseOptions options, IProgress<DenoiseProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AudioProbeResult> ProbeAudioAsync(string inputPath, int? audioStreamIndex = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
