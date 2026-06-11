using Files_Tools.Services;

namespace Files.Tools.Tests;

/// <summary>
/// Exercises the long-audio chunking paths (added to bound RAM: DeepFilterNet processes &gt;60 s in
/// overlap-discard chunks, FlashSR processes &gt;10 s in crossfaded chunks). Confirms they run and
/// produce correct-length, healthy output. Skips when models aren't present locally.
/// </summary>
[TestClass]
public class StudioVoiceChunkingTests
{
    private static string? Tooling()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var c = Path.Combine(dir.FullName, "tools", "deepfilternet-rt");
            if (Directory.Exists(c))
            {
                return c;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static float[] SyntheticSpeech(int samples, int sr)
    {
        // A couple of voiced formants + light noise — enough to exercise the models meaningfully.
        var x = new float[samples];
        var rng = new Random(0);
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)sr;
            x[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 140 * t)
                           + 0.15 * Math.Sin(2 * Math.PI * 320 * t)
                           + 0.05 * (rng.NextDouble() - 0.5));
        }

        return x;
    }

    [TestMethod]
    public void DeepFilterNet_LongAudio_ChunksAndMatchesLength()
    {
        var tooling = Tooling();
        var model = tooling is null ? null : Path.Combine(tooling, "models", "dfn3", "combined.onnx");
        var clip = tooling is null ? null : Path.Combine(tooling, "noisy48k.wav");
        if (model is null || !File.Exists(model) || clip is null || !File.Exists(clip))
        {
            Assert.Inconclusive("DFN3 model or test clip not present.");
            return;
        }

        // Real speech tiled to > 60 s so EnhanceMono takes the chunking path.
        var unit = WaveReader.ReadMonoFloatWav(clip);
        int target = 70 * DeepFilterNetService.SampleRate;
        var input = new float[target];
        for (int i = 0; i < target; i++)
        {
            input[i] = unit[i % unit.Length];
        }

        using var dfn = new DeepFilterNetService(model);
        var reported = new List<double>();
        var output = dfn.EnhanceMono(input, progress: new SynchronousProgress(reported.Add));

        AssertProgressStream(reported);
        Assert.AreEqual(input.Length, output.Length, "chunked DFN output length should match input");
        Assert.IsTrue(output.All(float.IsFinite), "output must be finite (no NaN/Inf at seams)");
        double rms = Math.Sqrt(output.Select(v => (double)v * v).DefaultIfEmpty(0).Average());
        Assert.IsTrue(rms is > 0.001 and < 2.0, $"output rms {rms:F4} should be healthy");
        Assert.IsTrue(output.Max(Math.Abs) < 5.0, $"peak {output.Max(Math.Abs):F2} indicates a seam blowup");
    }

    [TestMethod]
    public void FlashSr_LongAudio_ChunksAndUpsamples()
    {
        var tooling = Tooling();
        var model = tooling is null ? null : Path.Combine(tooling, "flashsr_model.onnx");
        if (model is null || !File.Exists(model))
        {
            Assert.Inconclusive("FlashSR model not present.");
            return;
        }

        var input = SyntheticSpeech(13 * FlashSrService.InputSampleRate, FlashSrService.InputSampleRate); // > 10 s
        using var flash = new FlashSrService(model);
        var reported = new List<double>();
        var output = flash.UpsampleMono(input, progress: new SynchronousProgress(reported.Add));

        AssertProgressStream(reported);

        // 16 kHz -> 48 kHz is a 3x sample-count increase.
        Assert.IsTrue(Math.Abs(output.Length - (input.Length * 3)) < 16000,
            $"expected ~3x length, got {output.Length} for input {input.Length}");
        Assert.IsTrue(output.All(v => Math.Abs(v) < 4.0), "no sample should blow up at a crossfade seam");
    }

    /// <summary>Chunked inference must emit a monotonic 0→1 progress stream for ETA estimation.</summary>
    private static void AssertProgressStream(List<double> reported)
    {
        Assert.IsTrue(reported.Count >= 3, $"expected per-chunk progress reports, got {reported.Count}");
        Assert.AreEqual(0d, reported[0], "stream should start at 0");
        Assert.AreEqual(1d, reported[^1], 1e-9, "stream should end at 1");
        for (var i = 1; i < reported.Count; i++)
        {
            Assert.IsTrue(reported[i] >= reported[i - 1], "progress must be monotonic");
        }
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
