using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Files_Tools.Services;

/// <summary>
/// Speech bandwidth extension / super-resolution with FlashSR (16 kHz → 48 kHz), run via ONNX
/// Runtime. A feed-forward waveform-to-waveform model (~0.5 MB, bundled) that regenerates the
/// high-frequency content missing from low-bandwidth/dull recordings, adding "fullness". DSP is
/// internal to the model, so the .NET side just feeds 16 kHz mono and reads 48 kHz mono.
/// </summary>
public sealed class FlashSrService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _input;
    private bool _disposed;

    /// <summary>Required input sample rate.</summary>
    public const int InputSampleRate = 16000;

    /// <summary>Output sample rate.</summary>
    public const int OutputSampleRate = 48000;

    public FlashSrService(string? modelPath = null)
    {
        modelPath ??= ResolveDefaultModelPath();
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("FlashSR model not found.", modelPath);
        }

        _session = new InferenceSession(modelPath);
        _input = _session.InputMetadata.Keys.First();
    }

    /// <summary>Default bundled model location.</summary>
    public static string ResolveDefaultModelPath()
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "FlashSr", "model.onnx");

    /// <summary>Whether the bundled model is present.</summary>
    public static bool IsAvailable() => File.Exists(ResolveDefaultModelPath());

    // The model's activation memory scales with input length (~85 MB per second of input), so long
    // audio is processed in overlapped chunks with a linear crossfade to bound RAM without seams.
    private const int Ratio = OutputSampleRate / InputSampleRate;   // 3
    private const int ChunkSamples = 10 * InputSampleRate;          // 10 s @ 16 kHz
    private const int OverlapSamples = 2 * InputSampleRate;         // 2 s crossfade

    /// <summary>
    /// Upsamples mono 16 kHz audio to mono 48 kHz with restored high-frequency content.
    /// </summary>
    public float[] UpsampleMono(float[] samples16k, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples16k);
        if (samples16k.Length == 0)
        {
            return Array.Empty<float>();
        }

        if (samples16k.Length <= ChunkSamples)
        {
            return RunModel(samples16k, cancellationToken);
        }

        int hop = ChunkSamples - OverlapSamples;
        int outOverlap = OverlapSamples * Ratio;
        var output = new float[(long)samples16k.Length * Ratio];

        for (int start = 0; start < samples16k.Length; start += hop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int segLen = Math.Min(ChunkSamples, samples16k.Length - start);
            var seg = new float[segLen];
            Array.Copy(samples16k, start, seg, 0, segLen);

            var y = RunModel(seg, cancellationToken);
            bool isLast = start + hop >= samples16k.Length;
            int outStart = start * Ratio;
            for (int k = 0; k < y.Length; k++)
            {
                int idx = outStart + k;
                if (idx >= output.Length)
                {
                    break;
                }

                float g = 1f;
                if (start > 0 && k < outOverlap)
                {
                    g = (float)k / outOverlap;                       // crossfade in
                }

                if (!isLast && k >= y.Length - outOverlap)
                {
                    g *= (float)(y.Length - 1 - k) / outOverlap;     // crossfade out
                }

                output[idx] += y[k] * g;
            }
        }

        return output;
    }

    private float[] RunModel(float[] samples16k, CancellationToken cancellationToken)
    {
        var input = new DenseTensor<float>(samples16k, new[] { 1, samples16k.Length });
        cancellationToken.ThrowIfCancellationRequested();
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_input, input) });
        return results.First().AsTensor<float>().ToArray();
    }

    /// <summary>Runs <see cref="UpsampleMono"/> off the calling thread.</summary>
    public Task<float[]> UpsampleMonoAsync(float[] samples16k, CancellationToken cancellationToken = default)
        => Task.Run(() => UpsampleMono(samples16k, cancellationToken), cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }
}
