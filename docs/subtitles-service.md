# Subtitles Service

`SubtitlesService` now handles both plain `.srt` generation and the advanced subtitle pipeline built on top of Whisper timing.

## Service Location

- `Services/SubtitlesService.cs`

## Responsibility

This service complements `AudioTranscriptionService`.

`AudioTranscriptionService` handles:

- Whisper model installation
- media preparation
- local transcription
- timestamped segment extraction
- timestamped word extraction

`SubtitlesService` handles:

- generating plain `.srt` files from transcript segments
- generating advanced transcription drafts for user review
- building advanced subtitle drafts from reviewed timestamped transcription segments
- generating karaoke-style `.ass` files from reviewed transcription or direct one-shot calls
- postprocessing cues for readability and timing quality
- rendering final styled and karaoke ASS output
- applying user corrections to processed subtitle drafts
- applying visual subtitle style presets

## Public API

- `GenerateSrtAsync(...)`
- `GenerateAdvancedTranscriptionDraftAsync(...)`
- `GenerateAdvancedDraftAsync(...)`
- `BuildSubtitleDraftFromTranscription(...)`
- `GenerateKaraokeAssAsync(...)`
- `RenderStyledAss(...)`
- `RenderKaraokeAss(...)`
- `ApplyCorrections(...)`
- `ApplyStylePreset(...)`

## Core Models

- `TranscriptionDraft`
  - raw timestamped transcription segments for review before ASS rendering
- `TranscriptionSegment`
  - segment id, start, end, and text
- `TranscriptionSegmentCorrection`
  - user text correction payload for a transcription segment
- `SubtitleDraft`
  - processed subtitle cues plus validation issues
- `StyledSubtitleDraft`
  - processed cues with a selected visual preset
- `SubtitleCue`
  - cue id, start, end, and text
- `SubtitleSegmentCorrection`
  - user override payload for text and/or timing
- `SubtitlePostprocessingOptions`
  - readability and timing thresholds
- `SubtitleStylePreset`
  - visual preset metadata for future ASS and other styled outputs

## Plain SRT Path

`GenerateSrtAsync(...)` keeps the original simple behavior:

- requests timestamped segments from `AudioTranscriptionService`
- skips blank or whitespace-only transcript segments
- numbers cues sequentially starting at `1`
- uses standard SRT timestamps: `HH:mm:ss,fff`
- forces a minimum positive duration of `1 ms` when end is not later than start
- writes UTF-8 without BOM

## Advanced Review Path

The current advanced app flow is intentionally staged:

1. `AudioTranscriptionService` generates timestamped Whisper segments
2. `SubtitlesService.GenerateAdvancedTranscriptionDraftAsync(...)` exposes those segments for UI review
3. the user edits transcription text per segment in the editor
4. `SubtitlesService` builds the final subtitle draft or karaoke output after corrections
5. final `.ass` is rendered only at the end

Important rule for app integration:

- never edit rendered ASS text for advanced flow
- review timestamped transcription first
- treat `.ass` as an output artifact for mux/burn, not as an editable source

`BuildSubtitleDraftFromTranscription(...)` takes the reviewed `TranscriptionDraft` and runs the subtitle-specific postprocessing pipeline:

1. normalize raw text
2. remove empty or invalid cues
3. fix overlapping timestamps
4. merge tiny fragments
5. split oversized cues
6. reflow lines
7. adjust timing for readability
8. clamp gaps and overlaps
9. re-index cues
10. validate output

This means Whisper segment timing is the review source, but `SubtitlesService` remains the canonical cue builder for advanced styled output.

## Karaoke ASS Path

There are now two karaoke paths:

- `GenerateKaraokeAssAsync(...)`
  - one-shot generation for callers that want immediate karaoke ASS output
- `RenderKaraokeAss(...)`
  - final karaoke rendering from a reviewed `TranscriptionDraft`

The review-based karaoke path used by the video editor uses this pipeline:

1. timestamped transcription segments
2. per-segment user corrections
3. synthetic aligned words distributed across reviewed segment timing
4. karaoke cue grouping and line layout
5. ASS rendering

The one-shot karaoke generation path calls `AudioTranscriptionService.TranscribeToSegmentsAsync(...)` and synthesizes per-word timing from segment envelopes (duration distributed proportional to word character count).

The renderer behavior is:

- output path is normalized to `.ass`
- output is UTF-8 without BOM
- one base dialogue event per cue shows the full cue text
- one overlay dialogue event per word highlights only the active word
- past and future words remain visible through the base event, while the overlay event keeps non-active words transparent
- default visual style is bold bottom-centered text with white fill, black outline, and a yellow active-word highlight

## Styling Layer

`ApplyStylePreset(...)` is a separate layer on top of `SubtitleDraft`.

Recommended advanced app pipeline:

1. generate `TranscriptionDraft`
2. edit transcription segments in UI
3. convert reviewed transcription into `SubtitleDraft`
4. apply styling with `ApplyStylePreset(...)`
5. render final `.ass` with `RenderStyledAss(...)`
6. use the rendered `.ass` for mux/burn

### Styled ASS Subtitle Presets

Built-in presets for styled `.ass` subtitle output (`StyledSubtitlePresets`):

- `StyledSubtitlePresets.SocialImpact` — bold social-style subtitles:
  - primary font: `Impact`, fallback stack: `Impact`, `Anton`, `Bebas Neue`, `Arial Black`
  - uppercase transform, white fill, thick black outline
  - fade-plus-pop animation, bottom-center alignment
  - max lines: 2, max chars per line: 28

- `StyledSubtitlePresets.CleanSans` — clean sans-serif captions with subtle fade-in/out
- `StyledSubtitlePresets.CaptionBox` — boxed captions with opaque background styling
- `StyledSubtitlePresets.BroadcastLowerThird` — broadcast-style lower thirds with uppercase text and pop animation

### Karaoke Subtitle Presets

Built-in presets for karaoke `.ass` subtitle output (`KaraokeSubtitlePresets`). Each preset has a distinct default font for visual differentiation:

- `KaraokeSubtitlePresets.NeonKaraoke` — neon-style karaoke with bold pop animation
  - **default font**: `Segoe UI Semibold`
  - bright cyan-to-yellow highlight color for high-energy word effects
  - scale pop (112% → 100%) animation on entry
  - 80ms fade duration, bottom-center alignment
  - designed for fast-paced, energetic presentations

- `KaraokeSubtitlePresets.Punch` — bold, high-impact word emphasis style
  - **default font**: `Arial Black` (bold, standard case)
  - white fill with strong black outline for maximum contrast
  - user-selectable highlight color for current-word emphasis (default: orange)
  - instant fill (no animation) — highlights entire word at once
  - 10px outline, 1.5px black shadow for depth
  - optimized for emphasizing individual words with full-word coloring

The preset model remains separate from file export so future styled outputs can reuse the same readability pipeline.

## Progress

The service reuses `AudioTranscriptionProgress`.

Transcription work comes from `AudioTranscriptionService`, then `SubtitlesService` reports:

- `WritingSubtitles`
- `Completed`

This keeps the UI progress behavior consistent in both the audio and video editors.

## UI Usage

Current subtitle generation flows that use this service:

- `Pages/AudioEditorPage.xaml.cs`
- `Pages/VideoEditorPage.xaml.cs`

Current video editor behavior:

- the transcription review panel only appears inside the `Media:Subtitles` section
- styled ASS and karaoke ASS both pause at transcription review first
- the review panel has no manual apply button
- review edits are applied automatically when the user processes the video
