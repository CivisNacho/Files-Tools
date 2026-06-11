using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Files_Tools.Helpers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Files_Tools.Services;

/// <summary>
/// Defines local DTLN-backed denoise operations for standalone audio files and video audio streams.
/// </summary>
public interface IVideoAudioDenoiseService
{
    /// <summary>
    /// Applies the configured denoise strategy to a standalone audio file and writes a processed audio file.
    /// </summary>
    Task<DenoiseResult> DenoiseAudioAsync(
        string inputAudioPath,
        string outputAudioPath,
        AudioDenoiseOptions options,
        IProgress<DenoiseProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a video audio stream, denoises it, and remuxes the processed audio into a new video file.
    /// </summary>
    Task<DenoiseResult> DenoiseVideoAudioAsync(
        string inputVideoPath,
        string outputVideoPath,
        VideoAudioDenoiseOptions options,
        IProgress<DenoiseProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads audio stream metadata using FFprobe so callers can preflight denoise options.
    /// </summary>
    Task<AudioProbeResult> ProbeAudioAsync(
        string inputPath,
        int? audioStreamIndex = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter contract for DTLN model inference over mono float PCM at the model sample rate.
/// </summary>
public interface IDtlnDenoiseEngine
{
    /// <summary>
    /// Returns a denoised mono signal with the same number of samples as the input signal.
    /// </summary>
    Task<float[]> DenoiseMonoAsync(
        IReadOnlyList<float> samples,
        int sampleRate,
        IProgress<InferenceProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Controls how source channels are prepared and reconstructed around DTLN inference.
/// </summary>
public enum AudioDenoiseMode
{
    /// <summary>
    /// Converts the source to mono, denoises that mono signal, and outputs mono audio.
    /// </summary>
    Mono,

    /// <summary>
    /// Legacy stereo-preserving mode. Requests are treated as <see cref="StrongStereo"/> for compatibility.
    /// </summary>
    MidSide,

    /// <summary>
    /// Preserves stereo channel count while denoising left and right independently.
    /// </summary>
    StrongStereo
}

/// <summary>
/// Deterministic stages used for traceable weighted denoise progress reporting.
/// </summary>
public enum DenoiseProcessingStage
{
    Probing,
    ExtractingAudio,
    DecodingAudio,
    ResamplingModelInput,
    PreparingMidSide,
    RunningInference,
    Blending,
    ReconstructingStereo,
    PreventingClipping,
    ResamplingOutput,
    EncodingAudio,
    RemuxingVideo,
    CleaningTemporaryFiles,
    Finalizing,
    Completed,
    Cancelling
}

/// <summary>
/// Options shared by standalone audio denoise and video audio denoise operations.
/// </summary>
public class AudioDenoiseOptions
{
    /// <summary>
    /// Channel strategy used by the denoise pipeline.
    /// </summary>
    public AudioDenoiseMode Mode { get; init; } = AudioDenoiseMode.Mono;

    /// <summary>
    /// Denoised signal blend amount from 0 to 100.
    /// </summary>
    public int DenoiseAmount { get; init; } = 100;

    /// <summary>
    /// Number of DTLN inference passes to apply. V1 supports 1 through 3; additional passes can suppress stronger steady noise at the cost of more artifacts.
    /// </summary>
    public int DenoisePasses { get; init; } = 1;

    /// <summary>
    /// Sample rate required by the DTLN model. The current service accepts 16000 Hz.
    /// </summary>
    public int ModelSampleRate { get; init; } = 16000;

    /// <summary>
    /// Final delivery sample rate. When null, the service preserves the source sample rate when possible.
    /// </summary>
    public int? OutputSampleRate { get; init; }

    /// <summary>
    /// Reduces final peak level to a predictable headroom target when needed.
    /// </summary>
    public bool NormalizePeak { get; init; } = true;

    /// <summary>
    /// Applies gain reduction when processed samples would clip.
    /// </summary>
    public bool PreventClipping { get; init; } = true;

    /// <summary>
    /// Keeps intermediate WAV files on disk for debugging instead of deleting them after processing.
    /// </summary>
    public bool KeepTemporaryFiles { get; init; }
}

/// <summary>
/// Options used when denoising a selected audio stream and remuxing it into a video container.
/// </summary>
public sealed class VideoAudioDenoiseOptions : AudioDenoiseOptions
{
    /// <summary>
    /// Absolute FFmpeg audio stream index to process. When null, the first audio stream is used.
    /// </summary>
    public int? AudioStreamIndex { get; init; }

    /// <summary>
    /// Copies the original video stream without re-encoding when true.
    /// </summary>
    public bool CopyVideoStream { get; init; } = true;

    /// <summary>
    /// FFmpeg audio encoder name for the remuxed video, such as aac, libopus, or pcm_s16le.
    /// </summary>
    public string? OutputAudioCodec { get; init; }

    /// <summary>
    /// Optional output audio bitrate in kilobits per second.
    /// </summary>
    public int? OutputAudioBitrateKbps { get; init; }
}

/// <summary>
/// Audio stream metadata collected before denoise processing begins.
/// </summary>
public sealed class AudioProbeResult
{
    /// <summary>
    /// Source audio sample rate in hertz.
    /// </summary>
    public int SampleRate { get; init; }

    /// <summary>
    /// Number of source channels reported by FFprobe.
    /// </summary>
    public int Channels { get; init; }

    /// <summary>
    /// FFprobe channel layout name when available.
    /// </summary>
    public string? ChannelLayout { get; init; }

    /// <summary>
    /// Source audio codec name when available.
    /// </summary>
    public string? CodecName { get; init; }

    /// <summary>
    /// Stream or container duration when available.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Source audio bitrate in kilobits per second when available.
    /// </summary>
    public int? BitrateKbps { get; init; }
}

/// <summary>
/// Metadata returned after a denoise operation completes.
/// </summary>
public sealed class DenoiseResult
{
    /// <summary>
    /// Absolute path to the generated output file.
    /// </summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>
    /// Denoise mode that was executed.
    /// </summary>
    public AudioDenoiseMode Mode { get; init; }

    /// <summary>
    /// Sample rate used for DTLN model inference.
    /// </summary>
    public int ModelSampleRate { get; init; }

    /// <summary>
    /// Final output sample rate.
    /// </summary>
    public int OutputSampleRate { get; init; }

    /// <summary>
    /// Final output channel count.
    /// </summary>
    public int OutputChannels { get; init; }

    /// <summary>
    /// Processed media duration when known.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Indicates whether the video stream was copied during a video remux operation.
    /// </summary>
    public bool VideoStreamCopied { get; init; }

    /// <summary>
    /// Non-fatal notes about channel conversion, resampling, or stereo preservation choices.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Live progress snapshot for long-running denoise work.
/// </summary>
public sealed class DenoiseProgress
{
    /// <summary>
    /// Current deterministic processing stage.
    /// </summary>
    public DenoiseProcessingStage Stage { get; init; }

    /// <summary>
    /// Weighted overall progress from 0.0 to 1.0.
    /// </summary>
    public double OverallPercent { get; init; }

    /// <summary>
    /// Progress inside the current stage from 0.0 to 1.0.
    /// </summary>
    public double StagePercent { get; init; }

    /// <summary>
    /// User-facing description of the current operation.
    /// </summary>
    public string StageDescription { get; init; } = string.Empty;

    /// <summary>
    /// Processed media duration for stages that can report timestamp progress.
    /// </summary>
    public TimeSpan? ProcessedDuration { get; init; }

    /// <summary>
    /// Total media duration when known.
    /// </summary>
    public TimeSpan? TotalDuration { get; init; }

    /// <summary>
    /// Indicates that model inference is currently active.
    /// </summary>
    public bool IsInferenceActive { get; init; }

    /// <summary>
    /// Indicates that FFmpeg or FFprobe is currently active.
    /// </summary>
    public bool IsFfmpegActive { get; init; }

    /// <summary>
    /// Optional current processing rate expressed as audio/video seconds per wall-clock second.
    /// </summary>
    public double? CurrentFpsEquivalent { get; init; }

    /// <summary>
    /// Estimated time remaining when enough progress information exists.
    /// </summary>
    public TimeSpan? EstimatedRemainingTime { get; init; }
}

/// <summary>
/// Granular model inference progress reported by an <see cref="IDtlnDenoiseEngine"/>.
/// </summary>
public sealed class InferenceProgressInfo
{
    /// <summary>
    /// Number of model frames or samples processed so far.
    /// </summary>
    public long ProcessedFrames { get; init; }

    /// <summary>
    /// Total number of model frames or samples expected.
    /// </summary>
    public long TotalFrames { get; init; }

    /// <summary>
    /// Inference progress from 0.0 to 1.0.
    /// </summary>
    public double Percent { get; init; }
}

/// <summary>
/// Thrown when caller-provided denoise options are invalid.
/// </summary>
public class DenoiseValidationException : ArgumentException
{
    /// <summary>
    /// Creates an exception describing an invalid denoise option.
    /// </summary>
    public DenoiseValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when the input media cannot support the requested denoise mode.
/// </summary>
public class DenoiseUnsupportedMediaException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception describing unsupported input media.
    /// </summary>
    public DenoiseUnsupportedMediaException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when the configured DTLN model or inference engine cannot process the request.
/// </summary>
public class DenoiseModelException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception describing a model loading or inference failure.
    /// </summary>
    public DenoiseModelException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when FFmpeg conversion, internal PCM processing, or file system work fails.
/// </summary>
public class DenoiseProcessingException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception describing a denoise processing failure.
    /// </summary>
    public DenoiseProcessingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when processed audio cannot be remuxed back into the requested video output.
/// </summary>
public class DenoiseRemuxException : DenoiseProcessingException
{
    /// <summary>
    /// Creates an exception describing a final video remux failure.
    /// </summary>
    public DenoiseRemuxException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Captures rich FFmpeg or FFprobe process failure details for diagnostics.
/// </summary>
public sealed class DenoiseProcessException : DenoiseProcessingException
{
    /// <summary>
    /// Creates an exception for a failed FFmpeg or FFprobe invocation.
    /// </summary>
    public DenoiseProcessException(
        string message,
        string binaryPath,
        string commandLine,
        int? exitCode,
        string standardOutput,
        string standardError,
        Exception? innerException = null)
        : base(message, innerException)
    {
        BinaryPath = binaryPath;
        CommandLine = commandLine;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>
    /// Executed binary path or executable name.
    /// </summary>
    public string BinaryPath { get; }

    /// <summary>
    /// Fully formatted command line used for the failed process.
    /// </summary>
    public string CommandLine { get; }

    /// <summary>
    /// Process exit code when available.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>
    /// Captured standard output.
    /// </summary>
    public string StandardOutput { get; }

    /// <summary>
    /// Captured standard error.
    /// </summary>
    public string StandardError { get; }
}

/// <summary>
/// Placeholder engine used when no DTLN ONNX adapter has been supplied.
/// </summary>
public sealed class MissingDtlnDenoiseEngine : IDtlnDenoiseEngine
{
    /// <summary>
    /// Always throws to make missing DTLN model wiring explicit.
    /// </summary>
    public Task<float[]> DenoiseMonoAsync(
        IReadOnlyList<float> samples,
        int sampleRate,
        IProgress<InferenceProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new DenoiseModelException(
            "No DTLN denoise engine is configured. Provide an IDtlnDenoiseEngine implementation backed by the selected DTLN ONNX model.");
    }
}

/// <summary>
/// ONNX Runtime implementation of the two-stage DTLN denoise model.
/// </summary>
public sealed class DtlnOnnxDenoiseEngine : IDtlnDenoiseEngine, IDisposable
{
    private const int DtlnSampleRate = 16000;
    private const int BlockLength = 512;
    private const int BlockShift = 128;
    private const int SpectrumLength = (BlockLength / 2) + 1;

    private readonly InferenceSession _model1;
    private readonly InferenceSession _model2;
    private readonly ModelBindings _model1Bindings;
    private readonly ModelBindings _model2Bindings;
    private bool _disposed;

    /// <summary>
    /// Creates a DTLN engine from explicit model paths.
    /// </summary>
    public DtlnOnnxDenoiseEngine(string model1Path, string model2Path)
    {
        ValidateModelPath(model1Path, nameof(model1Path));
        ValidateModelPath(model2Path, nameof(model2Path));

        try
        {
            _model1 = new InferenceSession(model1Path);
            _model2 = new InferenceSession(model2Path);
            _model1Bindings = CreateBindings(_model1, SpectrumLength, "model_1.onnx");
            _model2Bindings = CreateBindings(_model2, BlockLength, "model_2.onnx");
        }
        catch (DenoiseModelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DenoiseModelException("Failed to initialize DTLN ONNX Runtime sessions.", ex);
        }
    }

    /// <summary>
    /// Creates an engine from bundled model files in Assets/Models/Dtln under the app base directory.
    /// </summary>
    public static DtlnOnnxDenoiseEngine CreateDefault()
    {
        var modelRoot = ResolveDefaultModelRoot();
        return new DtlnOnnxDenoiseEngine(
            Path.Combine(modelRoot, "model_1.onnx"),
            Path.Combine(modelRoot, "model_2.onnx"));
    }

    /// <inheritdoc />
    public Task<float[]> DenoiseMonoAsync(
        IReadOnlyList<float> samples,
        int sampleRate,
        IProgress<InferenceProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(samples);

        if (sampleRate != DtlnSampleRate)
        {
            throw new DenoiseModelException("DTLN ONNX inference requires 16000 Hz mono input.");
        }

        return Task.Run(() => DenoiseMono(samples, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Releases ONNX Runtime sessions.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _model1.Dispose();
        _model2.Dispose();
        _disposed = true;
    }

    private float[] DenoiseMono(
        IReadOnlyList<float> samples,
        IProgress<InferenceProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var output = new float[samples.Count];
        if (samples.Count == 0)
        {
            progress?.Report(new InferenceProgressInfo { ProcessedFrames = 0, TotalFrames = 0, Percent = 1 });
            return output;
        }

        var inputBuffer = new float[BlockLength];
        var outputBuffer = new float[BlockLength];
        var model1State = CreateInitialStates(_model1Bindings);
        var model2State = CreateInitialStates(_model2Bindings);
        var totalBlocks = Math.Max(1, (samples.Count - (BlockLength - BlockShift)) / BlockShift);
        if (samples.Count <= BlockLength)
        {
            totalBlocks = (int)Math.Ceiling(samples.Count / (double)BlockShift);
        }

        for (var blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Array.Copy(inputBuffer, BlockShift, inputBuffer, 0, BlockLength - BlockShift);
            Array.Clear(inputBuffer, BlockLength - BlockShift, BlockShift);

            var inputStart = blockIndex * BlockShift;
            var copiedSamples = Math.Min(BlockShift, samples.Count - inputStart);
            for (var i = 0; i < copiedSamples; i++)
            {
                inputBuffer[BlockLength - BlockShift + i] = samples[inputStart + i];
            }

            var spectrum = RealFft(inputBuffer);
            var magnitude = new float[SpectrumLength];
            var phase = new double[SpectrumLength];
            for (var i = 0; i < SpectrumLength; i++)
            {
                magnitude[i] = (float)spectrum[i].Magnitude;
                phase[i] = Math.Atan2(spectrum[i].Imaginary, spectrum[i].Real);
            }

            var model1Outputs = RunModel(_model1, _model1Bindings, magnitude, model1State);
            var mask = model1Outputs[0].Data;
            UpdateStates(model1State, model1Outputs, stateOutputOffset: 1);

            var estimatedSpectrum = new Complex[SpectrumLength];
            for (var i = 0; i < SpectrumLength; i++)
            {
                var estimatedMagnitude = magnitude[i] * mask[i];
                estimatedSpectrum[i] = Complex.FromPolarCoordinates(estimatedMagnitude, phase[i]);
            }

            var estimatedBlock = RealIfft(estimatedSpectrum);
            var model2Outputs = RunModel(_model2, _model2Bindings, estimatedBlock, model2State);
            var refinedBlock = model2Outputs[0].Data;
            UpdateStates(model2State, model2Outputs, stateOutputOffset: 1);

            Array.Copy(outputBuffer, BlockShift, outputBuffer, 0, BlockLength - BlockShift);
            Array.Clear(outputBuffer, BlockLength - BlockShift, BlockShift);
            for (var i = 0; i < Math.Min(BlockLength, refinedBlock.Length); i++)
            {
                outputBuffer[i] += refinedBlock[i];
            }

            var writeCount = Math.Min(BlockShift, samples.Count - inputStart);
            for (var i = 0; i < writeCount; i++)
            {
                output[inputStart + i] = outputBuffer[i];
            }

            progress?.Report(new InferenceProgressInfo
            {
                ProcessedFrames = Math.Min(blockIndex + 1, totalBlocks),
                TotalFrames = totalBlocks,
                Percent = Math.Clamp((blockIndex + 1) / (double)totalBlocks, 0d, 1d)
            });
        }

        return output;
    }

    private static List<TensorBuffer> RunModel(
        InferenceSession session,
        ModelBindings bindings,
        float[] primaryInput,
        List<TensorBuffer> stateInputs)
    {
        var values = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(
                bindings.PrimaryInput.Name,
                new DenseTensor<float>(primaryInput, bindings.PrimaryInput.Dimensions))
        };

        for (var i = 0; i < stateInputs.Count; i++)
        {
            values.Add(NamedOnnxValue.CreateFromTensor(
                bindings.StateInputs[i].Name,
                new DenseTensor<float>(stateInputs[i].Data, stateInputs[i].Dimensions)));
        }

        using var results = session.Run(values);
        return results
            .Select(result =>
            {
                var tensor = result.AsTensor<float>();
                return new TensorBuffer(tensor.ToArray(), tensor.Dimensions.ToArray());
            })
            .ToList();
    }

    private static void UpdateStates(List<TensorBuffer> states, IReadOnlyList<TensorBuffer> outputs, int stateOutputOffset)
    {
        for (var i = 0; i < states.Count; i++)
        {
            var outputIndex = stateOutputOffset + i;
            if (outputIndex >= outputs.Count)
            {
                throw new DenoiseModelException("DTLN model did not return enough state outputs.");
            }

            states[i] = outputs[outputIndex];
        }
    }

    private static List<TensorBuffer> CreateInitialStates(ModelBindings bindings)
    {
        return bindings.StateInputs
            .Select(input => new TensorBuffer(new float[input.ElementCount], input.Dimensions))
            .ToList();
    }

    private static ModelBindings CreateBindings(InferenceSession session, int expectedPrimaryLength, string modelName)
    {
        var inputs = session.InputMetadata
            .Select(pair => TensorBinding.FromMetadata(pair.Key, pair.Value.Dimensions))
            .ToList();

        if (inputs.Count < 2)
        {
            throw new DenoiseModelException($"{modelName} must expose a primary input and at least one state input.");
        }

        var primary = inputs.FirstOrDefault(input => input.ElementCount == expectedPrimaryLength) ?? inputs[0];
        if (primary.ElementCount != expectedPrimaryLength)
        {
            throw new DenoiseModelException($"{modelName} primary input must contain {expectedPrimaryLength} float values.");
        }

        var stateInputs = inputs.Where(input => !ReferenceEquals(input, primary)).ToList();
        return new ModelBindings(primary, stateInputs);
    }

    private static Complex[] RealFft(float[] samples)
    {
        var values = new Complex[BlockLength];
        for (var i = 0; i < samples.Length; i++)
        {
            values[i] = new Complex(samples[i], 0);
        }

        Fft(values, inverse: false);
        return values.Take(SpectrumLength).ToArray();
    }

    private static float[] RealIfft(IReadOnlyList<Complex> spectrum)
    {
        var values = new Complex[BlockLength];
        values[0] = spectrum[0];
        for (var i = 1; i < SpectrumLength - 1; i++)
        {
            values[i] = spectrum[i];
            values[BlockLength - i] = Complex.Conjugate(spectrum[i]);
        }

        values[SpectrumLength - 1] = spectrum[SpectrumLength - 1];
        Fft(values, inverse: true);

        var result = new float[BlockLength];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (float)values[i].Real;
        }

        return result;
    }

    private static void Fft(Complex[] buffer, bool inverse)
    {
        var n = buffer.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = 2 * Math.PI / length * (inverse ? 1 : -1);
            var root = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;
                for (var j = 0; j < length / 2; j++)
                {
                    var u = buffer[i + j];
                    var v = buffer[i + j + (length / 2)] * w;
                    buffer[i + j] = u + v;
                    buffer[i + j + (length / 2)] = u - v;
                    w *= root;
                }
            }
        }

        if (!inverse)
        {
            return;
        }

        for (var i = 0; i < n; i++)
        {
            buffer[i] /= n;
        }
    }

    private static void ValidateModelPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Model path cannot be null or whitespace.", parameterName);
        }

        if (!File.Exists(path))
        {
            throw new DenoiseModelException($"DTLN model file was not found: {path}");
        }
    }

    private static string ResolveDefaultModelRoot()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Dtln");
        if (HasDtlnModelFiles(outputRoot))
        {
            return outputRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Assets", "Models", "Dtln");
            if (HasDtlnModelFiles(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return outputRoot;
    }

    private static bool HasDtlnModelFiles(string root)
    {
        return File.Exists(Path.Combine(root, "model_1.onnx")) &&
            File.Exists(Path.Combine(root, "model_2.onnx"));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DtlnOnnxDenoiseEngine));
        }
    }

    private sealed record ModelBindings(TensorBinding PrimaryInput, List<TensorBinding> StateInputs);

    private sealed record TensorBinding(string Name, int[] Dimensions)
    {
        public int ElementCount { get; } = Dimensions.Aggregate(1, (product, value) => product * value);

        public static TensorBinding FromMetadata(string name, IReadOnlyList<int> dimensions)
        {
            var normalized = dimensions.Select(dimension => dimension > 0 ? dimension : 1).ToArray();
            return new TensorBinding(name, normalized);
        }
    }

    private sealed record TensorBuffer(float[] Data, int[] Dimensions)
    {
        public int ElementCount => Data.Length;
    }
}

/// <summary>
/// FFmpeg-backed media pipeline that prepares audio for DTLN denoise and writes audio or video outputs.
/// </summary>
public class VideoAudioDenoiseService : IVideoAudioDenoiseService
{
    private const int DtlnSampleRate = 16000;
    private const string FfprobeJsonArgs = "-v error -print_format json -show_streams -show_format";

    private static readonly IReadOnlyDictionary<DenoiseProcessingStage, double> StageWeights =
        new Dictionary<DenoiseProcessingStage, double>
        {
            [DenoiseProcessingStage.Probing] = 2,
            [DenoiseProcessingStage.ExtractingAudio] = 8,
            [DenoiseProcessingStage.DecodingAudio] = 4,
            [DenoiseProcessingStage.ResamplingModelInput] = 6,
            [DenoiseProcessingStage.PreparingMidSide] = 2,
            [DenoiseProcessingStage.RunningInference] = 55,
            [DenoiseProcessingStage.Blending] = 2,
            [DenoiseProcessingStage.ReconstructingStereo] = 2,
            [DenoiseProcessingStage.PreventingClipping] = 1,
            [DenoiseProcessingStage.ResamplingOutput] = 4,
            [DenoiseProcessingStage.EncodingAudio] = 6,
            [DenoiseProcessingStage.RemuxingVideo] = 6,
            [DenoiseProcessingStage.CleaningTemporaryFiles] = 1,
            [DenoiseProcessingStage.Finalizing] = 1
        };

    private readonly IDtlnDenoiseEngine _denoiseEngine;

    public VideoAudioDenoiseService()
        : this(DtlnOnnxDenoiseEngine.CreateDefault())
    {
    }

    /// <summary>
    /// Creates a denoise service that delegates model inference to the supplied DTLN engine.
    /// </summary>
    public VideoAudioDenoiseService(IDtlnDenoiseEngine denoiseEngine)
    {
        _denoiseEngine = denoiseEngine ?? throw new ArgumentNullException(nameof(denoiseEngine));
    }

    /// <inheritdoc />
    public async Task<DenoiseResult> DenoiseAudioAsync(
        string inputAudioPath,
        string outputAudioPath,
        AudioDenoiseOptions options,
        IProgress<DenoiseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputAudioPath);
        ValidateOutputPath(outputAudioPath);
        ValidateOptions(options);

        var tempFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            Report(progress, DenoiseProcessingStage.Probing, 0, "Probing audio", null, null, false, true);
            var probe = await ProbeAudioAsync(inputAudioPath, null, cancellationToken).ConfigureAwait(false);
            Report(progress, DenoiseProcessingStage.Probing, 1, "Audio metadata ready", probe.Duration, probe.Duration, false, false);

            var targetOutputRate = options.OutputSampleRate ?? probe.SampleRate;
            AddCommonWarnings(warnings, probe, options, targetOutputRate);

            var processedModelAudio = await DecodeProcessAndWriteModelAudioAsync(
                inputAudioPath,
                null,
                probe,
                options,
                targetOutputRate,
                tempFiles,
                warnings,
                progress,
                cancellationToken).ConfigureAwait(false);

            Report(progress, DenoiseProcessingStage.EncodingAudio, 0, "Encoding output audio", null, probe.Duration, false, true);
            await EncodeAudioAsync(processedModelAudio.Path, outputAudioPath, targetOutputRate, options, null, progress, probe.Duration, cancellationToken).ConfigureAwait(false);
            Report(progress, DenoiseProcessingStage.Finalizing, 1, "Audio denoise completed", probe.Duration, probe.Duration, false, false);

            return new DenoiseResult
            {
                OutputPath = Path.GetFullPath(outputAudioPath),
                Mode = options.Mode,
                ModelSampleRate = options.ModelSampleRate,
                OutputSampleRate = targetOutputRate,
                OutputChannels = processedModelAudio.Channels,
                Duration = probe.Duration,
                VideoStreamCopied = false,
                Warnings = warnings
            };
        }
        finally
        {
            CleanupTemporaryFiles(tempFiles, options.KeepTemporaryFiles, progress);
        }
    }

    /// <inheritdoc />
    public async Task<DenoiseResult> DenoiseVideoAudioAsync(
        string inputVideoPath,
        string outputVideoPath,
        VideoAudioDenoiseOptions options,
        IProgress<DenoiseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateInputPath(inputVideoPath);
        ValidateOutputPath(outputVideoPath);
        ValidateOptions(options);

        var tempFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            Report(progress, DenoiseProcessingStage.Probing, 0, "Probing video audio", null, null, false, true);
            var probe = await ProbeAudioAsync(inputVideoPath, options.AudioStreamIndex, cancellationToken).ConfigureAwait(false);
            Report(progress, DenoiseProcessingStage.Probing, 1, "Video audio metadata ready", probe.Duration, probe.Duration, false, false);

            var targetOutputRate = options.OutputSampleRate ?? probe.SampleRate;
            AddCommonWarnings(warnings, probe, options, targetOutputRate);

            var processedModelAudio = await DecodeProcessAndWriteModelAudioAsync(
                inputVideoPath,
                options.AudioStreamIndex,
                probe,
                options,
                targetOutputRate,
                tempFiles,
                warnings,
                progress,
                cancellationToken).ConfigureAwait(false);

            Report(progress, DenoiseProcessingStage.RemuxingVideo, 0, "Remuxing video with processed audio", null, probe.Duration, false, true);
            await RemuxVideoAsync(inputVideoPath, processedModelAudio.Path, outputVideoPath, targetOutputRate, options, progress, probe.Duration, cancellationToken).ConfigureAwait(false);
            Report(progress, DenoiseProcessingStage.Completed, 1, "Video denoise completed", probe.Duration, probe.Duration, false, false);

            return new DenoiseResult
            {
                OutputPath = Path.GetFullPath(outputVideoPath),
                Mode = options.Mode,
                ModelSampleRate = options.ModelSampleRate,
                OutputSampleRate = targetOutputRate,
                OutputChannels = processedModelAudio.Channels,
                Duration = probe.Duration,
                VideoStreamCopied = options.CopyVideoStream,
                Warnings = warnings
            };
        }
        finally
        {
            CleanupTemporaryFiles(tempFiles, options.KeepTemporaryFiles, progress);
        }
    }

    /// <inheritdoc />
    public async Task<AudioProbeResult> ProbeAudioAsync(
        string inputPath,
        int? audioStreamIndex = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        if (audioStreamIndex is < 0)
        {
            throw new DenoiseValidationException("Audio stream index must be greater than or equal to 0.");
        }

        var ffprobeCandidates = FfmpegLocator.ResolveExecutableCandidates("ffprobe");
        var args = new List<string>(SplitArguments(FfprobeJsonArgs))
        {
            Path.GetFullPath(inputPath)
        };

        var result = await RunProcessWithFallbackAsync(ffprobeCandidates, args, cancellationToken, null).ConfigureAwait(false);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var audioStreams = root.GetProperty("streams")
            .EnumerateArray()
            .Where(stream => string.Equals(TryGetString(stream, "codec_type"), "audio", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (audioStreams.Count == 0)
        {
            throw new DenoiseUnsupportedMediaException("Input file does not contain a readable audio stream.");
        }

        var targetStream = audioStreamIndex.HasValue
            ? audioStreams.FirstOrDefault(stream => TryGetInt(stream, "index") == audioStreamIndex.Value)
            : audioStreams[0];

        if (targetStream.ValueKind == JsonValueKind.Undefined)
        {
            throw new DenoiseUnsupportedMediaException($"Audio stream index {audioStreamIndex} was not found.");
        }

        var sampleRate = TryGetInt(targetStream, "sample_rate") ?? 0;
        var channels = TryGetInt(targetStream, "channels") ?? 0;
        if (sampleRate <= 0 || channels <= 0)
        {
            throw new DenoiseUnsupportedMediaException("Audio stream is missing sample rate or channel metadata.");
        }

        var duration = TryGetDuration(targetStream);
        if (duration is null && root.TryGetProperty("format", out var formatElement))
        {
            duration = TryGetDuration(formatElement);
        }

        return new AudioProbeResult
        {
            SampleRate = sampleRate,
            Channels = channels,
            ChannelLayout = TryGetString(targetStream, "channel_layout"),
            CodecName = TryGetString(targetStream, "codec_name"),
            Duration = duration,
            BitrateKbps = TryGetLong(targetStream, "bit_rate") is long bitrate ? (int)Math.Max(1, bitrate / 1000) : null
        };
    }

    private async Task<WaveAudio> DecodeProcessAndWriteModelAudioAsync(
        string inputPath,
        int? audioStreamIndex,
        AudioProbeResult probe,
        AudioDenoiseOptions options,
        int outputSampleRate,
        List<string> tempFiles,
        List<string> warnings,
        IProgress<DenoiseProgress>? progress,
        CancellationToken cancellationToken)
    {
        var modelInputChannels = options.Mode == AudioDenoiseMode.Mono ? 1 : 2;
        var modelInputPath = CreateTempFile(tempFiles, ".wav");

        Report(progress, DenoiseProcessingStage.ExtractingAudio, 0, "Extracting and preparing model input audio", null, probe.Duration, false, true);
        await DecodeToModelWavAsync(inputPath, audioStreamIndex, modelInputPath, options.ModelSampleRate, modelInputChannels, progress, probe.Duration, cancellationToken).ConfigureAwait(false);

        Report(progress, DenoiseProcessingStage.DecodingAudio, 0, "Reading model input audio", null, probe.Duration, false, false);
        var modelInput = ReadPcm16Wave(modelInputPath);

        var processed = options.Mode switch
        {
            AudioDenoiseMode.Mono => await ProcessMonoAsync(modelInput, options, progress, cancellationToken).ConfigureAwait(false),
            AudioDenoiseMode.MidSide => await ProcessStrongStereoAsync(modelInput, options, progress, cancellationToken).ConfigureAwait(false),
            AudioDenoiseMode.StrongStereo => await ProcessStrongStereoAsync(modelInput, options, progress, cancellationToken).ConfigureAwait(false),
            _ => throw new DenoiseValidationException("Unsupported denoise mode.")
        };

        if (options.PreventClipping || options.NormalizePeak)
        {
            Report(progress, DenoiseProcessingStage.PreventingClipping, 0, "Applying clipping protection", null, probe.Duration, false, false);
            ApplyPeakPolicy(processed.Samples, options.PreventClipping, options.NormalizePeak);
            Report(progress, DenoiseProcessingStage.PreventingClipping, 1, "Clipping protection complete", null, probe.Duration, false, false);
        }

        var processedModelPath = CreateTempFile(tempFiles, ".wav");
        WritePcm16Wave(processedModelPath, processed);

        if (outputSampleRate != options.ModelSampleRate)
        {
            warnings.Add($"Output audio was resampled from {options.ModelSampleRate} Hz to {outputSampleRate} Hz.");
        }

        return processed with { Path = processedModelPath };
    }

    private async Task<WaveAudio> ProcessMonoAsync(
        WaveAudio modelInput,
        AudioDenoiseOptions options,
        IProgress<DenoiseProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (modelInput.Channels != 1)
        {
            throw new DenoiseProcessingException("Mono denoise expected mono model input audio.");
        }

        var clean = await RunDenoiseAsync(modelInput.Samples, options.ModelSampleRate, options.DenoisePasses, progress, cancellationToken).ConfigureAwait(false);
        var output = BlendSignals(modelInput.Samples, clean, options.DenoiseAmount / 100f);
        return new WaveAudio(output, options.ModelSampleRate, 1);
    }

    private async Task<WaveAudio> ProcessStrongStereoAsync(
        WaveAudio modelInput,
        AudioDenoiseOptions options,
        IProgress<DenoiseProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (modelInput.Channels != 2)
        {
            throw new DenoiseUnsupportedMediaException("Strong stereo denoise requires stereo model input audio.");
        }

        var frames = modelInput.Samples.Length / 2;
        var left = new float[frames];
        var right = new float[frames];

        for (var frame = 0; frame < frames; frame++)
        {
            left[frame] = modelInput.Samples[frame * 2];
            right[frame] = modelInput.Samples[frame * 2 + 1];
        }

        var cleanLeft = await RunDenoiseAsync(left, options.ModelSampleRate, options.DenoisePasses, progress, cancellationToken, 0d, 0.5d, "Running DTLN inference on left channel").ConfigureAwait(false);
        var cleanRight = await RunDenoiseAsync(right, options.ModelSampleRate, options.DenoisePasses, progress, cancellationToken, 0.5d, 0.5d, "Running DTLN inference on right channel").ConfigureAwait(false);
        var amount = options.DenoiseAmount / 100f;

        Report(progress, DenoiseProcessingStage.Blending, 0, "Blending denoised stereo channels", null, null, false, false);
        var output = new float[modelInput.Samples.Length];
        for (var frame = 0; frame < frames; frame++)
        {
            output[frame * 2] = (left[frame] * (1f - amount)) + (cleanLeft[frame] * amount);
            output[frame * 2 + 1] = (right[frame] * (1f - amount)) + (cleanRight[frame] * amount);
        }

        Report(progress, DenoiseProcessingStage.Blending, 1, "Stereo channel blend complete", null, null, false, false);
        return new WaveAudio(output, options.ModelSampleRate, 2);
    }

    private async Task<float[]> RunDenoiseAsync(
        IReadOnlyList<float> samples,
        int sampleRate,
        int denoisePasses,
        IProgress<DenoiseProgress>? progress,
        CancellationToken cancellationToken,
        double stageProgressOffset = 0d,
        double stageProgressScale = 1d,
        string stageDescription = "Running DTLN inference")
    {
        var totalSamples = samples.Count;
        IReadOnlyList<float> current = samples;
        float[] result = [];
        for (var pass = 0; pass < denoisePasses; pass++)
        {
            var passOffset = stageProgressOffset + (stageProgressScale * pass / denoisePasses);
            var passScale = stageProgressScale / denoisePasses;
            var etaEstimator = new EtaEstimator();
            var passDescription = denoisePasses == 1 ? stageDescription : $"{stageDescription} (pass {pass + 1} of {denoisePasses})";
            var inferenceProgress = new Progress<InferenceProgressInfo>(info =>
            {
                var percent = Math.Clamp(info.Percent, 0d, 1d);
                var scaledPercent = Math.Clamp(passOffset + (percent * passScale), 0d, 1d);
                var eta = percent < 1d ? etaEstimator.AddSample(percent) : TimeSpan.Zero;

                Report(
                    progress,
                    DenoiseProcessingStage.RunningInference,
                    scaledPercent,
                    passDescription,
                    TimeSpan.FromSeconds(totalSamples == 0 ? 0 : (totalSamples * percent) / sampleRate),
                    TimeSpan.FromSeconds(totalSamples / (double)sampleRate),
                    true,
                    false,
                    eta);
            });

            Report(progress, DenoiseProcessingStage.RunningInference, passOffset, passDescription, TimeSpan.Zero, TimeSpan.FromSeconds(totalSamples / (double)sampleRate), true, false);
            result = await _denoiseEngine.DenoiseMonoAsync(current, sampleRate, inferenceProgress, cancellationToken).ConfigureAwait(false);
            if (result.Length != samples.Count)
            {
                throw new DenoiseModelException("DTLN denoise engine returned a different sample count than it received.");
            }

            current = result;
        }

        Report(progress, DenoiseProcessingStage.RunningInference, Math.Clamp(stageProgressOffset + stageProgressScale, 0d, 1d), "DTLN inference complete", TimeSpan.FromSeconds(totalSamples / (double)sampleRate), TimeSpan.FromSeconds(totalSamples / (double)sampleRate), false, false);
        return result;
    }

    private static float[] BlendSignals(IReadOnlyList<float> original, IReadOnlyList<float> clean, float amount)
    {
        var clampedAmount = Math.Clamp(amount, 0f, 1f);
        var output = new float[original.Count];
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = (original[i] * (1f - clampedAmount)) + (clean[i] * clampedAmount);
        }

        return output;
    }

    private static async Task DecodeToModelWavAsync(
        string inputPath,
        int? audioStreamIndex,
        string outputWavPath,
        int sampleRate,
        int channels,
        IProgress<DenoiseProgress>? progress,
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-progress",
            "pipe:2",
            "-nostats",
            "-i",
            Path.GetFullPath(inputPath),
            "-map",
            audioStreamIndex.HasValue ? $"0:{audioStreamIndex.Value}" : "0:a:0",
            "-vn",
            "-ac",
            channels.ToString(CultureInfo.InvariantCulture),
            "-ar",
            sampleRate.ToString(CultureInfo.InvariantCulture),
            "-sample_fmt",
            "s16",
            "-c:a",
            "pcm_s16le",
            Path.GetFullPath(outputWavPath)
        };

        var observer = CreateFfmpegProgressObserver(DenoiseProcessingStage.ExtractingAudio, "Preparing model input audio", progress, duration);
        await RunProcessWithFallbackAsync(FfmpegLocator.ResolveExecutableCandidates("ffmpeg"), args, cancellationToken, observer).ConfigureAwait(false);
    }

    private static async Task EncodeAudioAsync(
        string inputWavPath,
        string outputPath,
        int outputSampleRate,
        AudioDenoiseOptions audioOptions,
        VideoAudioDenoiseOptions? videoOptions,
        IProgress<DenoiseProgress>? progress,
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-progress",
            "pipe:2",
            "-nostats",
            "-i",
            Path.GetFullPath(inputWavPath),
            "-ar",
            outputSampleRate.ToString(CultureInfo.InvariantCulture)
        };

        var codec = videoOptions?.OutputAudioCodec;
        if (!string.IsNullOrWhiteSpace(codec))
        {
            args.Add("-c:a");
            args.Add(codec);
        }

        if (videoOptions?.OutputAudioBitrateKbps is int bitrate)
        {
            args.Add("-b:a");
            args.Add(FormattableString.Invariant($"{bitrate}k"));
        }

        args.Add(Path.GetFullPath(outputPath));

        var observer = CreateFfmpegProgressObserver(DenoiseProcessingStage.EncodingAudio, "Encoding output audio", progress, duration);
        await RunProcessWithFallbackAsync(FfmpegLocator.ResolveExecutableCandidates("ffmpeg"), args, cancellationToken, observer).ConfigureAwait(false);
    }

    private static async Task RemuxVideoAsync(
        string inputVideoPath,
        string processedAudioPath,
        string outputVideoPath,
        int outputSampleRate,
        VideoAudioDenoiseOptions options,
        IProgress<DenoiseProgress>? progress,
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-progress",
            "pipe:2",
            "-nostats",
            "-i",
            Path.GetFullPath(inputVideoPath),
            "-i",
            Path.GetFullPath(processedAudioPath),
            "-map",
            "0:v:0",
            "-map",
            "1:a:0",
            "-map",
            "0:s?",
            "-c:v",
            options.CopyVideoStream ? "copy" : "libx264",
            "-c:a",
            ResolveVideoOutputAudioCodec(outputVideoPath, options.OutputAudioCodec),
            "-ar",
            outputSampleRate.ToString(CultureInfo.InvariantCulture),
            "-c:s",
            "copy",
            "-shortest",
            Path.GetFullPath(outputVideoPath)
        };

        if (options.OutputAudioBitrateKbps is int bitrate)
        {
            args.Insert(args.Count - 1, "-b:a");
            args.Insert(args.Count - 1, FormattableString.Invariant($"{bitrate}k"));
        }

        var observer = CreateFfmpegProgressObserver(DenoiseProcessingStage.RemuxingVideo, "Remuxing processed audio", progress, duration);
        try
        {
            await RunProcessWithFallbackAsync(FfmpegLocator.ResolveExecutableCandidates("ffmpeg"), args, cancellationToken, observer).ConfigureAwait(false);
        }
        catch (DenoiseProcessException ex)
        {
            throw new DenoiseRemuxException("Failed to remux processed audio into the output video.", ex);
        }
    }

    private static Action<string>? CreateFfmpegProgressObserver(
        DenoiseProcessingStage stage,
        string description,
        IProgress<DenoiseProgress>? progress,
        TimeSpan? duration)
    {
        if (progress is null || duration is null || duration <= TimeSpan.Zero)
        {
            return null;
        }

        var etaEstimator = new EtaEstimator();
        var lastProcessed = TimeSpan.Zero;

        return line =>
        {
            if (line.StartsWith("out_time=", StringComparison.Ordinal))
            {
                lastProcessed = ParseProgressTimestamp(line["out_time=".Length..]);
                return;
            }

            if (line.StartsWith("out_time_ms=", StringComparison.Ordinal) &&
                long.TryParse(line["out_time_ms=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var outTimeMs))
            {
                lastProcessed = TimeSpan.FromMilliseconds(outTimeMs / 1000d);
                return;
            }

            if (!line.StartsWith("progress=", StringComparison.Ordinal))
            {
                return;
            }

            var isCompleted = string.Equals(line["progress=".Length..], "end", StringComparison.Ordinal);
            var clampedProcessed = lastProcessed > duration.Value ? duration.Value : lastProcessed;
            var fraction = duration.Value.TotalMilliseconds <= 0
                ? 0d
                : Math.Clamp(clampedProcessed.TotalMilliseconds / duration.Value.TotalMilliseconds, 0d, 1d);

            var eta = etaEstimator.AddSample(isCompleted ? 1d : fraction);

            Report(progress, stage, isCompleted ? 1d : fraction, description, isCompleted ? duration : clampedProcessed, duration, false, true, eta);
        };
    }

    private static void ValidateOptions(AudioDenoiseOptions options)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            throw new DenoiseValidationException("Denoise mode must be valid.");
        }

        if (options.DenoiseAmount is < 0 or > 100)
        {
            throw new DenoiseValidationException("Denoise amount must be between 0 and 100.");
        }

        if (options.DenoisePasses is < 1 or > 3)
        {
            throw new DenoiseValidationException("Denoise passes must be between 1 and 3.");
        }

        if (options.ModelSampleRate <= 0)
        {
            throw new DenoiseValidationException("Model sample rate must be greater than 0.");
        }

        if (options.ModelSampleRate != DtlnSampleRate)
        {
            throw new DenoiseValidationException("DTLN model sample rate must be 16000 Hz.");
        }

        if (options.OutputSampleRate is <= 0)
        {
            throw new DenoiseValidationException("Output sample rate must be greater than 0.");
        }

        if (options is VideoAudioDenoiseOptions videoOptions)
        {
            if (videoOptions.AudioStreamIndex is < 0)
            {
                throw new DenoiseValidationException("Audio stream index must be greater than or equal to 0.");
            }

            if (videoOptions.OutputAudioBitrateKbps is <= 0)
            {
                throw new DenoiseValidationException("Output audio bitrate must be greater than 0.");
            }
        }
    }

    private static string ResolveVideoOutputAudioCodec(string outputVideoPath, string? requestedCodec)
    {
        if (!string.IsNullOrWhiteSpace(requestedCodec))
        {
            return requestedCodec;
        }

        return string.Equals(Path.GetExtension(outputVideoPath), ".webm", StringComparison.OrdinalIgnoreCase)
            ? "libopus"
            : "aac";
    }

    private static void AddCommonWarnings(
        List<string> warnings,
        AudioProbeResult probe,
        AudioDenoiseOptions options,
        int outputSampleRate)
    {
        if (options.Mode == AudioDenoiseMode.Mono && probe.Channels != 1)
        {
            warnings.Add("Input audio was converted to mono.");
        }

        if (options.Mode is AudioDenoiseMode.StrongStereo or AudioDenoiseMode.MidSide)
        {
            if (probe.Channels < 2)
            {
                throw new DenoiseUnsupportedMediaException("Strong stereo denoise requires stereo source audio.");
            }

            warnings.Add("Stereo was preserved by denoising left and right channels independently.");
        }

        if (probe.SampleRate != options.ModelSampleRate)
        {
            warnings.Add($"Input audio was resampled to {options.ModelSampleRate} Hz for DTLN.");
        }

        if (outputSampleRate != probe.SampleRate)
        {
            warnings.Add("Output sample rate differs from source sample rate.");
        }
    }

    private static void ApplyPeakPolicy(float[] samples, bool preventClipping, bool normalizePeak)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        if (peak <= 0f)
        {
            return;
        }

        var targetPeak = normalizePeak ? 0.98f : 1f;
        if ((preventClipping && peak > 1f) || (normalizePeak && peak > targetPeak))
        {
            var gain = targetPeak / peak;
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }
    }

    private static WaveAudio ReadPcm16Wave(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
        {
            throw new DenoiseProcessingException("WAV input is missing RIFF header.");
        }

        reader.ReadInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
        {
            throw new DenoiseProcessingException("WAV input is not a WAVE file.");
        }

        int? channels = null;
        int? sampleRate = null;
        short? bitsPerSample = null;
        byte[]? data = null;

        while (stream.Position < stream.Length)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadInt32();
            var chunkStart = stream.Position;

            if (chunkId == "fmt ")
            {
                var audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();

                if (audioFormat != 1 || bitsPerSample != 16)
                {
                    throw new DenoiseProcessingException("Only PCM 16-bit WAV files are supported for internal denoise buffers.");
                }
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }

            stream.Position = chunkStart + chunkSize + (chunkSize % 2);
        }

        if (channels is null || sampleRate is null || bitsPerSample is null || data is null)
        {
            throw new DenoiseProcessingException("WAV input is missing required format or data chunks.");
        }

        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i * 2, 2));
            samples[i] = value / 32768f;
        }

        return new WaveAudio(samples, sampleRate.Value, channels.Value);
    }

    private static void WritePcm16Wave(string path, WaveAudio audio)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

        var dataLength = audio.Samples.Length * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)audio.Channels);
        writer.Write(audio.SampleRate);
        writer.Write(audio.SampleRate * audio.Channels * 2);
        writer.Write((short)(audio.Channels * 2));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        foreach (var sample in audio.Samples)
        {
            var clamped = Math.Clamp(sample, -1f, 1f);
            var pcm = (short)Math.Clamp(Math.Round(clamped * 32767f), short.MinValue, short.MaxValue);
            writer.Write(pcm);
        }
    }

    private static void CleanupTemporaryFiles(
        IReadOnlyList<string> tempFiles,
        bool keepTemporaryFiles,
        IProgress<DenoiseProgress>? progress)
    {
        Report(progress, DenoiseProcessingStage.CleaningTemporaryFiles, 0, "Cleaning temporary files", null, null, false, false);

        if (!keepTemporaryFiles)
        {
            foreach (var tempFile in tempFiles)
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }
        }

        Report(progress, DenoiseProcessingStage.CleaningTemporaryFiles, 1, "Temporary file cleanup complete", null, null, false, false);
    }

    private static void Report(
        IProgress<DenoiseProgress>? progress,
        DenoiseProcessingStage stage,
        double stagePercent,
        string description,
        TimeSpan? processedDuration,
        TimeSpan? totalDuration,
        bool isInferenceActive,
        bool isFfmpegActive,
        TimeSpan? eta = null)
    {
        if (progress is null)
        {
            return;
        }

        var completedWeight = 0d;
        foreach (var (candidate, weight) in StageWeights)
        {
            if (candidate == stage)
            {
                break;
            }

            completedWeight += weight;
        }

        var currentWeight = StageWeights.TryGetValue(stage, out var stageWeight) ? stageWeight : 0d;
        var overallPercent = Math.Clamp((completedWeight + (currentWeight * Math.Clamp(stagePercent, 0d, 1d))) / 100d, 0d, 1d);

        progress.Report(new DenoiseProgress
        {
            Stage = stage,
            OverallPercent = stage == DenoiseProcessingStage.Completed ? 1d : overallPercent,
            StagePercent = stage == DenoiseProcessingStage.Completed ? 1d : Math.Clamp(stagePercent, 0d, 1d),
            StageDescription = description,
            ProcessedDuration = processedDuration,
            TotalDuration = totalDuration,
            IsInferenceActive = isInferenceActive,
            IsFfmpegActive = isFfmpegActive,
            EstimatedRemainingTime = eta
        });
    }

    private static string CreateTempFile(List<string> tempFiles, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "files-tools-denoise", Guid.NewGuid().ToString("N") + extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        tempFiles.Add(path);
        return path;
    }

    private static void ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Input path cannot be null or whitespace.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Input file was not found.", path);
        }
    }

    private static void ValidateOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(path));
        }
    }

    private static TimeSpan? TryGetDuration(JsonElement element)
    {
        var durationString = TryGetString(element, "duration");
        return durationString is not null &&
            double.TryParse(durationString, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : null;
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return null;
    }

    private static long? TryGetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return null;
    }

    private static IReadOnlyList<string> SplitArguments(string arguments)
    {
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<ProcessResult> RunProcessWithFallbackAsync(
        IReadOnlyList<string> binaryCandidates,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? standardErrorLineObserver)
    {
        DenoiseProcessException? lastException = null;

        foreach (var candidate in binaryCandidates)
        {
            try
            {
                return await RunProcessAsync(candidate, arguments, cancellationToken, standardErrorLineObserver).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DenoiseProcessException ex) when (CanFallbackToPath(candidate, ex, binaryCandidates))
            {
                lastException = ex;
            }
        }

        throw lastException ?? new DenoiseProcessingException("No FFmpeg executable candidates were available.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string binaryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? standardErrorLineObserver)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(binaryPath) ?? AppContext.BaseDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start process '{binaryPath}'.");
            }
        }
        catch (Exception ex)
        {
            throw new DenoiseProcessException(
                "Failed to start FFmpeg/FFprobe process.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                null,
                string.Empty,
                ex.Message,
                ex);
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort kill on cancellation.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                stderrBuilder.AppendLine(line);
                standardErrorLineObserver?.Invoke(line);
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);
        var stderr = stderrBuilder.ToString();

        if (process.ExitCode != 0)
        {
            throw new DenoiseProcessException(
                "FFmpeg/FFprobe exited with a non-zero code.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                process.ExitCode,
                stdout,
                stderr);
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static bool CanFallbackToPath(string candidate, DenoiseProcessException exception, IReadOnlyList<string> candidates)
    {
        return candidates.Count > 1 &&
            !string.Equals(candidate, candidates[^1], StringComparison.OrdinalIgnoreCase) &&
            exception.ExitCode is null;
    }

    private static string FormatCommandLine(string binaryPath, IReadOnlyList<string> arguments)
    {
        return string.Join(" ", new[] { Quote(binaryPath) }.Concat(arguments.Select(Quote)));
    }

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static TimeSpan ParseProgressTimestamp(string value)
    {
        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture, out var precise))
        {
            return precise;
        }

        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var centiseconds))
        {
            return centiseconds;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;
    }

    private sealed record WaveAudio(float[] Samples, int SampleRate, int Channels)
    {
        public string Path { get; init; } = string.Empty;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

/// <summary>
/// Convenience concrete class name for the video/audio denoise feature.
/// </summary>
public sealed class VideoAudioDenoise : VideoAudioDenoiseService
{
    /// <summary>
    /// Creates a denoise service without a configured DTLN engine. Processing will fail until an engine is supplied.
    /// </summary>
    public VideoAudioDenoise()
        : base(DtlnOnnxDenoiseEngine.CreateDefault())
    {
    }

    /// <summary>
    /// Creates a denoise service that uses the supplied DTLN engine for model inference.
    /// </summary>
    public VideoAudioDenoise(IDtlnDenoiseEngine denoiseEngine)
        : base(denoiseEngine)
    {
    }
}
