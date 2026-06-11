using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Files_Tools.Helpers;

/// <summary>
/// Shared estimator for "time remaining" readouts. Feed it overall progress fractions as they
/// arrive (<see cref="AddSample"/>) and it returns a smoothed remaining-time estimate based on
/// recent throughput (sliding window) with a linear-from-start fallback, plus exponential
/// smoothing so the displayed value doesn't jitter. Between samples, <see cref="Tick"/> counts
/// the estimate down in real time so the readout never appears frozen.
/// Not thread-safe; use one instance per operation and call from a single thread.
/// </summary>
public sealed class EtaEstimator
{
    private const int MaxSamples = 30;
    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(15);

    // Estimates are unreliable before any meaningful progress or elapsed time has accrued.
    private const double MinFraction = 0.01;
    private const double MinElapsedSeconds = 2;
    private const double SmoothingAlpha = 0.35;

    private readonly Stopwatch _stopwatch = new();
    private readonly Func<TimeSpan>? _clock;
    private readonly Queue<(TimeSpan Elapsed, double Fraction)> _samples = new();
    private double _smoothedRemainingSec = -1;
    private bool _started;
    private TimeSpan _startElapsed;
    private TimeSpan _lastUpdateElapsed;

    public EtaEstimator()
    {
    }

    /// <summary>Test hook: supply a fake monotonic clock instead of a wall-clock stopwatch.</summary>
    internal EtaEstimator(Func<TimeSpan> clock) => _clock = clock;

    private TimeSpan Elapsed => (_clock?.Invoke() ?? _stopwatch.Elapsed) - _startElapsed;

    /// <summary>The current smoothed estimate, or null while still calculating.</summary>
    public TimeSpan? Remaining => _smoothedRemainingSec < 0
        ? null
        : TimeSpan.FromSeconds(Math.Max(0, _smoothedRemainingSec));

    /// <summary>Clears all state so the instance can track a new operation.</summary>
    public void Reset()
    {
        _stopwatch.Reset();
        _samples.Clear();
        _smoothedRemainingSec = -1;
        _started = false;
        _startElapsed = TimeSpan.Zero;
        _lastUpdateElapsed = TimeSpan.Zero;
    }

    /// <summary>
    /// Records a progress sample (overall fraction in [0,1]) and returns the updated estimate,
    /// or null while there is not yet enough signal to estimate.
    /// </summary>
    public TimeSpan? AddSample(double fraction)
    {
        if (!_started)
        {
            _started = true;
            if (_clock is null)
            {
                _stopwatch.Start();
            }
            else
            {
                _startElapsed = _clock();
            }
        }

        fraction = Math.Clamp(fraction, 0d, 1d);
        var elapsed = Elapsed;
        _lastUpdateElapsed = elapsed;

        if (fraction >= 1)
        {
            _smoothedRemainingSec = 0;
            return TimeSpan.Zero;
        }

        _samples.Enqueue((elapsed, fraction));
        while (_samples.Count > MaxSamples
               || (_samples.Count > 2 && elapsed - _samples.Peek().Elapsed > SampleWindow))
        {
            _samples.Dequeue();
        }

        if (fraction < MinFraction || elapsed.TotalSeconds < MinElapsedSeconds || _samples.Count < 2)
        {
            return Remaining;
        }

        var (firstElapsed, firstFraction) = _samples.Peek();
        var deltaFraction = fraction - firstFraction;
        var deltaSec = (elapsed - firstElapsed).TotalSeconds;

        double remainSec;
        if (deltaFraction > 1e-4 && deltaSec > 0.5)
        {
            // Recent windowed throughput.
            remainSec = (1.0 - fraction) * deltaSec / deltaFraction;
        }
        else if (_smoothedRemainingSec < 0 && fraction > 0)
        {
            // No estimate yet and the window has no usable throughput: seed with a linear
            // projection from the start of the operation.
            remainSec = elapsed.TotalSeconds * (1.0 - fraction) / fraction;
        }
        else
        {
            // Progress stalled within the window; keep the previous estimate rather than
            // projecting from a rate we don't have.
            return Remaining;
        }

        _smoothedRemainingSec = _smoothedRemainingSec < 0
            ? remainSec
            : (SmoothingAlpha * remainSec) + ((1 - SmoothingAlpha) * _smoothedRemainingSec);

        return Remaining;
    }

    /// <summary>
    /// Counts the current estimate down by the wall-clock time elapsed since the last sample or
    /// tick, so periodic UI timers can keep the readout moving between progress reports.
    /// Returns the adjusted estimate, or null if there is none yet.
    /// </summary>
    public TimeSpan? Tick()
    {
        if (_smoothedRemainingSec < 0 || !_started)
        {
            return Remaining;
        }

        var elapsed = Elapsed;
        var delta = (elapsed - _lastUpdateElapsed).TotalSeconds;
        if (delta > 0)
        {
            _smoothedRemainingSec = Math.Max(0, _smoothedRemainingSec - delta);
            _lastUpdateElapsed = elapsed;
        }

        return Remaining;
    }

    /// <summary>
    /// Formats a duration for ETA readouts: "42s", "3m 12s", "1h 5m". Rounds up to the next
    /// second so the readout never shows "0s" while work is still running.
    /// </summary>
    public static string FormatDuration(TimeSpan value)
    {
        var totalSeconds = Math.Max(0, (long)Math.Ceiling(value.TotalSeconds));
        if (totalSeconds < 60)
        {
            return $"{totalSeconds}s";
        }

        if (totalSeconds < 3600)
        {
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return seconds == 0 ? $"{minutes}m" : $"{minutes}m {seconds}s";
        }

        var hours = totalSeconds / 3600;
        var remMinutes = (totalSeconds % 3600) / 60;
        return remMinutes == 0 ? $"{hours}h" : $"{hours}h {remMinutes}m";
    }
}
