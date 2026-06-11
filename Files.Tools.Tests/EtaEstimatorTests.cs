using Files_Tools.Helpers;

namespace Files.Tools.Tests;

/// <summary>
/// Validates the shared ETA estimator: warm-up gating, windowed-throughput estimates,
/// smoothing across rate changes, tick-down between samples, and duration formatting.
/// </summary>
[TestClass]
public class EtaEstimatorTests
{
    private static (EtaEstimator Estimator, Action<double> Advance) Create()
    {
        var now = TimeSpan.Zero;
        var estimator = new EtaEstimator(() => now);
        return (estimator, seconds => now += TimeSpan.FromSeconds(seconds));
    }

    [TestMethod]
    public void ReturnsNullBeforeEnoughSignal()
    {
        var (estimator, advance) = Create();

        Assert.IsNull(estimator.AddSample(0));
        advance(0.5);
        Assert.IsNull(estimator.AddSample(0.005), "below minimum fraction and elapsed");
        Assert.IsNull(estimator.Remaining);
    }

    [TestMethod]
    public void EstimatesFromSteadyThroughput()
    {
        var (estimator, advance) = Create();

        // 10% per 2 seconds → 5% per second; at 30% done, 70% remain → ~14 s
        // (smoothing across the first samples leaves it slightly above the raw value).
        estimator.AddSample(0.10);
        advance(2);
        estimator.AddSample(0.20);
        advance(2);
        var eta = estimator.AddSample(0.30);

        Assert.IsNotNull(eta);
        Assert.AreEqual(15, eta.Value.TotalSeconds, 2.5);
    }

    [TestMethod]
    public void CompletionReportsZero()
    {
        var (estimator, advance) = Create();

        estimator.AddSample(0.5);
        advance(5);
        var eta = estimator.AddSample(1.0);

        Assert.AreEqual(TimeSpan.Zero, eta);
    }

    [TestMethod]
    public void StallKeepsPreviousEstimateInsteadOfProjectingGlobally()
    {
        var (estimator, advance) = Create();

        estimator.AddSample(0.10);
        advance(2);
        estimator.AddSample(0.20);
        advance(2);
        var before = estimator.AddSample(0.30);
        Assert.IsNotNull(before);

        // Push the moving samples out of the 15 s window with no progress.
        advance(20);
        estimator.AddSample(0.30);
        advance(20);
        var stalled = estimator.AddSample(0.30);

        Assert.IsNotNull(stalled, "estimate should survive a stall");
        Assert.AreEqual(before.Value.TotalSeconds, stalled.Value.TotalSeconds, 0.001,
            "a stall must not rewrite the estimate from the global average");
    }

    [TestMethod]
    public void TickCountsDownBetweenSamples()
    {
        var (estimator, advance) = Create();

        estimator.AddSample(0.10);
        advance(2);
        estimator.AddSample(0.20);
        advance(2);
        var eta = estimator.AddSample(0.30)!.Value;

        advance(4);
        var ticked = estimator.Tick();

        Assert.IsNotNull(ticked);
        Assert.AreEqual(eta.TotalSeconds - 4, ticked.Value.TotalSeconds, 0.001);
    }

    [TestMethod]
    public void TickNeverGoesNegative()
    {
        var (estimator, advance) = Create();

        estimator.AddSample(0.40);
        advance(2);
        estimator.AddSample(0.90);

        advance(1000);
        var ticked = estimator.Tick();

        Assert.IsNotNull(ticked);
        Assert.AreEqual(TimeSpan.Zero, ticked.Value);
    }

    [TestMethod]
    public void SlowdownRaisesEstimateGradually()
    {
        var (estimator, advance) = Create();

        // Fast phase: 10%/s.
        estimator.AddSample(0.10);
        advance(1);
        estimator.AddSample(0.20);
        advance(1);
        var fastEta = estimator.AddSample(0.30)!.Value;

        // Slow phase: 1%/s.
        for (var i = 1; i <= 20; i++)
        {
            advance(1);
            estimator.AddSample(0.30 + (0.01 * i));
        }

        var slowEta = estimator.Remaining!.Value;
        Assert.IsTrue(slowEta > fastEta, $"ETA should grow after a slowdown (fast {fastEta}, slow {slowEta})");
        // 50% remaining at ~1%/s ≈ 50 s once the window only holds slow samples.
        Assert.AreEqual(50, slowEta.TotalSeconds, 15);
    }

    [TestMethod]
    public void ResetClearsState()
    {
        var (estimator, advance) = Create();

        estimator.AddSample(0.5);
        advance(5);
        estimator.AddSample(0.8);
        estimator.Reset();

        Assert.IsNull(estimator.Remaining);
        Assert.IsNull(estimator.Tick());
    }

    [TestMethod]
    public void FormatDurationIsHumanReadable()
    {
        Assert.AreEqual("0s", EtaEstimator.FormatDuration(TimeSpan.Zero));
        Assert.AreEqual("1s", EtaEstimator.FormatDuration(TimeSpan.FromMilliseconds(200)));
        Assert.AreEqual("42s", EtaEstimator.FormatDuration(TimeSpan.FromSeconds(42)));
        Assert.AreEqual("1m", EtaEstimator.FormatDuration(TimeSpan.FromSeconds(60)));
        Assert.AreEqual("3m 12s", EtaEstimator.FormatDuration(TimeSpan.FromSeconds(192)));
        Assert.AreEqual("1h 5m", EtaEstimator.FormatDuration(TimeSpan.FromMinutes(65)));
        Assert.AreEqual("2h", EtaEstimator.FormatDuration(TimeSpan.FromHours(2)));
        Assert.AreEqual("0s", EtaEstimator.FormatDuration(TimeSpan.FromSeconds(-5)));
    }
}
