# Studio Voice — UI Wiring Handoff

The **backend is done, tested, and bundled.** This doc is everything needed to wire the UI in
`Pages/VideoEditorPage.xaml` + `.xaml.cs`, mirroring the existing "Apply audio denoise" feature.

---

## 1. What exists (backend, under `Services/`)

| File | Role |
|---|---|
| `DeepFilterNetDsp.cs` | DSP: Vorbis STFT/ISTFT, ERB bands, features, mask+deep-filter. (internal) |
| `DeepFilterNetService.cs` | DFN3 denoise/dereverb. `EnhanceMono(float[]48k)`. |
| `FlashSrService.cs` | FlashSR bandwidth-extension (fullness). `UpsampleMono(float[]16k)→48k`. |
| `VoiceStudioService.cs` | **The orchestrator you call from the UI.** |

Models are **bundled** (no download): `Assets/Models/DeepFilterNet/combined.onnx`,
`Assets/Models/FlashSr/model.onnx` (registered in `Files Tools.csproj`). They resolve via
`AppContext.BaseDirectory` automatically.

Tests (all green): `DeepFilterNetDspTests` (4), `DeepFilterNetServiceTests`,
`VoiceStudioServiceTests`. Note: per project memory, the GUI itself can't be run in this
environment — validate logic via the test project (`dotnet test`).

---

## 2. The API you'll call

```csharp
var service = new VoiceStudioService();              // uses bundled models
var options = new VoiceStudioOptions
{
    Denoise = true,            // DeepFilterNet3
    SuperResolution = true,    // FlashSR fullness (best on dull/low-bandwidth sources)
    Master = true,             // FFmpeg EQ + compressor + loudnorm -16 LUFS
    // MasteringFilter = "..."  // optional FFmpeg -af override; sensible default provided
};
await service.ProcessAudioAsync(inputPath, outputWavPath, options, progress, ct);
```

- `ProcessAudioAsync` accepts **any FFmpeg-decodable audio/video** as input and writes an
  **enhanced 48 kHz WAV** to `outputWavPath`.
- `IProgress<VoiceStudioProgress>` reports `Stage` (`Extracting`/`Denoising`/`RestoringFullness`/
  `Mastering`/`Finalizing`/`Completed`) + a coarse `Fraction`.
- Availability guards if you want them: `DeepFilterNetService.IsAvailable()`,
  `FlashSrService.IsAvailable()`.

⚠️ **Output is a WAV, not a video.** `ProcessAudioAsync` does **not** remux back into a video
container (unlike `DenoiseVideoAudioAsync`). See §5.

---

## 3. The pattern to mirror — existing denoise wiring

Look at how "Apply audio denoise" is done and copy the shape.

**XAML** (`Pages/VideoEditorPage.xaml`), inside `MediaAudioPanel` (the "Audio and volume" card,
starts ~line 459):
- `EnableAudioDenoiseCheckBox` (~510) + `AudioDenoisePanel` (~511) with its sub-controls.

**Code-behind** (`Pages/VideoEditorPage.xaml.cs`):
- Field: `_videoAudioDenoiseService` (line 73).
- Options built in `BuildOptionsFromUi()` → `VideoDenoiseRequest` (~4018–4052).
- Panel visibility toggling in `UpdateXxxVisibility` helpers (search `AudioDenoisePanel`).
- **Apply flow** (the key part), ~lines 548–596:
  1. `outputPath` = final target; `processingOutputPath` = temp video when a post-step is needed.
  2. `_videoProcessingService.ProcessVideoAsync(..., processingOutputPath, options, progress)`.
  3. If denoise: `await _videoAudioDenoiseService.DenoiseVideoAudioAsync(processingOutputPath,
     outputPath, denoiseOptions, denoiseProgress)` — extracts audio, processes, remuxes to video.

---

## 4. UI steps (recommended)

1. **XAML** — add a card in `MediaAudioPanel` below the denoise block:
   - `CheckBox x:Name="EnableVoiceStudioCheckBox"` "Enhance voice (studio quality)".
   - A sub-`StackPanel x:Name="VoiceStudioPanel"` with three `CheckBox`es:
     `VoiceDenoiseCheckBox`, `VoiceFullnessCheckBox`, `VoiceMasterCheckBox` (default all checked).
   - Wire `Checked/Unchecked` to `AnyOptionChanged_CheckChanged` for the dirty-state plumbing.
   - Add `x:Uid`s + matching `.resw` entries (see project localization conventions).
2. **Code-behind** — add a `private readonly VoiceStudioService _voiceStudioService = new();`
   field. Build a `VoiceStudioOptions` from the three checkboxes in `BuildOptionsFromUi()`
   (return it alongside the existing `denoise`).
3. **Apply flow** — treat voice-studio as a post-step like denoise (see §5 for video vs audio).
4. **Visibility** — show `VoiceStudioPanel` only when `EnableVoiceStudioCheckBox.IsChecked`.
5. **Progress** — map `VoiceStudioProgress.Stage` to the existing `ProcessingStatus*` text/bar
   (reuse the `UpdateDenoiseProgress` style).

Consider whether denoise + voice-studio should be mutually exclusive in the UI (both touch the
audio); simplest is to let voice-studio supersede denoise when enabled.

---

## 5. Video vs audio (the one real gap to close)

`ProcessAudioAsync` outputs a **WAV**. Two cases:

- **Audio-only export**: write/encode that WAV to the chosen output (or run it through the
  existing audio-export path). Done.
- **Video**: you must remux the enhanced WAV back into the video. Cleanest fix — add a
  `ProcessVideoAudioAsync(inputVideo, outputVideo, options, progress, ct)` to
  `VoiceStudioService` that mirrors `VideoAudioDenoiseService.DenoiseVideoAudioAsync`:
  1. `ProcessAudioAsync(inputVideo, tempWav, options)` (already extracts audio internally),
  2. FFmpeg remux: `-i inputVideo -i tempWav -map 0:v -map 1:a -c:v copy -c:a aac -shortest
     outputVideo` (reuse the FFmpeg-run helper already in `VoiceStudioService`).
  Then the apply flow calls it exactly like `DenoiseVideoAudioAsync`.

---

## 6. Tuning notes

- **Mastering** is a plain FFmpeg `-af` chain in `VoiceStudioOptions.MasteringFilter`
  (highpass + corrective EQ + presence + air + `acompressor` + `loudnorm=I=-16`). Adjust to taste;
  expose sliders later if desired.
- **FlashSR** is best on dull/low-bandwidth sources; for already-full-band 48 kHz audio it adds
  little and could be left off (consider auto-detecting source bandwidth later).
- **Performance**: ~3–4 min for 2-min audio on CPU (FlashSR is the bottleneck at ~0.7× realtime;
  DFN3 is ~80× realtime). All CPU, no GPU.

---

## 7. Dev tooling / reference

`tools/deepfilternet-rt/` (cloned `shimondoodkin/deepfilter-rt`) has the validated Python
reference pipeline (`dfn_reference.py`), the ground-truth Rust example, the fixture dumper
(`dump_dfn_fixture.py` → `Files.Tools.Tests/Fixtures/dfn_dsp.json`), and the demo builder
(`build_demo.py` → `demo1..4.wav`). Safe to delete once you're confident; only the bundled `.onnx`
models and the `Services/` code are needed at runtime. (DFN3 = MIT/Apache; FlashSR from HF
`YatharthS/FlashSR`.)
