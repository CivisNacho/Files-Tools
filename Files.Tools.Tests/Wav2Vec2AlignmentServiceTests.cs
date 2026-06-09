using System;
using System.IO;
using System.Linq;
using Files_Tools.Services;

namespace Files.Tools.Tests;

/// <summary>
/// Integration check for the ONNX forced aligner against the known torchaudio VOiCES sample
/// ("I HAD THAT CURIOSITY BESIDE ME AT THIS MOMENT"). Skips gracefully when the model or sample
/// is not present, so it never breaks CI machines that lack the (large) aligner model.
/// </summary>
[TestClass]
public sealed class Wav2Vec2AlignmentServiceTests
{
    private static readonly string SampleWavPath =
        @"C:\Users\Nacho\source\repos\Files Tools\tools\export-aligners\sample16k.wav";

    private const string Transcript = "I HAD THAT CURIOSITY BESIDE ME AT THIS MOMENT";

    [TestMethod]
    public void Align_ProducesMonotonicWordTimings_ForKnownSample()
    {
        var modelDir = Path.Combine(Wav2Vec2AlignmentService.ResolveAlignersRoot(), "mms-fa");
        if (!File.Exists(Path.Combine(modelDir, "model.onnx")) || !File.Exists(SampleWavPath))
        {
            Assert.Inconclusive($"Aligner model or sample missing (model='{modelDir}', sample='{SampleWavPath}').");
            return;
        }

        var aligner = new Wav2Vec2AlignmentService(modelDir);
        Assert.IsTrue(aligner.IsInstalled(), "Aligner reported not installed.");

        var segment = new AudioTranscriptionSegment(
            TimeSpan.Zero, TimeSpan.FromSeconds(3.4), Transcript);

        var result = aligner.Align(SampleWavPath, new[] { segment });

        Assert.AreEqual(1, result.Count);
        var words = result[0].Words;
        Assert.IsNotNull(words, "Alignment produced no word timings.");

        var expected = Transcript.Split(' ');
        Assert.AreEqual(expected.Length, words!.Count, "Word count mismatch.");

        // Print the alignment so the run output can be eyeballed against the Python reference.
        Console.WriteLine($"sample duration ~3.40s, {words.Count} words:");
        TimeSpan previousEnd = TimeSpan.MinValue;
        for (var i = 0; i < words.Count; i++)
        {
            var w = words[i];
            Console.WriteLine($"  {w.Text,-12} {w.Start.TotalSeconds,6:0.00}s -> {w.End.TotalSeconds,6:0.00}s");

            Assert.AreEqual(expected[i], w.Text, $"Word {i} text mismatch.");
            Assert.IsTrue(w.End > w.Start, $"Word {i} '{w.Text}' has non-positive duration.");
            Assert.IsTrue(w.Start >= previousEnd, $"Word {i} '{w.Text}' overlaps previous word.");
            Assert.IsTrue(w.End <= TimeSpan.FromSeconds(3.6), $"Word {i} '{w.Text}' ends past clip.");
            previousEnd = w.End;
        }

        // "CURIOSITY" is the long word in the middle; it should occupy a clearly longer span than
        // the short function words, confirming timings track speech rather than being uniform.
        var curiosity = words.First(w => w.Text == "CURIOSITY");
        var me = words.First(w => w.Text == "ME");
        Assert.IsTrue((curiosity.End - curiosity.Start) > (me.End - me.Start),
            "Expected 'CURIOSITY' to span longer than 'ME'.");
    }
}
