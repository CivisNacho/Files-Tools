# Audio Processing Service

`AudioProcessingService` provides standalone audio-file processing. It is separate from video audio extraction, and it uses local FFmpeg/FFprobe tooling for predictable conversion, compression, normalization, podcast voice processing, trimming, silence removal, and equalizer work. When podcast denoise is enabled, it delegates denoise to the DTLN-backed `VideoAudioDenoise` service before FFmpeg voice shaping.

## Service Location

- `Services/AudioProcessingService.cs`

## Public API

- `ConvertAsync(...)`
- `CompressAsync(...)`
- `NormalizeAsync(...)`
- `ProcessPodcastAudioAsync(...)`
- `TrimAsync(...)`
- `RemoveSilenceAsync(...)`
- `ApplyEqualizerAsync(...)`

Each operation accepts explicit options, optional `IProgress<AudioProcessProgress>`, and a `CancellationToken`. Each operation returns `AudioProcessResult` populated from an output FFprobe pass. Podcast processing also returns an `AudioAnalysisResult` with pre-processing loudness and peak measurements when FFmpeg can report them.

## Supported Formats And Codecs

V1 maps common output extensions to FFmpeg encoders:

- `.mp3` -> `libmp3lame`
- `.m4a` / `.aac` -> `aac`
- `.opus` -> `libopus`
- `.ogg` -> `libvorbis`
- `.flac` -> `flac`
- `.wav` -> `pcm_s16le`

`Channels = null` preserves the source layout. `Channels = 1` outputs mono. `Channels = 2` outputs stereo. Other channel counts are rejected in v1.

## Operations

### Convert

Use `AudioConversionOptions` to set output format, codec, bitrate, sample rate, channel count, and metadata preservation. Metadata is preserved by default; when disabled the service passes `-map_metadata -1`.

### Compress

Use `AudioCompressionOptions` with `Lossy` or `Lossless`. Lossy compression uses the selected/inferred encoder and optional target bitrate. Lossless compression supports FLAC in v1.

### Normalize

Use `AudioNormalizationOptions`.

- `Peak` uses FFmpeg volume adjustment with an optional limiter.
- `Lufs` uses FFmpeg `loudnorm`, defaulting to `-16 LUFS` when no target is supplied.

### Podcast Processing

Use `AudioPodcastProcessingOptions` when the target is a finished spoken-word track. The service probes the input, analyzes loudness and peaks with FFmpeg `volumedetect` and `loudnorm`, optionally applies DTLN denoise into a temporary WAV, applies a high-pass filter, applies the Podcast Voice EQ preset, reduces sibilance with EQ, applies a dynamic compressor, applies a limiter, normalizes to LUFS, applies a final limiter for clipping protection, encodes the output, and returns output metadata, warnings, and the pre-processing analysis.

Defaults are tuned for common podcast delivery: `80 Hz` high-pass, `-16 LUFS`, limiter ceiling `0.98`, de-esser enabled, compressor enabled, metadata preserved, and denoise disabled. Enable DTLN denoise with `EnableDtlnDenoise`; control the channel strategy, blend, and optional stronger extra inference passes with `DtlnDenoiseMode`, `DtlnDenoiseAmount`, and `DtlnDenoisePasses`. DTLN can reduce many speech-noise problems, but background music under speech is closer to source separation and may remain audible.

```csharp
await service.ProcessPodcastAudioAsync(input, output, new AudioPodcastProcessingOptions
{
    EnableDtlnDenoise = true,
    DtlnDenoiseMode = AudioDenoiseMode.Mono,
    DtlnDenoiseAmount = 100,
    DtlnDenoisePasses = 3,
    TargetLufs = -16,
    BitrateKbps = 128,
    Channels = 1
});
```

### Trim

Use `AudioTrimOptions` with start, end, or duration. `ReEncode = false` uses audio stream copy; otherwise the service re-encodes using the output extension’s default codec.

### Remove Silence

Use `AudioSilenceRemovalOptions` for leading, trailing, or leading-and-trailing silence. Internal silence removal is rejected in v1 because safe internal segment removal requires more complex timing and concatenation behavior.

### Equalizer

Use `AudioEqualizerOptions` with a preset or custom bands. Presets include Podcast Voice, Voice Clarity, Warm Voice, Bright Voice, bass/treble reduction, phone/radio voice, and 50/60 Hz hum reduction. Custom bands require positive frequency/width and gain between `-24` and `24 dB`.

## Progress

The service uses FFmpeg `-progress pipe:2` output to report:

- current stage
- overall progress
- current-stage progress
- processed duration
- total duration
- estimated remaining time
- FFmpeg activity

## Exceptions

- `AudioProcessingValidationException`: invalid options
- `AudioProcessingUnsupportedMediaException`: no usable audio stream
- `AudioProcessingFfmpegException`: FFmpeg or FFprobe failure
- `AudioProcessingFileSystemException`: reserved for file-system failures
- `OperationCanceledException`: cancellation requested

## V1 Limitations

- No UI page is added in this pass.
- Internal silence removal is not implemented.
- Lossless compression supports FLAC only.
- Dedicated de-click, de-crackle, de-hum, stem separation, and real-time microphone processing are out of scope.
- DTLN denoise is available to podcast processing, but severe music removal remains outside v1 scope.
