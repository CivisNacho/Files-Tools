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
  - segment id, start, end, text, and optional real word-level timings (`Words`)
- `TranscriptionSegmentCorrection`
  - user text correction payload for a transcription segment
- `SubtitleDraft`
  - processed subtitle cues plus validation issues, and the preserved real word timeline (`SourceWords`)
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

1. timestamped transcription segments (with real word timing when available)
2. per-segment user corrections
3. per-cue word timing resolved against the real word timeline (synthetic fallback)
4. karaoke cue grouping and line layout
5. ASS rendering

The one-shot karaoke generation path calls `AudioTranscriptionService.TranscribeToSegmentsAsync(...)` and reuses the real per-word timing those segments now carry.

### Word-level timing

Whisper transcription now runs with token timestamps enabled, so each `AudioTranscriptionSegment` can
carry real word-level timings (`AudioTranscriptionSegment.Words`). These flow through the pipeline so the
karaoke highlight tracks the actually spoken word:

- `TranscriptionSegment.Words` carries the real timing into the review draft.
- `SubtitleDraft.SourceWords` preserves the real word timeline across postprocessing (merge/split/reflow)
  so the editor's `RenderKaraokeAss(SubtitleDraft, ...)` path can still use it.
- a cue uses real timing only when the overlapping real words line up one-to-one with the cue's tokens;
  editing a cue's text (or a model without usable token timestamps) falls back to distributing word timing
  across the cue envelope by character weight.

The renderer behavior is:

- output path is normalized to `.ass`
- output is UTF-8 without BOM
- one dialogue event per cue carries the line text with per-word `\k`/`\kf` karaoke tags
- each word is anchored to its absolute timing, rounded relative to the cue's centisecond line start, so
  per-word rounding never accumulates into drift
- silence before the first word and gaps between words are bridged with empty filler syllables so a word
  never highlights early
- entry "pop" scale is capped so it cannot push glyphs past the safe area and clip at the frame edge
- default visual style is bold bottom-centered text with white fill, black outline, and a yellow active-word highlight

## Styling Layer

`ApplyStylePreset(...)` is a separate layer on top of `SubtitleDraft`.

### Composable effects

Animation is described by a list of composable `SubtitleEffect` values rather than a fixed enum, so a new
look is data, not renderer code:

- `SubtitleEffectKind` — `EntryFade`, `ExitFade`, `EntryPop`, `KaraokeColorSweep`, `KaraokeColorInstant`, `DropIn`.
- `SubtitleEffects` — factory helpers (`EntryFade(ms)`, `EntryPop(scale)`, `KaraokeColorInstant()`, …).
- `SubtitleStylePreset.Effects` — when set, drives rendering directly. When null, the renderer derives an
  equivalent list from the legacy `PresentationAnimation` + fade/scale fields, so existing presets are
  unchanged. A single compiler (`BuildLineOverrideTags`) turns the effect list into ASS override tags, and
  the karaoke fill mode (`\k` / `\kf` / drop-in) is resolved from the same effects.
- entry pop scale is capped to a safe ceiling (125%) so it cannot clip at the frame edge.

### Style catalog

`SubtitleStyleCatalog` is the single registry of built-in styles. Each `SubtitleStyleCatalogEntry` carries
an id, display name, `SubtitleStyleKind` (`Styled` / `Karaoke`), and a factory. Adding a style means adding
one catalog entry plus its factory — the renderer needs no changes. `SubtitleStyleCatalog.Entries`,
`ByKind(...)`, `Find(id)`, and `Create(id)` (case-insensitive) let a UI enumerate styles instead of
hard-coding names.

The advanced-subtitle pickers in `VideoEditorPage` and `BatchEditorPage` are driven entirely by this
catalog: they populate the preset combo box from `SubtitleStyleCatalog.ByKind(...)` and store the
selected entry's `Id`. Registering a new catalog entry makes it appear in both pickers automatically,
with no UI code changes.

Recommended advanced app pipeline:

1. generate `TranscriptionDraft`
2. edit transcription segments in UI
3. convert reviewed transcription into `SubtitleDraft`
4. apply styling with `ApplyStylePreset(...)`
5. render final `.ass` with `RenderStyledAss(...)`
6. use the rendered `.ass` for mux/burn

All built-in presets were tuned for on-screen legibility: larger type, outline widths kept roughly
proportional to font size for contrast on any background, and bottom margins lifted into a ~10% safe
area so captions clear platform UI chrome.

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

- `KaraokeSubtitlePresets.WordPop` — autosubtitles-style chunked karaoke
  - **default font**: `Montserrat` (fallbacks `Arial Black`, `Segoe UI Black`)
  - big, centered, uppercase; at most 3 words on screen at once (`MaxWordsPerChunk = 3`)
  - the active word pops in (scale 118% → 100%) and switches to a vivid yellow highlight
  - rendered as one dialogue event per word (see chunked rendering below)

### Chunked karaoke rendering

When a preset sets `MaxWordsPerChunk`, karaoke is rendered in the chunked "viral" style instead of the
classic single-line `\k` sweep:

- words are grouped into chunks of at most `MaxWordsPerChunk` words (and within `MaxCharsPerLine`)
- each word becomes active in **its own dialogue event**, which draws the whole chunk with that word in
  the highlight colour and an optional scale pop (`ActiveWordPop`), the rest in the base colour
- events are contiguous and non-overlapping, so exactly one chunk is visible at a time and the highlight
  advances word by word with no flicker; fades are applied only at a chunk's outer edges
- this path is glitch-free across burners because it uses explicit per-event `\t` transforms rather than
  karaoke templates

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
