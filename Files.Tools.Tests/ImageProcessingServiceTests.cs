using Files_Tools.Services;
using NetVips;

namespace Files.Tools.Tests;

[TestClass]
public class ImageProcessingServiceTests
{
    private string _tempRoot = null!;
    private ImageProcessingService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _service = new ImageProcessingService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temporary test assets. Do not fail tests for delayed file release.
        }
    }

    [TestMethod]
    public async Task CompressAsync_RejectsInvalidQuality()
    {
        var input = CreateSolidImage("invalid-quality-input.jpg", 120, 80, ImageFormat.Jpeg);
        var output = Path.Combine(_tempRoot, "out.jpg");

        await AssertThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _service.CompressAsync(input, output, new CompressionOptions
            {
                MaintainOriginalQuality = false,
                Quality = 101,
                Format = ImageFormat.Jpeg
            }));
    }

    [TestMethod]
    public async Task ResizeAsync_RejectsUpscaleRequest()
    {
        var input = CreateSolidImage("resize-source.png", 100, 100, ImageFormat.Png);
        var output = Path.Combine(_tempRoot, "resized.png");

        await AssertThrowsAsync<InvalidOperationException>(async () =>
            await _service.ResizeAsync(input, output, new ResizeOptions
            {
                Width = 140,
                Height = 140
            }));
    }

    [TestMethod]
    public async Task UpscaleAsync_RejectsDownscaleRequest()
    {
        var input = CreateSolidImage("upscale-source.png", 200, 150, ImageFormat.Png);
        var output = Path.Combine(_tempRoot, "upscaled.png");

        await AssertThrowsAsync<InvalidOperationException>(async () =>
            await _service.UpscaleAsync(input, output, new UpscaleOptions
            {
                Width = 120,
                Height = 120
            }));
    }

    [TestMethod]
    public async Task RotateAsync_RejectsAngleOutsideNinetyAndOneEighty()
    {
        var input = CreateSolidImage("rotate-source.webp", 120, 80, ImageFormat.Webp);
        var output = Path.Combine(_tempRoot, "rotated.webp");

        await AssertThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _service.RotateAsync(input, output, new RotateOptions { Angle = 45 }));
    }

    [TestMethod]
    public async Task ProcessImageAsync_CanCombineMirrorAndCompressionWithoutFormatChange()
    {
        var input = CreateGradientJpeg("combo-source.jpg", 220, 140);
        var outputBase = Path.Combine(_tempRoot, "combo-output");

        await _service.ProcessImageAsync(input, outputBase, new ProcessImageOptions
        {
            Mirror = new MirrorOptions { Horizontal = true },
            Output = new OutputOptions
            {
                Format = null,
                QualityMode = ImageQualityMode.ExplicitQuality,
                Quality = 90,
                KeepMetadata = true
            }
        });

        var output = Path.ChangeExtension(outputBase, ".jpg");
        Assert.IsTrue(File.Exists(output));

        using var image = Image.NewFromFile(output, access: Enums.Access.Sequential);
        Assert.AreEqual(220, image.Width);
        Assert.AreEqual(140, image.Height);
    }

    [TestMethod]
    public async Task UpscaleAsync_ProducesRequestedDimensions()
    {
        var input = CreateSolidImage("upscale-dim-source.jpg", 64, 48, ImageFormat.Jpeg);
        var outputBase = Path.Combine(_tempRoot, "upscale-dim-output");

        await _service.UpscaleAsync(input, outputBase, new UpscaleOptions
        {
            Width = 256,
            Height = 192
        }, new OutputOptions
        {
            Format = ImageFormat.Webp,
            QualityMode = ImageQualityMode.ExplicitQuality,
            Quality = 80
        });

        var output = Path.ChangeExtension(outputBase, ".webp");
        Assert.IsTrue(File.Exists(output));

        using var image = Image.NewFromFile(output, access: Enums.Access.Sequential);
        Assert.AreEqual(256, image.Width);
        Assert.AreEqual(192, image.Height);
    }

    [TestMethod]
    public async Task CropAsync_ProducesRequestedDimensions()
    {
        var input = CreateGradientJpeg("crop-source.jpg", 220, 140);
        var outputBase = Path.Combine(_tempRoot, "crop-output");

        await _service.CropAsync(input, outputBase, new CropOptions
        {
            Left = 20,
            Top = 10,
            Width = 80,
            Height = 60
        }, new OutputOptions
        {
            Format = ImageFormat.Png,
            QualityMode = ImageQualityMode.MaintainOriginal
        });

        var output = Path.ChangeExtension(outputBase, ".png");
        Assert.IsTrue(File.Exists(output));

        using var image = Image.NewFromFile(output, access: Enums.Access.Sequential);
        Assert.AreEqual(80, image.Width);
        Assert.AreEqual(60, image.Height);
    }

    [TestMethod]
    public async Task CropAsync_RejectsRectangleOutsideSourceBounds()
    {
        var input = CreateSolidImage("crop-invalid-source.png", 100, 80, ImageFormat.Png);
        var output = Path.Combine(_tempRoot, "crop-invalid-output.png");

        await AssertThrowsAsync<InvalidOperationException>(async () =>
            await _service.CropAsync(input, output, new CropOptions
            {
                Left = 70,
                Top = 10,
                Width = 40,
                Height = 40
            }));
    }

    [TestMethod]
    public async Task ProcessImageAsync_CropPreventsCopyThroughAndKeepsDimensions()
    {
        var input = CreateSolidImage("crop-copy-source.png", 90, 70, ImageFormat.Png);
        var outputBase = Path.Combine(_tempRoot, "crop-copy-output");

        await _service.ProcessImageAsync(input, outputBase, new ProcessImageOptions
        {
            Crop = new CropOptions
            {
                Left = 0,
                Top = 0,
                Width = 90,
                Height = 70
            },
            Output = new OutputOptions
            {
                Format = null,
                QualityMode = ImageQualityMode.MaintainOriginal,
                KeepMetadata = true
            }
        });

        var output = Path.ChangeExtension(outputBase, ".png");
        Assert.IsTrue(File.Exists(output));

        using var image = Image.NewFromFile(output, access: Enums.Access.Sequential);
        Assert.AreEqual(90, image.Width);
        Assert.AreEqual(70, image.Height);
    }

    [TestMethod]
    public async Task ProcessImageAsync_AppliesCropAndRotationBeforeResize()
    {
        var input = CreateSolidImage("crop-rotate-resize-source.png", 100, 80, ImageFormat.Png);
        var outputBase = Path.Combine(_tempRoot, "crop-rotate-resize-output");

        await _service.ProcessImageAsync(input, outputBase, new ProcessImageOptions
        {
            Crop = new CropOptions
            {
                Left = 10,
                Top = 10,
                Width = 60,
                Height = 40
            },
            Rotate = new RotateOptions { Angle = 90 },
            Resize = new ResizeOptions
            {
                Width = 20,
                Height = 30
            },
            Output = new OutputOptions
            {
                Format = ImageFormat.Png,
                QualityMode = ImageQualityMode.MaintainOriginal,
                KeepMetadata = true
            }
        });

        var output = Path.ChangeExtension(outputBase, ".png");
        Assert.IsTrue(File.Exists(output));

        using var image = Image.NewFromFile(output, access: Enums.Access.Sequential);
        Assert.AreEqual(20, image.Width);
        Assert.AreEqual(30, image.Height);
    }

    [TestMethod]
    public async Task ProcessImageAsync_AnimatedGif_PreservesFrameCount()
    {
        var input = CreateAnimatedGif("animated-source.gif", frameCount: 3, frameWidth: 80, frameHeight: 60);
        var outputBase = Path.Combine(_tempRoot, "animated-output");

        await _service.ProcessImageAsync(input, outputBase, new ProcessImageOptions
        {
            Mirror = new MirrorOptions { Horizontal = true },
            Output = new OutputOptions
            {
                Format = ImageFormat.Gif,
                QualityMode = ImageQualityMode.MaintainOriginal,
                KeepMetadata = true
            }
        });

        var output = Path.ChangeExtension(outputBase, ".gif");
        Assert.IsTrue(File.Exists(output));

        using var animated = LoadAllFrames(output);
        var pages = (int)animated.Get("n-pages");
        Assert.AreEqual(3, pages);
        Assert.AreEqual(60, animated.PageHeight);
    }

    [TestMethod]
    public async Task ProcessImageAsync_AnimatedGifCrop_PreservesFrameCountAndUpdatesPageHeight()
    {
        var input = CreateAnimatedGif("animated-crop-source.gif", frameCount: 3, frameWidth: 80, frameHeight: 60);
        var outputBase = Path.Combine(_tempRoot, "animated-crop-output");

        await _service.ProcessImageAsync(input, outputBase, new ProcessImageOptions
        {
            Crop = new CropOptions
            {
                Left = 10,
                Top = 12,
                Width = 40,
                Height = 30
            },
            Output = new OutputOptions
            {
                Format = ImageFormat.Gif,
                QualityMode = ImageQualityMode.MaintainOriginal,
                KeepMetadata = true
            }
        });

        var output = Path.ChangeExtension(outputBase, ".gif");
        Assert.IsTrue(File.Exists(output));

        using var animated = LoadAllFrames(output);
        var pages = (int)animated.Get("n-pages");
        Assert.AreEqual(3, pages);
        Assert.AreEqual(30, animated.PageHeight);
        Assert.AreEqual(40, animated.Width);
        Assert.AreEqual(90, animated.Height);
    }

    private string CreateSolidImage(string fileName, int width, int height, ImageFormat format)
    {
        var output = Path.Combine(_tempRoot, fileName);
        using var black = Image.Black(width, height, bands: 3);
        using var image = black + new[] { 120.0, 60.0, 30.0 };
        SaveByFormat(image, output, format);
        return output;
    }

    private string CreateGradientJpeg(string fileName, int width, int height)
    {
        var output = Path.Combine(_tempRoot, fileName);
        using var grey = Image.Grey(width, height);
        using var rgb = grey.Bandjoin(new[]
        {
            grey,
            grey.Linear(new[] { 0.5 }, new[] { 10.0 })
        });
        rgb.Jpegsave(output, q: 95);
        return output;
    }

    private string CreateAnimatedGif(string fileName, int frameCount, int frameWidth, int frameHeight)
    {
        var output = Path.Combine(_tempRoot, fileName);
        var frames = new List<Image>(frameCount);

        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                using var baseFrame = Image.Black(frameWidth, frameHeight, bands: 3);
                frames.Add(baseFrame + new[] { (double)(30 + i * 50), 20.0, 10.0 });
            }

            using var strip = Image.Arrayjoin(frames.ToArray(), across: 1);
            using var animated = strip.Mutate(mutable =>
            {
                mutable.Set(GValue.GIntType, "page-height", frameHeight);
                mutable.Set(GValue.GIntType, "n-pages", frameCount);
                mutable.Set(GValue.GIntType, "loop", 0);
                mutable.Set(GValue.ArrayIntType, "delay", new[] { 100, 100, 100 });
            });

            animated.Gifsave(output, effort: 6, keep: Enums.ForeignKeep.All, pageHeight: frameHeight);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }

        return output;
    }

    private static Image LoadAllFrames(string path)
    {
        var kwargs = new VOption();
        kwargs.Add("n", -1);
        return Image.NewFromFile(path, access: Enums.Access.Sequential, kwargs: kwargs);
    }

    private static void SaveByFormat(Image image, string path, ImageFormat format)
    {
        switch (format)
        {
            case ImageFormat.Jpeg:
                image.Jpegsave(path, q: 92);
                break;
            case ImageFormat.Png:
                image.Pngsave(path, compression: 4);
                break;
            case ImageFormat.Webp:
                image.Webpsave(path, q: 80);
                break;
            case ImageFormat.Avif:
                image.Heifsave(path, compression: Enums.ForeignHeifCompression.Av1, q: 50);
                break;
            case ImageFormat.Tiff:
                image.Tiffsave(path, compression: Enums.ForeignTiffCompression.Deflate, q: 90);
                break;
            case ImageFormat.Heif:
                image.Heifsave(path, compression: Enums.ForeignHeifCompression.Hevc, q: 50);
                break;
            case ImageFormat.Gif:
                image.Gifsave(path, effort: 6);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}, but no exception was thrown.");
    }
}
