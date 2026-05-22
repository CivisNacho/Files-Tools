# DTLN Video Audio Denoise Service

`VideoAudioDenoise` provides local denoise processing for standalone audio files and video audio streams.
The service prepares media with FFmpeg, runs mono DTLN inference through the bundled ONNX engine by default, and writes either a processed audio file or a video remuxed with processed audio.

## Service Location

- `Services/VideoAudioDenoise.cs`

## Public Entry Points

- `DenoiseAudioAsync(string inputAudioPath, string outputAudioPath, AudioDenoiseOptions options, IProgress<DenoiseProgress>? progress = null, CancellationToken ct = default)`
- `DenoiseVideoAudioAsync(string inputVideoPath, string outputVideoPath, VideoAudioDenoiseOptions options, IProgress<DenoiseProgress>? progress = null, CancellationToken ct = default)`
- `ProbeAudioAsync(string inputPath, int? audioStreamIndex = null, CancellationToken ct = default)`

Use `VideoAudioDenoise` as the convenience concrete class, or depend on `IVideoAudioDenoiseService` where an abstraction is useful.

## Bundled DTLN Engine

The parameterless `VideoAudioDenoise` constructor loads `DtlnOnnxDenoiseEngine.CreateDefault()`.
That factory resolves the bundled model files from:

- `Assets/Models/Dtln/model_1.onnx`
- `Assets/Models/Dtln/model_2.onnx`

The engine implements the standard two-stage DTLN ONNX pipeline:

- sample rate: `16000 Hz`
- block length: `512` samples
- block shift: `128` samples
- `model_1.onnx`: magnitude mask prediction
- `model_2.onnx`: time-domain refinement
- output: mono float signal with the same sample count as input

The service still depends on the `IDtlnDenoiseEngine` abstraction so tests and future custom model providers can inject another implementation:

```csharp
public interface IDtlnDenoiseEngine
{
    Task<float[]> DenoiseMonoAsync(
        IReadOnlyList<float> samples,
        int sampleRate,
        IProgress<InferenceProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}
```

`MissingDtlnDenoiseEngine` remains available only as an explicit failure stub for tests or diagnostics.

## Processing Modes

### Mono

`AudioDenoiseMode.Mono` converts the selected audio stream to mono before inference.
The output is always mono, even if the source was stereo or multichannel.

Use this for voice-first content such as interviews, podcasts, lectures, voice notes, tutorials, and narrated screen recordings.

### Stereo

`AudioDenoiseMode.StrongStereo` is exposed in the UI as `Stereo`.
It requires stereo source audio, denoises left and right independently, blends each channel with `DenoiseAmount`, and outputs stereo audio.

Use this when the goal is audible noise suppression across the full stereo image.
Both channels are modified by the model, so this is the only stereo mode exposed in the UX.

## Sample Rate Policy

- Model processing sample rate: `AudioDenoiseOptions.ModelSampleRate`, currently required to be `16000`
- Output sample rate: `AudioDenoiseOptions.OutputSampleRate`
- When `OutputSampleRate` is null, the service tries to preserve the source sample rate
- `DenoiseAmount` blends the original signal with the DTLN result for mono and stereo modes
- `DenoisePasses` supports `1` through `3` DTLN inference passes; extra passes can reduce stronger steady noise but may introduce more speech artifacts

FFmpeg performs input decoding, channel conversion, model-rate resampling, output-rate resampling, and final encoding/remuxing.

## Video Remux Policy

`DenoiseVideoAudioAsync` maps the first video stream and the processed audio stream into the output file.
By default, `VideoAudioDenoiseOptions.CopyVideoStream` is true, so the original video stream is copied without re-encoding.

If `OutputAudioCodec` is not provided, the service uses:

- `libopus` for `.webm`
- `aac` for other video containers

Use `OutputAudioBitrateKbps` to set `-b:a`.

## Progress

`DenoiseProgress` reports:

- deterministic pipeline stage
- weighted overall progress from `0.0` to `1.0`
- current-stage progress from `0.0` to `1.0`
- processed and total duration where FFmpeg timestamps are available
- whether FFmpeg or model inference is active
- estimated remaining time when enough data exists

The inference engine should report `InferenceProgressInfo` during block or sample processing so long files do not appear frozen during DTLN inference.

## Result Metadata

`DenoiseResult` includes:

- output path
- denoise mode
- model sample rate
- output sample rate
- output channel count
- duration when known
- whether the video stream was copied
- warnings for expected conversions, such as mono conversion, model-rate resampling, and Mid/Side side-channel preservation

## Expected Exceptions

- `DenoiseValidationException`: invalid options
- `DenoiseUnsupportedMediaException`: no audio stream, missing audio metadata, or incompatible channel mode
- `DenoiseModelException`: missing or failed DTLN inference
- `DenoiseProcessingException`: FFmpeg conversion or internal PCM processing failure
- `DenoiseRemuxException`: final video remux failure
- `OperationCanceledException`: cancellation requested during processing

## Licenses

The denoise feature adds these license obligations:

- DTLN pretrained ONNX models: MIT license, copyright 2020 Nils L. Westhausen, sourced from `breizhn/DTLN`.
- ONNX Runtime / `Microsoft.ML.OnnxRuntime`: MIT license, copyright Microsoft Corporation.

Both are listed on `Pages/LicensesPage.xaml` with selectable license dialogs.
