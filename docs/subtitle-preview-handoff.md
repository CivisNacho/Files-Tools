# Handoff: Live subtitle preview — font sizing mismatch (Part A) — FIX APPLIED

**Branch:** `SubtitlesV2` (Parts D and B already committed+pushed; Part A is **uncommitted** working changes).
**Status:** Live preview works (renders, draggable, restyles live, chunks karaoke, animates) **except** the
font size for **non-chunked presets** (all Styled + full-line Karaoke) renders **too big** vs the burned
output. Only **WordPop** (chunked karaoke) matches.

This document is a self-contained handoff so another engineer/LLM can finish the sizing fix.

---

## Project context

WinUI 3 app "Files Tools" (packaged MSIX, runs from VS). Feature: generate styled/karaoke `.ass`
subtitles from a video (Whisper transcription + MMS wav2vec2 forced alignment), with a live on-player
preview and the ability to switch styles without regenerating.

Roadmap:
- **Part D — wav2vec2/MMS aligner** for word timestamps: DONE, committed/pushed (`35ce50b`). See
  memory `files-tools-aligner-onnx.md`.
- **Part B — switch styles without regenerating**: DONE, committed/pushed (`b488c67`).
- **Part A — live subtitle preview**: IN PROGRESS (this doc). Uncommitted.
- **Part C — RAM-based Whisper model selection** (`<8GB` Turbo, `≥8GB` Large-v3): NOT STARTED.

Build (x64 Debug):
```
dotnet build "Files Tools.csproj" -c Debug -p:Platform=x64 --nologo -v q
```
GUI can't be driven from the agent sandbox; the user runs it. There is an on-screen yellow debug
overlay (see below) because `Debug.WriteLine` isn't visible without a debugger attached.

---

## The bug

On a **720×1280 vertical** test video:
- **WordPop** (chunked karaoke, `MaxWordsPerChunk=3`): preview font ≈ **42px** → **matches** the burn. ✅
- **Styled presets** (Social Impact `FontSize=86`, etc.) and **full-line karaoke** (Neon/Punch/Bubbly):
  preview font ≈ **110px** → **too big**. The actual burned output is small/correct. ❌

User confirmed explicitly: *"Burn is small/correct, preview too big."*

### Measured data (from the on-screen readout)
```
video 720x1280 | host 778x1378 | bounds 775x1378 | font 92->39px (WordPop, correct)
```
So `bounds.Height = 1378` (the displayed video height in the app) is correct — the vertical video
fills the preview pane height.

---

## How sizing works

### Preview (Pages/VideoEditorPage.xaml.cs)
- `UpdateSubtitlePreview(position)` builds the preset:
  `preset = CreateAdvancedSubtitleStylePresetFromConfiguration()` then
  `preset = _subtitlesService.ApplyRenderTarget(preset, target)`.
- `BuildSubtitlePreviewContent(...)`: `scale = bounds.Height / preset.PlayResY; fontPx = preset.FontSize * scale;`
- For non-chunked, `ApplyRenderTarget` sets `FontSize = designFont * target.Height/1080` and
  `PlayResY = target.Height`, so **fontPx = designFont * bounds.Height / 1080** — the target cancels out.
  → `86 * 1378/1080 ≈ 110px` regardless of target. **That's why the probe-target change did nothing.**

### Burn (Services/SubtitlesService.cs → Services/VideoProcessingService.cs)
- `RenderStyledAss`/`RenderKaraokeAss` call `ApplyTargetResolutionToPreset(preset, target)`
  (`SubtitlesService.cs:2772`). For non-chunked: `fontSize = preset.FontSize * (target.Height/1080)`,
  `PlayResY = target.Height`. For chunked (`MaxWordsPerChunk>0`) it **additionally clamps** the font to
  fit one chunk in `target.Width` (`SubtitlesService.cs:2796-2805`) — this is why WordPop is small.
- `target` comes from `ResolveSubtitleRenderTargetAsync()` (`VideoEditorPage.xaml.cs:1734`) →
  `_videoProcessingService.ProbeSourceAsync(...)`.

### KEY FINDING (the likely root cause)
`ProbeSourceAsync` returns **display dimensions with rotation already applied**
(`VideoProcessingService.cs:745` `GetDisplayDimensions`, comment at `:757`). So:
- `_previewRenderTarget` (probe) **==** `_previewVideoSize` (MediaPlayerElement Natural size) **== 720×1280**.
- Preview target == burn target. So the preview math and the burn's *preset* math are identical
  (both 8% of frame height for styled). Yet the **burned result is smaller**.

Therefore the divergence is **not** in the preview's choice of target — it is in **how FFmpeg actually
burns the `.ass`** (the `subtitles` filter), specifically a probable **orientation/resolution mismatch**:

**Leading hypothesis:** the video is stored **1280×720 landscape with a rotate=90 tag** (plays as
720×1280). The `.ass` is generated with `PlayResX/Y = 720×1280` (display orientation, from the probe).
But the FFmpeg `subtitles` filter likely runs on **storage-orientation frames (1280×720)** *before*
autorotation. libass then scales a `PlayResY=1280` script onto a **720-tall** frame → font scaled by
`720/1280 = 0.5625` → `102 → ~57px`. After autorotate the output is 720×1280 with a ~57px subtitle →
`57/1280 ≈ 4.4%` → **small**. WordPop is already small from its width clamp, so it looks fine either way.

If this hypothesis holds, the burn is effectively under-sizing non-chunked subtitles on rotated/vertical
video due to a PlayRes-vs-frame orientation mismatch.

---

## What to do next (in priority order)

1. **Confirm the orientation mismatch with hard data:**
   - Inspect the actual generated `.ass` for a Styled preset: open the file at the subtitle path
     (`SubtitlePathTextBox.Text`, under `%TEMP%\files-tools-whisper-subtitles\`). Note `PlayResX`,
     `PlayResY`, and the `Style:` `Fontsize`.
   - Inspect the test video with `ffprobe` for `width`, `height`, and the `rotate`/`displaymatrix` tag.
   - Read `VideoProcessingService.BuildSubtitleFilter` (around `:1144`) and the filter-graph assembly:
     determine the resolution/orientation of frames the `subtitles` filter sees, and whether
     autorotation happens before/after it. Confirm whether the burned font is ~57px (hypothesis) vs
     ~102px.

2. **Decide where to fix:**
   - **Option A (fix the burn):** make the burn render the `.ass` in the same orientation/resolution
     the subtitles filter processes (e.g. set `.ass` PlayRes to the *storage* frame the filter sees, or
     force autorotation before `subtitles`, or pass `original_size`/`force_style` correctly). This makes
     non-chunked subtitles the intended ~8% on rotated video and keeps preview==burn. **Preferred if the
     burn is genuinely mis-sizing** (it would also fix the exported file, not just the preview).
   - **Option B (match the preview to the burn):** if the burn's smaller size is considered "correct",
     replicate it in the preview. Concretely, scale the preview font by the **storage** height rather
     than the display height when a rotate tag is present. Requires exposing the rotation / storage dims
     (extend `VideoSourceInfo`/`ProbeSourceAsync` to surface coded size + rotation, or add a
     `SubtitleRenderTarget` that reflects what libass actually renders against).

3. **Re-verify WordPop still matches** after any change (it's the one currently-correct case — don't
   regress it). Also verify a normal **landscape 16:9** video (no rotation) still matches, since that
   path should be unaffected.

---

## Current code state (Part A, uncommitted on `SubtitlesV2`)

All in `Pages/VideoEditorPage.xaml` + `Pages/VideoEditorPage.xaml.cs`, plus one service method.

- **XAML:** `SubtitlePreviewCanvas` overlay added inside `PreviewHost` (between `VideoPlayer` and
  `SubtitlePlacementCanvas`). The inline **Subtitle style** picker (`AdvancedSubtitleStyleComboBox`) +
  hint were added for Part B.
- **Preview engine** (search `// ----- Live subtitle preview (Part A)`):
  - `DispatcherTimer` (50ms) → `SubtitlePreviewTimer_Tick` → `UpdateSubtitlePreview(position)`.
  - `PickPreviewCue` (active cue, else nearest dimmed "ghost" so it's always draggable).
  - `ResolvePreviewWords` (chunks the words for `MaxWordsPerChunk` karaoke; whole cue otherwise).
  - `BuildSubtitlePreviewContent(preset, words, wordStarts, isActive)` — renders outline (8 offset
    copies), fill (per-word runs), shadow, background box, font/weight/italic via
    `ResolvePreviewFontFamily`, positions via `PositionPreviewContent`, runs `PlayPreviewEntryAnimation`.
  - **Draggable:** the rendered subtitle is the placement handle (`SubtitlePreview_Pointer*`), updating
    `_subtitlePlacementX/Y`. The old box (`SubtitlePlacementMarker`) is hidden while preview is active
    (`ShouldShowSubtitlePlacementControls` returns false when `ShouldShowSubtitlePreview()`).
  - **Colour gotcha (fixed):** `SubtitleColor` uses ASS alpha (0=opaque); `ToPreviewColor` inverts it
    for WinUI (0=transparent). Use `ToPreviewColor`, not `ToUiColor`, in the preview.
  - `_previewRenderTarget` cached via `UpdatePreviewRenderTargetAsync()` (called in `MediaPlayer_MediaOpened`).
    **NOTE: currently equals display size (probe returns display dims) → no-op. Likely needs storage
    size + rotation instead — see fix options above.**
  - **Debug overlay:** `ShowPreviewDebugOverlay = true` draws a yellow readout
    (`display | renderTarget | bounds | effFont playResY -> fontPx`). **Remove it (and set the const
    false / delete the block) once sizing is fixed.**
- **Service:** `ISubtitlesService.ApplyRenderTarget(preset, target)` added
  (`SubtitlesService.cs`, wraps `ApplyTargetResolutionToPreset(NormalizePreset(preset), target)`).

### Other notes
- New XAML strings have `x:Uid` but no `.resw` entries yet (English literal fallback): `VideoPage_SubtitleStyle`,
  `VideoPage_SubtitleStyleHint`. Add resources in a localization pass.
- `TranscriptionReadyStatusTextBlock` was removed from XAML by the user; code references were deleted.
- Animations (`PlayPreviewEntryAnimation`): fade + scale "pop" on cue entry; only fire for `active`
  cues during playback (not the dimmed ghost). User reported "no animations" — likely because they were
  paused/off-cue, or the styles tested had `EntryFadeMilliseconds=0`/`IntroScale=1`. Re-confirm once
  size is fixed.

---

## Validated facts (don't re-derive)
- `bounds.Height = 1378` is the correct displayed video height; WordPop at 42px is correct.
- For non-chunked, preview `fontPx = designFont * bounds.Height / 1080` (target cancels) ≈ 110px.
- `ProbeSourceAsync` returns **display** dims (rotation applied) → probe == display == 720×1280.
- The chunked clamp (`SubtitlesService.cs:2796`) is the only width-fit logic; non-chunked has none.
