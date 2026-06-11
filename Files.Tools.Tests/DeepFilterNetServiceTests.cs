using Files_Tools.Services;

namespace Files.Tools.Tests;

/// <summary>
/// End-to-end test for <see cref="DeepFilterNetService"/>: runs the real DFN3 model on the noisy
/// test clip and checks the C# output matches the Rust ground truth (the reference impl) and is
/// healthy speech. Skips when the model/wavs aren't present locally (they live under
/// tools/deepfilternet-rt, not in CI).
/// </summary>
[TestClass]
public class DeepFilterNetServiceTests
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

    [TestMethod]
    public void EnhanceMono_MatchesRustGroundTruth()
    {
        var tooling = Tooling();
        if (tooling is null)
        {
            Assert.Inconclusive("tooling dir not found.");
            return;
        }

        var model = Path.Combine(tooling, "models", "dfn3", "combined.onnx");
        var noisy = Path.Combine(tooling, "noisy48k.wav");
        var gt = Path.Combine(tooling, "gt_enhanced.wav");
        if (!File.Exists(model) || !File.Exists(noisy) || !File.Exists(gt))
        {
            Assert.Inconclusive("DFN3 model or reference wavs not present locally.");
            return;
        }

        var input = WaveReader.ReadMonoFloatWav(noisy);       // reads samples regardless of declared rate
        var groundTruth = WaveReader.ReadMonoFloatWav(gt);

        using var service = new DeepFilterNetService(model);
        var output = service.EnhanceMono(input);

        double rms = Math.Sqrt(output.Select(v => (double)v * v).DefaultIfEmpty(0).Average());
        Assert.IsTrue(rms is > 0.001 and < 2.0, $"output rms {rms:F4} should be healthy speech");

        double corr = AlignedCorrelation(output, groundTruth);
        Assert.IsTrue(corr > 0.95, $"correlation to Rust ground truth {corr:F4} too low (expected > 0.95)");
    }

    // Cross-correlation alignment (the reference is delay-trimmed) then gain-invariant Pearson.
    private static double AlignedCorrelation(float[] a, float[] b)
    {
        int n = Math.Min(Math.Min(a.Length, b.Length), 120000);
        int bestLag = 0;
        double bestAbs = -1;
        for (int lag = -4000; lag <= 4000; lag += 20)
        {
            double dot = 0;
            for (int i = 0; i < n; i++)
            {
                int j = i + lag;
                if (j >= 0 && j < n)
                {
                    dot += a[i] * (double)b[j];
                }
            }

            if (Math.Abs(dot) > bestAbs)
            {
                bestAbs = Math.Abs(dot);
                bestLag = lag;
            }
        }

        double sxy = 0, sxx = 0, syy = 0, mx = 0, my = 0;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            int j = i + bestLag;
            if (j >= 0 && j < n)
            {
                mx += a[i];
                my += b[j];
                count++;
            }
        }

        mx /= count;
        my /= count;
        for (int i = 0; i < n; i++)
        {
            int j = i + bestLag;
            if (j >= 0 && j < n)
            {
                double dx = a[i] - mx, dy = b[j] - my;
                sxy += dx * dy;
                sxx += dx * dx;
                syy += dy * dy;
            }
        }

        return sxy / (Math.Sqrt(sxx * syy) + 1e-12);
    }
}
