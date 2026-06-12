using NetVips;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services;

/// <summary>
/// Defines image processing operations backed by libvips.
/// </summary>
public interface IImageProcessingService
{
    /// <summary>
    /// Executes a full image processing pipeline in one pass.
    /// </summary>
    /// <param name="inputPath">Absolute or relative path to the source image.</param>
    /// <param name="outputPath">Absolute or relative path for the output image.</param>
    /// <param name="options">Pipeline configuration, including transforms and output encoding settings.</param>
    /// <param name="cancellationToken">Cancellation token propagated to background processing.</param>
    Task ProcessImageAsync(string inputPath, string outputPath, ProcessImageOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts an image to another format.
    /// </summary>
    Task ConvertFormatAsync(string inputPath, string outputPath, ImageFormat format, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compresses an image using output options focused on quality and metadata handling.
    /// </summary>
    Task CompressAsync(string inputPath, string outputPath, CompressionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resizes an image to smaller dimensions.
    /// </summary>
    Task ResizeAsync(string inputPath, string outputPath, ResizeOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crops an image to a rectangular pixel region.
    /// </summary>
    Task CropAsync(string inputPath, string outputPath, CropOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upscales an image to larger dimensions using Lanczos3.
    /// </summary>
    Task UpscaleAsync(string inputPath, string outputPath, UpscaleOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates an image by a fixed angle (90 or 180 degrees).
    /// </summary>
    Task RotateAsync(string inputPath, string outputPath, RotateOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mirrors an image horizontally and/or vertically.
    /// </summary>
    Task MirrorAsync(string inputPath, string outputPath, MirrorOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies basic RGB adjustments.
    /// </summary>
    Task AdjustRgbAsync(string inputPath, string outputPath, RgbAdjustOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Supported output formats.
/// </summary>
public enum ImageFormat
{
    Jpeg,
    Png,
    Webp,
    Avif,
    Tiff,
    Heif,
    Gif
}

/// <summary>
/// Encoding mode used on final save.
/// </summary>
public enum ImageQualityMode
{
    /// <summary>
    /// Keep original quality when possible. If no pixel changes and output format is unchanged, the file is copied.
    /// Otherwise, output is encoded once with libvips format defaults unless explicit values are provided.
    /// </summary>
    MaintainOriginal,

    /// <summary>
    /// Forces explicit quality-based encoding.
    /// </summary>
    ExplicitQuality
}

/// <summary>
/// Full processing pipeline options.
/// </summary>
public sealed class ProcessImageOptions
{
    /// <summary>
    /// Optional crop operation. Coordinates are measured in source-image pixels before resize, rotation or mirroring.
    /// </summary>
    public CropOptions? Crop { get; init; }

    /// <summary>
    /// Optional strict downscale operation.
    /// </summary>
    public ResizeOptions? Resize { get; init; }

    /// <summary>
    /// Optional strict upscale operation (always Lanczos3).
    /// </summary>
    public UpscaleOptions? Upscale { get; init; }

    /// <summary>
    /// Optional fixed-angle rotation operation.
    /// </summary>
    public RotateOptions? Rotate { get; init; }

    /// <summary>
    /// Optional mirror operation.
    /// </summary>
    public MirrorOptions? Mirror { get; init; }

    /// <summary>
    /// Optional RGB adjustments.
    /// </summary>
    public RgbAdjustOptions? RgbAdjust { get; init; }

    /// <summary>
    /// Output format and encoding options.
    /// </summary>
    public OutputOptions Output { get; init; } = new();

    /// <summary>
    /// Returns true when any pixel-transform operation is configured.
    /// </summary>
    public bool HasPixelOperations =>
        Crop is not null ||
        Resize is not null ||
        Upscale is not null ||
        Rotate is not null ||
        (Mirror is not null && (Mirror.Horizontal || Mirror.Vertical)) ||
        (RgbAdjust is not null && !RgbAdjust.IsIdentity);
}

/// <summary>
/// Output and encoding options used by the final save step.
/// </summary>
public sealed class OutputOptions
{
    /// <summary>
    /// Output format. If null, the input format is preserved.
    /// </summary>
    public ImageFormat? Format { get; init; }

    /// <summary>
    /// Encoding quality mode.
    /// </summary>
    public ImageQualityMode QualityMode { get; init; } = ImageQualityMode.MaintainOriginal;

    /// <summary>
    /// Explicit quality value (1-100). Only used when supported by the output codec.
    /// </summary>
    public int? Quality { get; init; }

    /// <summary>
    /// Enables lossless mode for codecs that support it.
    /// </summary>
    public bool Lossless { get; init; }

    /// <summary>
    /// Keeps metadata (EXIF/XMP/ICC and animation metadata where supported).
    /// </summary>
    public bool KeepMetadata { get; init; } = true;
}

/// <summary>
/// Options focused on compression and final encoding behavior.
/// </summary>
public sealed class CompressionOptions
{
    /// <summary>
    /// Output format. If null, the input format is preserved.
    /// </summary>
    public ImageFormat? Format { get; init; }

    /// <summary>
    /// If true, uses maintain-original mode.
    /// </summary>
    public bool MaintainOriginalQuality { get; init; }

    /// <summary>
    /// Explicit quality value (1-100) used when <see cref="MaintainOriginalQuality"/> is false.
    /// </summary>
    public int? Quality { get; init; }

    /// <summary>
    /// Enables lossless mode when supported by output codec.
    /// </summary>
    public bool Lossless { get; init; }

    /// <summary>
    /// Keeps metadata in output.
    /// </summary>
    public bool KeepMetadata { get; init; } = true;
}

/// <summary>
/// Pixel crop options.
/// </summary>
public sealed class CropOptions
{
    /// <summary>
    /// Left edge of the crop rectangle in source-image pixels. Must be greater than or equal to 0.
    /// </summary>
    public int Left { get; init; }

    /// <summary>
    /// Top edge of the crop rectangle in source-image pixels. Must be greater than or equal to 0.
    /// </summary>
    public int Top { get; init; }

    /// <summary>
    /// Width of the crop rectangle in pixels. Must be positive and fit inside the source image.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Height of the crop rectangle in pixels. Must be positive and fit inside the source image.
    /// </summary>
    public int Height { get; init; }
}

/// <summary>
/// Strict downscale options.
/// </summary>
public sealed class ResizeOptions
{
    /// <summary>
    /// Target width in pixels. Must be positive and smaller or equal to source width.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Target height in pixels. Must be positive and smaller or equal to source height.
    /// </summary>
    public int Height { get; init; }
}

/// <summary>
/// Strict upscale options.
/// </summary>
public sealed class UpscaleOptions
{
    /// <summary>
    /// Target width in pixels. Must be positive and larger or equal to source width.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Target height in pixels. Must be positive and larger or equal to source height.
    /// </summary>
    public int Height { get; init; }
}

/// <summary>
/// Fixed-angle rotation options.
/// </summary>
public sealed class RotateOptions
{
    /// <summary>
    /// Rotation angle in degrees. Allowed values: 90, 180 and 270.
    /// </summary>
    public int Angle { get; init; }
}

/// <summary>
/// Mirroring options.
/// </summary>
public sealed class MirrorOptions
{
    /// <summary>
    /// Mirrors image around the vertical axis.
    /// </summary>
    public bool Horizontal { get; init; }

    /// <summary>
    /// Mirrors image around the horizontal axis.
    /// </summary>
    public bool Vertical { get; init; }
}

/// <summary>
/// Basic RGB adjustments.
/// </summary>
public sealed class RgbAdjustOptions
{
    /// <summary>
    /// Per-channel red multiplier in range [0, 3]. Default is 1.
    /// </summary>
    public double RedScale { get; init; } = 1;

    /// <summary>
    /// Per-channel green multiplier in range [0, 3]. Default is 1.
    /// </summary>
    public double GreenScale { get; init; } = 1;

    /// <summary>
    /// Per-channel blue multiplier in range [0, 3]. Default is 1.
    /// </summary>
    public double BlueScale { get; init; } = 1;

    /// <summary>
    /// Brightness offset in range [-1, 1].
    /// Internally mapped to additive offset in [-255, 255].
    /// </summary>
    public double Brightness { get; init; }

    /// <summary>
    /// Contrast multiplier in range [0, 3]. Default is 1.
    /// </summary>
    public double Contrast { get; init; } = 1;

    /// <summary>
    /// Saturation multiplier in range [0, 3]. Default is 1.
    /// </summary>
    public double Saturation { get; init; } = 1;

    /// <summary>
    /// Returns true when options represent a no-op transformation.
    /// </summary>
    public bool IsIdentity =>
        Math.Abs(RedScale - 1) < 0.0001 &&
        Math.Abs(GreenScale - 1) < 0.0001 &&
        Math.Abs(BlueScale - 1) < 0.0001 &&
        Math.Abs(Brightness) < 0.0001 &&
        Math.Abs(Contrast - 1) < 0.0001 &&
        Math.Abs(Saturation - 1) < 0.0001;
}

/// <summary>
/// NetVips/libvips implementation of processing operations.
/// </summary>
public sealed class ImageProcessingService : IImageProcessingService
{
    /// <inheritdoc />
    public Task ProcessImageAsync(string inputPath, string outputPath, ProcessImageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Output);

        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateCommonOptions(options);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputFormat = InferFormatFromPath(inputPath);
            var outputFormat = options.Output.Format ?? inputFormat;
            var finalOutputPath = EnsureOutputExtension(outputPath, outputFormat);

            if (CanCopyWithoutReencode(inputPath, finalOutputPath, inputFormat, outputFormat, options))
            {
                CopyInputToOutput(inputPath, finalOutputPath);
                return;
            }

            using var source = LoadForProcessing(inputPath, inputFormat);
            using var processed = ApplyPipelineOperations(source, options);
            SaveImage(processed, source, finalOutputPath, outputFormat, options.Output);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ConvertFormatAsync(string inputPath, string outputPath, ImageFormat format, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        outputOptions ??= new OutputOptions();

        var output = new OutputOptions
        {
            Format = format,
            QualityMode = outputOptions.QualityMode,
            Quality = outputOptions.Quality,
            Lossless = outputOptions.Lossless,
            KeepMetadata = outputOptions.KeepMetadata
        };

        return ProcessImageAsync(inputPath, outputPath, new ProcessImageOptions { Output = output }, cancellationToken);
    }

    /// <inheritdoc />
    public Task CompressAsync(string inputPath, string outputPath, CompressionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var output = new OutputOptions
        {
            Format = options.Format,
            QualityMode = options.MaintainOriginalQuality ? ImageQualityMode.MaintainOriginal : ImageQualityMode.ExplicitQuality,
            Quality = options.Quality,
            Lossless = options.Lossless,
            KeepMetadata = options.KeepMetadata
        };

        return ProcessImageAsync(inputPath, outputPath, new ProcessImageOptions { Output = output }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ResizeAsync(string inputPath, string outputPath, ResizeOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProcessImageAsync(
            inputPath,
            outputPath,
            new ProcessImageOptions
            {
                Resize = options,
                Output = outputOptions ?? new OutputOptions()
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task CropAsync(string inputPath, string outputPath, CropOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProcessImageAsync(
            inputPath,
            outputPath,
            new ProcessImageOptions
            {
                Crop = options,
                Output = outputOptions ?? new OutputOptions()
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpscaleAsync(string inputPath, string outputPath, UpscaleOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProcessImageAsync(
            inputPath,
            outputPath,
            new ProcessImageOptions
            {
                Upscale = options,
                Output = outputOptions ?? new OutputOptions()
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RotateAsync(string inputPath, string outputPath, RotateOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProcessImageAsync(
            inputPath,
            outputPath,
            new ProcessImageOptions
            {
                Rotate = options,
                Output = outputOptions ?? new OutputOptions()
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task MirrorAsync(string inputPath, string outputPath, MirrorOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProcessImageAsync(
            inputPath,
            outputPath,
            new ProcessImageOptions
            {
                Mirror = options,
                Output = outputOptions ?? new OutputOptions()
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task AdjustRgbAsync(string inputPath, string outputPath, RgbAdjustOptions options, OutputOptions? outputOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ProcessImageAsync(
            inputPath,
            outputPath,
            new ProcessImageOptions
            {
                RgbAdjust = options,
                Output = outputOptions ?? new OutputOptions()
            },
            cancellationToken);
    }

    private static Image LoadForProcessing(string inputPath, ImageFormat format)
    {
        // For GIF, load all frames ("n" = -1) so animations survive processing.
        var kwargs = format == ImageFormat.Gif ? new VOption { { "n", -1 } } : null;
        return Image.NewFromFile(inputPath, access: Enums.Access.Sequential, kwargs: kwargs);
    }

    private static Image ApplyPipelineOperations(Image source, ProcessImageOptions options)
    {
        if (!options.HasPixelOperations)
        {
            return source.Copy();
        }

        if (IsAnimated(source))
        {
            return ApplyPipelineToAnimated(source, options);
        }

        return ApplyPipelineToSingleImage(source, options);
    }

    private static Image ApplyPipelineToSingleImage(Image image, ProcessImageOptions options)
    {
        var current = image.Copy();

        current = ApplyCrop(current, options.Crop);
        current = ApplyRotate(current, options.Rotate);
        current = ApplyMirror(current, options.Mirror);
        current = ApplyResizeOrUpscale(current, options);
        current = ApplyRgbAdjustments(current, options.RgbAdjust);

        return current;
    }

    private static Image ApplyCrop(Image image, CropOptions? options)
    {
        if (options is null)
        {
            return image;
        }

        ValidateCropOptions(options, image.Width, image.Height);

        using var cropped = image.Crop(options.Left, options.Top, options.Width, options.Height);
        image.Dispose();
        return cropped.Copy();
    }

    private static Image ApplyPipelineToAnimated(Image strip, ProcessImageOptions options)
    {
        var frameCount = TryGetInt(strip, "n-pages") ?? 1;
        var sourcePageHeight = strip.PageHeight;
        var delays = TryGetIntArray(strip, "delay");
        var loop = TryGetInt(strip, "loop");

        if (frameCount <= 1 || sourcePageHeight <= 0)
        {
            return ApplyPipelineToSingleImage(strip, options);
        }

        var frames = new List<Image>(frameCount);
        var processedFrames = new List<Image>(frameCount);

        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                var top = i * sourcePageHeight;
                frames.Add(strip.Crop(0, top, strip.Width, sourcePageHeight));
            }

            foreach (var frame in frames)
            {
                processedFrames.Add(ApplyPipelineToSingleImage(frame, options));
            }

            using var joined = Image.Arrayjoin(processedFrames.ToArray(), across: 1);
            var outputPageHeight = processedFrames[0].Height;

            return joined.Mutate(mutable =>
            {
                mutable.Set(GValue.GIntType, "page-height", outputPageHeight);
                mutable.Set(GValue.GIntType, "n-pages", frameCount);

                if (loop.HasValue)
                {
                    mutable.Set(GValue.GIntType, "loop", loop.Value);
                }

                if (delays is { Length: > 0 })
                {
                    mutable.Set(GValue.ArrayIntType, "delay", NormalizeDelays(delays, frameCount));
                }
            });
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            foreach (var frame in processedFrames)
            {
                frame.Dispose();
            }
        }
    }

    private static Image ApplyResizeOrUpscale(Image image, ProcessImageOptions options)
    {
        var hasResize = options.Resize is not null;
        var hasUpscale = options.Upscale is not null;

        if (!hasResize && !hasUpscale)
        {
            return image;
        }

        if (hasResize && hasUpscale)
        {
            throw new InvalidOperationException("Resize and Upscale cannot be requested together in a single operation.");
        }

        var targetWidth = hasResize ? options.Resize!.Width : options.Upscale!.Width;
        var targetHeight = hasResize ? options.Resize!.Height : options.Upscale!.Height;

        if (targetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(hasResize ? nameof(options.Resize.Width) : nameof(options.Upscale.Width), "Width must be greater than 0.");
        }

        if (targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(hasResize ? nameof(options.Resize.Height) : nameof(options.Upscale.Height), "Height must be greater than 0.");
        }

        if (hasResize)
        {
            if (targetWidth > image.Width || targetHeight > image.Height)
            {
                throw new InvalidOperationException($"Resize requires target size <= source size. Source: {image.Width}x{image.Height}, Target: {targetWidth}x{targetHeight}.");
            }
        }
        else
        {
            if (targetWidth < image.Width || targetHeight < image.Height)
            {
                throw new InvalidOperationException($"Upscale requires target size >= source size. Source: {image.Width}x{image.Height}, Target: {targetWidth}x{targetHeight}.");
            }
        }

        var scaleX = (double)targetWidth / image.Width;
        var scaleY = (double)targetHeight / image.Height;

        using var resized = image.Resize(scaleX, kernel: Enums.Kernel.Lanczos3, vscale: scaleY);
        image.Dispose();
        return resized.Copy();
    }

    private static Image ApplyRotate(Image image, RotateOptions? options)
    {
        if (options is null)
        {
            return image;
        }

        using var rotated = options.Angle switch
        {
            90 => image.Rot(Enums.Angle.D90),
            180 => image.Rot(Enums.Angle.D180),
            270 => image.Rot(Enums.Angle.D270),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Angle), "Rotation angle must be 90, 180 or 270 degrees.")
        };

        image.Dispose();
        return rotated.Copy();
    }

    private static Image ApplyMirror(Image image, MirrorOptions? options)
    {
        if (options is null || (!options.Horizontal && !options.Vertical))
        {
            return image;
        }

        var current = image;

        if (options.Horizontal)
        {
            using var flipped = current.Flip(Enums.Direction.Horizontal);
            current.Dispose();
            current = flipped.Copy();
        }

        if (options.Vertical)
        {
            using var flipped = current.Flip(Enums.Direction.Vertical);
            current.Dispose();
            current = flipped.Copy();
        }

        return current;
    }

    private static Image ApplyRgbAdjustments(Image image, RgbAdjustOptions? options)
    {
        if (options is null || options.IsIdentity)
        {
            return image;
        }

        ValidateRgbOptions(options);

        var channelCount = image.Bands;
        var multipliers = Enumerable.Repeat(options.Contrast, channelCount).ToArray();
        if (channelCount >= 3)
        {
            multipliers[0] *= options.RedScale;
            multipliers[1] *= options.GreenScale;
            multipliers[2] *= options.BlueScale;
        }

        var offsets = Enumerable.Repeat(options.Brightness * 255.0, channelCount).ToArray();
        if (channelCount == 4)
        {
            offsets[3] = 0;
            multipliers[3] = 1;
        }

        using var linearAdjusted = image.Linear(multipliers, offsets);

        using var lch = linearAdjusted.Colourspace(Enums.Interpretation.Lch);
        using var lightness = lch.ExtractBand(0);
        using var chroma = lch.ExtractBand(1).Linear(new[] { options.Saturation }, new[] { 0.0 });
        using var hue = lch.ExtractBand(2);
        using var adjustedLch = lightness.Bandjoin(new[] { chroma, hue });
        using var backToSrgb = adjustedLch.Colourspace(Enums.Interpretation.Srgb, sourceSpace: Enums.Interpretation.Lch);

        image.Dispose();
        return backToSrgb.Copy();
    }

    private static void SaveImage(Image image, Image source, string outputPath, ImageFormat format, OutputOptions output)
    {
        var keep = output.KeepMetadata ? Enums.ForeignKeep.All : Enums.ForeignKeep.None;
        var pageHeight = IsAnimated(image) ? image.PageHeight : (int?)null;

        var quality = ResolveQuality(format, output);

        switch (format)
        {
            case ImageFormat.Jpeg:
                image.Jpegsave(outputPath, q: quality, keep: keep, pageHeight: pageHeight);
                break;

            case ImageFormat.Png:
                image.Pngsave(outputPath, compression: MapPngCompression(quality), keep: keep, pageHeight: pageHeight);
                break;

            case ImageFormat.Webp:
                image.Webpsave(outputPath, q: quality, lossless: output.Lossless, keep: keep, pageHeight: pageHeight);
                break;

            case ImageFormat.Avif:
                image.Heifsave(outputPath,
                    q: quality,
                    compression: Enums.ForeignHeifCompression.Av1,
                    lossless: output.Lossless,
                    keep: keep,
                    pageHeight: pageHeight);
                break;

            case ImageFormat.Heif:
                SaveHeifWithFallback(image, outputPath, quality, output.Lossless, keep, pageHeight);
                break;

            case ImageFormat.Tiff:
                SaveTiffWithCompatibleOptions(image, outputPath, quality, output.Lossless, keep, pageHeight);
                break;

            case ImageFormat.Gif:
                ValidateGifOutputOptions(output);
                image.Gifsave(outputPath, effort: MapGifEffort(quality), keep: keep, pageHeight: pageHeight);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported output format.");
        }
    }

    private static void SaveHeifWithFallback(Image image, string outputPath, int quality, bool lossless, Enums.ForeignKeep keep, int? pageHeight)
    {
        try
        {
            image.Heifsave(outputPath,
                q: quality,
                compression: Enums.ForeignHeifCompression.Hevc,
                lossless: lossless,
                keep: keep,
                pageHeight: pageHeight);
        }
        catch
        {
            // Some libvips builds lack HEVC encoder support. Fallback to AV1.
            image.Heifsave(outputPath,
                q: quality,
                compression: Enums.ForeignHeifCompression.Av1,
                lossless: lossless,
                keep: keep,
                pageHeight: pageHeight);
        }
    }

    private static void SaveTiffWithCompatibleOptions(Image image, string outputPath, int quality, bool lossless, Enums.ForeignKeep keep, int? pageHeight)
    {
        if (lossless)
        {
            image.Tiffsave(outputPath,
                compression: Enums.ForeignTiffCompression.Deflate,
                lossless: true,
                keep: keep,
                pageHeight: pageHeight);
            return;
        }

        // TIFF quality only applies to JPEG-compressed TIFF output.
        image.Tiffsave(outputPath,
            compression: Enums.ForeignTiffCompression.Jpeg,
            q: quality,
            keep: keep,
            pageHeight: pageHeight);
    }

    private static int ResolveQuality(ImageFormat format, OutputOptions options)
    {
        if (options.Quality.HasValue)
        {
            ValidateQuality(options.Quality.Value);
            return options.Quality.Value;
        }

        // Per-format default quality, used by both quality modes when no explicit value is given.
        return format switch
        {
            ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.Tiff => 90,
            ImageFormat.Webp => 85,
            ImageFormat.Avif or ImageFormat.Heif => 50,
            ImageFormat.Gif => 80,
            _ => 85
        };
    }

    private static int MapPngCompression(int quality)
    {
        var normalized = (100 - quality) / 100.0;
        return Math.Clamp((int)Math.Round(normalized * 9, MidpointRounding.AwayFromZero), 0, 9);
    }

    private static int MapGifEffort(int quality)
    {
        var normalized = quality / 100.0;
        return Math.Clamp((int)Math.Round(normalized * 9 + 1, MidpointRounding.AwayFromZero), 1, 10);
    }

    private static bool IsAnimated(Image image)
    {
        var nPages = TryGetInt(image, "n-pages");
        var pageHeight = image.PageHeight;
        return nPages is > 1 && pageHeight > 0 && pageHeight < image.Height;
    }

    private static int[] NormalizeDelays(int[] delays, int frameCount)
    {
        if (delays.Length == frameCount)
        {
            return delays;
        }

        if (delays.Length == 0)
        {
            return Enumerable.Repeat(100, frameCount).ToArray();
        }

        if (delays.Length > frameCount)
        {
            return delays.Take(frameCount).ToArray();
        }

        var output = new int[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            output[i] = delays[Math.Min(i, delays.Length - 1)];
        }

        return output;
    }

    private static int? TryGetInt(Image image, string key)
    {
        if (image.GetTypeOf(key) == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return (int)image.Get(key);
        }
        catch
        {
            return null;
        }
    }

    private static int[]? TryGetIntArray(Image image, string key)
    {
        if (image.GetTypeOf(key) == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return image.Get(key) as int[];
        }
        catch
        {
            return null;
        }
    }

    private static bool CanCopyWithoutReencode(string inputPath, string outputPath, ImageFormat inputFormat, ImageFormat outputFormat, ProcessImageOptions options) =>
        options.Output.QualityMode == ImageQualityMode.MaintainOriginal &&
        !options.Output.Quality.HasValue &&
        !options.Output.Lossless &&
        inputFormat == outputFormat &&
        !options.HasPixelOperations;

    private static void CopyInputToOutput(string inputPath, string outputPath)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var fullOutput = Path.GetFullPath(outputPath);

        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Copy(fullInput, fullOutput, overwrite: true);
    }

    private static string EnsureOutputExtension(string outputPath, ImageFormat format)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var extension = format switch
        {
            ImageFormat.Jpeg => ".jpg",
            ImageFormat.Png => ".png",
            ImageFormat.Webp => ".webp",
            ImageFormat.Avif => ".avif",
            ImageFormat.Tiff => ".tif",
            ImageFormat.Heif => ".heif",
            ImageFormat.Gif => ".gif",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported image format.")
        };

        return Path.ChangeExtension(outputPath, extension);
    }

    private static ImageFormat InferFormatFromPath(string path)
    {
        var extension = Path.GetExtension(path)?.ToLowerInvariant();

        return extension switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".webp" => ImageFormat.Webp,
            ".avif" => ImageFormat.Avif,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".heif" or ".heic" => ImageFormat.Heif,
            ".gif" => ImageFormat.Gif,
            _ => throw new NotSupportedException($"Unsupported image extension '{extension}'. Supported extensions are: .jpg, .jpeg, .png, .webp, .avif, .tif, .tiff, .heif, .heic, .gif.")
        };
    }

    private static void ValidateCommonOptions(ProcessImageOptions options)
    {
        if (options.Resize is not null && options.Upscale is not null)
        {
            throw new InvalidOperationException("Resize and Upscale cannot be used together in the same request.");
        }

        if (options.Output.Quality.HasValue)
        {
            ValidateQuality(options.Output.Quality.Value);
        }

        if (options.Crop is not null)
        {
            ValidateCropOptions(options.Crop);
        }

        if (options.Rotate is not null && options.Rotate.Angle is not (90 or 180 or 270))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Rotate.Angle), "Rotation angle must be 90, 180 or 270 degrees.");
        }

        if (options.RgbAdjust is not null)
        {
            ValidateRgbOptions(options.RgbAdjust);
        }
    }

    private static void ValidateCropOptions(CropOptions options, int? sourceWidth = null, int? sourceHeight = null)
    {
        if (options.Left < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Left), "Crop left must be greater than or equal to 0.");
        }

        if (options.Top < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Top), "Crop top must be greater than or equal to 0.");
        }

        if (options.Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Width), "Crop width must be greater than 0.");
        }

        if (options.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Height), "Crop height must be greater than 0.");
        }

        if (sourceWidth.HasValue && options.Left + options.Width > sourceWidth.Value)
        {
            throw new InvalidOperationException($"Crop rectangle exceeds source width. Source width: {sourceWidth.Value}, Crop: left {options.Left}, width {options.Width}.");
        }

        if (sourceHeight.HasValue && options.Top + options.Height > sourceHeight.Value)
        {
            throw new InvalidOperationException($"Crop rectangle exceeds source height. Source height: {sourceHeight.Value}, Crop: top {options.Top}, height {options.Height}.");
        }
    }

    private static void ValidateGifOutputOptions(OutputOptions output)
    {
        if (output.Lossless)
        {
            throw new NotSupportedException("GIF does not support a lossless toggle in this service. Set Lossless=false for GIF output.");
        }
    }

    private static void ValidateRgbOptions(RgbAdjustOptions options)
    {
        if (options.Brightness < -1 || options.Brightness > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Brightness), "Brightness must be between -1 and 1.");
        }

        if (options.RedScale < 0 || options.RedScale > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RedScale), "RedScale must be between 0 and 3.");
        }

        if (options.GreenScale < 0 || options.GreenScale > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(options.GreenScale), "GreenScale must be between 0 and 3.");
        }

        if (options.BlueScale < 0 || options.BlueScale > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(options.BlueScale), "BlueScale must be between 0 and 3.");
        }

        if (options.Contrast < 0 || options.Contrast > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Contrast), "Contrast must be between 0 and 3.");
        }

        if (options.Saturation < 0 || options.Saturation > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Saturation), "Saturation must be between 0 and 3.");
        }
    }

    private static void ValidateInputPath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path cannot be null or empty.", nameof(inputPath));
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input image file was not found.", inputPath);
        }
    }

    private static void ValidateOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));
        }
    }

    private static void ValidateQuality(int quality)
    {
        if (quality < 1 || quality > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100.");
        }
    }
}
