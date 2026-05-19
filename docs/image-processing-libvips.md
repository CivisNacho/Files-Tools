# libvips Image Processing Service v2

This project uses `libvips` through `NetVips` for image transformation and encoding.

## Packages

- `NetVips`
- `NetVips.Native.win-x86`
- `NetVips.Native.win-x64`
- `NetVips.Native.win-arm64`

## Service Location

- `Services/ImageProcessingService.cs`

## Public API

### Main pipeline

- `ProcessImageAsync(string inputPath, string outputPath, ProcessImageOptions options, CancellationToken ct = default)`

This is the main entry point for combined operations in a single pass.

### Convenience wrappers

- `ConvertFormatAsync(...)`
- `CompressAsync(...)`
- `ResizeAsync(...)`
- `UpscaleAsync(...)`
- `RotateAsync(...)`
- `MirrorAsync(...)`
- `AdjustRgbAsync(...)`

All wrappers call `ProcessImageAsync(...)` internally.

## Supported Formats

- `Jpeg`
- `Png`
- `Webp`
- `Avif`
- `Tiff`
- `Heif` (`.heif` output extension)
- `Gif`

## Operation Options

- `ResizeOptions`: strict downscale only (`target <= source`)
- `UpscaleOptions`: strict upscale only (`target >= source`, always Lanczos3)
- `RotateOptions`: `90`, `180`, or `270`
- `MirrorOptions`: horizontal and/or vertical
- `RgbAdjustOptions`: brightness, contrast, saturation
- `OutputOptions`: output format, quality mode, quality value, lossless, metadata behavior

## Execution Order (Fixed)

The pipeline always applies operations in this order:

1. Crop
2. Rotation
3. Mirroring
4. Resize or upscale
5. RGB adjustments
6. Final single encode/save

This avoids multiple re-encodes and quality loss.

## UI Integration Rule

- `ImageEditorPage` must use `ImageProcessingService` for both:
  - final output processing (`Apply` action)
  - live preview recomputation after option changes
- Do not keep a separate manual pixel-transform implementation in page code-behind for preview.
- This guarantees preview/output parity and keeps transform semantics in one place.

## Quality Modes

### MaintainOriginal

- If there are no pixel transforms and format does not change, input is copied directly.
- Otherwise, the image is encoded once with libvips defaults unless explicit values are provided.

### ExplicitQuality

- Final save uses explicit quality behavior per codec.
- Quality range is validated (`1..100`).

## Animated GIF Behavior

- GIF input is loaded with all frames.
- Transformations are applied per-frame.
- Output preserves animation metadata (`n-pages`, `page-height`, `delay`, `loop`) when available.

## Validation and Errors

The service throws clear exceptions for:

- Missing input file / invalid paths
- Unsupported file extensions
- Invalid quality values
- Invalid resize or upscale direction
- Unsupported rotation angles
- Conflicting options (`Resize` + `Upscale` together)

## Usage Examples

### Example 1: Convert PNG to AVIF

```csharp
using Files_Tools.Services;

var service = new ImageProcessingService();

await service.ConvertFormatAsync(
    inputPath: @"C:\images\input.png",
    outputPath: @"C:\images\output",
    format: ImageFormat.Avif,
    outputOptions: new OutputOptions
    {
        QualityMode = ImageQualityMode.ExplicitQuality,
        Quality = 50
    });
```

### Example 2: Keep JPG format + compress to 90 + mirror horizontally

```csharp
using Files_Tools.Services;

var service = new ImageProcessingService();

await service.ProcessImageAsync(
    inputPath: @"C:\images\input.jpg",
    outputPath: @"C:\images\output",
    options: new ProcessImageOptions
    {
        Mirror = new MirrorOptions { Horizontal = true },
        Output = new OutputOptions
        {
            Format = null, // keep source format
            QualityMode = ImageQualityMode.ExplicitQuality,
            Quality = 90,
            KeepMetadata = true
        }
    });
```

### Example 3: Strict resize

```csharp
await service.ResizeAsync(
    inputPath: @"C:\images\large.webp",
    outputPath: @"C:\images\small",
    options: new ResizeOptions { Width = 1280, Height = 720 },
    outputOptions: new OutputOptions
    {
        Format = ImageFormat.Webp,
        QualityMode = ImageQualityMode.ExplicitQuality,
        Quality = 82
    });
```

### Example 4: Strict upscale with Lanczos3

```csharp
await service.UpscaleAsync(
    inputPath: @"C:\images\small.png",
    outputPath: @"C:\images\big",
    options: new UpscaleOptions { Width = 1920, Height = 1080 },
    outputOptions: new OutputOptions
    {
        Format = ImageFormat.Png,
        QualityMode = ImageQualityMode.MaintainOriginal
    });
```

## Debugging Checklist

- Confirm input format is one of supported formats.
- Validate target dimensions relative to source before calling resize/upscale.
- Validate rotation angle (`90`, `180`, or `270`) before calling rotate.
- Log full exception messages (validation exceptions are actionable by design).
- For GIF animation checks, inspect output metadata (`n-pages`, `page-height`, `delay`, `loop`).
- Verify runtime architecture (`x86`, `x64`, `ARM64`) matches deployed build.
