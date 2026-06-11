using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services;

/// <summary>Toggles and tuning for the <see cref="VoiceStudioService"/> "studio voice" chain.</summary>
public sealed class VoiceStudioOptions
{
    /// <summary>Apply DeepFilterNet3 denoise/dereverb.</summary>
    public bool Denoise { get; init; } = true;

    /// <summary>Apply FlashSR bandwidth extension (fullness). Best on low-bandwidth/dull sources.</summary>
    public bool SuperResolution { get; init; } = true;

    /// <summary>Apply the FFmpeg mastering chain (EQ + compression + loudness normalization).</summary>
    public bool Master { get; init; } = true;

    /// <summary>FFmpeg <c>-af</c> mastering filter graph; a tuned broadcast chain by default.</summary>
    public string MasteringFilter { get; init; } =
        "highpass=f=70," +
        "equalizer=f=200:t=q:w=1.0:g=-1.5," +
        "equalizer=f=3500:t=q:w=2.0:g=2.5," +
        "treble=g=3:f=8000," +
        "acompressor=threshold=-18dB:ratio=3:attack=10:release=120:makeup=2," +
        "loudnorm=I=-16:TP=-1.5:LRA=11";
}

/// <summary>Stage labels reported via <see cref="IProgress{T}"/> during processing.</summary>
public enum VoiceStudioStage
{
    Extracting,
    Denoising,
    RestoringFullness,
    Mastering,
    Finalizing,
    Completed,
}

/// <summary>Progress update for a <see cref="VoiceStudioService"/> run.</summary>
public sealed record VoiceStudioProgress(VoiceStudioStage Stage, double Fraction);

/// <summary>
/// Orchestrates the local "professional podcast / studio voice" chain on an audio file:
/// FFmpeg extract → DeepFilterNet3 denoise → FlashSR super-res → FFmpeg mastering. The ML stages
/// run via ONNX Runtime (<see cref="DeepFilterNetService"/>, <see cref="FlashSrService"/>); FFmpeg
/// handles decoding, resampling between stages, and mastering. Pure-local, CPU-only.
/// </summary>
public sealed class VoiceStudioService
{
    private readonly string? _dfnModelPath;
    private readonly string? _flashModelPath;

    public VoiceStudioService(string? dfnModelPath = null, string? flashModelPath = null)
    {
        _dfnModelPath = dfnModelPath;
        _flashModelPath = flashModelPath;
    }

    /// <summary>
    /// Produces an enhanced WAV at <paramref name="outputWavPath"/> from any FFmpeg-decodable audio.
    /// </summary>
    public async Task ProcessAudioAsync(
        string inputPath,
        string outputWavPath,
        VoiceStudioOptions options,
        IProgress<VoiceStudioProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var temp = Path.Combine(Path.GetTempPath(), "files-tools-voice", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            // 1. Extract to 48 kHz mono float WAV.
            progress?.Report(new(VoiceStudioStage.Extracting, 0.0));
            var stageWav = Path.Combine(temp, "extracted.wav");
            await RunFfmpegAsync(
                ["-y", "-i", inputPath, "-vn", "-ac", "1", "-ar",
                 DeepFilterNetService.SampleRate.ToString(), "-c:a", "pcm_f32le", stageWav],
                cancellationToken).ConfigureAwait(false);

            // 2. Denoise (DFN3) at 48 kHz.
            if (options.Denoise)
            {
                progress?.Report(new(VoiceStudioStage.Denoising, 0.25));
                var samples = WaveReader.ReadMono16k(stageWav);
                using var dfn = new DeepFilterNetService(_dfnModelPath);
                var enhanced = await dfn.EnhanceMonoAsync(samples, cancellationToken).ConfigureAwait(false);
                var denoised = Path.Combine(temp, "denoised.wav");
                WriteMonoFloat32Wav(denoised, enhanced, DeepFilterNetService.SampleRate);
                stageWav = denoised;
            }

            // 3. Super-resolution (FlashSR): resample to 16 kHz, run, get 48 kHz.
            if (options.SuperResolution)
            {
                progress?.Report(new(VoiceStudioStage.RestoringFullness, 0.5));
                var low = Path.Combine(temp, "low16k.wav");
                await RunFfmpegAsync(
                    ["-y", "-i", stageWav, "-ac", "1", "-ar", FlashSrService.InputSampleRate.ToString(),
                     "-c:a", "pcm_f32le", low], cancellationToken).ConfigureAwait(false);
                var low16 = WaveReader.ReadMono16k(low);
                using var flash = new FlashSrService(_flashModelPath);
                var full = await flash.UpsampleMonoAsync(low16, cancellationToken).ConfigureAwait(false);
                var restored = Path.Combine(temp, "restored.wav");
                WriteMonoFloat32Wav(restored, full, FlashSrService.OutputSampleRate);
                stageWav = restored;
            }

            // 4. Mastering (FFmpeg) -> output, or just copy through.
            cancellationToken.ThrowIfCancellationRequested();
            if (options.Master)
            {
                progress?.Report(new(VoiceStudioStage.Mastering, 0.8));
                await RunFfmpegAsync(
                    ["-y", "-i", stageWav, "-af", options.MasteringFilter, "-ar",
                     FlashSrService.OutputSampleRate.ToString(), outputWavPath],
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                progress?.Report(new(VoiceStudioStage.Finalizing, 0.9));
                File.Copy(stageWav, outputWavPath, overwrite: true);
            }

            progress?.Report(new(VoiceStudioStage.Completed, 1.0));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static async Task RunFfmpegAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var exe in FfmpegLocator.ResolveExecutableCandidates("ffmpeg"))
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            try
            {
                using var process = new Process { StartInfo = psi };
                var stderr = new StringBuilder();
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        stderr.AppendLine(e.Data);
                    }
                };
                if (!process.Start())
                {
                    continue;
                }

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode == 0)
                {
                    return;
                }

                last = new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {Tail(stderr.ToString())}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("FFmpeg executable not found.");
    }

    private static string Tail(string s)
    {
        s = s.Trim();
        return s.Length <= 400 ? s : s[^400..];
    }

    /// <summary>Writes mono 32-bit float PCM as a WAV file.</summary>
    internal static void WriteMonoFloat32Wav(string path, float[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        int dataBytes = samples.Length * 4;
        const short channels = 1;
        const short bitsPerSample = 32;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                       // fmt chunk size
        w.Write((short)3);                 // format = IEEE float
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)(channels * (bitsPerSample / 8))); // block align
        w.Write(bitsPerSample);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            w.Write(s);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
