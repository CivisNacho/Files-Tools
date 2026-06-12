using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Files_Tools.Services;

/// <summary>
/// Refines word-level timestamps for transcription segments using CTC forced alignment over a
/// wav2vec2 / MMS acoustic model exported to ONNX. Whisper supplies the words; this service
/// supplies accurate per-word timing (needed for karaoke).
/// </summary>
public interface IWordAligner
{
    /// <summary>Returns whether the aligner model is installed locally.</summary>
    bool IsInstalled();

    /// <summary>Downloads the aligner model when missing, reporting fraction-complete in [0, 1].</summary>
    Task InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns copies of <paramref name="segments"/> with <see cref="AudioTranscriptionSegment.Words"/>
    /// replaced by forced-aligned timings. Segments whose text cannot be aligned are returned
    /// unchanged so callers can fall back to synthesized timing.
    /// </summary>
    IReadOnlyList<AudioTranscriptionSegment> Align(
        string preparedWav16kMonoPath,
        IReadOnlyList<AudioTranscriptionSegment> segments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// ONNX-Runtime forced aligner. Loads the model only for the duration of an <see cref="Align"/>
/// call and disposes it before returning, so it never shares RAM with the Whisper model.
/// </summary>
public sealed class Wav2Vec2AlignmentService : IWordAligner
{
    private const int SampleRate = 16000;

    // wav2vec2 conv front-end needs at least ~400 samples to emit a single frame.
    private const int MinimumSliceSamples = 400;

    // Pad each segment's audio slice so word onsets/offsets near the boundary have acoustic context.
    private static readonly TimeSpan SlicePadding = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan MinimumWordDuration = TimeSpan.FromMilliseconds(1);

    private readonly string _modelDirectory;

    /// <summary>Creates the aligner using the default per-RAM-tier model directory.</summary>
    public Wav2Vec2AlignmentService()
        : this(ResolveDefaultModelDirectory())
    {
    }

    internal Wav2Vec2AlignmentService(string modelDirectory)
    {
        _modelDirectory = modelDirectory ?? throw new ArgumentNullException(nameof(modelDirectory));
    }

    /// <summary>
    /// The single aligner model used on every machine: the multilingual MMS_FA model. It is fast
    /// and accurate enough at int8 that the lighter English-only model is not worth maintaining.
    /// </summary>
    public const string ModelId = "mms-fa";

    /// <summary>Root folder that holds the aligner model's <c>model.onnx</c> / vocab / meta.</summary>
    public static string ResolveAlignersRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Media Tools",
            "Aligners");
    }

    private static string ResolveDefaultModelDirectory()
    {
        return Path.Combine(ResolveAlignersRoot(), ModelId);
    }

    /// <inheritdoc />
    public bool IsInstalled()
    {
        var installed = File.Exists(Path.Combine(_modelDirectory, "model.onnx"))
            && File.Exists(Path.Combine(_modelDirectory, "vocab.json"))
            && File.Exists(Path.Combine(_modelDirectory, "meta.json"));

        if (!installed)
        {
            Log($"not installed: looked in '{_modelDirectory}' (model id='{ModelId}').");
        }

        return installed;
    }

    // int8 MMS_FA aligner, hosted on Hugging Face. The three files mirror the local export layout.
    private const string DownloadBaseUrl = "https://huggingface.co/CivisNacho/mms-fa-int8/resolve/main";
    private static readonly string[] ModelFileNames = ["model.onnx", "vocab.json", "meta.json"];

    // SHA-256 of model.onnx, verified after download to reject truncated/corrupt files.
    private const string ModelSha256 = "55FE88DF5D14F179BC699A26A77F4DE4E272121107EB3E07BA065C2307A37EBB";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    /// <inheritdoc />
    public async Task InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (IsInstalled())
        {
            progress?.Report(1d);
            return;
        }

        Directory.CreateDirectory(_modelDirectory);
        Log($"downloading aligner model to '{_modelDirectory}' from {DownloadBaseUrl}");

        // Download to .part files first, then promote, so a half-finished file is never seen as
        // installed. The large model.onnx dominates progress; the tiny json files round it out.
        var partPaths = new List<(string Part, string Final)>();
        try
        {
            for (var i = 0; i < ModelFileNames.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = ModelFileNames[i];
                var finalPath = Path.Combine(_modelDirectory, fileName);
                var partPath = finalPath + ".part";
                partPaths.Add((partPath, finalPath));

                var isLargeModel = string.Equals(fileName, "model.onnx", StringComparison.Ordinal);
                var fileProgress = progress is null || !isLargeModel
                    ? null
                    // Only the model file reports granular progress; it is >99% of the bytes.
                    : new Progress<double>(value => progress.Report(Math.Clamp(value * 0.99d, 0d, 0.99d)));

                await DownloadFileAsync($"{DownloadBaseUrl}/{fileName}", partPath, fileProgress, cancellationToken).ConfigureAwait(false);

                if (isLargeModel)
                {
                    await VerifyChecksumAsync(partPath, ModelSha256, cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var (part, final) in partPaths)
            {
                if (File.Exists(final))
                {
                    File.Delete(final);
                }

                File.Move(part, final);
            }

            progress?.Report(1d);
            Log("aligner model download complete.");
        }
        catch
        {
            foreach (var (part, _) in partPaths)
            {
                TryDelete(part);
            }

            throw;
        }
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            if (totalBytes > 0)
            {
                progress?.Report(copied / (double)totalBytes);
            }
        }
    }

    private static async Task VerifyChecksumAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexString(hashBytes);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Aligner model checksum mismatch (expected {expectedSha256}, got {actual}).");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioTranscriptionSegment> Align(
        string preparedWav16kMonoPath,
        IReadOnlyList<AudioTranscriptionSegment> segments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var installed = IsInstalled();
        if (segments.Count == 0 || !installed)
        {
            Log($"skipped (segments={segments.Count}, installed={installed}, dir='{_modelDirectory}').");
            return segments;
        }

        var totalStopwatch = Stopwatch.StartNew();
        var meta = LoadMeta();
        var vocab = LoadVocab();
        Log($"model dir '{_modelDirectory}' | tokenizer={meta.TokenizerKind} blank={meta.BlankId} logProb={meta.LogitsAreLogProb} | segments={segments.Count}");

        var samples = WaveReader.ReadMonoFloatWav(preparedWav16kMonoPath);
        if (samples.Length == 0)
        {
            Log($"aborting: no samples read from '{preparedWav16kMonoPath}'.");
            return segments;
        }

        Log($"audio '{Path.GetFileName(preparedWav16kMonoPath)}' = {samples.Length} samples ({samples.Length / (double)SampleRate:0.00}s @ {SampleRate}Hz)");

        // One session for the whole batch; disposed before returning (never overlaps Whisper).
        var sessionStopwatch = Stopwatch.StartNew();
        using var session = new InferenceSession(Path.Combine(_modelDirectory, "model.onnx"));
        var inputName = session.InputMetadata.Keys.First();
        var outputName = session.OutputMetadata.Keys.First();
        sessionStopwatch.Stop();
        Log($"ONNX session loaded in {sessionStopwatch.ElapsedMilliseconds} ms (input='{inputName}', output='{outputName}')");

        var aligned = new List<AudioTranscriptionSegment>(segments.Count);
        var refinedCount = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = segments[index];
            var result = AlignSegment(session, inputName, outputName, meta, vocab, samples, segment, index);
            if (!ReferenceEquals(result, segment))
            {
                refinedCount++;
            }

            aligned.Add(result);
            progress?.Report((index + 1) / (double)segments.Count);
        }

        totalStopwatch.Stop();
        Log($"done: refined {refinedCount}/{segments.Count} segments in {totalStopwatch.ElapsedMilliseconds} ms.");
        return aligned;
    }

    private static AudioTranscriptionSegment AlignSegment(
        InferenceSession session,
        string inputName,
        string outputName,
        AlignerMeta meta,
        AlignerVocab vocab,
        float[] samples,
        AudioTranscriptionSegment segment,
        int segmentIndex)
    {
        var words = SplitWords(segment.Text);
        if (words.Count == 0)
        {
            Log($"  seg#{segmentIndex}: empty text -> kept as-is.");
            return segment;
        }

        // Tokenize each word into vocab ids, remembering which word each token belongs to.
        var tokens = new List<int>();
        var wordOfToken = new List<int>();
        for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
        {
            if (meta.TokenizerKind == "hf-char" && wordIndex > 0 && meta.WordDelimiterId is int delimiter)
            {
                tokens.Add(delimiter);
                wordOfToken.Add(-1);
            }

            foreach (var id in vocab.TokenizeWord(words[wordIndex], meta))
            {
                tokens.Add(id);
                wordOfToken.Add(wordIndex);
            }
        }

        if (tokens.Count == 0)
        {
            Log($"  seg#{segmentIndex}: no vocab tokens for '{Preview(segment.Text)}' -> kept as-is.");
            return segment;
        }

        // Slice the segment's audio (with padding) so alignment runs locally and bounds memory.
        var sliceStart = segment.Start - SlicePadding;
        if (sliceStart < TimeSpan.Zero)
        {
            sliceStart = TimeSpan.Zero;
        }

        var sliceEnd = (segment.End > segment.Start ? segment.End : segment.Start + MinimumWordDuration) + SlicePadding;
        var startSample = (int)Math.Clamp(Math.Round(sliceStart.TotalSeconds * SampleRate), 0, samples.Length - 1);
        var endSample = (int)Math.Clamp(Math.Round(sliceEnd.TotalSeconds * SampleRate), startSample + 1, samples.Length);
        var sliceLength = endSample - startSample;
        if (sliceLength < MinimumSliceSamples)
        {
            Log($"  seg#{segmentIndex}: slice too short ({sliceLength} samples) -> kept as-is.");
            return segment;
        }

        var slice = new float[sliceLength];
        Array.Copy(samples, startSample, slice, 0, sliceLength);
        if (meta.Normalize)
        {
            NormalizeInPlace(slice);
        }

        var (logProb, frames, vocabSize) = RunModel(session, inputName, outputName, slice, meta);
        if (frames == 0)
        {
            Log($"  seg#{segmentIndex}: model returned 0 frames -> kept as-is.");
            return segment;
        }

        var spans = CtcForcedAligner.Align(logProb, frames, vocabSize, tokens, meta.BlankId);
        if (spans.Count == 0)
        {
            Log($"  seg#{segmentIndex}: trellis produced 0 spans -> kept as-is.");
            return segment;
        }

        var secondsPerFrame = sliceLength / (double)SampleRate / frames;
        var sliceStartSeconds = startSample / (double)SampleRate;

        // Merge token spans into per-word frame bounds.
        var wordStartFrame = new int[words.Count];
        var wordEndFrame = new int[words.Count];
        Array.Fill(wordStartFrame, -1);
        foreach (var span in spans)
        {
            var wordIndex = wordOfToken[span.TokenIndex];
            if (wordIndex < 0)
            {
                continue;
            }

            if (wordStartFrame[wordIndex] < 0)
            {
                wordStartFrame[wordIndex] = span.StartFrame;
            }

            wordEndFrame[wordIndex] = span.EndFrame;
        }

        var alignedWords = BuildAlignedWords(words, wordStartFrame, wordEndFrame, sliceStartSeconds, secondsPerFrame, segment);
        if (alignedWords.Count == 0)
        {
            Log($"  seg#{segmentIndex}: produced 0 aligned words -> kept as-is.");
            return segment;
        }

        var first = alignedWords[0];
        var last = alignedWords[^1];
        Log($"  seg#{segmentIndex}: '{Preview(segment.Text)}' | {words.Count} words, {tokens.Count} tokens, {frames} frames ({secondsPerFrame * 1000:0.0} ms/frame) | aligned {first.Start.TotalSeconds:0.00}s->{last.End.TotalSeconds:0.00}s");
        return segment with { Words = alignedWords };
    }

    private static string Preview(string text)
    {
        text = (text ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return text.Length <= 48 ? text : text[..45] + "...";
    }

    private static void Log(string message)
    {
        Debug.WriteLine($"[Aligner] {message}");
    }

    private static IReadOnlyList<AudioTranscriptionWord> BuildAlignedWords(
        IReadOnlyList<string> words,
        int[] wordStartFrame,
        int[] wordEndFrame,
        double sliceStartSeconds,
        double secondsPerFrame,
        AudioTranscriptionSegment segment)
    {
        TimeSpan FrameToTime(int frame)
        {
            var seconds = sliceStartSeconds + (frame * secondsPerFrame);
            return TimeSpan.FromSeconds(seconds < 0 ? 0 : seconds);
        }

        var result = new AudioTranscriptionWord[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            if (wordStartFrame[i] >= 0)
            {
                var start = FrameToTime(wordStartFrame[i]);
                var end = FrameToTime(Math.Max(wordEndFrame[i], wordStartFrame[i] + 1));
                result[i] = new AudioTranscriptionWord(start, end < start ? start + MinimumWordDuration : end, words[i]);
            }
            else
            {
                // Unalignable word (e.g. all tokens dropped): leave a placeholder to fill below.
                result[i] = new AudioTranscriptionWord(TimeSpan.MinValue, TimeSpan.MinValue, words[i]);
            }
        }

        FillUnalignedWords(result, segment);

        // Enforce monotonic, non-overlapping, positive-duration timing.
        var cursor = segment.Start < TimeSpan.Zero ? TimeSpan.Zero : segment.Start;
        for (var i = 0; i < result.Length; i++)
        {
            var start = result[i].Start < cursor ? cursor : result[i].Start;
            var end = result[i].End <= start ? start + MinimumWordDuration : result[i].End;
            result[i] = new AudioTranscriptionWord(start, end, result[i].Text);
            cursor = end;
        }

        return result;
    }

    // Assign timing to words that produced no aligned tokens by interpolating between neighbours.
    private static void FillUnalignedWords(AudioTranscriptionWord[] words, AudioTranscriptionSegment segment)
    {
        var segStart = segment.Start < TimeSpan.Zero ? TimeSpan.Zero : segment.Start;
        var segEnd = segment.End > segStart ? segment.End : segStart + MinimumWordDuration;

        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Start != TimeSpan.MinValue)
            {
                continue;
            }

            var prevEnd = i > 0 && words[i - 1].End != TimeSpan.MinValue ? words[i - 1].End : segStart;
            var nextStart = segEnd;
            for (var j = i + 1; j < words.Length; j++)
            {
                if (words[j].Start != TimeSpan.MinValue)
                {
                    nextStart = words[j].Start;
                    break;
                }
            }

            if (nextStart <= prevEnd)
            {
                nextStart = prevEnd + MinimumWordDuration;
            }

            var mid = prevEnd + TimeSpan.FromTicks((nextStart - prevEnd).Ticks / 2);
            words[i] = new AudioTranscriptionWord(prevEnd, mid, words[i].Text);
        }
    }

    private static (float[] LogProb, int Frames, int VocabSize) RunModel(
        InferenceSession session,
        string inputName,
        string outputName,
        float[] slice,
        AlignerMeta meta)
    {
        var input = new DenseTensor<float>(slice, new[] { 1, slice.Length });
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) }, new[] { outputName });
        var tensor = results.First().AsTensor<float>();
        var dims = tensor.Dimensions; // [1, frames, vocab]
        var frames = dims[1];
        var vocabSize = dims[2];
        var data = tensor.ToArray();

        if (!meta.LogitsAreLogProb)
        {
            LogSoftmaxRows(data, frames, vocabSize);
        }

        return (data, frames, vocabSize);
    }

    private static void LogSoftmaxRows(float[] data, int frames, int vocabSize)
    {
        for (var t = 0; t < frames; t++)
        {
            var offset = t * vocabSize;
            var max = float.NegativeInfinity;
            for (var v = 0; v < vocabSize; v++)
            {
                if (data[offset + v] > max)
                {
                    max = data[offset + v];
                }
            }

            double sum = 0;
            for (var v = 0; v < vocabSize; v++)
            {
                sum += Math.Exp(data[offset + v] - max);
            }

            var logSum = max + (float)Math.Log(sum);
            for (var v = 0; v < vocabSize; v++)
            {
                data[offset + v] -= logSum;
            }
        }
    }

    private static void NormalizeInPlace(float[] samples)
    {
        double mean = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            mean += samples[i];
        }

        mean /= samples.Length;

        double variance = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var d = samples[i] - mean;
            variance += d * d;
        }

        var std = Math.Sqrt(variance / samples.Length) + 1e-7;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)((samples[i] - mean) / std);
        }
    }

    private static List<string> SplitWords(string text)
    {
        return (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private AlignerMeta LoadMeta()
    {
        using var stream = File.OpenRead(Path.Combine(_modelDirectory, "meta.json"));
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        int? wordDelimiter = root.TryGetProperty("word_delimiter_id", out var delimiterElement)
            && delimiterElement.ValueKind == JsonValueKind.Number
            ? delimiterElement.GetInt32()
            : null;

        return new AlignerMeta(
            Normalize: root.GetProperty("normalize").GetBoolean(),
            BlankId: root.GetProperty("blank_id").GetInt32(),
            WordDelimiterId: wordDelimiter,
            TokenizerKind: root.GetProperty("tokenizer_kind").GetString() ?? "hf-char",
            LogitsAreLogProb: root.TryGetProperty("logits_are_logprob", out var lp) && lp.GetBoolean());
    }

    private AlignerVocab LoadVocab()
    {
        using var stream = File.OpenRead(Path.Combine(_modelDirectory, "vocab.json"));
        var map = JsonSerializer.Deserialize<Dictionary<string, int>>(stream)
            ?? throw new InvalidOperationException("Aligner vocab.json could not be parsed.");
        return new AlignerVocab(map);
    }

    private sealed record AlignerMeta(bool Normalize, int BlankId, int? WordDelimiterId, string TokenizerKind, bool LogitsAreLogProb);

    /// <summary>Maps single-character tokens to vocab ids and tokenizes words per tokenizer kind.</summary>
    private sealed class AlignerVocab
    {
        private readonly Dictionary<string, int> _map;

        public AlignerVocab(Dictionary<string, int> map)
        {
            _map = map;
        }

        public IEnumerable<int> TokenizeWord(string word, AlignerMeta meta)
        {
            // mms-roman: ASCII-fold + lowercase; hf-char: uppercase. Then map each char, dropping
            // any character not in the vocabulary (digits, punctuation, unsupported scripts).
            var normalized = meta.TokenizerKind == "mms-roman"
                ? AsciiFold(word).ToLowerInvariant()
                : word.ToUpperInvariant();

            foreach (var character in normalized)
            {
                var key = character.ToString();
                if (_map.TryGetValue(key, out var id) && id != meta.BlankId)
                {
                    yield return id;
                }
            }
        }

        // Strip diacritics so Latin-script accented characters map onto the romanized vocab
        // (e.g. "café" -> "cafe"). Non-Latin scripts are largely dropped (uroman would be needed).
        private static string AsciiFold(string value)
        {
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}

/// <summary>
/// CTC forced alignment via a Viterbi trellis over the staggered (blank-interleaved) target
/// sequence. Direct port of the validated reference in tools/export-aligners/test_alignment.py.
/// </summary>
internal static class CtcForcedAligner
{
    public readonly record struct TokenSpan(int TokenIndex, int StartFrame, int EndFrame);

    public static IReadOnlyList<TokenSpan> Align(float[] logProb, int frames, int vocabSize, IReadOnlyList<int> tokens, int blank)
    {
        var n = tokens.Count;
        if (frames == 0 || n == 0)
        {
            return Array.Empty<TokenSpan>();
        }

        // Staggered sequence: blank, t0, blank, t1, ..., blank  (length 2N+1).
        var stateCount = (2 * n) + 1;
        var seq = new int[stateCount];
        seq[0] = blank;
        for (var i = 0; i < n; i++)
        {
            seq[(2 * i) + 1] = tokens[i];
            seq[(2 * i) + 2] = blank;
        }

        const double NegInf = -1e30;
        var dpPrev = new double[stateCount];
        var dpCur = new double[stateCount];
        // back[t * S + s] = chosen previous state index for (t, s).
        var back = new int[frames * stateCount];

        for (var s = 0; s < stateCount; s++)
        {
            dpPrev[s] = NegInf;
        }

        dpPrev[0] = logProb[seq[0]];
        if (stateCount > 1)
        {
            dpPrev[1] = logProb[seq[1]];
        }

        for (var t = 1; t < frames; t++)
        {
            var frameOffset = t * vocabSize;
            for (var s = 0; s < stateCount; s++)
            {
                var bestPrev = dpPrev[s];
                var bestK = s;

                if (s - 1 >= 0 && dpPrev[s - 1] > bestPrev)
                {
                    bestPrev = dpPrev[s - 1];
                    bestK = s - 1;
                }

                // Skip a blank between two distinct labels.
                if (s - 2 >= 0 && seq[s] != blank && seq[s] != seq[s - 2] && dpPrev[s - 2] > bestPrev)
                {
                    bestPrev = dpPrev[s - 2];
                    bestK = s - 2;
                }

                dpCur[s] = bestPrev + logProb[frameOffset + seq[s]];
                back[(t * stateCount) + s] = bestK;
            }

            (dpPrev, dpCur) = (dpCur, dpPrev);
        }

        // Backtrack from the better of the last two states.
        var state = dpPrev[stateCount - 1] >= dpPrev[stateCount - 2] ? stateCount - 1 : stateCount - 2;
        var path = new int[frames];
        for (var t = frames - 1; t >= 0; t--)
        {
            path[t] = state;
            state = back[(t * stateCount) + state];
        }

        // Collapse the path into per-token frame spans (ignoring blank states).
        var spans = new List<TokenSpan>();
        var currentLabel = -1;
        for (var t = 0; t < frames; t++)
        {
            var s = path[t];
            if (seq[s] == blank)
            {
                continue;
            }

            var label = s / 2; // 0-based index into tokens
            if (label != currentLabel)
            {
                spans.Add(new TokenSpan(label, t, t + 1));
                currentLabel = label;
            }
            else
            {
                var last = spans[^1];
                spans[^1] = last with { EndFrame = t + 1 };
            }
        }

        return spans;
    }
}

/// <summary>
/// Minimal mono WAV reader for 16-bit PCM and 32-bit float. Returns raw samples at whatever
/// rate the file declares — callers are responsible for feeding it the rate they expect.
/// </summary>
internal static class WaveReader
{
    public static float[] ReadMonoFloatWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (ReadTag(reader) != "RIFF")
        {
            throw new InvalidDataException("Not a RIFF/WAV file.");
        }

        reader.ReadInt32(); // overall size
        if (ReadTag(reader) != "WAVE")
        {
            throw new InvalidDataException("Not a WAVE file.");
        }

        short audioFormat = 1;
        short channels = 1;
        short bitsPerSample = 16;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadTag(reader);
            var chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                reader.ReadInt32(); // sample rate
                reader.ReadInt32(); // byte rate
                reader.ReadInt16(); // block align
                bitsPerSample = reader.ReadInt16();
                var consumed = 16;
                if (chunkSize > consumed)
                {
                    reader.ReadBytes(chunkSize - consumed);
                }
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }
            else
            {
                reader.ReadBytes(chunkSize + (chunkSize & 1)); // skip, honouring word alignment
            }
        }

        if (data is null)
        {
            return Array.Empty<float>();
        }

        return DecodeToMono(data, audioFormat, channels, bitsPerSample);
    }

    private static string ReadTag(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }

    private static float[] DecodeToMono(byte[] data, short audioFormat, short channels, short bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0 || channels <= 0)
        {
            return Array.Empty<float>();
        }

        var frameCount = data.Length / (bytesPerSample * channels);
        var result = new float[frameCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var index = ((frame * channels) + channel) * bytesPerSample;
                sum += ReadSample(data, index, audioFormat, bitsPerSample);
            }

            result[frame] = (float)(sum / channels);
        }

        return result;
    }

    private static double ReadSample(byte[] data, int index, short audioFormat, short bitsPerSample)
    {
        if (audioFormat == 3 && bitsPerSample == 32)
        {
            return BitConverter.ToSingle(data, index);
        }

        return bitsPerSample switch
        {
            16 => BitConverter.ToInt16(data, index) / 32768.0,
            32 => BitConverter.ToInt32(data, index) / 2147483648.0,
            8 => (data[index] - 128) / 128.0,
            _ => 0
        };
    }
}

