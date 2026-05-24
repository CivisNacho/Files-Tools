using Files_Tools.Services;

namespace Files.Tools.Tests;

[TestClass]
public class SubtitlesServiceTests
{
    private string _tempRoot = null!;
    private FakeAudioTranscriptionService _audioTranscriptionService = null!;
    private SubtitlesService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-subtitles-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _audioTranscriptionService = new FakeAudioTranscriptionService();
        _service = new SubtitlesService(_audioTranscriptionService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    [TestMethod]
    public async Task GenerateSrtAsync_WritesValidSrt_AndReturnsFinalPath()
    {
        var input = Path.Combine(_tempRoot, "input.wav");
        File.WriteAllBytes(input, [0x52, 0x49, 0x46, 0x46]);
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromMilliseconds(950), "First line"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "  "),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Second line")
        ];

        var requestedPath = Path.Combine(_tempRoot, "captions.txt");
        var finalPath = await _service.GenerateSrtAsync(input, requestedPath);

        Assert.AreEqual(Path.Combine(_tempRoot, "captions.srt"), finalPath);
        Assert.IsTrue(File.Exists(finalPath));

        var srt = await File.ReadAllTextAsync(finalPath);
        StringAssert.Contains(srt, "1\r\n00:00:00,000 --> 00:00:00,950\r\nFirst line");
        StringAssert.Contains(srt, "2\r\n00:00:02,000 --> 00:00:04,000\r\nSecond line");
        Assert.IsFalse(srt.Contains("  \r\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GenerateSrtAsync_WithProgress_ReportsSubtitleStage_AndCompletion()
    {
        var input = Path.Combine(_tempRoot, "input.wav");
        File.WriteAllBytes(input, [0x52, 0x49, 0x46, 0x46]);
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromMilliseconds(500), "First line")
        ];

        var progress = new CollectingProgress<AudioTranscriptionProgress>();

        await _service.GenerateSrtAsync(input, Path.Combine(_tempRoot, "captions.srt"), progress);

        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.PreparingAudio));
        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.Transcribing));
        Assert.IsTrue(progress.Items.Any(update => update.Stage == AudioTranscriptionStage.WritingSubtitles));
        Assert.AreEqual(AudioTranscriptionStage.Completed, progress.Items[^1].Stage);
    }

    [TestMethod]
    public void BuildSrt_UsesMinimumPositiveDuration_WhenSegmentEndIsNotAfterStart()
    {
        var srt = SubtitlesService.BuildSrt(
        [
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), "Static")
        ]);

        StringAssert.Contains(srt, "00:00:03,000 --> 00:00:03,001");
    }

    [TestMethod]
    public async Task GenerateAdvancedTranscriptionDraftAsync_ReturnsNormalizedSegmentsWithoutPostprocessing()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.FromMilliseconds(-250), TimeSpan.FromSeconds(2), "  Hello \n world "),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2.02), TimeSpan.FromSeconds(2.02), "Second line"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "   ")
        ];

        var draft = await _service.GenerateAdvancedTranscriptionDraftAsync(CreateInputFile());

        Assert.AreEqual(2, draft.Segments.Count);
        Assert.AreEqual(TimeSpan.Zero, draft.Segments[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(2), draft.Segments[0].End);
        Assert.AreEqual("Hello world", draft.Segments[0].Text);
        Assert.AreEqual(TimeSpan.FromSeconds(2.02), draft.Segments[1].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(2.021), draft.Segments[1].End);
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_NormalizesText_AndRemovesEmptySegments()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.FromMilliseconds(-500), TimeSpan.FromSeconds(2), "  Hello \n world\t "),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "   ")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());

        Assert.AreEqual(1, draft.Cues.Count);
        Assert.AreEqual(TimeSpan.Zero, draft.Cues[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(2), draft.Cues[0].End);
        Assert.AreEqual("Hello world", draft.Cues[0].Text);
    }

    [TestMethod]
    public async Task BuildSubtitleDraftFromTranscription_AppliesReviewedTextBeforePostprocessing()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(0.5), "Helo"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(0.55), TimeSpan.FromSeconds(2.5), "wrld")
        ];

        var transcriptionDraft = await _service.GenerateAdvancedTranscriptionDraftAsync(CreateInputFile());
        var subtitleDraft = _service.BuildSubtitleDraftFromTranscription(
            transcriptionDraft,
            [
                new TranscriptionSegmentCorrection(transcriptionDraft.Segments[0].Id, "Hello"),
                new TranscriptionSegmentCorrection(transcriptionDraft.Segments[1].Id, "world")
            ]);

        Assert.AreEqual(1, subtitleDraft.Cues.Count);
        Assert.AreEqual("Hello world", subtitleDraft.Cues[0].Text);
    }

    [TestMethod]
    public async Task BuildSubtitleDraftFromTranscription_UsesMaximumDurationToChangeSectionCount()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(8), "one two three four")
        ];

        var transcriptionDraft = await _service.GenerateAdvancedTranscriptionDraftAsync(CreateInputFile());
        var shortDraft = _service.BuildSubtitleDraftFromTranscription(
            transcriptionDraft,
            [],
            new SubtitlePostprocessingOptions
            {
                MaximumDuration = TimeSpan.FromSeconds(2),
                IdealDurationMax = TimeSpan.FromSeconds(2)
            });
        var longDraft = _service.BuildSubtitleDraftFromTranscription(
            transcriptionDraft,
            [],
            new SubtitlePostprocessingOptions
            {
                MaximumDuration = TimeSpan.FromSeconds(12),
                IdealDurationMax = TimeSpan.FromSeconds(12)
            });

        Assert.IsTrue(shortDraft.Cues.Count > longDraft.Cues.Count);
        Assert.AreEqual(1, longDraft.Cues.Count);
    }

    [TestMethod]
    public async Task BuildSubtitleDraftFromTranscription_UsesMaxWordsPerSectionToChangeSectionCount()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(6), "one two three four five six")
        ];

        var transcriptionDraft = await _service.GenerateAdvancedTranscriptionDraftAsync(CreateInputFile());
        var uncappedDraft = _service.BuildSubtitleDraftFromTranscription(
            transcriptionDraft,
            [],
            new SubtitlePostprocessingOptions
            {
                MaximumDuration = TimeSpan.FromSeconds(12),
                IdealDurationMax = TimeSpan.FromSeconds(12)
            });
        var wordCappedDraft = _service.BuildSubtitleDraftFromTranscription(
            transcriptionDraft,
            [],
            new SubtitlePostprocessingOptions
            {
                MaximumDuration = TimeSpan.FromSeconds(12),
                IdealDurationMax = TimeSpan.FromSeconds(12),
                MaxWordsPerSection = 3
            });

        Assert.IsTrue(wordCappedDraft.Cues.Count > uncappedDraft.Cues.Count);
        Assert.IsTrue(wordCappedDraft.Cues.All(cue => cue.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3));
    }

    [TestMethod]
    public async Task BuildSubtitleDraftFromTranscription_EnforcesDurationAndWordCapsTogether()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(8), "one two three four five six seven eight")
        ];

        var transcriptionDraft = await _service.GenerateAdvancedTranscriptionDraftAsync(CreateInputFile());
        var constrainedDraft = _service.BuildSubtitleDraftFromTranscription(
            transcriptionDraft,
            [],
            new SubtitlePostprocessingOptions
            {
                MaximumDuration = TimeSpan.FromSeconds(2),
                IdealDurationMax = TimeSpan.FromSeconds(2),
                MaxWordsPerSection = 3
            });

        Assert.IsTrue(constrainedDraft.Cues.Count > 1);
        Assert.IsTrue(constrainedDraft.Cues.All(cue => cue.End - cue.Start <= TimeSpan.FromSeconds(2.001)));
        Assert.IsTrue(constrainedDraft.Cues.All(cue => cue.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3));
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_FixesOverlaps_ByTrimmingPreviousCue()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "First subtitle"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4), "Second subtitle")
        ];
        _audioTranscriptionService.Words =
        [
            new AudioTranscriptionWord(TimeSpan.Zero, TimeSpan.FromSeconds(3), "First subtitle."),
            new AudioTranscriptionWord(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4), "Second subtitle")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());

        Assert.AreEqual(TimeSpan.FromSeconds(2.5), draft.Cues[0].End);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5), draft.Cues[1].Start);
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_MergesTinyFragments_WithClosestNeighbor()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(0.5), "Hi"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(0.55), TimeSpan.FromSeconds(2.5), "there everyone")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());

        Assert.AreEqual(1, draft.Cues.Count);
        Assert.AreEqual(TimeSpan.Zero, draft.Cues[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5), draft.Cues[0].End);
        Assert.AreEqual("Hi there everyone", draft.Cues[0].Text);
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_SplitsOversizedSegments_AndRedistributesTiming()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(9),
                "Short sentence. This is a much much much longer second sentence for the subtitle.")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());

        Assert.IsTrue(draft.Cues.Count >= 2);
        StringAssert.Contains(draft.Cues[0].Text, "Short sentence.");
        Assert.IsTrue(string.Join(" ", draft.Cues.Select(cue => cue.Text)).Contains("longer second sentence", StringComparison.Ordinal));
        for (var index = 0; index < draft.Cues.Count - 1; index++)
        {
            Assert.AreEqual(draft.Cues[index].End, draft.Cues[index + 1].Start);
        }

        var firstCueDuration = draft.Cues[0].End - draft.Cues[0].Start;
        var remainingDuration = draft.Cues[^1].End - draft.Cues[1].Start;
        Assert.IsTrue(firstCueDuration < remainingDuration);
        Assert.AreEqual(TimeSpan.Zero, draft.Cues[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(9), draft.Cues[^1].End);
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_ReflowsLines_ToTwoBalancedLines()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(4),
                "This subtitle should break into two balanced readable lines")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var lines = draft.Cues[0].Text.Split('\n');

        Assert.AreEqual(1, draft.Cues.Count);
        Assert.AreEqual(2, lines.Length);
        Assert.IsTrue(lines.All(line => line.Length <= draft.Options.MaxCharsPerLine));
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_ExtendsTiming_WhenReadableSlackExists()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Subtitle needs time now"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3.5), "Next cue")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());

        Assert.IsTrue(draft.Cues[0].End > TimeSpan.FromSeconds(1));
        Assert.IsTrue(draft.Cues[0].End < draft.Cues[1].Start);
        Assert.IsTrue(draft.Cues[1].Start - draft.Cues[0].End < TimeSpan.FromMilliseconds(700));
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_ClampsShortGaps_AndPreservesAcceptableAndIntentionalPauses()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "One"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1.05), TimeSpan.FromSeconds(2.05), "Two"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2.23), TimeSpan.FromSeconds(3.23), "Three"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(4.13), TimeSpan.FromSeconds(5.13), "Four")
        ];
        _audioTranscriptionService.Words =
        [
            new AudioTranscriptionWord(TimeSpan.Zero, TimeSpan.FromSeconds(1), "One."),
            new AudioTranscriptionWord(TimeSpan.FromSeconds(1.05), TimeSpan.FromSeconds(2.05), "Two."),
            new AudioTranscriptionWord(TimeSpan.FromSeconds(2.23), TimeSpan.FromSeconds(3.23), "Three."),
            new AudioTranscriptionWord(TimeSpan.FromSeconds(4.13), TimeSpan.FromSeconds(5.13), "Four")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());

        Assert.AreEqual(draft.Cues[0].End, draft.Cues[1].Start);
        Assert.AreEqual(TimeSpan.FromMilliseconds(180), draft.Cues[2].Start - draft.Cues[1].End);
        Assert.AreEqual(TimeSpan.FromMilliseconds(900), draft.Cues[3].Start - draft.Cues[2].End);
    }

    [TestMethod]
    public async Task ApplyCorrections_NormalizesTextOnlyEdits()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Original text"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4), "Second subtitle")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var corrected = _service.ApplyCorrections(
            draft,
            [new SubtitleSegmentCorrection(draft.Cues[0].Id, "  Updated \n text  ", null, null)]);

        Assert.AreEqual("Updated text", corrected.Cues[0].Text);
        Assert.AreEqual(draft.Cues[0].Start, corrected.Cues[0].Start);
        Assert.AreEqual(draft.Cues[0].End, corrected.Cues[0].End);
    }

    [TestMethod]
    public async Task ApplyCorrections_RepairsTimingOnlyEdits_Locally()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1.5), "One"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(3), "Two")
        ];
        _audioTranscriptionService.Words =
        [
            new AudioTranscriptionWord(TimeSpan.Zero, TimeSpan.FromSeconds(1.5), "One."),
            new AudioTranscriptionWord(TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(3), "Two")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var corrected = _service.ApplyCorrections(
            draft,
            [new SubtitleSegmentCorrection(draft.Cues[1].Id, null, TimeSpan.FromSeconds(1.45), TimeSpan.FromSeconds(3))]);

        Assert.AreEqual(TimeSpan.FromSeconds(1.45), corrected.Cues[0].End);
        Assert.AreEqual(TimeSpan.FromSeconds(1.45), corrected.Cues[1].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(3), corrected.Cues[1].End);
    }

    [TestMethod]
    public async Task ApplyCorrections_HandlesCombinedTextAndTimingEdits()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Short subtitle")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var corrected = _service.ApplyCorrections(
            draft,
            [
                new SubtitleSegmentCorrection(
                    draft.Cues[0].Id,
                    "This correction should wrap into two balanced lines nicely",
                    TimeSpan.FromSeconds(-1),
                    TimeSpan.FromSeconds(4))
            ]);

        Assert.AreEqual(TimeSpan.Zero, corrected.Cues[0].Start);
        Assert.IsTrue(corrected.Cues[0].Text.Contains('\n'));
        Assert.IsTrue(corrected.Cues[0].Text.Split('\n').All(line => line.Length <= corrected.Options.MaxCharsPerLine));
    }

    [TestMethod]
    public async Task GenerateAdvancedDraftAsync_ReportsValidationIssues_ForResidualProblems()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromMilliseconds(500), "Hi")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());

        Assert.IsTrue(draft.Issues.Any(issue => issue.Code == "short-duration"));
    }

    [TestMethod]
    public async Task GenerateKaraokeAssAsync_WritesAssFile_AndNormalizesExtension()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2.1), "Hello world")
        ];

        var finalPath = await _service.GenerateKaraokeAssAsync(CreateInputFile(), Path.Combine(_tempRoot, "karaoke.txt"));

        Assert.AreEqual(Path.Combine(_tempRoot, "karaoke.ass"), finalPath);
        Assert.IsTrue(File.Exists(finalPath));

        var ass = await File.ReadAllTextAsync(finalPath);
        StringAssert.Contains(ass, "[V4+ Styles]");
        StringAssert.Contains(ass, "Style: NeonKaraoke");
        StringAssert.Contains(ass, "Dialogue: 0,");
        Assert.IsFalse(ass.Contains("Dialogue: 1,", StringComparison.Ordinal));
        StringAssert.Contains(ass, @"{\fad(80,80)\fscx112\fscy112\t(0,160,\fscx100\fscy100)}");
    }

    [TestMethod]
    public async Task GenerateKaraokeAssAsync_RendersTimedKaraokeTags_PerWord()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2.4), "One two")
        ];

        var finalPath = await _service.GenerateKaraokeAssAsync(CreateInputFile(), Path.Combine(_tempRoot, "overlay.ass"));
        var ass = await File.ReadAllTextAsync(finalPath);
        var dialogueLines = ass.Split(["\r\n", "\n"], StringSplitOptions.None).Where(line => line.StartsWith("Dialogue:", StringComparison.Ordinal)).ToArray();

        Assert.AreEqual(1, dialogueLines.Length);
        StringAssert.Contains(dialogueLines[0], @"{\kf");
        StringAssert.Contains(dialogueLines[0], "One");
        StringAssert.Contains(dialogueLines[0], "two");
    }

    [TestMethod]
    public async Task RenderKaraokeAss_UsesReviewedTranscriptionText()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), "helo wrld")
        ];

        var draft = await _service.GenerateAdvancedTranscriptionDraftAsync(CreateInputFile());
        var reviewedDraft = new TranscriptionDraft(
        [
            new TranscriptionSegment(draft.Segments[0].Id, draft.Segments[0].Start, draft.Segments[0].End, "hello world again")
        ]);

        var ass = _service.RenderKaraokeAss(reviewedDraft);

        StringAssert.Contains(ass, "hello");
        StringAssert.Contains(ass, "world");
        StringAssert.Contains(ass, "again");
        StringAssert.Contains(ass, "Dialogue:");
    }

    [TestMethod]
    public void RenderKaraokeAss_FromSubtitleDraft_PreservesReviewedCueBoundaries()
    {
        var draft = new SubtitleDraft(
        [
            new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1.5), "hello world"),
            new SubtitleCue(2, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), "second line")
        ],
        new SubtitlePostprocessingOptions(),
        []);

        var ass = _service.RenderKaraokeAss(draft);
        var baseDialogueLines = ass
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.StartsWith("Dialogue: 0,", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(2, baseDialogueLines.Length);
        StringAssert.Contains(baseDialogueLines[0], "0:00:00.00,0:00:01.50");
        StringAssert.Contains(baseDialogueLines[1], "0:00:03.00,0:00:05.00");
        StringAssert.Contains(baseDialogueLines[0], "hello");
        StringAssert.Contains(baseDialogueLines[0], "world");
        StringAssert.Contains(baseDialogueLines[0], @"{\kf");
        StringAssert.Contains(baseDialogueLines[1], "second");
        StringAssert.Contains(baseDialogueLines[1], "line");
        StringAssert.Contains(baseDialogueLines[1], @"{\kf");
    }

    [TestMethod]
    public async Task GenerateKaraokeAssAsync_PreservesIntentionalPauseBoundaries()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1.2), "First"),
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(2.2), TimeSpan.FromSeconds(3.2), "Second")
        ];

        var finalPath = await _service.GenerateKaraokeAssAsync(CreateInputFile(), Path.Combine(_tempRoot, "paused.ass"));
        var ass = await File.ReadAllTextAsync(finalPath);
        var baseDialogueLines = ass
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.StartsWith("Dialogue: 0,", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(2, baseDialogueLines.Length);
    }

    [TestMethod]
    public async Task GenerateStyledAssAsync_WritesAssFile_AndNormalizesExtension()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hello styled subtitle")
        ];

        var finalPath = await _service.GenerateStyledAssAsync(CreateInputFile(), Path.Combine(_tempRoot, "styled.txt"));

        Assert.AreEqual(Path.Combine(_tempRoot, "styled.ass"), finalPath);
        Assert.IsTrue(File.Exists(finalPath));

        var ass = await File.ReadAllTextAsync(finalPath);
        StringAssert.Contains(ass, "[V4+ Styles]");
        StringAssert.Contains(ass, "Style: SocialImpact");
        StringAssert.Contains(ass, "Dialogue: 0,");
    }

    [TestMethod]
    public async Task BuildStyledAss_AfterCorrections_RendersCorrectedCueText()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Original subtitle")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var corrected = _service.ApplyCorrections(
            draft,
            [new SubtitleSegmentCorrection(draft.Cues[0].Id, "Corrected subtitle", TimeSpan.Zero, TimeSpan.FromSeconds(2.2))]);
        var styled = _service.ApplyStylePreset(corrected);
        var ass = SubtitlesService.BuildStyledAss(styled);

        StringAssert.Contains(ass, "CORRECTED SUBTITLE");
        StringAssert.Contains(ass, "Dialogue: 0,0:00:00.00,0:00:02.20,");
    }

    [TestMethod]
    public async Task BuildStyledAss_AfterTimingCorrections_RendersCorrectedCueTiming()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Timing sample")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var corrected = _service.ApplyCorrections(
            draft,
            [new SubtitleSegmentCorrection(draft.Cues[0].Id, null, TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(3.75))]);
        var styled = _service.ApplyStylePreset(corrected);
        var ass = SubtitlesService.BuildStyledAss(styled);

        StringAssert.Contains(ass, "Dialogue: 0,0:00:00.25,0:00:03.75,");
    }

    [TestMethod]
    public void StyledSubtitlePresets_SocialImpact_ExposesExpectedDefaults()
    {
        var preset = StyledSubtitlePresets.SocialImpact;

        Assert.AreEqual("SocialImpact", preset.Name);
        Assert.AreEqual("Impact", preset.PrimaryFontFamily);
        CollectionAssert.AreEqual(new[] { "Impact", "Anton", "Bebas Neue", "Arial Black" }, preset.FontFamilyFallbacks.ToArray());
        Assert.AreEqual(SubtitleTextTransform.Uppercase, preset.TextTransform);
        Assert.AreEqual(SubtitleVisualAlignment.BottomCenter, preset.Alignment);
        Assert.AreEqual(28, preset.MaxCharsPerLine);
        Assert.AreEqual(2, preset.MaxLines);
        Assert.AreEqual(6d, preset.OutlineWidth);
        Assert.AreEqual(1.5d, preset.ShadowDepth);
        Assert.IsFalse(preset.UseBackgroundBox);
    }

    [TestMethod]
    public void StyledSubtitlePresets_CaptionBox_UsesBoxPresentation()
    {
        var preset = StyledSubtitlePresets.CaptionBox;

        Assert.AreEqual("CaptionBox", preset.Name);
        Assert.IsTrue(preset.UseBackgroundBox);
        Assert.AreEqual(SubtitlePresentationAnimation.Fade, preset.PresentationAnimation);
        Assert.AreEqual(140, preset.EntryFadeMilliseconds);
        Assert.AreEqual(140, preset.ExitFadeMilliseconds);
        Assert.AreEqual(SubtitleVisualAlignment.BottomCenter, preset.Alignment);
    }

    [TestMethod]
    public void KaraokeSubtitlePresets_NeonKaraoke_UsesAnimatedKaraokeDefaults()
    {
        var preset = KaraokeSubtitlePresets.NeonKaraoke;

        Assert.AreEqual("NeonKaraoke", preset.Name);
        Assert.AreEqual(SubtitlePresentationAnimation.FadePop, preset.PresentationAnimation);
        Assert.AreEqual(80, preset.EntryFadeMilliseconds);
        Assert.AreEqual(80, preset.ExitFadeMilliseconds);
        Assert.AreEqual(1.12d, preset.IntroScale, 0.0001d);
        Assert.AreEqual(new SubtitleColor(0, 255, 220, 20), preset.KaraokeHighlightColor);
    }

    [TestMethod]
    public async Task ApplyStylePreset_UppercasesAndReflowsUsingPresetLimits()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(4),
                "Once we have improved the subtitle timing and style")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var styled = _service.ApplyStylePreset(draft);

        Assert.AreEqual(1, styled.Cues.Count);
        Assert.AreEqual("SocialImpact", styled.Preset.Name);
        Assert.IsTrue(styled.Cues[0].Text.All(character => !char.IsLetter(character) || char.IsUpper(character)));
        Assert.IsTrue(styled.Cues[0].Text.Contains('\n'));
        Assert.IsTrue(styled.Cues[0].Text.Split('\n').All(line => line.Length <= styled.Preset.MaxCharsPerLine));
        Assert.AreEqual(0, styled.Issues.Count);
    }

    [TestMethod]
    public async Task ApplyStylePreset_PreservesCueIdentityAndTiming()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "first subtitle")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var styled = _service.ApplyStylePreset(draft);

        Assert.AreEqual(draft.Cues[0].Id, styled.Cues[0].Id);
        Assert.AreEqual(draft.Cues[0].Start, styled.Cues[0].Start);
        Assert.AreEqual(draft.Cues[0].End, styled.Cues[0].End);
    }

    [TestMethod]
    public async Task BuildStyledAss_WithCaptionBox_EmitsFadeAnimation()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "caption box sample")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var styled = _service.ApplyStylePreset(draft, StyledSubtitlePresets.CaptionBox);
        var ass = SubtitlesService.BuildStyledAss(styled);

        StringAssert.Contains(ass, "Style: CaptionBox");
        StringAssert.Contains(ass, @"{\fad(140,140)}");
    }

    [TestMethod]
    public async Task RenderKaraokeAss_WithNeonKaraoke_EmitsPopAnimation()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "neon karaoke sample")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var ass = _service.RenderKaraokeAss(draft, KaraokeSubtitlePresets.NeonKaraoke);

        StringAssert.Contains(ass, @"{\fad(80,80)\fscx112\fscy112\t(0,160,\fscx100\fscy100)}");
        StringAssert.Contains(ass, "Style: NeonKaraoke");
        StringAssert.Contains(ass, "&H00FFFFFF&"); // White base color
        StringAssert.Contains(ass, "&H0014DCFF&"); // Vibrant cyan/yellow highlight for NeonKaraoke (RGB 255,220,20)
    }

    [TestMethod]
    public async Task RenderKaraokeAss_WithPunch_UsesWhiteBaseAndOrangeHighlight()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "punch karaoke sample")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var ass = _service.RenderKaraokeAss(draft, KaraokeSubtitlePresets.Punch);

        StringAssert.Contains(ass, "Style: Punch");
        StringAssert.Contains(ass, "&H00FFFFFF&"); // White base color
        StringAssert.Contains(ass, "&H000082FF&"); // Vibrant orange highlight color for Punch (RGB 255,130,0)
        StringAssert.Contains(ass, "Arial Black"); // Punch uses Arial Black font
        // Punch uses instant fill, so should have {\k tags, not {\kf
        StringAssert.Contains(ass, @"{\k");
    }

    [TestMethod]
    public async Task ApplyStylePreset_WithPlacement_UsesExactPositionOverrideMetadata()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "placed subtitle")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var styled = _service.ApplyStylePreset(
            draft,
            placement: new SubtitlePlacementOptions
            {
                NormalizedX = 0.15d,
                NormalizedY = 0.2d
            });

        Assert.AreEqual(SubtitleVisualAlignment.TopLeft, styled.Preset.Alignment);
        Assert.AreEqual(288, styled.Preset.PositionX);
        Assert.AreEqual(216, styled.Preset.PositionY);
        Assert.IsTrue(styled.Preset.MarginLeft > 0);
        Assert.IsTrue(styled.Preset.MarginVertical > 0);
    }

    [TestMethod]
    public async Task RenderKaraokeAss_WithPlacement_EmbedsAssPositionOverride()
    {
        _audioTranscriptionService.Segments =
        [
            new AudioTranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "center cue")
        ];

        var draft = await _service.GenerateAdvancedDraftAsync(CreateInputFile());
        var ass = _service.RenderKaraokeAss(
            draft,
            placement: new SubtitlePlacementOptions
            {
                NormalizedX = 0.5d,
                NormalizedY = 0.5d
            });

        StringAssert.Contains(ass, @"\an5\pos(960,540)");
    }

    private string CreateInputFile()
    {
        var input = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(input, [0x52, 0x49, 0x46, 0x46]);
        return input;
    }

    private sealed class FakeAudioTranscriptionService : IAudioTranscriptionService
    {
        public IReadOnlyList<AudioTranscriptionSegment> Segments { get; set; } = [];
        public IReadOnlyList<AudioTranscriptionWord>? Words { get; set; }

        public bool IsInstalled() => true;

        public Task InstallAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InstallAsync(IProgress<AudioTranscriptionInstallProgress>? progress, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeToSegmentsAsync(string inputPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Segments);
        }

        public Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<AudioTranscriptionWord>)(Words ?? BuildWordsFromSegments()));
        }

        public Task<IReadOnlyList<AudioTranscriptionSegment>> TranscribeToSegmentsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
        {
            progress?.Report(new AudioTranscriptionProgress
            {
                Stage = AudioTranscriptionStage.PreparingAudio,
                OverallPercent = 0.1d,
                StagePercent = 0.5d,
                StageDescription = "Preparing audio for transcription"
            });
            progress?.Report(new AudioTranscriptionProgress
            {
                Stage = AudioTranscriptionStage.Transcribing,
                OverallPercent = 0.95d,
                StagePercent = 1d,
                StageDescription = "Transcribing audio"
            });

            return Task.FromResult(Segments);
        }

        public Task<IReadOnlyList<AudioTranscriptionWord>> TranscribeToWordsAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default)
        {
            progress?.Report(new AudioTranscriptionProgress
            {
                Stage = AudioTranscriptionStage.PreparingAudio,
                OverallPercent = 0.1d,
                StagePercent = 0.5d,
                StageDescription = "Preparing audio for transcription"
            });
            progress?.Report(new AudioTranscriptionProgress
            {
                Stage = AudioTranscriptionStage.Transcribing,
                OverallPercent = 0.95d,
                StagePercent = 1d,
                StageDescription = "Transcribing audio"
            });

            return Task.FromResult((IReadOnlyList<AudioTranscriptionWord>)(Words ?? BuildWordsFromSegments()));
        }

        public Task<string> TranscribeToTextAsync(string inputPath, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        public Task<string> TranscribeToTextAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        public Task<string> TranscribeToTimestampedTextAsync(string inputPath, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        public Task<string> TranscribeToTimestampedTextAsync(string inputPath, IProgress<AudioTranscriptionProgress>? progress, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        private IReadOnlyList<AudioTranscriptionWord> BuildWordsFromSegments()
        {
            var words = new List<AudioTranscriptionWord>();
            foreach (var segment in Segments)
            {
                var text = segment.Text.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                words.Add(new AudioTranscriptionWord(segment.Start, segment.End, text));
            }

            return words;
        }
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value)
        {
            Items.Add(value);
        }
    }
}
