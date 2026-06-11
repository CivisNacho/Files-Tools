using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Files_Tools.Services;

/// <summary>
/// Speech denoising/dereverberation with DeepFilterNet3 (full-band 48 kHz), run via ONNX Runtime
/// in pure .NET. The neural net (<c>combined.onnx</c>, ~8.6 MB, bundled) produces an ERB gain mask
/// and complex deep-filter coefficients; <see cref="DeepFilterNetDsp"/> supplies the STFT/feature/
/// reconstruction DSP around it. Processes a whole signal in one batched pass (~80x realtime CPU).
/// </summary>
public sealed class DeepFilterNetService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _erbInput;
    private readonly string _specInput;
    private readonly double[] _window;
    private readonly int[] _erbWidths;
    private bool _disposed;

    /// <summary>Sample rate the model operates at; input must already be 48 kHz mono.</summary>
    public const int SampleRate = DeepFilterNetDsp.SampleRate;

    /// <summary>Creates the service from the bundled model, or an explicit path.</summary>
    public DeepFilterNetService(string? modelPath = null)
    {
        modelPath ??= ResolveDefaultModelPath();
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("DeepFilterNet model not found.", modelPath);
        }

        _session = new InferenceSession(modelPath);
        var inputs = _session.InputMetadata.Keys.ToList();
        _erbInput = inputs.FirstOrDefault(k => k.Contains("erb", StringComparison.OrdinalIgnoreCase)) ?? inputs[0];
        _specInput = inputs.FirstOrDefault(k => k.Contains("spec", StringComparison.OrdinalIgnoreCase)) ?? inputs[1];
        _window = DeepFilterNetDsp.VorbisWindow();
        _erbWidths = DeepFilterNetDsp.ErbFb();
    }

    /// <summary>Default bundled model location (mirrors the bundled DTLN models under Assets/Models).</summary>
    public static string ResolveDefaultModelPath()
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "DeepFilterNet", "combined.onnx");

    /// <summary>Whether the bundled model is present.</summary>
    public static bool IsAvailable() => File.Exists(ResolveDefaultModelPath());

    // Model/DSP memory scales with length (~13 MB per second). Long audio is processed in chunks
    // with a 1 s warm-up/delay pad on each side that is discarded (the GRU warms up within ~1 s and
    // the output is time-aligned), keeping only the central region — bounding RAM without seams.
    private const int MaxWholeSamples = 60 * SampleRate;   // process up to 60 s in one pass
    private const int ChunkSamples = 30 * SampleRate;      // central region per chunk
    private const int PadSamples = SampleRate;             // 1 s discarded warm-up/delay each side

    /// <summary>
    /// Enhances a mono 48 kHz signal and returns the denoised mono signal at the same rate.
    /// </summary>
    public float[] EnhanceMono(float[] samples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length < DeepFilterNetDsp.Fft)
        {
            return (float[])samples.Clone();
        }

        if (samples.Length <= MaxWholeSamples)
        {
            return EnhanceCore(samples, cancellationToken);
        }

        var output = new float[samples.Length];
        for (int a = 0; a < samples.Length; a += ChunkSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int segStart = Math.Max(0, a - PadSamples);
            int segEnd = Math.Min(samples.Length, a + ChunkSamples + PadSamples);
            var seg = new float[segEnd - segStart];
            Array.Copy(samples, segStart, seg, 0, seg.Length);

            var enh = EnhanceCore(seg, cancellationToken);
            int off = a - segStart;                                  // 'a' within the chunk's output
            int len = Math.Min(ChunkSamples, samples.Length - a);
            for (int k = 0; k < len; k++)
            {
                int src = off + k;
                output[a + k] = src < enh.Length ? enh[src] : 0f;
            }
        }

        return output;
    }

    private float[] EnhanceCore(float[] samples, CancellationToken cancellationToken)
    {
        var (re, im, frames) = DeepFilterNetDsp.Stft(samples, _window);
        DeepFilterNetDsp.Features(re, im, frames, _erbWidths, out var featErb, out var featSpec);
        cancellationToken.ThrowIfCancellationRequested();

        // feat_erb [1,1,T,32] is already frame-major; feat_spec must be [1,2,T,96] (channel-major).
        var erbTensor = new DenseTensor<float>(featErb, new[] { 1, 1, frames, DeepFilterNetDsp.NbErb });
        var specTensor = new DenseTensor<float>(new[] { 1, 2, frames, DeepFilterNetDsp.NbDf });
        for (int t = 0; t < frames; t++)
        {
            int baseRe = (t * 2 * DeepFilterNetDsp.NbDf);
            int baseIm = baseRe + DeepFilterNetDsp.NbDf;
            for (int f = 0; f < DeepFilterNetDsp.NbDf; f++)
            {
                specTensor[0, 0, t, f] = featSpec[baseRe + f];
                specTensor[0, 1, t, f] = featSpec[baseIm + f];
            }
        }

        using var results = _session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(_erbInput, erbTensor),
            NamedOnnxValue.CreateFromTensor(_specInput, specTensor),
        });

        var map = results.ToDictionary(r => r.Name, r => r.AsTensor<float>().ToArray());
        var mask = Pick(map, "m");          // [1,1,T,32] -> [T*32], frame-major
        var coefs = Pick(map, "coefs");     // [1,T,96,10] -> [T*960], row-major == apply layout

        cancellationToken.ThrowIfCancellationRequested();
        var enhanced = DeepFilterNetDsp.ApplyAndReconstruct(re, im, frames, _erbWidths, mask, coefs, _window);

        // The wnorm-scaled forward STFT makes the reconstruction 1/wnorm too quiet; restore level.
        float gain = (float)(1.0 / DeepFilterNetDsp.WNorm);
        for (int i = 0; i < enhanced.Length; i++)
        {
            enhanced[i] *= gain;
        }

        return enhanced;
    }

    /// <summary>Runs <see cref="EnhanceMono"/> off the calling thread.</summary>
    public Task<float[]> EnhanceMonoAsync(float[] samples, CancellationToken cancellationToken = default)
        => Task.Run(() => EnhanceMono(samples, cancellationToken), cancellationToken);

    private static float[] Pick(System.Collections.Generic.Dictionary<string, float[]> map, string name)
    {
        if (map.TryGetValue(name, out var exact))
        {
            return exact;
        }

        foreach (var kv in map)
        {
            if (kv.Key.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }

        throw new InvalidOperationException($"DeepFilterNet model output '{name}' not found.");
    }

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
