using Files_Tools.Services;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace Files.Tools.Tests;

[TestClass]
public class AudioDenoiseServiceTests
{
    private string _tempRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-denoise-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
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
    public async Task DenoiseAudioAsync_RejectsInvalidDtlnSampleRate()
    {
        var input = Path.Combine(_tempRoot, "input.wav");
        var output = Path.Combine(_tempRoot, "output.wav");
        File.WriteAllText(input, "placeholder");
        var service = new AudioDenoiseService(new PassthroughDenoiseEngine());

        await AssertThrowsAsync<DenoiseValidationException>(async () =>
            await service.DenoiseAudioAsync(input, output, new AudioDenoiseOptions
            {
                ModelSampleRate = 48000
            }));
    }

    [TestMethod]
    public async Task DenoiseAudioAsync_MonoMode_ProducesMonoOutput()
    {
        var input = Path.Combine(_tempRoot, "stereo-input.wav");
        var output = Path.Combine(_tempRoot, "mono-output.wav");
        WriteStereoSineWave(input);
        var service = new AudioDenoiseService(new PassthroughDenoiseEngine());

        var result = await service.DenoiseAudioAsync(input, output, new AudioDenoiseOptions
        {
            Mode = AudioDenoiseMode.Mono,
            ModelSampleRate = 16000,
            OutputSampleRate = 16000,
            NormalizePeak = false
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual(AudioDenoiseMode.Mono, result.Mode);
        Assert.AreEqual(1, result.OutputChannels);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("converted to mono", StringComparison.OrdinalIgnoreCase)));

        var probe = await service.ProbeAudioAsync(output);
        Assert.AreEqual(1, probe.Channels);
        Assert.AreEqual(16000, probe.SampleRate);
    }

    [TestMethod]
    public async Task DenoiseAudioAsync_StrongStereoMode_DenoisesBothChannels()
    {
        var input = Path.Combine(_tempRoot, "stereo-input.wav");
        var output = Path.Combine(_tempRoot, "strong-stereo-output.wav");
        WriteStereoSineWave(input);
        var service = new AudioDenoiseService(new ZeroDenoiseEngine());

        var result = await service.DenoiseAudioAsync(input, output, new AudioDenoiseOptions
        {
            Mode = AudioDenoiseMode.StrongStereo,
            DenoiseAmount = 100,
            ModelSampleRate = 16000,
            OutputSampleRate = 16000,
            NormalizePeak = false,
            PreventClipping = false
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual(AudioDenoiseMode.StrongStereo, result.Mode);
        Assert.AreEqual(2, result.OutputChannels);

        var samples = ReadPcm16Wave(output);
        Assert.IsTrue(samples.All(sample => Math.Abs(sample) <= 2), "Strong stereo mode should apply the denoised signal to both output channels at 100% strength.");
    }

    [TestMethod]
    public async Task DenoiseAudioAsync_MonoMode_RespectsDenoiseAmount()
    {
        var input = Path.Combine(_tempRoot, "mono-input.wav");
        var output = Path.Combine(_tempRoot, "mono-amount-output.wav");
        WriteMonoSineWave(input);
        var service = new AudioDenoiseService(new ZeroDenoiseEngine());

        await service.DenoiseAudioAsync(input, output, new AudioDenoiseOptions
        {
            Mode = AudioDenoiseMode.Mono,
            DenoiseAmount = 0,
            ModelSampleRate = 16000,
            OutputSampleRate = 16000,
            NormalizePeak = false,
            PreventClipping = false
        });

        var samples = ReadPcm16Wave(output);
        Assert.IsTrue(samples.Any(sample => Math.Abs(sample) > 1000), "Mono mode should preserve the original signal at 0% denoise amount.");
    }

    [TestMethod]
    public async Task DenoiseAudioAsync_DenoisePasses_RunsEngineMultipleTimes()
    {
        var input = Path.Combine(_tempRoot, "mono-input.wav");
        var output = Path.Combine(_tempRoot, "mono-passes-output.wav");
        WriteMonoSineWave(input);
        var engine = new CountingDenoiseEngine();
        var service = new AudioDenoiseService(engine);

        await service.DenoiseAudioAsync(input, output, new AudioDenoiseOptions
        {
            Mode = AudioDenoiseMode.Mono,
            DenoisePasses = 3,
            ModelSampleRate = 16000,
            OutputSampleRate = 16000,
            NormalizePeak = false,
            PreventClipping = false
        });

        Assert.AreEqual(3, engine.CallCount);
    }

    [TestMethod]
    public async Task DenoiseAudioAsync_DefaultEngine_UsesBundledDtlnEngine()
    {
        var input = Path.Combine(_tempRoot, "input.wav");
        var output = Path.Combine(_tempRoot, "output.wav");
        WriteMonoSineWave(input);
        var service = new AudioDenoiseService();

        var result = await service.DenoiseAudioAsync(input, output, new AudioDenoiseOptions
        {
            Mode = AudioDenoiseMode.Mono,
            ModelSampleRate = 16000,
            OutputSampleRate = 16000,
            NormalizePeak = false
        });

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual(1, result.OutputChannels);
        Assert.AreEqual(16000, result.OutputSampleRate);
    }

    [TestMethod]
    public void DtlnOnnxDenoiseEngine_MissingModelPath_ThrowsModelException()
    {
        var model1 = Path.Combine(_tempRoot, "missing-model-1.onnx");
        var model2 = Path.Combine(_tempRoot, "missing-model-2.onnx");

        AssertThrows<DenoiseModelException>(() =>
            _ = new DtlnOnnxDenoiseEngine(model1, model2));
    }

    [TestMethod]
    public async Task DtlnOnnxDenoiseEngine_RejectsNonDtlnSampleRate()
    {
        using var engine = CreateBundledEngine();

        await AssertThrowsAsync<DenoiseModelException>(async () =>
            await engine.DenoiseMonoAsync(new float[512], 48000));
    }

    private static DtlnOnnxDenoiseEngine CreateBundledEngine()
    {
        var root = FindRepoRoot();
        return new DtlnOnnxDenoiseEngine(
            Path.Combine(root, "Assets", "Models", "Dtln", "model_1.onnx"),
            Path.Combine(root, "Assets", "Models", "Dtln", "model_2.onnx"));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Files Tools.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for DTLN model tests.");
    }

    private static void WriteMonoSineWave(string path)
    {
        var samples = Enumerable.Range(0, 1600)
            .Select(index => (short)(Math.Sin(index * 2 * Math.PI * 440 / 16000) * short.MaxValue * 0.25))
            .ToArray();
        WritePcm16Wave(path, samples, sampleRate: 16000, channels: 1);
    }

    private static void WriteStereoSineWave(string path)
    {
        var samples = new short[1600 * 2];
        for (var frame = 0; frame < 1600; frame++)
        {
            samples[frame * 2] = (short)(Math.Sin(frame * 2 * Math.PI * 440 / 16000) * short.MaxValue * 0.25);
            samples[frame * 2 + 1] = (short)(Math.Sin(frame * 2 * Math.PI * 660 / 16000) * short.MaxValue * 0.25);
        }

        WritePcm16Wave(path, samples, sampleRate: 16000, channels: 2);
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

    private static short[] ReadPcm16Wave(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        _ = reader.ReadBytes(12);
        while (stream.Position < stream.Length)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadInt32();
            if (chunkId == "data")
            {
                var data = reader.ReadBytes(chunkSize);
                var samples = new short[data.Length / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i * 2, 2));
                }

                return samples;
            }

            stream.Position += chunkSize + (chunkSize % 2);
        }

        return [];
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

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}, but no exception was thrown.");
    }

    private sealed class PassthroughDenoiseEngine : IDtlnDenoiseEngine
    {
        public Task<float[]> DenoiseMonoAsync(
            IReadOnlyList<float> samples,
            int sampleRate,
            IProgress<InferenceProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new InferenceProgressInfo
            {
                ProcessedFrames = samples.Count,
                TotalFrames = samples.Count,
                Percent = 1
            });

            return Task.FromResult(samples.ToArray());
        }
    }

    private sealed class ZeroDenoiseEngine : IDtlnDenoiseEngine
    {
        public Task<float[]> DenoiseMonoAsync(
            IReadOnlyList<float> samples,
            int sampleRate,
            IProgress<InferenceProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new InferenceProgressInfo
            {
                ProcessedFrames = samples.Count,
                TotalFrames = samples.Count,
                Percent = 1
            });

            return Task.FromResult(new float[samples.Count]);
        }
    }

    private sealed class CountingDenoiseEngine : IDtlnDenoiseEngine
    {
        public int CallCount { get; private set; }

        public Task<float[]> DenoiseMonoAsync(
            IReadOnlyList<float> samples,
            int sampleRate,
            IProgress<InferenceProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            progress?.Report(new InferenceProgressInfo
            {
                ProcessedFrames = samples.Count,
                TotalFrames = samples.Count,
                Percent = 1
            });

            return Task.FromResult(samples.Select(sample => sample * 0.5f).ToArray());
        }
    }
}
