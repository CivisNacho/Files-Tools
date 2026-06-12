using Files_Tools.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Files.Tools.Tests;

[TestClass]
public class VideoServiceTests
{
    private string _tempRoot = null!;
    private VideoService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-video-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _service = new VideoService();
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
            // Best effort cleanup only.
        }
    }

    [TestMethod]
    public async Task ProcessVideoAsync_RejectsMissingInputFile()
    {
        var missingPath = Path.Combine(_tempRoot, "missing.mp4");
        var output = Path.Combine(_tempRoot, "output.mp4");

        await AssertThrowsAsync<FileNotFoundException>(async () =>
            await _service.ProcessVideoAsync(missingPath, output, new ProcessVideoOptions()));
    }

    [TestMethod]
    public async Task EstimateProcessAsync_ReturnsExpectedDimensionsAndReencodeFlags()
    {
        var input = CreateSampleVideo("estimate-source.mp4", width: 160, height: 90, durationSeconds: 3);

        var estimate = await _service.EstimateProcessAsync(input, new ProcessVideoOptions
        {
            Resize = new VideoResizeOptions
            {
                Width = 320,
                Height = 180,
                Mode = ResizeMode.PadToFit
            },
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

        Assert.AreEqual(VideoContainerFormat.Mp4, estimate.OutputFormat);
        Assert.AreEqual(320, estimate.EstimatedWidth);
        Assert.AreEqual(180, estimate.EstimatedHeight);
        Assert.IsTrue(estimate.RequiresVideoReencode);
        Assert.IsNotNull(estimate.EstimatedOutputSizeBytes);
        Assert.IsGreaterThan(0L, estimate.EstimatedOutputSizeBytes!.Value);
        Assert.AreEqual(VideoCodec.H265, estimate.OutputVideoCodec);
    }

    [TestMethod]
    public async Task ResizeAsync_RejectsInvalidDimensions()
    {
        var input = CreateSampleVideo("invalid-resize-source.mp4");
        var output = Path.Combine(_tempRoot, "invalid-resize-output.mp4");

        await AssertThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _service.ResizeAsync(input, output, new VideoResizeOptions
            {
                Width = 0,
                Height = 120
            }));
    }

    [TestMethod]
    public async Task TrimAsync_RejectsInvalidRange()
    {
        var input = CreateSampleVideo("invalid-trim-source.mp4");
        var output = Path.Combine(_tempRoot, "invalid-trim-output.mp4");

        await AssertThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _service.TrimAsync(input, output, new TrimOptions
            {
                Start = TimeSpan.FromSeconds(2),
                End = TimeSpan.FromSeconds(1)
            }));
    }

    [TestMethod]
    public async Task ChangeCodecAsync_RejectsUnsupportedContainerCombination()
    {
        var input = CreateSampleVideo("bad-combo-source.mp4");
        var output = Path.Combine(_tempRoot, "bad-combo-output.webm");

        await AssertThrowsAsync<NotSupportedException>(async () =>
            await _service.ChangeCodecAsync(input, output, new CodecChangeOptions
            {
                VideoCodec = VideoCodec.Vp9,
                AudioCodec = AudioCodec.Aac
            }, new VideoOutputOptions
            {
                Format = VideoContainerFormat.Webm
            }));
    }

    [TestMethod]
    public async Task CombineWithAudioAsync_RejectsMissingAudioFile()
    {
        var input = CreateSampleVideo("missing-audio-source.mp4");
        var output = Path.Combine(_tempRoot, "missing-audio-output.mp4");
        var audio = Path.Combine(_tempRoot, "missing.wav");

        await AssertThrowsAsync<FileNotFoundException>(async () =>
            await _service.CombineWithAudioAsync(input, output, new MuxAudioOptions
            {
                AudioPath = audio
            }));
    }

    [TestMethod]
    public async Task CombineWithSubtitlesAsync_RejectsMissingSubtitleFile()
    {
        var input = CreateSampleVideo("missing-subtitle-source.mp4");
        var output = Path.Combine(_tempRoot, "missing-subtitle-output.mp4");
        var subtitle = Path.Combine(_tempRoot, "missing.srt");

        await AssertThrowsAsync<FileNotFoundException>(async () =>
            await _service.CombineWithSubtitlesAsync(input, output, new MuxSubtitleOptions
            {
                SubtitlePath = subtitle,
                Mode = SubtitleMode.SoftMux
            }));
    }

    [TestMethod]
    public async Task RepairAsync_RemuxMode_ProducesPlayableOutput()
    {
        var input = CreateSampleVideo("repair-remux-source.mp4", width: 160, height: 120, durationSeconds: 2, metadataTitle: "Repair Me");
        var output = Path.Combine(_tempRoot, "repair-remux-output.mp4");

        await _service.RepairAsync(input, output, new RepairOptions
        {
            Mode = RepairMode.Remux
        });

        var info = ProbeMedia(output);
        Assert.IsNotNull(info.PrimaryVideoStream);
        Assert.IsNotNull(info.PrimaryAudioStream);
    }

    [TestMethod]
    public async Task RepairAsync_ReencodeMode_ProducesPlayableOutput()
    {
        var input = CreateSampleVideo("repair-reencode-source.mp4", width: 160, height: 120, durationSeconds: 2);
        var output = Path.Combine(_tempRoot, "repair-reencode-output.mp4");

        await _service.RepairAsync(input, output, new RepairOptions
        {
            Mode = RepairMode.Reencode
        });

        var info = ProbeMedia(output);
        Assert.IsNotNull(info.PrimaryVideoStream);
        Assert.IsNotNull(info.PrimaryAudioStream);
        Assert.AreEqual("h264", info.PrimaryVideoStream!.CodecName);
    }

    [TestMethod]
    public async Task ChangeContainerAsync_WritesRequestedExtension()
    {
        var input = CreateSampleVideo("change-container-source.mp4");
        var outputBase = Path.Combine(_tempRoot, "change-container-output");

        await _service.ChangeContainerAsync(input, outputBase, VideoContainerFormat.Mkv);

        var output = Path.ChangeExtension(outputBase, ".mkv");
        Assert.IsTrue(File.Exists(output));
    }

    [TestMethod]
    public async Task ResizeAsync_ProducesRequestedDimensions()
    {
        foreach (var mode in new[] { ResizeMode.Stretch, ResizeMode.CropToFill, ResizeMode.PadToFit })
        {
            var input = CreateSampleVideo($"resize-source-{mode}.mp4", width: 160, height: 90);
            var output = Path.Combine(_tempRoot, $"resize-{mode}.mp4");

            await _service.ResizeAsync(input, output, new VideoResizeOptions
            {
                Width = 120,
                Height = 120,
                Mode = mode
            });

            var info = ProbeMedia(output);
            Assert.AreEqual(120, info.PrimaryVideoStream!.Width);
            Assert.AreEqual(120, info.PrimaryVideoStream.Height);
        }
    }

    [TestMethod]
    public async Task CompressAsync_MapsPresetToSuccessfulTranscode()
    {
        foreach (var (codec, format) in new[]
        {
            (VideoCodec.H264, VideoContainerFormat.Mp4),
            (VideoCodec.H265, VideoContainerFormat.Mp4),
            (VideoCodec.Av1, VideoContainerFormat.Mkv)
        })
        {
            var input = CreateSampleVideo($"compress-{codec}-source.mp4");
            var outputBase = Path.Combine(_tempRoot, $"compress-{codec}-output");

            await _service.CompressAsync(input, outputBase, new VideoCompressionOptions
            {
                Preset = CompressionPreset.Balanced,
                VideoCodec = codec
            }, new VideoOutputOptions
            {
                Format = format
            });

            var output = Path.ChangeExtension(outputBase, GetExtension(format));
            var info = ProbeMedia(output);
            Assert.AreEqual(GetExpectedProbeCodec(codec), info.PrimaryVideoStream!.CodecName);
        }
    }

    [TestMethod]
    public async Task ChangeCodecAsync_UpdatesVideoAndAudioCodecNames()
    {
        var input = CreateSampleVideo("codec-change-source.mp4");
        var output = Path.Combine(_tempRoot, "codec-change-output.webm");

        await _service.ChangeCodecAsync(input, output, new CodecChangeOptions
        {
            VideoCodec = VideoCodec.Vp9,
            AudioCodec = AudioCodec.Opus
        }, new VideoOutputOptions
        {
            Format = VideoContainerFormat.Webm
        });

        var info = ProbeMedia(output);
        Assert.AreEqual("vp9", info.PrimaryVideoStream!.CodecName);
        Assert.AreEqual("opus", info.PrimaryAudioStream!.CodecName);
    }

    [TestMethod]
    public async Task TrimAsync_ShortensDurationWithinTolerance()
    {
        var input = CreateSampleVideo("trim-source.mp4", durationSeconds: 4);
        var output = Path.Combine(_tempRoot, "trim-output.mp4");

        await _service.TrimAsync(input, output, new TrimOptions
        {
            Start = TimeSpan.FromSeconds(0.5),
            End = TimeSpan.FromSeconds(2.0)
        });

        var info = ProbeMedia(output);
        Assert.IsNotNull(info.Duration);
        Assert.IsLessThan(0.3, Math.Abs(info.Duration!.Value.TotalSeconds - 1.5), $"Expected trimmed duration close to 1.5s, actual: {info.Duration.Value.TotalSeconds:F3}s.");
    }

    [TestMethod]
    public async Task RotateOrMirrorAsync_RotationSwapsDimensions()
    {
        foreach (var angle in new[] { 90, 270 })
        {
            var input = CreateSampleVideo($"rotate-source-{angle}.mp4", width: 160, height: 90);
            var output = Path.Combine(_tempRoot, $"rotate-{angle}.mp4");

            await _service.RotateOrMirrorAsync(input, output, new TransformOptions
            {
                RotationDegrees = angle
            });

            var info = ProbeMedia(output);
            Assert.AreEqual(90, info.PrimaryVideoStream!.Width);
            Assert.AreEqual(160, info.PrimaryVideoStream.Height);
        }
    }

    [TestMethod]
    public async Task RemoveMetadataAsync_ClearsContainerTags()
    {
        var input = CreateSampleVideo("metadata-source.mp4", metadataTitle: "Video Title");
        var output = Path.Combine(_tempRoot, "metadata-output.mp4");

        await _service.RemoveMetadataAsync(input, output);

        var info = ProbeMedia(output);
        Assert.IsFalse(info.FormatTags.ContainsKey("title"), "Expected container title metadata to be removed.");
    }

    [TestMethod]
    public async Task CombineWithAudioAsync_AddsAudioStream()
    {
        var input = CreateSilentVideo("audio-mux-source.mp4");
        var audio = CreateWaveTone("mux-audio.wav");
        var output = Path.Combine(_tempRoot, "audio-mux-output.mp4");

        await _service.CombineWithAudioAsync(input, output, new MuxAudioOptions
        {
            AudioPath = audio,
            AudioCodec = AudioCodec.Aac
        });

        var info = ProbeMedia(output);
        Assert.IsNotNull(info.PrimaryAudioStream);
        Assert.AreEqual("aac", info.PrimaryAudioStream!.CodecName);
    }

    [TestMethod]
    public async Task CombineWithSubtitlesAsync_SoftMux_AddsSubtitleStream()
    {
        var input = CreateSampleVideo("soft-subtitle-source.mp4");
        var subtitle = CreateSubtitleFile("soft-subtitle.srt", "Hello subtitle");
        var output = Path.Combine(_tempRoot, "soft-subtitle-output.mp4");

        await _service.CombineWithSubtitlesAsync(input, output, new MuxSubtitleOptions
        {
            SubtitlePath = subtitle,
            Mode = SubtitleMode.SoftMux,
            Language = "en",
            Title = "English"
        });

        var info = ProbeMedia(output);
        Assert.HasCount(1, info.SubtitleStreams);
        Assert.AreEqual("mov_text", info.SubtitleStreams[0].CodecName);
    }

    [TestMethod]
    public async Task CombineWithSubtitlesAsync_SoftMux_AssIntoMkv_EmbedsAssTrackByStreamCopy()
    {
        var input = CreateSampleVideo("ass-mkv-source.mp4");
        var subtitle = CreateAssSubtitleFile("styled.ass");
        var output = Path.Combine(_tempRoot, "ass-mkv-output.mkv");

        await _service.CombineWithSubtitlesAsync(input, output, new MuxSubtitleOptions
        {
            SubtitlePath = subtitle,
            Mode = SubtitleMode.SoftMux
        });

        // MKV keeps the subtitle inside the file, and it must remain ASS (stream-copied, not
        // transcoded) so the styling/karaoke survives.
        var info = ProbeMedia(output);
        Assert.HasCount(1, info.SubtitleStreams);
        Assert.AreEqual("ass", info.SubtitleStreams[0].CodecName);

        // No sidecar is written for MKV (it is self-contained).
        Assert.IsFalse(File.Exists(Path.ChangeExtension(output, ".ass")), "MKV should not get a sidecar; it embeds the track.");
    }

    [TestMethod]
    public async Task CombineWithSubtitlesAsync_BurnInChangesPixelsAndDoesNotAddSubtitleStream()
    {
        var input = CreateSampleVideo("burn-subtitle-source.mp4");
        var subtitle = CreateSubtitleFile("burn-subtitle.srt", "Burned in text");
        var output = Path.Combine(_tempRoot, "burn-subtitle-output.mp4");

        var inputHash = GetFirstFrameMd5(input);

        await _service.CombineWithSubtitlesAsync(input, output, new MuxSubtitleOptions
        {
            SubtitlePath = subtitle,
            Mode = SubtitleMode.BurnIn
        });

        var outputHash = GetFirstFrameMd5(output);
        var info = ProbeMedia(output);

        Assert.IsEmpty(info.SubtitleStreams);
        Assert.AreNotEqual(inputHash, outputHash, "Expected burn-in subtitles to alter the rendered pixels.");
    }

    [TestMethod]
    public async Task ProcessVideoAsync_MirroredKaraokeBurnIn_PreservesWordHighlightTiming()
    {
        var input = CreateSampleVideo("mirror-karaoke-source.mp4", durationSeconds: 3);
        var subtitle = CreateKaraokeAssFile("mirror-karaoke.ass");
        var output = Path.Combine(_tempRoot, "mirror-karaoke-output.mp4");

        await _service.ProcessVideoAsync(input, output, new ProcessVideoOptions
        {
            Transform = new TransformOptions
            {
                MirrorHorizontal = true
            },
            SubtitleMux = new MuxSubtitleOptions
            {
                SubtitlePath = subtitle,
                Mode = SubtitleMode.BurnIn
            }
        });

        var earlyFrameHash = GetFrameMd5AtTime(output, TimeSpan.FromMilliseconds(400));
        var laterFrameHash = GetFrameMd5AtTime(output, TimeSpan.FromMilliseconds(1400));
        var info = ProbeMedia(output);

        Assert.IsEmpty(info.SubtitleStreams);
        Assert.AreNotEqual(earlyFrameHash, laterFrameHash, "Expected karaoke highlight state to change between timed words after mirroring.");
    }

    [TestMethod]
    public async Task ProcessVideoAsync_FfmpegFailureIncludesDiagnostics()
    {
        var input = CreateSampleVideo("diag-source.mp4");
        var output = Path.Combine(_tempRoot, "diag-output.mp4");

        try
        {
            await _service.ProcessVideoAsync(input, output, new ProcessVideoOptions
            {
                Resize = new VideoResizeOptions
                {
                    Width = 120,
                    Height = 120,
                    Mode = ResizeMode.PadToFit,
                    PadColor = "definitely-not-a-color"
                }
            });
        }
        catch (VideoProcessingException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.BinaryPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.CommandLine));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.StandardError));
            Assert.IsNotNull(ex.ExitCode);
            return;
        }

        Assert.Fail("Expected a VideoProcessingException to be thrown.");
    }

    [TestMethod]
    public async Task CompressAsync_CanBeCancelled()
    {
        var input = CreateSampleVideo("cancel-source.mp4", width: 1280, height: 720, durationSeconds: 8);
        var output = Path.Combine(_tempRoot, "cancel-output.mkv");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await AssertThrowsAsync<OperationCanceledException>(async () =>
            await _service.CompressAsync(input, output, new VideoCompressionOptions
            {
                Preset = CompressionPreset.Balanced,
                VideoCodec = VideoCodec.Av1
            }, new VideoOutputOptions
            {
                Format = VideoContainerFormat.Mkv
            }, cts.Token));
    }

    [TestMethod]
    public void CreateVideoEncoderPlan_PrefersVerifiedHardwareEncoder()
    {
        var plan = VideoService.CreateVideoEncoderPlan(
            VideoCodec.H264,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "h264_qsv" },
            preferHardwareEncoding: true);

        Assert.AreEqual("h264_qsv", plan.EncoderName);
        Assert.IsTrue(plan.IsHardwareAccelerated);
    }

    [TestMethod]
    public void CreateVideoEncoderPlan_FallsBackToSoftwareWhenHardwareIsUnavailable()
    {
        var plan = VideoService.CreateVideoEncoderPlan(
            VideoCodec.H265,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            preferHardwareEncoding: true);

        Assert.AreEqual("libx265", plan.EncoderName);
        Assert.IsFalse(plan.IsHardwareAccelerated);
    }

    [TestMethod]
    public void GetHardwareEncoderCandidates_ReturnsExpectedPriorityOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "h264_nvenc", "h264_amf", "h264_qsv" },
            VideoService.GetHardwareEncoderCandidates(VideoCodec.H264).ToArray());
    }

    private string CreateSampleVideo(string fileName, int width = 160, int height = 120, int durationSeconds = 2, string? metadataTitle = null)
    {
        var output = Path.Combine(_tempRoot, fileName);
        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-f", "lavfi",
            "-i", $"testsrc=size={width}x{height}:rate=24",
            "-f", "lavfi",
            "-i", "sine=frequency=880:sample_rate=48000",
            "-t", durationSeconds.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac"
        };

        if (!string.IsNullOrWhiteSpace(metadataTitle))
        {
            arguments.Add("-metadata");
            arguments.Add($"title={metadataTitle}");
        }

        arguments.Add(output);
        RunFfmpeg(arguments);
        return output;
    }

    private string CreateSilentVideo(string fileName, int width = 160, int height = 120, int durationSeconds = 2)
    {
        var output = Path.Combine(_tempRoot, fileName);
        RunFfmpeg(
        [
            "-y",
            "-hide_banner",
            "-f", "lavfi",
            "-i", $"testsrc=size={width}x{height}:rate=24",
            "-t", durationSeconds.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-an",
            output
        ]);
        return output;
    }

    private string CreateWaveTone(string fileName, int durationSeconds = 2)
    {
        var output = Path.Combine(_tempRoot, fileName);
        RunFfmpeg(
        [
            "-y",
            "-hide_banner",
            "-f", "lavfi",
            "-i", "sine=frequency=660:sample_rate=48000",
            "-t", durationSeconds.ToString(CultureInfo.InvariantCulture),
            output
        ]);
        return output;
    }

    private string CreateSubtitleFile(string fileName, string text)
    {
        var output = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(output,
            "1" + Environment.NewLine +
            "00:00:00,000 --> 00:00:01,500" + Environment.NewLine +
            text + Environment.NewLine + Environment.NewLine);
        return output;
    }

    private string CreateKaraokeAssFile(string fileName)
    {
        var output = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(output,
            """
            [Script Info]
            Title: Test Karaoke
            ScriptType: v4.00+
            PlayResX: 1920
            PlayResY: 1080
            WrapStyle: 0
            ScaledBorderAndShadow: yes

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: KaraokeImpact,Impact,72,&H00FFFFFF,&H00006EFF,&H00000000,&H00000000,-1,0,0,0,100,100,0,0,1,5,0,2,80,80,90,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:02.00,KaraokeImpact,,0,0,0,,{\kf100}One{\kf100} two
            """.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        return output;
    }

    private string CreateAssSubtitleFile(string fileName)
    {
        var output = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(output,
            "[Script Info]" + Environment.NewLine +
            "ScriptType: v4.00+" + Environment.NewLine +
            "PlayResX: 1920" + Environment.NewLine +
            "PlayResY: 1080" + Environment.NewLine +
            Environment.NewLine +
            "[V4+ Styles]" + Environment.NewLine +
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding" + Environment.NewLine +
            "Style: Default,Arial,72,&H00FFFFFF&,&H00FFFFFF&,&H00000000&,&H00000000&,-1,0,0,0,100,100,0,0,1,3,0,2,40,40,40,1" + Environment.NewLine +
            Environment.NewLine +
            "[Events]" + Environment.NewLine +
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text" + Environment.NewLine +
            "Dialogue: 0,0:00:00.00,0:00:01.50,Default,,0,0,0,,{\\fad(60,0)}Hello karaoke" + Environment.NewLine);
        return output;
    }

    private MediaInfo ProbeMedia(string path)
    {
        var json = RunProcessAndCapture("ffprobe",
        [
            "-v", "error",
            "-print_format", "json",
            "-show_streams",
            "-show_format",
            path
        ]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = new List<MediaStream>();
        if (root.TryGetProperty("streams", out var streamsElement))
        {
            foreach (var stream in streamsElement.EnumerateArray())
            {
                streams.Add(new MediaStream
                {
                    CodecType = stream.GetProperty("codec_type").GetString(),
                    CodecName = stream.TryGetProperty("codec_name", out var codecName) ? codecName.GetString() : null,
                    Width = stream.TryGetProperty("width", out var width) && width.TryGetInt32(out var widthValue) ? widthValue : null,
                    Height = stream.TryGetProperty("height", out var height) && height.TryGetInt32(out var heightValue) ? heightValue : null
                });
            }
        }

        TimeSpan? duration = null;
        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var durationElement) &&
                double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds))
            {
                duration = TimeSpan.FromSeconds(durationSeconds);
            }

            if (format.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in tagsElement.EnumerateObject())
                {
                    tags[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        return new MediaInfo
        {
            Streams = streams,
            Duration = duration,
            FormatTags = tags
        };
    }

    private string GetFirstFrameMd5(string path)
    {
        var output = RunProcessAndCapture("ffmpeg",
        [
            "-v", "error",
            "-i", path,
            "-frames:v", "1",
            "-f", "framemd5",
            "-"
        ]);

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .First(line => !line.StartsWith("#", StringComparison.Ordinal))
            .Trim();
    }

    private string GetFrameMd5AtTime(string path, TimeSpan timestamp)
    {
        var output = RunProcessAndCapture("ffmpeg",
        [
            "-v", "error",
            "-ss", timestamp.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture),
            "-i", path,
            "-frames:v", "1",
            "-f", "framemd5",
            "-"
        ]);

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .First(line => !line.StartsWith("#", StringComparison.Ordinal))
            .Trim();
    }

    private static string GetExtension(VideoContainerFormat format)
    {
        return format switch
        {
            VideoContainerFormat.Mp4 => ".mp4",
            VideoContainerFormat.Webm => ".webm",
            VideoContainerFormat.Gif => ".gif",
            VideoContainerFormat.Mkv => ".mkv",
            VideoContainerFormat.Mov => ".mov",
            VideoContainerFormat.Avi => ".avi",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static string GetExpectedProbeCodec(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "h264",
            VideoCodec.H265 => "hevc",
            VideoCodec.Av1 => "av1",
            VideoCodec.Vp9 => "vp9",
            VideoCodec.Vp8 => "vp8",
            VideoCodec.Gif => "gif",
            VideoCodec.Mpeg4 => "mpeg4",
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
        };
    }

    private static void RunFfmpeg(IReadOnlyList<string> arguments)
    {
        _ = RunProcessAndCapture("ffmpeg", arguments);
    }

    private static string RunProcessAndCapture(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start process '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName}' failed with exit code {process.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
        }

        return stdout;
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

    private sealed class MediaInfo
    {
        public List<MediaStream> Streams { get; init; } = [];

        public TimeSpan? Duration { get; init; }

        public Dictionary<string, string> FormatTags { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public MediaStream? PrimaryVideoStream => Streams.FirstOrDefault(stream => stream.CodecType == "video");

        public MediaStream? PrimaryAudioStream => Streams.FirstOrDefault(stream => stream.CodecType == "audio");

        public List<MediaStream> SubtitleStreams => Streams.Where(stream => stream.CodecType == "subtitle").ToList();
    }

    private sealed class MediaStream
    {
        public string? CodecType { get; init; }

        public string? CodecName { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }
    }
}
