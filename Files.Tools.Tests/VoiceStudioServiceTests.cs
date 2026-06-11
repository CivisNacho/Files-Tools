using Files_Tools.Services;

namespace Files.Tools.Tests;

/// <summary>
/// End-to-end test for the full "studio voice" chain (<see cref="VoiceStudioService"/>):
/// extract → DeepFilterNet3 → FlashSR → FFmpeg mastering. Requires the local models + FFmpeg;
/// skips otherwise. Verifies the output is valid 48 kHz audio with restored high-frequency content.
/// </summary>
[TestClass]
public class VoiceStudioServiceTests
{
    private string _temp = null!;

    [TestInitialize]
    public void Init()
    {
        _temp = Path.Combine(Path.GetTempPath(), "files-tools-voice-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

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

    [TestMethod]
    public async Task ProcessAudio_FullChain_ProducesEnhancedOutput()
    {
        var tooling = Tooling();
        if (tooling is null)
        {
            Assert.Inconclusive("tooling dir not found.");
            return;
        }

        var dfn = Path.Combine(tooling, "models", "dfn3", "combined.onnx");
        var flash = Path.Combine(tooling, "flashsr_model.onnx");
        var input = Path.Combine(tooling, "noisy48k.wav");
        if (!File.Exists(dfn) || !File.Exists(flash) || !File.Exists(input))
        {
            Assert.Inconclusive("models or input wav not present locally.");
            return;
        }

        var output = Path.Combine(_temp, "studio.wav");
        var service = new VoiceStudioService(dfn, flash);
        var stages = new List<VoiceStudioStage>();
        var progress = new Progress<VoiceStudioProgress>(p => stages.Add(p.Stage));

        await service.ProcessAudioAsync(input, output, new VoiceStudioOptions(), progress);

        Assert.IsTrue(File.Exists(output), "output wav should be produced");
        var samples = WaveReader.ReadMonoFloatWav(output);
        Assert.IsTrue(samples.Length > 48000, $"output too short ({samples.Length} samples)");

        double rms = Math.Sqrt(samples.Select(v => (double)v * v).DefaultIfEmpty(0).Average());
        Assert.IsTrue(rms is > 0.001 and < 2.0, $"output rms {rms:F4} should be healthy audio");

        // The full chain restores high-frequency content vs the 16 kHz-sourced input (band-limited
        // to 8 kHz). Speech has little absolute energy this high, but a band-limited signal would
        // show ~0 here, so a clearly non-trivial fraction confirms FlashSR contributed.
        double highBandFraction = HighBandEnergyFraction(samples, 48000, 12000, 22000);
        Assert.IsTrue(highBandFraction > 1e-5,
            $"expected restored high-frequency energy above the band-limited baseline (got {highBandFraction:E2})");
    }

    private static double HighBandEnergyFraction(float[] x, int sr, double lo, double hi)
    {
        // Crude DFT-band energy over a mid segment (avoids loading an FFT lib in the test).
        int n = Math.Min(8192, x.Length);
        int start = Math.Max(0, (x.Length - n) / 2);
        double total = 1e-12, band = 0;
        for (int k = 0; k < n / 2; k++)
        {
            double re = 0, im = 0;
            for (int t = 0; t < n; t++)
            {
                double ang = -2.0 * Math.PI * k * t / n;
                double s = x[start + t];
                re += s * Math.Cos(ang);
                im += s * Math.Sin(ang);
            }

            double p = (re * re) + (im * im);
            double f = (double)k * sr / n;
            total += p;
            if (f >= lo && f < hi)
            {
                band += p;
            }
        }

        return band / total;
    }
}
