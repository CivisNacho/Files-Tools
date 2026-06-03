# FFmpeg Video Processing Service

This project uses FFmpeg and FFprobe for file-based video processing, remuxing, and subtitle/audio muxing.
FFprobe is also used for preflight estimates and repair-mode decisions.

## Service Location

- `Services/VideoProcessingService.cs`

## Public API

### Main pipeline

- `ProcessVideoAsync(string inputPath, string outputPath, ProcessVideoOptions options, CancellationToken ct = default)`
- `EstimateProcessAsync(string inputPath, ProcessVideoOptions options, CancellationToken ct = default)`
- `RepairAsync(string inputPath, string outputPath, RepairOptions options, CancellationToken ct = default)`

This is the main entry point for combined operations in a single processing call.

### Convenience wrappers

- `ChangeContainerAsync(...)`
- `ResizeAsync(...)`
- `CompressAsync(...)`
- `ChangeCodecAsync(...)`
- `TrimAsync(...)`
- `RotateOrMirrorAsync(...)`
- `RemoveMetadataAsync(...)`
- `CombineWithAudioAsync(...)`
- `CombineWithSubtitlesAsync(...)`
- `ExtractAudioAsync(...)`
- `EstimateProcessAsync(...)`
- `RepairAsync(...)`

All wrappers call `ProcessVideoAsync(...)` internally.

## Runtime Discovery

The service looks for:

- `AppContext.BaseDirectory/ffmpeg/<rid>/ffmpeg(.exe)`
- `AppContext.BaseDirectory/ffmpeg/<rid>/ffprobe(.exe)`

The current RID is derived from OS and process architecture.

If the bundled executable exists but cannot start because of a missing runtime dependency, the service falls back to `ffmpeg` / `ffprobe` on `PATH`. This keeps local debugging workable while preserving the bundled binaries as the primary runtime source.

## Supported Containers

- `Mp4`
- `Webm`
- `Gif`
- `Mkv`
- `Mov`
- `Avi`

## Supported Video Codecs

- `H264`
- `H265`
- `Av1`
- `Vp9`
- `Vp8`
- `Gif`
- `Mpeg4`

## Supported Audio Codecs

- `Aac`
- `Opus`
- `Vorbis`
- `Mp3`
- `Ac3`
- `Flac`
- `PcmS16Le`

## Operation Options

- `VideoOutputOptions`: target container plus optional explicit video/audio codecs
- `VideoResizeOptions`: exact width, height, and fit mode
- `VideoCompressionOptions`: preset-based CRF selection
- `CodecChangeOptions`: explicit video and/or audio codec change
- `TrimOptions`: absolute start and end timestamps
- `TransformOptions`: fixed rotation and mirroring
- `MuxAudioOptions`: external audio path, replacement policy, duration policy
- `AudioAdjustOptions`: final output audio adjustments such as volume percent, loudness normalization, and sync offset
- `MuxSubtitleOptions`: soft-mux or burn-in subtitle behavior
- `RepairOptions`: remux or re-encode recovery strategy for damaged media
- `VideoProcessingEstimate`: FFprobe-based estimate for output dimensions, duration, re-encode behavior and size
- `ProcessVideoOptions.RemoveAudio`: explicit final-output audio removal for video exports

## Deterministic Behavior

### Container change

- Remuxes with `-c copy` when no re-encode is needed and the target container accepts current streams.
- Falls back to safe container defaults when stream/container compatibility requires transcoding.

### Resolution change

- `Stretch`: direct scale to requested width and height
- `CropToFill`: scale preserving aspect ratio, then center crop
- `PadToFit`: scale preserving aspect ratio, then center pad

### Compression presets

Preset mappings are codec-specific CRF defaults:

- `VeryHigh`: H.264 `18`, H.265 `20`, AV1 `22`
- `High`: H.264 `21`, H.265 `24`, AV1 `28`
- `Balanced`: H.264 `23`, H.265 `28`, AV1 `34`
- `SmallSize`: H.264 `27`, H.265 `32`, AV1 `40`

### Hardware acceleration

- For compatible video re-encodes, the service now probes FFmpeg hardware encoders at runtime and uses the first working GPU path by default.
- Current automatic hardware candidates are `NVENC`, `AMF`, then `QSV` for `H264`, `H265`, and `AV1`.
- Requests that rely on CRF-based compression presets stay on software encoders so preset quality behavior remains deterministic.
- If no working hardware encoder is available on the current machine, the service automatically falls back to the existing software encoder path.

### Trim

- Uses accurate trimming with input probe validation.
- Applies `-ss` and output `-t` based on `End - Start`.

### FFprobe estimates

- Uses probed duration, dimensions, codecs, and bitrate metadata.
- Predicts output dimensions after resize/rotation.
- Predicts whether video or audio re-encoding will be required.
- Produces a heuristic size estimate when probe bitrate information is available.

### Repair mode

- `Remux`: rewrites the container with recovery flags while preserving compatible streams when possible.
- `Reencode`: rewrites damaged content using safe default codecs for the target container.
- Repair mode can regenerate timestamps, ignore recoverable errors, drop non-essential streams, and strip metadata.

### Rotation and mirroring

- Supports `0`, `90`, `180`, `270`
- Supports horizontal and vertical mirroring

### Metadata removal

- Removes container metadata with `-map_metadata -1`
- Removes chapters with `-map_chapters -1`
- Clears per-stream metadata entries

### Audio mux

- Can replace or preserve existing audio
- Can stop at the shortest stream
- Can mark the new audio stream as default

### Audio removal

- `ProcessVideoOptions.RemoveAudio` removes audio from the final video export
- Audio removal is explicit in the service contract and is separate from audio extraction
- Audio removal cannot be combined with external audio muxing in the same request

### Audio volume

- Applies to the final output audio stream, including muxed replacement audio
- Uses an FFmpeg audio filter stage rather than stream copy
- `100` preserves original loudness
- `0` mutes the audio without removing the stream
- `200` doubles the audio level
- Any non-identity volume adjustment forces audio re-encoding

### Audio normalization

- Applies a simple loudness normalization pass to the final output audio
- Uses a fixed `loudnorm` configuration for a practical v1 normalize option
- Forces audio re-encoding because it is implemented as an audio filter

### Audio sync offset

- Applies to the final output audio stream, including muxed replacement audio
- Supports signed millisecond offsets
- Positive values delay audio later than video
- Negative values move audio earlier than video
- The supported range is `-5000..5000` ms
- Any non-zero sync offset forces audio re-encoding because it uses audio filtering

### Audio extraction

- `ExtractAudioAsync(...)` exports the primary audio stream from a video into a standalone audio file
- Output format is inferred from the output extension
- Supported extraction extensions:
  - `.mp3`
  - `.aac`
  - `.m4a`
  - `.wav`
  - `.flac`
  - `.opus`
  - `.ogg`
- The service copies the source audio stream when the codec already matches the requested output format, otherwise it re-encodes

### Subtitle handling

- `SoftMux`: keeps the subtitle as a separate (non-burned) track. It is embedded into the container when the
  format can carry it natively (e.g. MKV embeds `.ass`). When the container cannot carry ASS styling
  (MP4/MOV/WebM with an `.ass`/`.ssa` source), the video is left untouched and the styled subtitle is written
  as a **sidecar `.ass`** beside the output (matching base name, e.g. `myvideo.ass` next to `myvideo.mp4`) so
  players load it automatically. SoftMux never burns and never transcodes ASS to a lossy codec — styling and
  karaoke animation are preserved. See `ShouldSidecarAssSubtitle` / `WriteSidecarSubtitleIfNeeded`.
- `BurnIn`: renders subtitles into the video image and emits no subtitle track
- plain `.srt` subtitle files and final reviewed `.ass` subtitle files are generated by `Services/SubtitlesService.cs`, then passed into this service for muxing or burn-in

## Validation and Errors

The service throws clear exceptions for:

- missing input files
- invalid output paths
- invalid resize dimensions
- invalid trim ranges
- unsupported rotation values
- unsupported container/codec combinations
- unsupported subtitle/container combinations
- input files without a video stream
- invalid repair + subtitle combinations in the same request

FFmpeg and FFprobe failures throw `VideoProcessingException` with:

- executed binary path
- full formatted command line
- exit code
- captured stdout
- captured stderr
- probed media JSON when available

## Debugging Checklist

- Confirm the input file actually contains a video stream.
- Confirm requested container and codec combinations are supported by the service matrix.
- Check whether the bundled `ffmpeg/<rid>/` executables exist in the output folder.
- If the bundled binary fails to launch, inspect whether the service fell back to `PATH`.
- For subtitle failures, confirm whether the request is `SoftMux` or `BurnIn`.
- For trim issues, inspect the probed duration and requested start/end values.
- For estimate issues, confirm the source exposes bitrate information in FFprobe output.
- For repair mode, compare remux vs re-encode if a damaged file still fails after remux.
- Log the full `VideoProcessingException` details when FFmpeg exits non-zero.

## Usage Examples

### Example 1: Resize with pad-to-fit

```csharp
using Files_Tools.Services;

var service = new VideoProcessingService();

await service.ResizeAsync(
    inputPath: @"C:\videos\input.mp4",
    outputPath: @"C:\videos\resized",
    options: new VideoResizeOptions
    {
        Width = 1280,
        Height = 720,
        Mode = ResizeMode.PadToFit
    });
```

### Example 2: Convert to WebM with VP9 + Opus

```csharp
await service.ChangeCodecAsync(
    inputPath: @"C:\videos\input.mp4",
    outputPath: @"C:\videos\output.webm",
    options: new CodecChangeOptions
    {
        VideoCodec = VideoCodec.Vp9,
        AudioCodec = AudioCodec.Opus
    },
    outputOptions: new VideoOutputOptions
    {
        Format = VideoContainerFormat.Webm
    });
```

### Example 3: Lower final audio volume to 50%

```csharp
await service.ProcessVideoAsync(
    inputPath: @"C:\videos\input.mp4",
    outputPath: @"C:\videos\quieter.mp4",
    options: new ProcessVideoOptions
    {
        AudioAdjust = new AudioAdjustOptions
        {
            VolumePercent = 50
        },
        Output = new VideoOutputOptions
        {
            Format = VideoContainerFormat.Mp4
        }
    });
```

### Example 4: Normalize audio and delay it by 300 ms

```csharp
await service.ProcessVideoAsync(
    inputPath: @"C:\videos\input.mp4",
    outputPath: @"C:\videos\normalized.mp4",
    options: new ProcessVideoOptions
    {
        AudioAdjust = new AudioAdjustOptions
        {
            NormalizeLoudness = true,
            SyncOffsetMilliseconds = 300
        },
        Output = new VideoOutputOptions
        {
            Format = VideoContainerFormat.Mp4
        }
    });
```

### Example 5: Extract audio to FLAC

```csharp
await service.ExtractAudioAsync(
    inputPath: @"C:\videos\input.mp4",
    outputPath: @"C:\audio\extracted.flac");
```

### Example 6: Burn subtitles into MP4 output

```csharp
await service.CombineWithSubtitlesAsync(
    inputPath: @"C:\videos\input.mp4",
    outputPath: @"C:\videos\burned.mp4",
    options: new MuxSubtitleOptions
    {
        SubtitlePath = @"C:\videos\subtitles.srt",
        Mode = SubtitleMode.BurnIn
    });
```

### Example 7: Estimate a compression run before executing it

```csharp
var estimate = await service.EstimateProcessAsync(
    inputPath: @"C:\videos\input.mp4",
    options: new ProcessVideoOptions
    {
        Compression = new VideoCompressionOptions
        {
            Preset = CompressionPreset.Balanced,
            VideoCodec = VideoCodec.H265
        },
        Output = new VideoOutputOptions
        {
            Format = VideoContainerFormat.Mp4
        }
    });
```

### Example 8: Repair a damaged MP4 by remuxing it

```csharp
await service.RepairAsync(
    inputPath: @"C:\videos\broken.mp4",
    outputPath: @"C:\videos\repaired.mp4",
    options: new RepairOptions
    {
        Mode = RepairMode.Remux
    });
```
