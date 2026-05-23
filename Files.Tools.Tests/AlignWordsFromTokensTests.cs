using Files_Tools.Services;
using Transcriber = Files_Tools.Services.AudioTranscriptionService.WhisperNetTranscriber;

namespace Files.Tools.Tests;

[TestClass]
public class AlignWordsFromTokensTests
{
    [TestMethod]
    public void GroupsTokensIntoWords_SplittingOnLeadingWhitespace()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hello world",
            [
                MakeToken(0, 0, "Hello", TimeSpan.Zero, TimeSpan.FromMilliseconds(400)),
                MakeToken(0, 1, " world", TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1))
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(2, words.Count);
        Assert.AreEqual("Hello", words[0].Text);
        Assert.AreEqual("world", words[1].Text);
    }

    [TestMethod]
    public void AttachesPunctuationToPrecedingWord()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hello, world",
            [
                MakeToken(0, 0, "Hello", TimeSpan.Zero, TimeSpan.FromMilliseconds(400)),
                MakeToken(0, 1, ",", TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(450)),
                MakeToken(0, 2, " world", TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1))
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(2, words.Count);
        Assert.AreEqual("Hello,", words[0].Text);
        Assert.AreEqual("world", words[1].Text);
    }

    [TestMethod]
    public void SkipsSpecialTokens()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello",
            [
                MakeToken(0, 0, "<|startoftranscript|>", TimeSpan.Zero, TimeSpan.Zero, isSpecial: true),
                MakeToken(0, 1, "Hello", TimeSpan.Zero, TimeSpan.FromMilliseconds(500)),
                MakeToken(0, 2, "<|endoftext|>", TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), isSpecial: true)
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(1, words.Count);
        Assert.AreEqual("Hello", words[0].Text);
    }

    [TestMethod]
    public void PrefersDtwTimestampWhenAvailable()
    {
        var dtwTime = TimeSpan.FromMilliseconds(180);
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello",
            [
                MakeToken(0, 0, "Hello", TimeSpan.Zero, TimeSpan.FromMilliseconds(400), dtwTimestamp: dtwTime)
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(1, words.Count);
        Assert.AreEqual(dtwTime, words[0].Start);
    }

    [TestMethod]
    public void FallsBackToRegularTimingWhenDtwIsNull()
    {
        var start = TimeSpan.FromMilliseconds(100);
        var end = TimeSpan.FromMilliseconds(500);
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello",
            [
                MakeToken(0, 0, "Hello", start, end, dtwTimestamp: null)
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(1, words.Count);
        Assert.AreEqual(start, words[0].Start);
        Assert.AreEqual(end, words[0].End);
    }

    [TestMethod]
    public void CombinesSubwordTokensIntoSingleWord()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "unbelievable",
            [
                MakeToken(0, 0, "un", TimeSpan.Zero, TimeSpan.FromMilliseconds(200)),
                MakeToken(0, 1, "believ", TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600)),
                MakeToken(0, 2, "able", TimeSpan.FromMilliseconds(600), TimeSpan.FromSeconds(1))
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(1, words.Count);
        Assert.AreEqual("unbelievable", words[0].Text);
    }

    [TestMethod]
    public void SkipsWhitespaceOnlyTokens()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hello world",
            [
                MakeToken(0, 0, "Hello", TimeSpan.Zero, TimeSpan.FromMilliseconds(400)),
                MakeToken(0, 1, "   ", TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(500)),
                MakeToken(0, 2, "world", TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1))
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(2, words.Count);
        Assert.AreEqual("Hello", words[0].Text);
        Assert.AreEqual("world", words[1].Text);
    }

    [TestMethod]
    public void HandlesMultipleSegments()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello",
            [
                MakeToken(0, 0, "Hello", TimeSpan.Zero, TimeSpan.FromMilliseconds(800))
            ]),
            (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "world",
            [
                MakeToken(1, 0, "world", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(2, words.Count);
        Assert.AreEqual(0, words[0].SegmentIndex);
        Assert.AreEqual(1, words[1].SegmentIndex);
    }

    [TestMethod]
    public void DtwTimingSpansTokenDuration()
    {
        var dtwStart = TimeSpan.FromMilliseconds(200);
        var regularStart = TimeSpan.FromMilliseconds(100);
        var regularEnd = TimeSpan.FromMilliseconds(500);
        var expectedDuration = regularEnd - regularStart;

        var (start, end) = Transcriber.GetTokenTiming(
            MakeToken(0, 0, "Hello", regularStart, regularEnd, dtwTimestamp: dtwStart));

        Assert.AreEqual(dtwStart, start);
        Assert.AreEqual(dtwStart + expectedDuration, end);
    }

    [TestMethod]
    public void CleanupAlignedWords_ProducesMonotonicTimings()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(2), "one two three",
            [
                MakeToken(0, 0, "one", TimeSpan.Zero, TimeSpan.FromMilliseconds(300)),
                MakeToken(0, 1, " two", TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400)),
                MakeToken(0, 2, " three", TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500))
            ]));

        var rawWords = Transcriber.AlignWordsFromTokens(segments);
        var cleaned = Transcriber.CleanupAlignedWords(segments, rawWords, AudioTranscriptionTimingSource.RawTokenAlignment);

        for (var i = 0; i < cleaned.Count; i++)
        {
            Assert.IsTrue(cleaned[i].End > cleaned[i].Start, $"Word {i} has non-positive duration");
            if (i > 0)
            {
                Assert.IsTrue(cleaned[i].Start >= cleaned[i - 1].End, $"Word {i} overlaps with word {i - 1}");
            }
        }
    }

    [TestMethod]
    public void ReturnsEmptyForSegmentWithNoVisibleTokens()
    {
        var segments = MakeSegments(
            (TimeSpan.Zero, TimeSpan.FromSeconds(1), "",
            [
                MakeToken(0, 0, "<|startoftranscript|>", TimeSpan.Zero, TimeSpan.Zero, isSpecial: true),
                MakeToken(0, 1, "   ", TimeSpan.Zero, TimeSpan.FromMilliseconds(500))
            ]));

        var words = Transcriber.AlignWordsFromTokens(segments);

        Assert.AreEqual(0, words.Count);
    }

    private static AudioTranscriptionToken MakeToken(
        int segmentIndex, int tokenIndex, string text,
        TimeSpan start, TimeSpan end,
        TimeSpan? dtwTimestamp = null, bool isSpecial = false)
    {
        return new AudioTranscriptionToken(
            segmentIndex, tokenIndex,
            TokenId: tokenIndex + 100,
            TimestampId: 0,
            text, start, end,
            dtwTimestamp,
            Probability: 0.9f,
            ProbabilityLog: -0.1f,
            TimestampProbability: 0.8f,
            TimestampProbabilitySum: 0.81f,
            VoiceLength: 1f,
            isSpecial);
    }

    private static IReadOnlyList<AudioTranscriptionDetailedSegment> MakeSegments(
        params (TimeSpan Start, TimeSpan End, string Text, AudioTranscriptionToken[] Tokens)[] entries)
    {
        return entries
            .Select((entry, index) => new AudioTranscriptionDetailedSegment(
                index, entry.Start, entry.End, entry.Text,
                0.9f, 0.8f, 1f, 0.1f, "en", entry.Tokens))
            .ToArray();
    }
}
