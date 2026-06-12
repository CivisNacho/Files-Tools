using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Files_Tools.Services;

/// <summary>
/// ASS rendering half of <see cref="SubtitlesService"/>: styled and karaoke ASS generation,
/// karaoke cue building, preset normalization/placement/target-resolution transforms, and the
/// supporting render-only nested types.
/// </summary>
public sealed partial class SubtitlesService
{
    internal static string BuildStyledAss(StyledSubtitleDraft styledDraft)
    {
        ArgumentNullException.ThrowIfNull(styledDraft);

        var preset = styledDraft.Preset;
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.Append("Title: ").AppendLine(preset.ScriptTitle);
        builder.AppendLine("ScriptType: v4.00+");
        builder.Append("PlayResX: ").AppendLine(preset.PlayResX.ToString());
        builder.Append("PlayResY: ").AppendLine(preset.PlayResY.ToString());
        builder.Append("WrapStyle: ").AppendLine(preset.WrapStyle.ToString());
        builder.Append("ScaledBorderAndShadow: ").AppendLine(preset.ScaledBorderAndShadow ? "yes" : "no");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.Append("Style: ")
            .Append(preset.AssStyleName).Append(',')
            .Append(preset.PrimaryFontFamily).Append(',')
            .Append(preset.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(ToAssColor(preset.FillColor)).Append(',')
            .Append(ToAssColor(preset.FillColor)).Append(',')
            .Append(ToAssColor(preset.OutlineColor)).Append(',')
            .Append(ToAssColor(preset.ShadowColor)).Append(',')
            .Append(preset.Bold ? "-1" : "0").Append(',')
            .Append(preset.Italic ? "-1" : "0").Append(",0,0,100,100,0,0,1,")
            .Append(preset.OutlineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(preset.ShadowDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(GetAssAlignmentCode(preset.Alignment)).Append(',')
            .Append(preset.MarginLeft).Append(',')
            .Append(preset.MarginRight).Append(',')
            .Append(preset.MarginVertical).Append(',')
            .AppendLine(preset.UseBackgroundBox ? "3" : "1");
        builder.AppendLine();

        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var cue in styledDraft.Cues)
        {
            builder.Append("Dialogue: 0,")
                .Append(FormatAssTimestamp(cue.Start)).Append(',')
                .Append(FormatAssTimestamp(cue.End)).Append(',')
                .Append(preset.AssStyleName).Append(", ,0,0,0,,")
                .Append(BuildAssCueOverrides(preset, cue.Start, cue.End))
                .AppendLine(cue.Text.Replace("\r", string.Empty).Replace("\n", "\\N"));
        }

        return builder.ToString();
    }

    private static string BuildKaraokeAss(IReadOnlyList<KaraokeCue> cues, KaraokeRenderPreset preset)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(preset);

        if (preset.TextTransform != SubtitleTextTransform.None)
        {
            foreach (var cue in cues)
            {
                foreach (var word in cue.Words)
                {
                    word.Text = preset.TextTransform switch
                    {
                        SubtitleTextTransform.Uppercase => word.Text.ToUpperInvariant(),
                        SubtitleTextTransform.Lowercase => word.Text.ToLowerInvariant(),
                        _ => word.Text
                    };
                }
            }
        }

        var isDropIn = preset.Fill == KaraokeFill.DropIn;
        var isChunked = preset is { MaxWordsPerChunk: > 0 } && !isDropIn;

        // The preset has already been rewritten into the target frame's coordinate space (PlayRes,
        // font, margins and placement scaled) by ApplyTargetResolutionToPreset, so the values below
        // are used as-is.
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.Append("Title: ").AppendLine(preset.ScriptTitle);
        builder.AppendLine("ScriptType: v4.00+");
        builder.Append("PlayResX: ").AppendLine(preset.PlayResX.ToString());
        builder.Append("PlayResY: ").AppendLine(preset.PlayResY.ToString());
        builder.Append("WrapStyle: ").AppendLine(preset.WrapStyle.ToString());
        builder.Append("ScaledBorderAndShadow: ").AppendLine(preset.ScaledBorderAndShadow ? "yes" : "no");
        builder.AppendLine();

        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.Append("Style: ")
            .Append(preset.StyleName).Append(',')
            .Append(preset.FontFamily).Append(',')
            .Append(preset.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(ToAssColor(isDropIn || isChunked ? preset.BaseColor : preset.HighlightColor)).Append(',')
            .Append(isDropIn ? "&HFF000000&" : ToAssColor(preset.BaseColor)).Append(',')
            .Append(ToAssColor(preset.OutlineColor)).Append(',')
            .Append(ToAssColor(preset.ShadowColor)).Append(',')
            .Append(preset.Bold ? "-1" : "0").Append(',')
            .Append(preset.Italic ? "-1" : "0").Append(',')
            .Append("0,0,100,100,0,0,1,")
            .Append(preset.OutlineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(preset.ShadowDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(GetAssAlignmentCode(preset.Alignment)).Append(',')
            .Append(preset.MarginLeft).Append(',')
            .Append(preset.MarginRight).Append(',')
            .Append(preset.MarginVertical).Append(',')
            .AppendLine(preset.UseBackgroundBox ? "3" : "1");
        builder.AppendLine();

        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var cue in cues)
        {
            if (cue.Words.Count == 0)
            {
                continue;
            }

            if (isDropIn)
            {
                RenderDropInKaraokeEvents(builder, cue, preset);
            }
            else if (isChunked)
            {
                RenderChunkedKaraokeEvents(builder, cue, preset);
            }
            else
            {
                builder.AppendLine(BuildAssDialogueLine(0, cue.Start, cue.End, preset.StyleName, BuildAssCueOverrides(preset, cue.Start, cue.End) + RenderKaraokeCueText(cue, preset)));
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<KaraokeCue> BuildKaraokeCues(IReadOnlyList<AudioTranscriptionWord> words, SubtitlePostprocessingOptions options)
    {
        var cues = BuildProvisionalKaraokeCues(words, options);
        RemoveInvalidKaraokeCues(cues);
        FixKaraokeCueOverlaps(cues);
        MergeTinyKaraokeFragments(cues, options);
        SplitOversizedKaraokeCues(cues, options);
        ReflowKaraokeLines(cues, options);
        AdjustKaraokeCueTimingForReadability(cues, options);
        ClampKaraokeCueGaps(cues, options);
        ReindexKaraokeCues(cues);
        return cues;
    }

    private static IReadOnlyList<KaraokeCue> BuildKaraokeCuesFromSubtitleDraft(IReadOnlyList<SubtitleCue> cues, IReadOnlyList<AudioTranscriptionWord>? sourceWords)
    {
        var karaokeCues = new List<KaraokeCue>();
        foreach (var cue in cues.OrderBy(cue => cue.Start).ThenBy(cue => cue.Id))
        {
            var words = BuildCueWordsFromSubtitleCue(cue, sourceWords);
            if (words.Count == 0)
            {
                continue;
            }

            // The reviewed cue boundaries are authoritative: the line is shown for the full
            // [Start, End] window while words highlight at their own (real) timing inside it.
            var cueStart = cue.Start < TimeSpan.Zero ? TimeSpan.Zero : cue.Start;
            var cueEnd = cue.End > cueStart ? cue.End : cueStart + MinimumPositiveDuration;
            karaokeCues.Add(new KaraokeCue(karaokeCues.Count + 1, cueStart, cueEnd, words));
        }

        return karaokeCues;
    }

    /// <summary>
    /// True when a source word that starts before <paramref name="cueStart"/> extends only
    /// trivially past it (&lt; 100 ms). Wav2Vec2AlignmentService cursor-clamps timestamps within
    /// each segment but not across segments, so the last word of segment N can spill slightly
    /// into the cue that begins segment N+1. Mapping such a word to the cue's first token gives
    /// it a near-zero duration after clamping, which renders as an instant karaoke fill (the
    /// "first word fills entirely" bug). Both the ASS generator and the live preview use this
    /// to drop the artifact so word-to-token mapping stays in sync between the two paths.
    /// </summary>
    internal static bool IsBoundarySpanningArtifact(AudioTranscriptionWord word, TimeSpan cueStart)
    {
        return word.Start < cueStart && word.End - cueStart < TimeSpan.FromMilliseconds(100);
    }

    private static List<KaraokeCueWord> BuildCueWordsFromSubtitleCue(SubtitleCue cue, IReadOnlyList<AudioTranscriptionWord>? sourceWords)
    {
        var normalizedText = cue.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalizedText.Split('\n');
        var weightedTokens = new List<(string Text, bool BreakBefore, int Weight)>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var tokens = SplitWords(lines[lineIndex]);
            for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                weightedTokens.Add((tokens[tokenIndex], lineIndex > 0 && tokenIndex == 0, CountTokenWeight(tokens[tokenIndex])));
            }
        }

        if (weightedTokens.Count == 0)
        {
            return [];
        }

        var cueStart = cue.Start < TimeSpan.Zero ? TimeSpan.Zero : cue.Start;
        var cueEnd = cue.End > cueStart ? cue.End : cueStart + MinimumPositiveDuration;

        // When real word timing overlaps this cue and lines up one-to-one with the cue tokens,
        // use it directly so the karaoke highlight tracks the actually spoken word. Otherwise
        // (no timing available, or the text was edited) fall back to weight-based distribution.
        if (sourceWords is not null)
        {
            var overlapping = new List<AudioTranscriptionWord>();
            foreach (var word in sourceWords)
            {
                if (word.Start < cueEnd && word.End > cueStart && !IsBoundarySpanningArtifact(word, cueStart))
                {
                    overlapping.Add(word);
                }
            }

            if (overlapping.Count == weightedTokens.Count)
            {
                var realOutput = new List<KaraokeCueWord>(weightedTokens.Count);
                var cursor = cueStart;
                for (var index = 0; index < weightedTokens.Count; index++)
                {
                    var start = overlapping[index].Start;
                    if (start < cursor)
                    {
                        start = cursor;
                    }

                    if (start > cueEnd)
                    {
                        start = cueEnd;
                    }

                    var end = overlapping[index].End;
                    if (end <= start)
                    {
                        end = start + MinimumPositiveDuration;
                    }

                    if (end > cueEnd && cueEnd > start)
                    {
                        end = cueEnd;
                    }

                    realOutput.Add(new KaraokeCueWord(weightedTokens[index].Text, start, end)
                    {
                        BreakBefore = weightedTokens[index].BreakBefore
                    });
                    cursor = end;
                }

                return realOutput;
            }
        }

        var totalTicks = Math.Max(MinimumPositiveDuration.Ticks, (cueEnd - cueStart).Ticks);
        var totalWeight = Math.Max(1, weightedTokens.Sum(token => token.Weight));
        long consumedTicks = 0;
        var consumedWeight = 0;
        var output = new List<KaraokeCueWord>(weightedTokens.Count);

        for (var index = 0; index < weightedTokens.Count; index++)
        {
            var token = weightedTokens[index];
            var wordStart = cueStart + TimeSpan.FromTicks(consumedTicks);
            consumedWeight += token.Weight;

            long wordEndTicks;
            if (index == weightedTokens.Count - 1)
            {
                wordEndTicks = totalTicks;
            }
            else
            {
                wordEndTicks = (long)Math.Round(totalTicks * (consumedWeight / (double)totalWeight));
                wordEndTicks = Math.Clamp(wordEndTicks, consumedTicks + MinimumPositiveDuration.Ticks, totalTicks);
            }

            var word = new KaraokeCueWord(token.Text, wordStart, cueStart + TimeSpan.FromTicks(wordEndTicks))
            {
                BreakBefore = token.BreakBefore
            };
            output.Add(word);
            consumedTicks = wordEndTicks;
        }

        return output;
    }

    private static List<KaraokeCue> BuildProvisionalKaraokeCues(IReadOnlyList<AudioTranscriptionWord> words, SubtitlePostprocessingOptions options)
    {
        var cues = new List<KaraokeCue>();
        KaraokeCue? currentCue = null;

        foreach (var word in words)
        {
            var normalizedWord = NormalizeSegmentText(word.Text);
            if (normalizedWord.Length == 0)
            {
                continue;
            }

            var wordStart = word.Start < TimeSpan.Zero ? TimeSpan.Zero : word.Start;
            var wordEnd = word.End > wordStart ? word.End : wordStart + MinimumPositiveDuration;
            var cueWord = new KaraokeCueWord(normalizedWord, wordStart, wordEnd);

            if (currentCue is null)
            {
                currentCue = new KaraokeCue(cues.Count + 1, wordStart, wordEnd, [cueWord]);
                continue;
            }

            var candidateText = NormalizeMergedText(GetKaraokeCueText(currentCue), normalizedWord);
            var gap = wordStart > currentCue.End ? wordStart - currentCue.End : TimeSpan.Zero;
            var candidateDuration = wordEnd - currentCue.Start;
            var softCharacterLimit = Math.Max(options.MaxCharsPerLine * options.MaxLines, options.MaxCharsPerLine + 8);
            var shouldBreak =
                gap >= options.IntentionalPauseAtOrAbove ||
                (EndsSentence(GetKaraokeCueText(currentCue)) && currentCue.End - currentCue.Start >= options.MinimumDuration) ||
                (gap >= TimeSpan.FromMilliseconds(250) && currentCue.End - currentCue.Start >= options.MinimumDuration) ||
                (candidateDuration > options.IdealDurationMax && currentCue.End - currentCue.Start >= options.MinimumDuration) ||
                candidateText.Length > softCharacterLimit;

            if (shouldBreak)
            {
                cues.Add(currentCue);
                currentCue = new KaraokeCue(cues.Count + 1, wordStart, wordEnd, [cueWord]);
                continue;
            }

            currentCue.End = wordEnd;
            currentCue.Words.Add(cueWord);
        }

        if (currentCue is not null)
        {
            cues.Add(currentCue);
        }

        return cues;
    }

    private static void RemoveInvalidKaraokeCues(List<KaraokeCue> cues)
    {
        for (var index = cues.Count - 1; index >= 0; index--)
        {
            var cue = cues[index];
            cue.Words.RemoveAll(word => NormalizeSegmentText(word.Text).Length == 0);
            if (cue.Words.Count == 0)
            {
                cues.RemoveAt(index);
                continue;
            }

            cue.Start = Max(TimeSpan.Zero, cue.Words[0].Start);
            cue.End = Max(cue.Words[^1].End, cue.Start + MinimumPositiveDuration);
        }

        SortKaraokeCues(cues);
    }

    private static void FixKaraokeCueOverlaps(List<KaraokeCue> cues)
    {
        SortKaraokeCues(cues);
        for (var index = 1; index < cues.Count; index++)
        {
            var previous = cues[index - 1];
            var current = cues[index];
            if (current.Start >= previous.End)
            {
                continue;
            }

            var boundary = current.Start;
            if (boundary > previous.Start)
            {
                previous.End = boundary;
            }
            else
            {
                current.Start = previous.End;
            }
        }
    }

    private static void MergeTinyKaraokeFragments(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        var index = 0;
        while (index < cues.Count)
        {
            if (!IsTinyKaraokeCue(cues[index], options))
            {
                index++;
                continue;
            }

            var mergeTarget = ChooseKaraokeMergeTarget(cues, index, options);
            if (mergeTarget is null)
            {
                index++;
                continue;
            }

            if (mergeTarget.Value < index)
            {
                var target = cues[mergeTarget.Value];
                var current = cues[index];
                target.End = Max(target.End, current.End);
                target.Words.AddRange(current.Words);
                cues.RemoveAt(index);
                index = Math.Max(mergeTarget.Value, 0);
            }
            else
            {
                var current = cues[index];
                var target = cues[mergeTarget.Value];
                target.Start = Min(target.Start, current.Start);
                target.Words.InsertRange(0, current.Words);
                cues.RemoveAt(index);
            }
        }
    }

    private static bool IsTinyKaraokeCue(KaraokeCue cue, SubtitlePostprocessingOptions options)
    {
        var duration = GetKaraokeCueDuration(cue);
        if (duration < options.MinimumDuration)
        {
            return true;
        }

        return duration < options.IdealDurationMin && CalculateCps(GetKaraokeCueText(cue), duration) > options.AcceptableCpsMax;
    }

    private static int? ChooseKaraokeMergeTarget(List<KaraokeCue> cues, int index, SubtitlePostprocessingOptions options)
    {
        var current = cues[index];
        var previousIndex = index > 0 ? index - 1 : (int?)null;
        var nextIndex = index < cues.Count - 1 ? index + 1 : (int?)null;

        TimeSpan? previousGap = null;
        if (previousIndex is not null)
        {
            previousGap = Max(current.Start - cues[previousIndex.Value].End, TimeSpan.Zero);
            if (previousGap >= options.IntentionalPauseAtOrAbove)
            {
                previousIndex = null;
                previousGap = null;
            }
        }

        TimeSpan? nextGap = null;
        if (nextIndex is not null)
        {
            nextGap = Max(cues[nextIndex.Value].Start - current.End, TimeSpan.Zero);
            if (nextGap >= options.IntentionalPauseAtOrAbove)
            {
                nextIndex = null;
                nextGap = null;
            }
        }

        if (previousIndex is null && nextIndex is null)
        {
            return null;
        }

        if (previousIndex is null)
        {
            return nextIndex;
        }

        if (nextIndex is null)
        {
            return previousIndex;
        }

        return previousGap <= nextGap ? previousIndex : nextIndex;
    }

    private static void SplitOversizedKaraokeCues(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        var index = 0;
        while (index < cues.Count)
        {
            var cue = cues[index];
            if (!ShouldSplitKaraokeCue(cue, options) || !TrySplitKaraokeCue(cue, options, out var first, out var second))
            {
                index++;
                continue;
            }

            cues[index] = first;
            cues.Insert(index + 1, second);
        }
    }

    private static bool ShouldSplitKaraokeCue(KaraokeCue cue, SubtitlePostprocessingOptions options)
    {
        var duration = GetKaraokeCueDuration(cue);
        if (duration > options.MaximumDuration)
        {
            return true;
        }

        if (options.MaxWordsPerSection is int maxWordsPerSection && CountWords(GetKaraokeCueText(cue)) > maxWordsPerSection)
        {
            return true;
        }

        if (CalculateCps(GetKaraokeCueText(cue), duration) > options.AcceptableCpsMax)
        {
            return true;
        }

        var layout = EvaluateLayout(GetKaraokeCueText(cue), options.MaxCharsPerLine);
        return layout.LineCount > options.MaxLines || layout.MaxLineLength > options.MaxCharsPerLine;
    }

    private static bool TrySplitKaraokeCue(KaraokeCue cue, SubtitlePostprocessingOptions options, out KaraokeCue first, out KaraokeCue second)
    {
        first = null!;
        second = null!;

        if (cue.Words.Count < 2)
        {
            return false;
        }

        var totalWeight = cue.Words.Sum(word => CountTokenWeight(word.Text));
        var runningWeight = 0;
        SplitCandidate? best = null;
        var bestScore = (Priority: int.MaxValue, WordOverflow: int.MaxValue, Distance: int.MaxValue, Overflow: int.MaxValue);

        for (var index = 1; index < cue.Words.Count; index++)
        {
            runningWeight += CountTokenWeight(cue.Words[index - 1].Text);
            var leftText = JoinKaraokeWords(cue.Words.Take(index));
            var rightText = JoinKaraokeWords(cue.Words.Skip(index));
            if (leftText.Length == 0 || rightText.Length == 0)
            {
                continue;
            }

            var leftLayout = EvaluateLayout(leftText, options.MaxCharsPerLine);
            var rightLayout = EvaluateLayout(rightText, options.MaxCharsPerLine);
            var overflow = Math.Max(0, leftLayout.MaxLineLength - options.MaxCharsPerLine) +
                Math.Max(0, rightLayout.MaxLineLength - options.MaxCharsPerLine);
            var priority = GetSplitPriority(TrimClosingPunctuation(cue.Words[index - 1].Text));
            var wordOverflow = options.MaxWordsPerSection is int maxWordsPerSection
                ? Math.Max(0, CountWords(leftText) - maxWordsPerSection) + Math.Max(0, CountWords(rightText) - maxWordsPerSection)
                : 0;
            var distance = Math.Abs(runningWeight - (totalWeight - runningWeight));
            var score = (priority, wordOverflow, distance, overflow);
            if (score.CompareTo(bestScore) < 0)
            {
                best = new SplitCandidate(index, priority);
                bestScore = score;
            }
        }

        if (best is null)
        {
            return false;
        }

        var leftWords = cue.Words.Take(best.Index).Select(word => word.Clone()).ToList();
        var rightWords = cue.Words.Skip(best.Index).Select(word => word.Clone()).ToList();
        if (leftWords.Count == 0 || rightWords.Count == 0)
        {
            return false;
        }

        var boundary = rightWords[0].Start;
        first = new KaraokeCue(cue.Id, leftWords[0].Start, Max(leftWords[^1].End, boundary), leftWords);
        second = new KaraokeCue(cue.Id, boundary, Max(rightWords[^1].End, boundary + MinimumPositiveDuration), rightWords);
        return true;
    }

    private static void ReflowKaraokeLines(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        foreach (var cue in cues)
        {
            foreach (var word in cue.Words)
            {
                word.BreakBefore = false;
            }

            if (options.MaxLines <= 1 || cue.Words.Count <= 1)
            {
                continue;
            }

            if (options.MaxLines == 2)
            {
                var breakIndex = GetBestKaraokeLineBreakIndex(cue.Words, options.MaxCharsPerLine);
                if (breakIndex > 0 && breakIndex < cue.Words.Count)
                {
                    cue.Words[breakIndex].BreakBefore = true;
                }

                continue;
            }

            var currentLineLength = cue.Words[0].Text.Length;
            var currentLineWordCount = 1;
            for (var index = 1; index < cue.Words.Count; index++)
            {
                var candidateLength = currentLineLength + 1 + cue.Words[index].Text.Length;
                var remainingWords = cue.Words.Count - index;
                var usedLines = cue.Words.Count(word => word.BreakBefore) + 1;
                var remainingLines = Math.Max(1, options.MaxLines - usedLines);
                if (candidateLength > options.MaxCharsPerLine && remainingWords >= remainingLines)
                {
                    cue.Words[index].BreakBefore = true;
                    currentLineLength = cue.Words[index].Text.Length;
                    currentLineWordCount = 1;
                    continue;
                }

                currentLineLength = candidateLength;
                currentLineWordCount++;
            }
        }
    }

    private static int GetBestKaraokeLineBreakIndex(IReadOnlyList<KaraokeCueWord> words, int maxCharsPerLine)
    {
        if (words.Count < 2)
        {
            return -1;
        }

        var fullText = JoinKaraokeWords(words);
        if (fullText.Length <= maxCharsPerLine)
        {
            return -1;
        }

        var bestIndex = -1;
        var bestScore = (Fits: int.MaxValue, Overflow: int.MaxValue, SecondLineWordPenalty: int.MaxValue, Balance: int.MaxValue);
        for (var index = 1; index < words.Count; index++)
        {
            var left = JoinKaraokeWords(words.Take(index));
            var right = JoinKaraokeWords(words.Skip(index));
            var overflow = Math.Max(0, left.Length - maxCharsPerLine) + Math.Max(0, right.Length - maxCharsPerLine);
            var fits = overflow == 0 ? 0 : 1;
            var secondLinePenalty = words.Count - index == 1 ? 1 : 0;
            var balance = Math.Abs(left.Length - right.Length);
            var score = (fits, overflow, secondLinePenalty, balance);
            if (score.CompareTo(bestScore) < 0)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static void AdjustKaraokeCueTimingForReadability(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        for (var index = 0; index < cues.Count - 1; index++)
        {
            var current = cues[index];
            var next = cues[index + 1];
            var gap = next.Start - current.End;
            if (gap <= TimeSpan.Zero || gap >= options.IntentionalPauseAtOrAbove)
            {
                continue;
            }

            var duration = GetKaraokeCueDuration(current);
            var desiredDurationSeconds = Math.Max(options.MinimumDuration.TotalSeconds, CountTextWeight(GetKaraokeCueText(current)) / options.GoodCpsMax);
            desiredDurationSeconds = Math.Max(desiredDurationSeconds, duration.TotalSeconds);
            desiredDurationSeconds = Math.Min(desiredDurationSeconds, options.MaximumDuration.TotalSeconds);
            var desiredDuration = TimeSpan.FromSeconds(desiredDurationSeconds);
            if (desiredDuration <= duration)
            {
                continue;
            }

            var leaveGap = gap > options.CloseGapBelow ? options.CloseGapBelow : TimeSpan.Zero;
            var availableExtension = gap - leaveGap;
            if (availableExtension > TimeSpan.Zero)
            {
                current.End += Min(desiredDuration - duration, availableExtension);
            }
        }
    }

    private static void ClampKaraokeCueGaps(List<KaraokeCue> cues, SubtitlePostprocessingOptions options)
    {
        SortKaraokeCues(cues);
        for (var index = 0; index < cues.Count - 1; index++)
        {
            var current = cues[index];
            var next = cues[index + 1];
            if (current.End > next.Start)
            {
                current.End = next.Start;
            }

            var gap = next.Start - current.End;
            if (gap > TimeSpan.Zero && gap < options.CloseGapBelow)
            {
                current.End = next.Start;
            }
        }
    }

    private static void ReindexKaraokeCues(List<KaraokeCue> cues)
    {
        for (var index = 0; index < cues.Count; index++)
        {
            cues[index].Id = index + 1;
        }
    }

    private static void SortKaraokeCues(List<KaraokeCue> cues)
    {
        cues.Sort(static (left, right) =>
        {
            var startComparison = left.Start.CompareTo(right.Start);
            if (startComparison != 0)
            {
                return startComparison;
            }

            var endComparison = left.End.CompareTo(right.End);
            if (endComparison != 0)
            {
                return endComparison;
            }

            return left.Id.CompareTo(right.Id);
        });
    }

    private static TimeSpan GetKaraokeCueDuration(KaraokeCue cue)
    {
        return cue.End > cue.Start ? cue.End - cue.Start : MinimumPositiveDuration;
    }

    private static string GetKaraokeCueText(KaraokeCue cue)
    {
        return JoinKaraokeWords(cue.Words);
    }

    private static string JoinKaraokeWords(IEnumerable<KaraokeCueWord> words)
    {
        return string.Join(" ", words.Select(word => NormalizeSegmentText(word.Text)).Where(text => text.Length > 0));
    }

    private static KaraokeRenderPreset CreateDefaultKaraokePreset(SubtitleStylePreset? preset = null, SubtitlePlacementOptions? placement = null, SubtitleRenderTarget? target = null)
    {
        var basePreset = ApplyTargetResolutionToPreset(
            ApplyPlacementToPreset(NormalizePreset(preset ?? KaraokeSubtitlePresets.GlowKaraoke), placement),
            target);
        return new KaraokeRenderPreset
        {
            ScriptTitle = "Karaoke subtitles",
            StyleName = basePreset.AssStyleName,
            PlayResX = basePreset.PlayResX,
            PlayResY = basePreset.PlayResY,
            WrapStyle = basePreset.WrapStyle,
            ScaledBorderAndShadow = basePreset.ScaledBorderAndShadow,
            FontFamily = basePreset.PrimaryFontFamily,
            FontSize = basePreset.FontSize,
            Bold = basePreset.Bold,
            Italic = basePreset.Italic,
            OutlineWidth = basePreset.OutlineWidth,
            ShadowDepth = basePreset.ShadowDepth,
            Alignment = basePreset.Alignment,
            MarginLeft = basePreset.MarginLeft,
            MarginRight = basePreset.MarginRight,
            MarginVertical = basePreset.MarginVertical,
            PositionX = basePreset.PositionX,
            PositionY = basePreset.PositionY,
            UseBackgroundBox = basePreset.UseBackgroundBox,
            LineEffects = ResolveLineEffects(basePreset),
            Fill = ResolveKaraokeFill(basePreset),
            ExitFadeMilliseconds = basePreset.ExitFadeMilliseconds,
            EntryFadeMilliseconds = basePreset.EntryFadeMilliseconds,
            MaxWordsPerChunk = basePreset.MaxWordsPerChunk,
            ActiveWordScale = ResolveActiveWordScale(basePreset),
            MaxCharsPerLine = basePreset.MaxCharsPerLine,
            TextTransform = basePreset.TextTransform,
            BaseColor = basePreset.FillColor,
            HighlightColor = basePreset.KaraokeHighlightColor,
            OutlineColor = basePreset.OutlineColor,
            ShadowColor = basePreset.ShadowColor
        };
    }

    private static string BuildAssDialogueLine(int layer, TimeSpan start, TimeSpan end, string styleName, string text)
    {
        return $"Dialogue: {layer},{FormatAssTimestamp(start)},{FormatAssTimestamp(end > start ? end : start + MinimumPositiveDuration)},{styleName},,0,0,0,,{text}";
    }

    /// <summary>
    /// Renders a cue in the chunked "autosubtitles" style: at most <c>MaxWordsPerChunk</c> words are on
    /// screen at once, and each word becomes active in its own dialogue event so it can be emphasised
    /// (highlight colour plus an optional scale pop) while the rest of the chunk stays in the base colour.
    /// Events are contiguous and non-overlapping, so exactly one chunk is visible at any moment.
    /// </summary>
    private static void RenderChunkedKaraokeEvents(StringBuilder builder, KaraokeCue cue, KaraokeRenderPreset preset)
    {
        if (cue.Words.Count == 0)
        {
            return;
        }

        var chunkSize = Math.Max(1, preset.MaxWordsPerChunk ?? 1);
        var maxChars = Math.Max(1, preset.MaxCharsPerLine);
        var chunks = SplitWordsIntoChunks(cue.Words, chunkSize, maxChars);

        var positionOverride = BuildAssPositionOverride(preset.Alignment, preset.PositionX, preset.PositionY);
        var entryFade = Math.Clamp(preset.EntryFadeMilliseconds, 0, 5000);
        var exitFade = Math.Clamp(preset.ExitFadeMilliseconds, 0, 5000);
        var activeScale = preset.ActiveWordScale > 0 ? Math.Min(preset.ActiveWordScale, 1.4d) : 1d;
        var hasPop = Math.Abs(activeScale - 1d) > 0.01d;
        var activeScalePercent = Math.Max(1, (int)Math.Round(activeScale * 100d, MidpointRounding.AwayFromZero));
        var highlightColor = ToAssColor(preset.HighlightColor);

        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];

            // The chunk is shown from the cue start (first chunk) or its first word, until the next
            // chunk begins (or the cue end for the last chunk).
            var chunkStart = chunkIndex == 0 ? cue.Start : Max(cue.Start, chunk[0].Start);
            var chunkEnd = chunkIndex == chunks.Count - 1
                ? cue.End
                : Max(chunkStart + MinimumPositiveDuration, chunks[chunkIndex + 1][0].Start);
            if (chunkEnd <= chunkStart)
            {
                chunkEnd = chunkStart + MinimumPositiveDuration;
            }

            for (var activeIndex = 0; activeIndex < chunk.Count; activeIndex++)
            {
                var wordStart = activeIndex == 0 ? chunkStart : Max(chunkStart, chunk[activeIndex].Start);
                var wordEnd = activeIndex == chunk.Count - 1
                    ? chunkEnd
                    : Max(wordStart + MinimumPositiveDuration, chunk[activeIndex + 1].Start);
                if (wordEnd > chunkEnd)
                {
                    wordEnd = chunkEnd;
                }

                if (wordEnd <= wordStart)
                {
                    wordEnd = Min(wordStart + MinimumPositiveDuration, chunkEnd);
                    if (wordEnd <= wordStart)
                    {
                        wordEnd = wordStart + MinimumPositiveDuration;
                    }
                }

                var line = new StringBuilder();

                // Line-level tags: position always; fade only at the chunk's outer edges so the active
                // word does not flicker as the highlight advances within a chunk.
                var leading = new StringBuilder();
                if (positionOverride.Length > 0)
                {
                    leading.Append(positionOverride);
                }

                var thisEntry = activeIndex == 0 ? entryFade : 0;
                var thisExit = activeIndex == chunk.Count - 1 ? exitFade : 0;
                if (thisEntry > 0 || thisExit > 0)
                {
                    leading.Append(FormattableString.Invariant($@"\fad({thisEntry},{thisExit})"));
                }

                if (leading.Length > 0)
                {
                    line.Append('{').Append(leading).Append('}');
                }

                for (var wordIndex = 0; wordIndex < chunk.Count; wordIndex++)
                {
                    var word = chunk[wordIndex];
                    if (wordIndex > 0)
                    {
                        line.Append(word.BreakBefore ? @"\N" : " ");
                    }

                    if (wordIndex == activeIndex)
                    {
                        line.Append(@"{\1c").Append(highlightColor);
                        if (hasPop)
                        {
                            line.Append(@"\fscx").Append(activeScalePercent)
                                .Append(@"\fscy").Append(activeScalePercent)
                                .Append(@"\t(0,120,\fscx100\fscy100)");
                        }

                        line.Append('}')
                            .Append(EscapeAssText(word.Text))
                            .Append(@"{\r}"); // reset to the style (base colour, 100% scale) for the rest
                    }
                    else
                    {
                        line.Append(EscapeAssText(word.Text));
                    }
                }

                builder.AppendLine(BuildAssDialogueLine(0, wordStart, wordEnd, preset.StyleName, line.ToString()));
            }
        }
    }

    private static List<List<KaraokeCueWord>> SplitWordsIntoChunks(IReadOnlyList<KaraokeCueWord> words, int maxWords, int maxChars)
    {
        var chunks = new List<List<KaraokeCueWord>>();
        var current = new List<KaraokeCueWord>();
        var currentLength = 0;

        foreach (var word in words)
        {
            var addition = (current.Count > 0 ? 1 : 0) + word.Text.Length;
            if (current.Count > 0 && (current.Count >= maxWords || currentLength + addition > maxChars))
            {
                chunks.Add(current);
                current = [];
                currentLength = 0;
                addition = word.Text.Length;
            }

            current.Add(word);
            currentLength += addition;
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    private static string RenderKaraokeCueText(KaraokeCue cue, KaraokeRenderPreset preset)
    {
        var builder = new StringBuilder();
        var useInstantFill = preset.Fill == KaraokeFill.Instant;
        AppendKaraokeSyllables(builder, cue, useInstantFill);
        return builder.ToString();
    }

    /// <summary>
    /// Emits the karaoke syllable run for a cue. Each word is anchored to its absolute timing
    /// (rounded relative to the cue's centisecond line start), so per-word rounding never
    /// accumulates into drift. Silence before the first word and between words is bridged with
    /// empty filler syllables so highlighting never fires early.
    /// </summary>
    private static void AppendKaraokeSyllables(StringBuilder builder, KaraokeCue cue, bool useInstantFill)
    {
        var fillTag = useInstantFill ? @"\k" : @"\kf";

        // The Dialogue line start is truncated to centiseconds (see FormatAssTimestamp), so the
        // karaoke clock is measured relative to that same floored value.
        var lineStartCs = (long)Math.Floor(cue.Start.TotalMilliseconds / 10d);
        long emittedCs = 0;

        for (var index = 0; index < cue.Words.Count; index++)
        {
            var word = cue.Words[index];
            var wordStartCs = Math.Max(0L, (long)Math.Round(word.Start.TotalMilliseconds / 10d, MidpointRounding.AwayFromZero) - lineStartCs);
            var wordEndCs = Math.Max(wordStartCs + 1L, (long)Math.Round(word.End.TotalMilliseconds / 10d, MidpointRounding.AwayFromZero) - lineStartCs);

            if (wordStartCs > emittedCs)
            {
                // Silent gap filler. When the word that follows opens a new visual line, append
                // \N to the gap syllable's text so the line break falls *between* syllables rather
                // than inside the incoming word's fill-sweep bounding box — which would cause the
                // renderer to complete the sweep instantly (the "first word fills entirely" bug).
                builder.Append('{').Append(fillTag)
                    .Append((wordStartCs - emittedCs).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('}');
                if (word.BreakBefore)
                {
                    builder.Append(@"\N");
                }

                emittedCs = wordStartCs;
            }
            else if (word.BreakBefore)
            {
                // No gap before this word, but it starts a new visual line. Emit a 1cs filler
                // syllable that owns the \N so the line break falls *between* syllables — not
                // inside the preceding word's syllable text (which would cause the renderer to
                // complete the next word's fill-sweep instantly, the "first word of lane 2 fills
                // entirely" bug). Mirrors the gap-filler approach used when wordStartCs > emittedCs.
                builder.Append('{').Append(fillTag).Append("1}\\N");
                emittedCs += 1;
            }

            var durationCs = Math.Max(1L, wordEndCs - emittedCs);
            // Inter-word space: only for same-line words that follow another word (never after a
            // line break, where \N already provides the visual separation).
            var space = !word.BreakBefore && index > 0 ? " " : string.Empty;
            builder.Append('{').Append(fillTag)
                .Append(durationCs.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('}')
                .Append(space)
                .Append(EscapeAssText(word.Text));
            emittedCs += durationCs;
        }
    }

    private static void RenderDropInKaraokeEvents(StringBuilder builder, KaraokeCue cue, KaraokeRenderPreset preset)
    {
        // DropIn uses standard \kf karaoke tags with SecondaryColour set to fully transparent
        // in the style definition. Words gradually fill from transparent to PrimaryColour as
        // karaoke progresses, creating the "words appearing one by one" effect.

        // Build position override — reuse the same logic as normal karaoke.
        var posOverride = BuildAssPositionOverride(preset.Alignment, preset.PositionX, preset.PositionY);

        // A short entry fade softens the line's appearance as its words drop in; the exit fade
        // carries it back out. Both are clamped, and the whole-line \fad composes with the per-word \kf.
        var entryFade = Math.Clamp(preset.EntryFadeMilliseconds, 0, 5000);
        var exitFade = Math.Clamp(preset.ExitFadeMilliseconds, 0, 5000);
        var overrideTags = new StringBuilder();
        if (posOverride.Length > 0)
            overrideTags.Append(posOverride);
        if (entryFade > 0 || exitFade > 0)
            overrideTags.Append(FormattableString.Invariant($@"\fad({entryFade},{exitFade})"));

        var overridePrefix = overrideTags.Length > 0 ? $"{{{overrideTags}}}" : string.Empty;

        // Build karaoke text with \kf tags (gradual fill per word), drift-free and gap-aware.
        var textBuilder = new StringBuilder();
        AppendKaraokeSyllables(textBuilder, cue, useInstantFill: false);

        builder.AppendLine(BuildAssDialogueLine(0, cue.Start, cue.End, preset.StyleName, overridePrefix + textBuilder.ToString()));
    }

    private static string FormatAssTimestamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var centiseconds = value.Milliseconds / 10;
        return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{centiseconds:00}";
    }

    private static string EscapeAssText(string value)
    {
        return value
            .Replace("\\", @"\\", StringComparison.Ordinal)
            .Replace("{", "(", StringComparison.Ordinal)
            .Replace("}", ")", StringComparison.Ordinal);
    }

    private static string ToAssColor(SubtitleColor color)
    {
        return $"&H{color.Alpha:X2}{color.Blue:X2}{color.Green:X2}{color.Red:X2}&";
    }

    private static string BuildAssCueOverrides(SubtitleStylePreset preset, TimeSpan start, TimeSpan end)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var tags = new List<string>(2);
        var positionOverride = BuildAssPositionOverride(preset.Alignment, preset.PositionX, preset.PositionY);
        if (positionOverride.Length > 0)
        {
            tags.Add(positionOverride);
        }

        var animationOverride = BuildLineOverrideTags(ResolveLineEffects(preset), start, end, preset.KaraokeHighlightColor, preset.OutlineColor);
        if (animationOverride.Length > 0)
        {
            tags.Add(animationOverride);
        }

        return tags.Count == 0 ? string.Empty : $"{{{string.Join(string.Empty, tags)}}}";
    }

    private static string BuildAssCueOverrides(KaraokeRenderPreset preset, TimeSpan start, TimeSpan end)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var tags = new List<string>(2);
        var positionOverride = BuildAssPositionOverride(preset.Alignment, preset.PositionX, preset.PositionY);
        if (positionOverride.Length > 0)
        {
            tags.Add(positionOverride);
        }

        var animationOverride = BuildLineOverrideTags(preset.LineEffects, start, end, preset.HighlightColor, preset.OutlineColor);
        if (animationOverride.Length > 0)
        {
            tags.Add(animationOverride);
        }

        return tags.Count == 0 ? string.Empty : $"{{{string.Join(string.Empty, tags)}}}";
    }

    private static string BuildAssPositionOverride(SubtitleVisualAlignment alignment, int? positionX, int? positionY)
    {
        if (!positionX.HasValue || !positionY.HasValue)
        {
            return string.Empty;
        }

        return FormattableString.Invariant($@"\an{GetAssAlignmentCode(alignment)}\pos({positionX.Value},{positionY.Value})");
    }

    /// <summary>
    /// Returns the effect list a styled preset should render with: its explicit
    /// <see cref="SubtitleStylePreset.Effects"/> when set, otherwise an equivalent list derived
    /// from the legacy animation fields.
    /// </summary>
    private static IReadOnlyList<SubtitleEffect> ResolveLineEffects(SubtitleStylePreset preset)
    {
        return preset.Effects is { Count: > 0 }
            ? preset.Effects
            : DeriveLegacyEffects(preset.PresentationAnimation, preset.EntryFadeMilliseconds, preset.ExitFadeMilliseconds, preset.IntroScale);
    }

    private static IReadOnlyList<SubtitleEffect> DeriveLegacyEffects(SubtitlePresentationAnimation animation, int entryFadeMilliseconds, int exitFadeMilliseconds, double introScale)
    {
        var effects = new List<SubtitleEffect>();
        if (entryFadeMilliseconds > 0)
        {
            effects.Add(SubtitleEffects.EntryFade(entryFadeMilliseconds));
        }

        if (exitFadeMilliseconds > 0)
        {
            effects.Add(SubtitleEffects.ExitFade(exitFadeMilliseconds));
        }

        switch (animation)
        {
            case SubtitlePresentationAnimation.Pop:
            case SubtitlePresentationAnimation.FadePop:
                effects.Add(SubtitleEffects.EntryPop(introScale));
                break;
            case SubtitlePresentationAnimation.DropIn:
                effects.Add(SubtitleEffects.DropIn());
                break;
        }

        return effects;
    }

    /// <summary>
    /// Resolves the karaoke fill behaviour from a preset's effects, falling back to the legacy
    /// presentation animation when no explicit fill effect is present.
    /// </summary>
    private static KaraokeFill ResolveKaraokeFill(SubtitleStylePreset preset)
    {
        if (preset.Effects is { Count: > 0 } effects)
        {
            foreach (var effect in effects)
            {
                switch (effect.Kind)
                {
                    case SubtitleEffectKind.DropIn:
                        return KaraokeFill.DropIn;
                    case SubtitleEffectKind.KaraokeColorInstant:
                        return KaraokeFill.Instant;
                    case SubtitleEffectKind.KaraokeColorSweep:
                        return KaraokeFill.Sweep;
                }
            }
        }

        return preset.PresentationAnimation switch
        {
            SubtitlePresentationAnimation.DropIn => KaraokeFill.DropIn,
            SubtitlePresentationAnimation.None => KaraokeFill.Instant,
            _ => KaraokeFill.Sweep
        };
    }

    /// <summary>
    /// Resolves the active-word entry scale (for chunked karaoke) from an ActiveWordPop effect,
    /// or 1 when there is none.
    /// </summary>
    private static double ResolveActiveWordScale(SubtitleStylePreset preset)
    {
        if (preset.Effects is { Count: > 0 } effects)
        {
            foreach (var effect in effects)
            {
                if (effect.Kind == SubtitleEffectKind.ActiveWordPop)
                {
                    return effect.Scale > 0 ? Math.Min(effect.Scale, 1.4d) : 1d;
                }
            }
        }

        return 1d;
    }

    /// <summary>
    /// Compiles a list of effects into the line-level ASS override tags (fade, entry pop, glow,
    /// and outline flash). This is the single place effects become ASS, so new looks need no
    /// renderer changes beyond adding cases here.
    /// <para>
    /// <paramref name="highlightColor"/> and <paramref name="outlineColor"/> are required for
    /// <see cref="SubtitleEffectKind.KaraokeOutlineFlash"/> (the flash starts at the highlight
    /// colour and fades to the base outline colour); they are ignored by all other effects.
    /// </para>
    /// </summary>
    private static string BuildLineOverrideTags(
        IReadOnlyList<SubtitleEffect> effects,
        TimeSpan start,
        TimeSpan end,
        SubtitleColor? highlightColor = null,
        SubtitleColor? outlineColor = null)
    {
        var entryFade = 0;
        var exitFade = 0;
        double? popScale = null;
        double? glowBlurRadius = null;
        var glowDurationMs = 240;
        var outlineFlashDurationMs = 0;

        foreach (var effect in effects)
        {
            switch (effect.Kind)
            {
                case SubtitleEffectKind.EntryFade:
                    entryFade = Math.Max(entryFade, Math.Clamp(effect.DurationMs, 0, 5000));
                    break;
                case SubtitleEffectKind.ExitFade:
                    exitFade = Math.Max(exitFade, Math.Clamp(effect.DurationMs, 0, 5000));
                    break;
                case SubtitleEffectKind.EntryPop:
                    popScale = effect.Scale;
                    break;
                case SubtitleEffectKind.KaraokeGlowBurst:
                    glowBlurRadius = effect.Scale > 0 ? Math.Min(effect.Scale, 20d) : 8d;
                    glowDurationMs = effect.DurationMs > 0 ? Math.Clamp(effect.DurationMs, 50, 2000) : 240;
                    break;
                case SubtitleEffectKind.KaraokeOutlineFlash:
                    outlineFlashDurationMs = effect.DurationMs > 0 ? Math.Clamp(effect.DurationMs, 50, 2000) : 280;
                    break;
            }
        }

        var builder = new StringBuilder();
        if (entryFade > 0 || exitFade > 0)
        {
            builder.Append(@"\fad(")
                .Append(entryFade)
                .Append(',')
                .Append(exitFade)
                .Append(')');
        }

        if (popScale is double scale)
        {
            scale = scale > 0 ? scale : 1d;
            // Cap the entry pop so it cannot push glyphs past the safe area and clip at the frame edge.
            scale = Math.Min(scale, 1.25d);
            var scalePercent = Math.Abs(scale - 1d) > 0.01d
                ? Math.Max(1, (int)Math.Round(scale * 100d, MidpointRounding.AwayFromZero))
                : 108;
            builder.Append(@"\fscx")
                .Append(scalePercent)
                .Append(@"\fscy")
                .Append(scalePercent)
                .Append(@"\t(0,160,\fscx100\fscy100)");
        }

        // Glow: start blurred and sharpen over glowDurationMs. \blur transitions smoothly in
        // libass and all modern renderers; a value of 0 means fully sharp.
        if (glowBlurRadius is double blurRadius)
        {
            var blurInt = Math.Max(1, (int)Math.Round(blurRadius, MidpointRounding.AwayFromZero));
            builder.Append(@"\blur")
                .Append(blurInt)
                .Append(FormattableString.Invariant($@"\t(0,{glowDurationMs},\blur0)"));
        }

        // Outline flash: outline starts at the highlight colour and fades to the base outline colour.
        if (outlineFlashDurationMs > 0 && highlightColor is not null && outlineColor is not null)
        {
            builder.Append(@"\3c")
                .Append(ToAssColor(highlightColor))
                .Append(FormattableString.Invariant($@"\t(0,{outlineFlashDurationMs},\3c{ToAssColor(outlineColor)})"));
        }

        if (builder.Length > 0 && start >= end)
        {
            return string.Empty;
        }

        return builder.ToString();
    }

    private static int GetAssAlignmentCode(SubtitleVisualAlignment alignment)
    {
        return alignment switch
        {
            SubtitleVisualAlignment.BottomLeft => 1,
            SubtitleVisualAlignment.BottomRight => 3,
            SubtitleVisualAlignment.MiddleLeft => 4,
            SubtitleVisualAlignment.Center => 5,
            SubtitleVisualAlignment.MiddleRight => 6,
            SubtitleVisualAlignment.TopLeft => 7,
            SubtitleVisualAlignment.TopCenter => 8,
            SubtitleVisualAlignment.TopRight => 9,
            _ => 2
        };
    }

    private static SubtitleStylePreset NormalizePreset(SubtitleStylePreset? preset)
    {
        var source = preset ?? StyledSubtitlePresets.SocialImpact;
        var fontFallbacks = source.FontFamilyFallbacks is { Count: > 0 }
            ? source.FontFamilyFallbacks.ToArray()
            : [source.PrimaryFontFamily];

        return new SubtitleStylePreset
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Custom" : source.Name.Trim(),
            AssStyleName = string.IsNullOrWhiteSpace(source.AssStyleName) ? "Default" : source.AssStyleName.Trim(),
            ScriptTitle = string.IsNullOrWhiteSpace(source.ScriptTitle) ? "Styled subtitles" : source.ScriptTitle.Trim(),
            PlayResX = Math.Max(1, source.PlayResX),
            PlayResY = Math.Max(1, source.PlayResY),
            WrapStyle = Math.Clamp(source.WrapStyle, 0, 3),
            ScaledBorderAndShadow = source.ScaledBorderAndShadow,
            PrimaryFontFamily = string.IsNullOrWhiteSpace(source.PrimaryFontFamily) ? "Arial Black" : source.PrimaryFontFamily.Trim(),
            FontFamilyFallbacks = fontFallbacks,
            FontSize = source.FontSize > 0 ? source.FontSize : 72,
            Bold = source.Bold,
            Italic = source.Italic,
            TextTransform = source.TextTransform,
            FillColor = source.FillColor ?? SubtitleColor.White,
            OutlineColor = source.OutlineColor ?? SubtitleColor.Black,
            ShadowColor = source.ShadowColor ?? SubtitleColor.Black,
            KaraokeHighlightColor = source.KaraokeHighlightColor,
            UseBackgroundBox = source.UseBackgroundBox,
            PresentationAnimation = source.PresentationAnimation,
            EntryFadeMilliseconds = Math.Max(0, source.EntryFadeMilliseconds),
            ExitFadeMilliseconds = Math.Max(0, source.ExitFadeMilliseconds),
            IntroScale = source.IntroScale > 0 ? source.IntroScale : 1d,
            Effects = source.Effects,
            OutlineWidth = Math.Max(0, source.OutlineWidth),
            ShadowDepth = Math.Max(0, source.ShadowDepth),
            Alignment = source.Alignment,
            MarginLeft = Math.Max(0, source.MarginLeft),
            MarginRight = Math.Max(0, source.MarginRight),
            MarginVertical = Math.Max(0, source.MarginVertical),
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            MaxLines = Math.Max(1, source.MaxLines),
            MaxCharsPerLine = Math.Max(1, source.MaxCharsPerLine),
            MaxWordsPerChunk = source.MaxWordsPerChunk is int chunk ? Math.Max(1, chunk) : null
        };
    }

    private static SubtitleStylePreset ApplyPlacementToPreset(SubtitleStylePreset preset, SubtitlePlacementOptions? placement)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (placement is null)
        {
            return preset;
        }

        var normalizedX = Math.Clamp(placement.NormalizedX, 0d, 1d);
        var normalizedY = Math.Clamp(placement.NormalizedY, 0d, 1d);
        var width = Math.Max(1, preset.PlayResX);
        var height = Math.Max(1, preset.PlayResY);
        var safeMarginX = Math.Max(24, (int)Math.Round(width * 0.04d, MidpointRounding.AwayFromZero));
        var safeMarginY = Math.Max(24, (int)Math.Round(height * 0.04d, MidpointRounding.AwayFromZero));
        var alignment = ResolveVisualAlignment(normalizedX, normalizedY);

        return new SubtitleStylePreset
        {
            Name = preset.Name,
            AssStyleName = preset.AssStyleName,
            ScriptTitle = preset.ScriptTitle,
            PlayResX = preset.PlayResX,
            PlayResY = preset.PlayResY,
            WrapStyle = preset.WrapStyle,
            ScaledBorderAndShadow = preset.ScaledBorderAndShadow,
            PrimaryFontFamily = preset.PrimaryFontFamily,
            FontFamilyFallbacks = preset.FontFamilyFallbacks,
            FontSize = preset.FontSize,
            Bold = preset.Bold,
            Italic = preset.Italic,
            TextTransform = preset.TextTransform,
            FillColor = preset.FillColor,
            OutlineColor = preset.OutlineColor,
            ShadowColor = preset.ShadowColor,
            KaraokeHighlightColor = preset.KaraokeHighlightColor,
            UseBackgroundBox = preset.UseBackgroundBox,
            PresentationAnimation = preset.PresentationAnimation,
            EntryFadeMilliseconds = preset.EntryFadeMilliseconds,
            ExitFadeMilliseconds = preset.ExitFadeMilliseconds,
            IntroScale = preset.IntroScale,
            Effects = preset.Effects,
            OutlineWidth = preset.OutlineWidth,
            ShadowDepth = preset.ShadowDepth,
            Alignment = alignment,
            MarginLeft = alignment is SubtitleVisualAlignment.BottomLeft or SubtitleVisualAlignment.MiddleLeft or SubtitleVisualAlignment.TopLeft
                ? Math.Max(safeMarginX, (int)Math.Round(normalizedX * width, MidpointRounding.AwayFromZero))
                : preset.MarginLeft,
            MarginRight = alignment is SubtitleVisualAlignment.BottomRight or SubtitleVisualAlignment.MiddleRight or SubtitleVisualAlignment.TopRight
                ? Math.Max(safeMarginX, (int)Math.Round((1d - normalizedX) * width, MidpointRounding.AwayFromZero))
                : preset.MarginRight,
            MarginVertical = alignment switch
            {
                SubtitleVisualAlignment.TopLeft or SubtitleVisualAlignment.TopCenter or SubtitleVisualAlignment.TopRight
                    => Math.Max(safeMarginY, (int)Math.Round(normalizedY * height, MidpointRounding.AwayFromZero)),
                SubtitleVisualAlignment.MiddleLeft or SubtitleVisualAlignment.Center or SubtitleVisualAlignment.MiddleRight
                    => 0,
                _ => Math.Max(safeMarginY, (int)Math.Round((1d - normalizedY) * height, MidpointRounding.AwayFromZero))
            },
            PositionX = Math.Clamp((int)Math.Round(normalizedX * width, MidpointRounding.AwayFromZero), 0, width),
            PositionY = Math.Clamp((int)Math.Round(normalizedY * height, MidpointRounding.AwayFromZero), 0, height),
            MaxLines = preset.MaxLines,
            MaxCharsPerLine = preset.MaxCharsPerLine,
            MaxWordsPerChunk = preset.MaxWordsPerChunk
        };
    }

    /// <summary>
    /// Rewrites a preset into the real frame's coordinate space so subtitles render at a consistent,
    /// undistorted size on any resolution/aspect. PlayRes becomes the target size; font, outline,
    /// shadow and the vertical margin scale by the height ratio (standard subtitle scaling); the
    /// horizontal margins and the absolute placement scale by the width/height ratios so a \pos lands
    /// at the same relative spot. For the chunked "viral" style (which cannot wrap) the font is
    /// additionally clamped to fit the usable width. A null/invalid target, or a target equal to the
    /// design resolution, returns the preset unchanged (a no-op for 16:9 output).
    /// </summary>
    private static SubtitleStylePreset ApplyTargetResolutionToPreset(SubtitleStylePreset preset, SubtitleRenderTarget? target)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (target is not { Width: > 0, Height: > 0 } size
            || (size.Width == preset.PlayResX && size.Height == preset.PlayResY))
        {
            return preset;
        }

        var designWidth = Math.Max(1, preset.PlayResX);
        var designHeight = Math.Max(1, preset.PlayResY);
        var scaleX = size.Width / (double)designWidth;
        var scaleY = size.Height / (double)designHeight;

        var marginLeft = (int)Math.Round(preset.MarginLeft * scaleX, MidpointRounding.AwayFromZero);
        var marginRight = (int)Math.Round(preset.MarginRight * scaleX, MidpointRounding.AwayFromZero);
        var marginVertical = (int)Math.Round(preset.MarginVertical * scaleY, MidpointRounding.AwayFromZero);

        // Standard subtitle sizing: scale the font by the height ratio.
        var fontSize = preset.FontSize * scaleY;

        // Fit-to-frame clamp: for the chunked "viral" style every chunk is a single, non-wrapping
        // line, so we must ensure MaxCharsPerLine characters (plus the active-word pop) fit within
        // the usable width. Non-chunked presets rely on libass WrapStyle wrapping and do not need a
        // font-size cap — clamping them would prevent user-configured font sizes from taking effect
        // on portrait / square frames even though libass will wrap the text naturally.
        if (preset.MaxWordsPerChunk is > 0)
        {
            var usableWidth = Math.Max(1, size.Width - marginLeft - marginRight);
            var maxChars = Math.Max(1, preset.MaxCharsPerLine);
            var activeScale = ResolveActiveWordScale(preset);
            activeScale = activeScale > 0 ? Math.Min(activeScale, 1.4d) : 1d;

            const double averageGlyphAdvance = 0.62d;
            var widthFitFont = usableWidth / (maxChars * averageGlyphAdvance * activeScale);
            fontSize = Math.Min(fontSize, widthFitFont);
        }

        var fontRatio = preset.FontSize > 0 ? fontSize / preset.FontSize : 1d;

        return new SubtitleStylePreset
        {
            Name = preset.Name,
            AssStyleName = preset.AssStyleName,
            ScriptTitle = preset.ScriptTitle,
            PlayResX = size.Width,
            PlayResY = size.Height,
            WrapStyle = preset.WrapStyle,
            ScaledBorderAndShadow = preset.ScaledBorderAndShadow,
            PrimaryFontFamily = preset.PrimaryFontFamily,
            FontFamilyFallbacks = preset.FontFamilyFallbacks,
            FontSize = fontSize,
            Bold = preset.Bold,
            Italic = preset.Italic,
            TextTransform = preset.TextTransform,
            FillColor = preset.FillColor,
            OutlineColor = preset.OutlineColor,
            ShadowColor = preset.ShadowColor,
            KaraokeHighlightColor = preset.KaraokeHighlightColor,
            UseBackgroundBox = preset.UseBackgroundBox,
            PresentationAnimation = preset.PresentationAnimation,
            EntryFadeMilliseconds = preset.EntryFadeMilliseconds,
            ExitFadeMilliseconds = preset.ExitFadeMilliseconds,
            IntroScale = preset.IntroScale,
            Effects = preset.Effects,
            OutlineWidth = preset.OutlineWidth * fontRatio,
            ShadowDepth = preset.ShadowDepth * fontRatio,
            Alignment = preset.Alignment,
            MarginLeft = marginLeft,
            MarginRight = marginRight,
            MarginVertical = marginVertical,
            PositionX = preset.PositionX is int px
                ? Math.Clamp((int)Math.Round(px * scaleX, MidpointRounding.AwayFromZero), 0, size.Width)
                : null,
            PositionY = preset.PositionY is int py
                ? Math.Clamp((int)Math.Round(py * scaleY, MidpointRounding.AwayFromZero), 0, size.Height)
                : null,
            MaxLines = preset.MaxLines,
            MaxCharsPerLine = preset.MaxCharsPerLine,
            MaxWordsPerChunk = preset.MaxWordsPerChunk
        };
    }

    private static SubtitleVisualAlignment ResolveVisualAlignment(double normalizedX, double normalizedY)
    {
        var horizontalBand = normalizedX switch
        {
            < 0.33d => -1,
            > 0.67d => 1,
            _ => 0
        };

        var verticalBand = normalizedY switch
        {
            < 0.33d => 1,
            > 0.67d => -1,
            _ => 0
        };

        return (verticalBand, horizontalBand) switch
        {
            (1, -1) => SubtitleVisualAlignment.TopLeft,
            (1, 0) => SubtitleVisualAlignment.TopCenter,
            (1, 1) => SubtitleVisualAlignment.TopRight,
            (0, -1) => SubtitleVisualAlignment.MiddleLeft,
            (0, 0) => SubtitleVisualAlignment.Center,
            (0, 1) => SubtitleVisualAlignment.MiddleRight,
            (-1, -1) => SubtitleVisualAlignment.BottomLeft,
            (-1, 0) => SubtitleVisualAlignment.BottomCenter,
            (-1, 1) => SubtitleVisualAlignment.BottomRight,
            _ => SubtitleVisualAlignment.BottomCenter
        };
    }

    private sealed class KaraokeCue
    {
        public KaraokeCue(int id, TimeSpan start, TimeSpan end, List<KaraokeCueWord> words)
        {
            Id = id;
            Start = start;
            End = end;
            Words = words ?? throw new ArgumentNullException(nameof(words));
        }

        public int Id { get; set; }

        public TimeSpan Start { get; set; }

        public TimeSpan End { get; set; }

        public List<KaraokeCueWord> Words { get; }
    }

    private sealed class KaraokeCueWord
    {
        public KaraokeCueWord(string text, TimeSpan start, TimeSpan end)
        {
            Text = text ?? string.Empty;
            Start = start;
            End = end;
        }

        public string Text { get; set; }

        public TimeSpan Start { get; set; }

        public TimeSpan End { get; set; }

        public bool BreakBefore { get; set; }

        public KaraokeCueWord Clone()
        {
            return new KaraokeCueWord(Text, Start, End)
            {
                BreakBefore = BreakBefore
            };
        }
    }

    private sealed class KaraokeRenderPreset
    {
        public required string ScriptTitle { get; init; }

        public required string StyleName { get; init; }

        public int PlayResX { get; init; }

        public int PlayResY { get; init; }

        public int WrapStyle { get; init; }

        public bool ScaledBorderAndShadow { get; init; }

        public required string FontFamily { get; init; }

        public double FontSize { get; init; }

        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public double OutlineWidth { get; init; }

        public double ShadowDepth { get; init; }

        public SubtitleVisualAlignment Alignment { get; init; }

        public int MarginLeft { get; init; }

        public int MarginRight { get; init; }

        public int MarginVertical { get; init; }

        public int? PositionX { get; init; }

        public int? PositionY { get; init; }

        public bool UseBackgroundBox { get; init; }

        public IReadOnlyList<SubtitleEffect> LineEffects { get; init; } = [];

        public KaraokeFill Fill { get; init; }

        public int ExitFadeMilliseconds { get; init; }

        public int EntryFadeMilliseconds { get; init; }

        /// <summary>When set, render chunked (at most this many words on screen, one event per word).</summary>
        public int? MaxWordsPerChunk { get; init; }

        /// <summary>Active-word entry scale for chunked rendering (1 = no pop).</summary>
        public double ActiveWordScale { get; init; } = 1d;

        public int MaxCharsPerLine { get; init; } = 28;

        public SubtitleTextTransform TextTransform { get; init; }

        public required SubtitleColor BaseColor { get; init; }

        public required SubtitleColor HighlightColor { get; init; }

        public required SubtitleColor OutlineColor { get; init; }

        public required SubtitleColor ShadowColor { get; init; }
    }
}
