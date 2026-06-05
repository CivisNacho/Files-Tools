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

Built-in preset for styled `.ass` subtitle output (`StyledSubtitlePresets`):

- `StyledSubtitlePresets.SocialImpact` — bold social-style subtitles (the single internal base):
  - primary font: `Impact`, fallback stack: `Impact`, `Anton`, `Bebas Neue`, `Arial Black`
  - uppercase transform, white fill, thick black outline
  - fade-plus-pop animation, bottom-center alignment
  - max lines: 2, max chars per line: 26

The page layer lets the user customise font, size, outline width, vertical margin, bold, text
transform, word fill colour, and outline colour on top of this base. No additional styled presets
are exposed in the catalog — customisation replaces preset selection for the styled path.

### Karaoke Subtitle Presets

Built-in presets for karaoke `.ass` subtitle output (`KaraokeSubtitlePresets`). Each preset has a distinct default font for visual differentiation:

- `KaraokeSubtitlePresets.GlowKaraoke` — soft entry glow + per-word colour sweep (default preset)
  - **default font**: `Segoe UI Semibold`
  - line blurs in sharp on entry (`\blur8` → `\blur0` over 240 ms), giving a neon-glow feel
  - electric cyan (`#00FFD200`) highlight sweeps left-to-right through each word (`\kf`)
  - 80 ms entry/exit fade; 10 px dark navy outline for contrast
  - bottom-center alignment, designed for music and dramatic narration

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

#### Resolution-adaptive sizing

Presets are authored in a fixed 1920×1080 design space. To render at a consistent, undistorted size on any
resolution/aspect, `RenderKaraokeAss(...)`, `RenderStyledAss(...)` and `ApplyStylePreset(...)` take an
optional `SubtitleRenderTarget` (the real video width/height); the video editor probes the source file for
its true display dimensions (rotation-aware) and passes them.

`ApplyTargetResolutionToPreset(...)` rewrites the (placement-applied) preset into the target frame's
coordinate space — a single place that **all** styles flow through:

- `PlayResX`/`PlayResY` become the video size, so libass renders 1:1 (no non-uniform stretch),
- font, outline, shadow and the vertical margin scale by the **height** ratio (standard subtitle scaling),
- horizontal margins and the absolute `\pos` placement scale by the width/height ratios, so a placed
  subtitle lands at the same relative spot (this is why a portrait frame's `\pos` is e.g. `360,1126`, not
  the design-space `960,950` which would sit off a 720-wide frame),
- for the **chunked** style only (which is single-line and cannot wrap), the font is additionally clamped to
  fit the usable frame width (average-glyph-advance heuristic, since no font metrics are available).

A null target — or a target equal to the design resolution (16:9 1080p) — is a no-op, so existing output is
unchanged. Wrapping styles (`WrapStyle 0`, `MaxLines 2`) reflow within the real frame; only the unwrapped
chunked style needs the extra width clamp.

The preset model remains separate from file export so future styled outputs can reuse the same readability pipeline.

### JSON preset files

Presets can also be defined as JSON files instead of (or in addition to) the hardcoded C# factories,
so adding or tweaking a look is data, not code. The pieces live in `Services/Presets/`:

- `SubtitlePresetDto` — the JSON shape. Every field maps 1:1 onto `SubtitleStylePreset`, with the
  same defaults, so a minimal file (just `id` plus a few fields) still deserializes sensibly. Carries
  `schemaVersion`, the catalog metadata (`id`, `displayName`, `kind`), and an optional composable
  `effects` list mirroring `SubtitleEffect`.
- `SubtitleColorJsonConverter` — (de)serializes `SubtitleColor` as `"#AARRGGBB"`. Alpha follows the
  ASS/domain convention (`00` = opaque, `FF` = transparent); this converter is the single place that
  documents and enforces it.
- `SubtitlePresetJsonContext` — source-generated `System.Text.Json` context (trim/AOT safe, no
  reflection). Enum-typed properties serialize **by name**, which makes enum member names part of the
  preset file contract — renaming an enum member breaks existing preset files.
- `SubtitlePresetMapper` — the only bridge between JSON and the renderer. `ToPreset` builds an
  immutable `SubtitleStylePreset`; `ToCatalogEntry` wraps it in a `SubtitleStyleCatalogEntry` whose
  factory returns the single pre-built (immutable) instance.
- `SubtitlePresetLoader` — reads two directories and merges them by id, **user overrides built-in**:
  1. built-ins shipped under `Assets/Presets/*.json` (resolved via `AppContext.BaseDirectory`, so it
     works packaged or unpackaged), then
  2. user presets under `%LOCALAPPDATA%\FilesTools\Presets\*.json`.

  A file that fails to parse or lacks a required field is skipped and surfaced via
  `SubtitlePresetLoadError` rather than aborting the whole load. The loader uses plain `System.IO`
  (not WinRT `ApplicationData`), so it is unit-testable and packaging-agnostic.

`SubtitleStyleCatalog.RegisterPresets(...)` merges loaded entries over the built-ins: an entry whose
id matches a built-in replaces it **in place** (keeping display order); new ids are appended. The
built-in C# factories remain as a guaranteed fallback if the JSON assets are missing or malformed.
`App` calls the loader at startup (failures there are non-fatal). The eight built-in styles also ship
as JSON under `Assets/Presets/` so they are the canonical, editable source going forward.

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
