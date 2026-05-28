using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services;

// ── Interface ────────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates multi-file, multi-type batch processing by delegating to existing specialized services.
/// </summary>
public interface IBatchProcessingService
{
    /// <summary>
    /// Analyzes the supplied file paths by extension to determine type groups and curated operations.
    /// Files with unrecognized extensions are classified as <see cref="BatchFileType.Unknown"/> and
    /// excluded from available-operation lists but still appear in <see cref="BatchAnalysis.Files"/>.
    /// This method is synchronous and performs no I/O.
    /// </summary>
    BatchAnalysis AnalyzeBatch(IReadOnlyList<string> inputPaths);

    /// <summary>
    /// Executes the batch plan produced from a prior <see cref="AnalyzeBatch"/> call.
    /// Reports per-file and overall progress. Returns a result for every input file regardless
    /// of individual failures unless cancellation is requested or
    /// <see cref="BatchOptions.StopOnFirstError"/> halts the run.
    /// </summary>
    Task<BatchResult> ProcessBatchAsync(
        BatchPlan plan,
        BatchOptions options,
        IProgress<BatchProcessProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

// ── Enums ────────────────────────────────────────────────────────────────────

/// <summary>Broad category a file belongs to, inferred from its extension.</summary>
public enum BatchFileType
{
    Audio,
    Video,
    Document,
    Pdf,
    Image,
    Unknown
}

/// <summary>High-level stage reported during batch processing.</summary>
public enum BatchProcessStage
{
    Analyzing,
    Queued,
    ProcessingFile,
    Completed,
    Cancelled
}

/// <summary>Identifies which operation a <see cref="BatchOperation"/> subclass represents.</summary>
public enum BatchOperationKind
{
    // Audio
    AudioConvert,
    AudioCompress,
    AudioNormalize,

    // Video
    VideoChangeContainer,
    VideoChangeCodec,
    VideoCompress,
    VideoResize,
    VideoExtractAudio,
    VideoSubtitles,

    // Document
    DocumentConvertToPdf,

    // PDF
    PdfCompress,
    PdfRepair,
    PdfOcr,
    PdfMerge,

    // Image
    ImageConvertFormat,
    ImageCompress,
    ImageResize
}

// ── File-type registry ────────────────────────────────────────────────────────

/// <summary>
/// Maps file extensions to <see cref="BatchFileType"/> values and provides the curated
/// list of operations available per type.
/// </summary>
internal static class FileTypeRegistry
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".aac", ".opus", ".ogg", ".flac", ".wav", ".wma", ".aiff", ".aif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".ts", ".mts", ".m2ts"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".odt", ".pptx", ".ppt", ".odp", ".xlsx", ".xls", ".ods", ".csv"
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".avif", ".tif", ".tiff", ".heif", ".heic", ".gif"
    };

    internal static BatchFileType Classify(string extension) =>
        AudioExtensions.Contains(extension)    ? BatchFileType.Audio    :
        VideoExtensions.Contains(extension)    ? BatchFileType.Video    :
        DocumentExtensions.Contains(extension) ? BatchFileType.Document :
        PdfExtensions.Contains(extension)      ? BatchFileType.Pdf      :
        ImageExtensions.Contains(extension)    ? BatchFileType.Image    :
                                                 BatchFileType.Unknown;

    internal static IReadOnlyList<BatchOperationKind> AvailableOperations(BatchFileType type) => type switch
    {
        BatchFileType.Audio => new[]
        {
            BatchOperationKind.AudioConvert,
            BatchOperationKind.AudioCompress,
            BatchOperationKind.AudioNormalize
        },
        BatchFileType.Video => new[]
        {
            BatchOperationKind.VideoChangeContainer,
            BatchOperationKind.VideoChangeCodec,
            BatchOperationKind.VideoCompress,
            BatchOperationKind.VideoResize,
            BatchOperationKind.VideoExtractAudio,
            BatchOperationKind.VideoSubtitles
        },
        BatchFileType.Document => new[]
        {
            BatchOperationKind.DocumentConvertToPdf
        },
        BatchFileType.Pdf => new[]
        {
            BatchOperationKind.PdfCompress,
            BatchOperationKind.PdfRepair,
            BatchOperationKind.PdfOcr,
            BatchOperationKind.PdfMerge
        },
        BatchFileType.Image => new[]
        {
            BatchOperationKind.ImageConvertFormat,
            BatchOperationKind.ImageCompress,
            BatchOperationKind.ImageResize
        },
        _ => Array.Empty<BatchOperationKind>()
    };
}

// ── Operation hierarchy ──────────────────────────────────────────────────────

/// <summary>
/// Base class for all batch operations. Each subclass carries its own strongly-typed options
/// so no parallel options-union is needed.
/// </summary>
public abstract class BatchOperation
{
    public abstract BatchOperationKind Kind { get; }
}

// ── Audio operations ──────────────────────────────────────────────────────────

/// <summary>Converts audio files to a different format, codec, bitrate, or sample rate.</summary>
public sealed class AudioConvertBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.AudioConvert;

    /// <summary>Output file extension including dot (e.g. <c>".mp3"</c>). Required.</summary>
    public required string OutputExtension { get; init; }

    public AudioConversionOptions Options { get; init; } = new();
}

/// <summary>Compresses audio files using a lossy or lossless codec.</summary>
public sealed class AudioCompressBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.AudioCompress;

    public AudioCompressionOptions Options { get; init; } = new();
}

/// <summary>Normalizes audio loudness using peak or LUFS normalization.</summary>
public sealed class AudioNormalizeBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.AudioNormalize;

    public AudioNormalizationOptions Options { get; init; } = new();
}

// ── Video operations ──────────────────────────────────────────────────────────

/// <summary>Changes the container format of video files (e.g. MKV → MP4).</summary>
public sealed class VideoChangeContainerBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.VideoChangeContainer;

    public required VideoContainerFormat TargetContainer { get; init; }
}

/// <summary>Changes the video and/or audio codec of video files.</summary>
public sealed class VideoChangeCodecBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.VideoChangeCodec;

    public required CodecChangeOptions Options { get; init; }
}

/// <summary>Compresses video files using a quality preset.</summary>
public sealed class VideoCompressBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.VideoCompress;

    public VideoCompressionOptions Options { get; init; } = new();
}

/// <summary>Resizes video files to target dimensions.</summary>
public sealed class VideoResizeBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.VideoResize;

    public required VideoResizeOptions Options { get; init; }
}

/// <summary>Extracts the primary audio stream from each video into a standalone audio file.</summary>
public sealed class VideoExtractAudioBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.VideoExtractAudio;

    /// <summary>Output audio file extension including dot. Defaults to <c>".mp3"</c>.</summary>
    public string OutputExtension { get; init; } = ".mp3";
}

/// <summary>
/// Transcribes each video with Whisper and muxes the resulting subtitle file into the output.
/// </summary>
public sealed class VideoSubtitlesBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.VideoSubtitles;

    /// <summary>
    /// When <see langword="true"/>, generates styled ASS subtitles using the advanced
    /// postprocessing pipeline. When <see langword="false"/> (default), generates a plain SRT file.
    /// </summary>
    public bool UseAdvancedAss { get; init; } = false;

    /// <summary>Maximum on-screen duration of a subtitle section in seconds (advanced mode only).</summary>
    public double MaxSectionSeconds { get; init; } = 6.5;

    /// <summary>Maximum word count per subtitle section, or <see langword="null"/> for no cap (advanced mode only).</summary>
    public int? MaxWordsPerSection { get; init; }

    /// <summary>How the generated subtitle file is muxed into the video.</summary>
    public SubtitleMode MuxMode { get; init; } = SubtitleMode.SoftMux;

    /// <summary>BCP-47 language tag written into the subtitle stream metadata (e.g. <c>en</c>).</summary>
    public string? Language { get; init; }

    /// <summary>Human-readable title written into the subtitle stream metadata.</summary>
    public string? Title { get; init; }

    /// <summary>Whether to mark the muxed subtitle stream as the default subtitle stream.</summary>
    public bool SetAsDefault { get; init; }

    /// <summary>
    /// When <see langword="true"/> and <see cref="UseAdvancedAss"/> is also true, generates
    /// karaoke-style ASS subtitles instead of a styled ASS subtitle file.
    /// </summary>
    public bool UseKaraoke { get; init; } = false;

    /// <summary>Optional style preset to apply during advanced ASS generation.</summary>
    public SubtitleStylePreset? StylePreset { get; init; }

    /// <summary>Optional placement to apply during advanced ASS generation and burn-in.</summary>
    public SubtitlePlacementOptions? Placement { get; init; }
}

// ── Document operations ───────────────────────────────────────────────────────

/// <summary>Converts Office and ODF documents to PDF using LibreOffice.</summary>
public sealed class DocumentConvertToPdfBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.DocumentConvertToPdf;

    public DocumentConversionOptions? Options { get; init; }
}

// ── PDF operations ────────────────────────────────────────────────────────────

/// <summary>
/// Compresses PDF files by performing a full qpdf rewrite that rebuilds object streams and
/// removes redundant data. Delegates to <see cref="IPdfService.RepairAsync"/> internally.
/// </summary>
public sealed class PdfCompressBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.PdfCompress;
}

/// <summary>Repairs PDF files by performing a full qpdf load-and-rewrite cycle.</summary>
public sealed class PdfRepairBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.PdfRepair;
}

/// <summary>Adds a searchable text layer to PDF files using Tesseract OCR.</summary>
public sealed class PdfOcrBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.PdfOcr;

    public required PdfOcrOptions Options { get; init; }
}

/// <summary>
/// Merges all PDF files in the group into a single output file.
/// All contributing files produce a <see cref="BatchFileResult"/> that points to the
/// same merged output path.
/// </summary>
public sealed class PdfMergeBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.PdfMerge;

    /// <summary>Filename (without directory) for the merged PDF. Defaults to <c>"merged.pdf"</c>.</summary>
    public string MergedFileName { get; init; } = "merged.pdf";
}

// ── Image operations ──────────────────────────────────────────────────────────

/// <summary>Converts images to a different format (JPEG, PNG, WebP, AVIF, etc.).</summary>
public sealed class ImageConvertFormatBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.ImageConvertFormat;

    public required ImageFormat TargetFormat { get; init; }
    public OutputOptions? OutputOptions { get; init; }
}

/// <summary>Compresses images using quality and lossless settings.</summary>
public sealed class ImageCompressBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.ImageCompress;

    public CompressionOptions Options { get; init; } = new();
}

/// <summary>Resizes images to smaller dimensions.</summary>
public sealed class ImageResizeBatchOperation : BatchOperation
{
    public override BatchOperationKind Kind => BatchOperationKind.ImageResize;

    public required ResizeOptions Options { get; init; }
    public OutputOptions? OutputOptions { get; init; }
}

// ── Plan and options ──────────────────────────────────────────────────────────

/// <summary>
/// Options governing the overall batch execution behavior.
/// </summary>
public sealed class BatchOptions
{
    /// <summary>
    /// Directory where output files are written. When <see langword="null"/>, a
    /// <c>batch_output</c> subdirectory is created next to the first input file.
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// Suffix appended to the input stem for each output filename (e.g. <c>"_processed"</c>).
    /// Defaults to <c>"_processed"</c>. Ignored by <see cref="PdfMergeBatchOperation"/>.
    /// </summary>
    public string OutputSuffix { get; init; } = "_processed";

    /// <summary>
    /// Stops the batch after the first file failure when <see langword="true"/>.
    /// Defaults to <see langword="false"/> (continue-on-error).
    /// </summary>
    public bool StopOnFirstError { get; init; } = false;

    /// <summary>
    /// Maximum number of files processed concurrently. Defaults to 1 (sequential).
    /// Must be at least 1.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = 1;
}

/// <summary>The operation to apply to all files within a single type group.</summary>
public sealed class BatchTypeGroupPlan
{
    /// <summary>The type group this plan applies to.</summary>
    public required BatchFileType FileType { get; init; }

    /// <summary>The operation to apply. Must be compatible with <see cref="FileType"/>.</summary>
    public required BatchOperation Operation { get; init; }
}

/// <summary>
/// Complete plan built by the caller from a <see cref="BatchAnalysis"/>, ready for execution.
/// </summary>
public sealed class BatchPlan
{
    /// <summary>The analysis this plan was built from, used for file and grouping references.</summary>
    public required BatchAnalysis Analysis { get; init; }

    /// <summary>
    /// One plan per type group present in the analysis. Type groups not represented here are
    /// skipped; their files appear in <see cref="BatchResult.FileResults"/> with no output path.
    /// At most one plan per <see cref="BatchFileType"/> is allowed.
    /// </summary>
    public IReadOnlyList<BatchTypeGroupPlan> TypeGroupPlans { get; init; } = Array.Empty<BatchTypeGroupPlan>();
}

// ── Analysis types ────────────────────────────────────────────────────────────

/// <summary>A single file entry in the batch with its resolved type.</summary>
public sealed class BatchFileEntry
{
    public required string InputPath { get; init; }
    public required BatchFileType FileType { get; init; }
}

/// <summary>
/// A group of files sharing the same <see cref="BatchFileType"/>, along with the curated
/// list of operations that can be applied to the group.
/// </summary>
public sealed class BatchTypeGroup
{
    public required BatchFileType FileType { get; init; }
    public required IReadOnlyList<BatchFileEntry> Files { get; init; }

    /// <summary>Operations curated for this type group.</summary>
    public required IReadOnlyList<BatchOperationKind> AvailableOperations { get; init; }
}

/// <summary>
/// Output of <see cref="IBatchProcessingService.AnalyzeBatch"/>. Describes the composition
/// of the supplied file set and the curated operations available per type group.
/// </summary>
public sealed class BatchAnalysis
{
    /// <summary>All input files in supply order, including <see cref="BatchFileType.Unknown"/> entries.</summary>
    public required IReadOnlyList<BatchFileEntry> Files { get; init; }

    /// <summary>Files grouped by type, excluding Unknown.</summary>
    public required IReadOnlyList<BatchTypeGroup> TypeGroups { get; init; }

    /// <summary>Files whose extension was not recognized.</summary>
    public required IReadOnlyList<BatchFileEntry> UnknownFiles { get; init; }
}

// ── Progress ──────────────────────────────────────────────────────────────────

/// <summary>Progress snapshot for an in-flight batch processing job.</summary>
public sealed class BatchProcessProgress
{
    /// <summary>Current high-level stage.</summary>
    public BatchProcessStage Stage { get; init; }

    /// <summary>Overall progress from 0.0 to 1.0 across all files.</summary>
    public double OverallFraction { get; init; }

    /// <summary>1-based index of the file currently being processed. 0 before the first file starts.</summary>
    public int CurrentFileIndex { get; init; }

    /// <summary>Total number of processable files in the batch (Unknown and skipped files excluded).</summary>
    public int TotalFileCount { get; init; }

    /// <summary>Absolute path of the file currently being processed.</summary>
    public string? CurrentFilePath { get; init; }

    /// <summary>
    /// Sub-progress [0.0–1.0] within the current file's operation forwarded from the underlying service.
    /// <see langword="null"/> when the underlying service does not expose measurable per-file progress.
    /// </summary>
    public double? FileProgress { get; init; }

    /// <summary>User-facing description of the current activity.</summary>
    public string StageDescription { get; init; } = string.Empty;
}

// ── Results ───────────────────────────────────────────────────────────────────

/// <summary>Result for a single file in the batch.</summary>
public sealed class BatchFileResult
{
    public required string InputPath { get; init; }

    /// <summary><see langword="true"/> when the file was processed successfully.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Absolute path of the output file on success.
    /// For <see cref="PdfMergeBatchOperation"/> all contributing files share the same output path.
    /// <see langword="null"/> on failure or when the file's type had no plan.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>The exception that caused failure, or <see langword="null"/> on success.</summary>
    public Exception? Error { get; init; }

    /// <summary>Non-fatal warnings forwarded from the underlying service when available.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>Aggregate result for the entire batch operation.</summary>
public sealed class BatchResult
{
    public required IReadOnlyList<BatchFileResult> FileResults { get; init; }

    /// <summary>Number of files that completed successfully.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of files that failed with an exception.</summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Number of files skipped because their type had no corresponding plan or they were Unknown.
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>Whether the batch was cancelled before all files completed.</summary>
    public bool WasCancelled { get; init; }
}

// ── Exception ─────────────────────────────────────────────────────────────────

/// <summary>
/// Thrown when a <see cref="BatchPlan"/> or <see cref="BatchOptions"/> is structurally invalid
/// before any processing begins.
/// </summary>
public sealed class BatchProcessingValidationException : ArgumentException
{
    public BatchProcessingValidationException(string message) : base(message) { }
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates multi-file, multi-type batch processing by delegating to the app's existing
/// specialized services: Audio, Video, Document, PDF, and Image.
/// </summary>
public sealed class BatchProcessingService : IBatchProcessingService
{
    private readonly IAudioProcessingService _audio;
    private readonly IVideoProcessingService _video;
    private readonly IDocumentService _document;
    private readonly IPdfService _pdf;
    private readonly IImageProcessingService _image;

    public BatchProcessingService(
        IAudioProcessingService audioProcessingService,
        IVideoProcessingService videoProcessingService,
        IDocumentService documentService,
        IPdfService pdfService,
        IImageProcessingService imageProcessingService)
    {
        _audio    = audioProcessingService;
        _video    = videoProcessingService;
        _document = documentService;
        _pdf      = pdfService;
        _image    = imageProcessingService;
    }

    // ── IBatchProcessingService ───────────────────────────────────────────────

    /// <inheritdoc />
    public BatchAnalysis AnalyzeBatch(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths is null) throw new ArgumentNullException(nameof(inputPaths));

        var allEntries = inputPaths
            .Select(p => new BatchFileEntry
            {
                InputPath = p,
                FileType  = FileTypeRegistry.Classify(Path.GetExtension(p))
            })
            .ToList();

        var unknownFiles = allEntries
            .Where(e => e.FileType == BatchFileType.Unknown)
            .ToList();

        var typeGroups = allEntries
            .Where(e => e.FileType != BatchFileType.Unknown)
            .GroupBy(e => e.FileType)
            .Select(g => new BatchTypeGroup
            {
                FileType           = g.Key,
                Files              = g.ToList(),
                AvailableOperations = FileTypeRegistry.AvailableOperations(g.Key)
            })
            .ToList();

        return new BatchAnalysis
        {
            Files        = allEntries,
            TypeGroups   = typeGroups,
            UnknownFiles = unknownFiles
        };
    }

    /// <inheritdoc />
    public async Task<BatchResult> ProcessBatchAsync(
        BatchPlan plan,
        BatchOptions options,
        IProgress<BatchProcessProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (plan is null)    throw new ArgumentNullException(nameof(plan));
        if (options is null) throw new ArgumentNullException(nameof(options));

        ValidatePlan(plan, options);

        // Build lookup: FileType → pipeline-ordered list of plans
        var plansByType = plan.TypeGroupPlans
            .GroupBy(p => p.FileType)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<BatchTypeGroupPlan>)g
                         .OrderBy(p => GetPipelineOrder(p.Operation.Kind))
                         .ToList());

        // Resolve output directory
        var outputDirectory = ResolveOutputDirectory(options, plan.Analysis.Files);
        Directory.CreateDirectory(outputDirectory);

        // Partition files into work items and skipped items
        var workItems = new List<(BatchFileEntry Entry, IReadOnlyList<BatchTypeGroupPlan> Plans)>();
        var skipped   = new List<BatchFileEntry>();

        foreach (var entry in plan.Analysis.Files)
        {
            if (entry.FileType == BatchFileType.Unknown || !plansByType.TryGetValue(entry.FileType, out var plans))
                skipped.Add(entry);
            else
                workItems.Add((entry, plans));
        }

        // Separate merge group (PdfMerge is many-to-one and handled atomically)
        var mergeWorkItems = workItems
            .Where(w => w.Plans.Count == 1 && w.Plans[0].Operation.Kind == BatchOperationKind.PdfMerge)
            .ToList();
        var regularWorkItems = workItems
            .Where(w => !(w.Plans.Count == 1 && w.Plans[0].Operation.Kind == BatchOperationKind.PdfMerge))
            .ToList();

        var totalProcessable = regularWorkItems.Count + (mergeWorkItems.Count > 0 ? 1 : 0);

        Report(progress, BatchProcessStage.Analyzing, 0.0, 0, totalProcessable, null, null, "Analyzing batch");

        var fileResults = new List<BatchFileResult>();
        var cancelled   = false;
        var filesDone   = 0;

        // ── Handle PdfMerge group atomically ─────────────────────────────────

        if (mergeWorkItems.Count > 0)
        {
            var mergeOp      = (PdfMergeBatchOperation)mergeWorkItems[0].Plans[0].Operation;
            var mergedOutput  = Path.Combine(outputDirectory, mergeOp.MergedFileName);
            var mergePaths    = mergeWorkItems.Select(w => w.Entry.InputPath).ToList();
            var currentIndex  = filesDone + 1;

            Report(progress, BatchProcessStage.ProcessingFile,
                (double)filesDone / totalProcessable,
                currentIndex, totalProcessable,
                mergeWorkItems[0].Entry.InputPath, null,
                $"Merging {mergePaths.Count} PDFs");

            string? mergedPath = null;
            Exception? mergeError = null;

            try
            {
                await _pdf.MergeAsync(mergePaths, mergedOutput, cancellationToken).ConfigureAwait(false);
                mergedPath = mergedOutput;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                mergeError = ex;
            }

            foreach (var w in mergeWorkItems)
            {
                fileResults.Add(new BatchFileResult
                {
                    InputPath  = w.Entry.InputPath,
                    IsSuccess  = mergedPath is not null,
                    OutputPath = mergedPath,
                    Error      = mergeError
                });
            }

            filesDone++;

            if (cancelled)
                return BuildResult(fileResults, skipped, wasCancelled: true);

            if (mergeError is not null && options.StopOnFirstError)
                return BuildResult(fileResults, skipped, wasCancelled: false);
        }

        // ── Main processing loop ──────────────────────────────────────────────

        using var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);
        var tasks = new List<Task>();
        var resultsLock = new object();
        var stopRequested = false;

        foreach (var (entry, plans) in regularWorkItems)
        {
            if (cancellationToken.IsCancellationRequested || stopRequested)
            {
                lock (resultsLock)
                {
                    fileResults.Add(new BatchFileResult { InputPath = entry.InputPath, IsSuccess = false });
                }
                continue;
            }

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var capturedEntry = entry;
            var capturedPlans = plans;
            var capturedIndex = Interlocked.Increment(ref filesDone);

            tasks.Add(Task.Run(async () =>
            {
                BatchFileResult result;
                try
                {
                    var outputPath = BuildOutputPath(capturedEntry.InputPath, capturedPlans, outputDirectory, options.OutputSuffix);

                    double subProgress = 0;
                    void SubProgressCallback(double fraction)
                    {
                        subProgress = fraction;
                        Report(progress, BatchProcessStage.ProcessingFile,
                            (capturedIndex - 1 + fraction) / totalProcessable,
                            capturedIndex, totalProcessable,
                            capturedEntry.InputPath, fraction,
                            $"Processing {Path.GetFileName(capturedEntry.InputPath)}");
                    }

                    Report(progress, BatchProcessStage.ProcessingFile,
                        (double)(capturedIndex - 1) / totalProcessable,
                        capturedIndex, totalProcessable,
                        capturedEntry.InputPath, 0.0,
                        $"Processing {Path.GetFileName(capturedEntry.InputPath)}");

                    var (outPath, warnings) = capturedPlans.Count == 1
                        ? await ProcessFileAsync(
                              capturedEntry.InputPath, outputPath,
                              capturedPlans[0].Operation,
                              SubProgressCallback, cancellationToken).ConfigureAwait(false)
                        : await ProcessFilePipelineAsync(
                              capturedEntry.InputPath, outputPath,
                              capturedPlans,
                              SubProgressCallback, cancellationToken).ConfigureAwait(false);

                    result = new BatchFileResult
                    {
                        InputPath  = capturedEntry.InputPath,
                        IsSuccess  = true,
                        OutputPath = outPath,
                        Warnings   = warnings
                    };
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    result = new BatchFileResult { InputPath = capturedEntry.InputPath, IsSuccess = false };
                }
                catch (Exception ex)
                {
                    result = new BatchFileResult { InputPath = capturedEntry.InputPath, IsSuccess = false, Error = ex };
                    if (options.StopOnFirstError)
                        stopRequested = true;
                }

                lock (resultsLock)
                {
                    fileResults.Add(result);
                }

                semaphore.Release();
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var wasCancelledFinal = cancelled || cancellationToken.IsCancellationRequested;
        Report(progress,
            wasCancelledFinal ? BatchProcessStage.Cancelled : BatchProcessStage.Completed,
            1.0, totalProcessable, totalProcessable, null, null,
            wasCancelledFinal ? "Batch cancelled" : "Batch complete");

        return BuildResult(fileResults, skipped, wasCancelledFinal);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ValidatePlan(BatchPlan plan, BatchOptions options)
    {
        if (options.MaxDegreeOfParallelism < 1)
            throw new BatchProcessingValidationException(
                $"{nameof(BatchOptions.MaxDegreeOfParallelism)} must be at least 1.");

        foreach (var p in plan.TypeGroupPlans)
        {
            var allowed = FileTypeRegistry.AvailableOperations(p.FileType);
            if (!allowed.Contains(p.Operation.Kind))
                throw new BatchProcessingValidationException(
                    $"Operation '{p.Operation.Kind}' is not valid for file type '{p.FileType}'.");
        }

        // PdfMerge is many-to-one and cannot be combined with other PDF operations.
        var pdfOps = plan.TypeGroupPlans.Where(p => p.FileType == BatchFileType.Pdf).ToList();
        if (pdfOps.Any(p => p.Operation.Kind == BatchOperationKind.PdfMerge) && pdfOps.Count > 1)
            throw new BatchProcessingValidationException(
                "PdfMerge cannot be combined with other PDF operations in the same batch.");
    }

    /// <summary>
    /// Defines the pipeline order within a file-type group so that operations that change the
    /// container/format run before those that re-encode or adjust quality.
    /// </summary>
    private static int GetPipelineOrder(BatchOperationKind kind) => kind switch
    {
        BatchOperationKind.AudioConvert   => 0,
        BatchOperationKind.AudioCompress  => 1,
        BatchOperationKind.AudioNormalize => 2,

        BatchOperationKind.VideoChangeContainer => 0,
        BatchOperationKind.VideoChangeCodec     => 1,
        BatchOperationKind.VideoCompress        => 2,
        BatchOperationKind.VideoResize          => 3,
        BatchOperationKind.VideoExtractAudio    => 4,
        BatchOperationKind.VideoSubtitles       => 5,

        BatchOperationKind.PdfRepair   => 0,
        BatchOperationKind.PdfCompress => 1,
        BatchOperationKind.PdfOcr      => 2,

        BatchOperationKind.ImageConvertFormat => 0,
        BatchOperationKind.ImageCompress      => 1,
        BatchOperationKind.ImageResize        => 2,

        _ => 99
    };

    private static string ResolveOutputDirectory(BatchOptions options, IReadOnlyList<BatchFileEntry> allFiles)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
            return options.OutputDirectory;

        var knownFile = allFiles.FirstOrDefault(f => f.FileType != BatchFileType.Unknown);
        var referenceDir = knownFile is not null
            ? Path.GetDirectoryName(knownFile.InputPath) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;

        return Path.Combine(referenceDir, "batch_output");
    }

    private static string BuildOutputPath(string inputPath, IReadOnlyList<BatchTypeGroupPlan> plans, string outputDirectory, string suffix)
    {
        // The final output extension is determined by the last operation in the pipeline.
        var lastOp = plans[plans.Count - 1].Operation;
        var stem   = Path.GetFileNameWithoutExtension(inputPath);
        var ext    = ResolveOutputExtension(inputPath, lastOp);
        return Path.Combine(outputDirectory, $"{stem}{suffix}{ext}");
    }

    /// <summary>
    /// Runs a sequence of operations on a single file as a pipeline: the output of each step
    /// becomes the input of the next, using temp files for intermediate steps.
    /// The final output is written to <paramref name="finalOutputPath"/>.
    /// </summary>
    private async Task<(string OutputPath, IReadOnlyList<string> Warnings)> ProcessFilePipelineAsync(
        string inputPath,
        string finalOutputPath,
        IReadOnlyList<BatchTypeGroupPlan> plans,
        Action<double>? subProgressCallback,
        CancellationToken ct)
    {
        var allWarnings = new List<string>();
        var tempFiles   = new List<string>();

        try
        {
            var currentInput = inputPath;

            for (var i = 0; i < plans.Count; i++)
            {
                var isLast     = i == plans.Count - 1;
                var stepOutput = isLast
                    ? finalOutputPath
                    : Path.Combine(
                          Path.GetTempPath(),
                          $"batch_tmp_{Guid.NewGuid():N}{ResolveOutputExtension(currentInput, plans[i].Operation)}");

                if (!isLast)
                    tempFiles.Add(stepOutput);

                var stepIndex = i;
                void StepProgress(double f) =>
                    subProgressCallback?.Invoke((stepIndex + f) / plans.Count);

                var (_, warnings) = await ProcessFileAsync(
                    currentInput, stepOutput, plans[i].Operation,
                    StepProgress, ct).ConfigureAwait(false);

                allWarnings.AddRange(warnings);
                currentInput = stepOutput;
            }

            return (finalOutputPath, allWarnings);
        }
        finally
        {
            foreach (var tmp in tempFiles)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    private static string ResolveOutputExtension(string inputPath, BatchOperation operation) => operation switch
    {
        AudioConvertBatchOperation op        => op.OutputExtension,
        VideoChangeContainerBatchOperation op => ContainerToExtension(op.TargetContainer),
        VideoExtractAudioBatchOperation op   => op.OutputExtension,
        DocumentConvertToPdfBatchOperation   => ".pdf",
        ImageConvertFormatBatchOperation op  => FormatToExtension(op.TargetFormat),
        _                                    => Path.GetExtension(inputPath)
    };

    private static string ContainerToExtension(VideoContainerFormat format) => format switch
    {
        VideoContainerFormat.Mp4  => ".mp4",
        VideoContainerFormat.Webm => ".webm",
        VideoContainerFormat.Gif  => ".gif",
        VideoContainerFormat.Mkv  => ".mkv",
        VideoContainerFormat.Mov  => ".mov",
        VideoContainerFormat.Avi  => ".avi",
        _                         => ".mp4"
    };

    private static string FormatToExtension(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Png  => ".png",
        ImageFormat.Webp => ".webp",
        ImageFormat.Avif => ".avif",
        ImageFormat.Tiff => ".tiff",
        ImageFormat.Heif => ".heif",
        ImageFormat.Gif  => ".gif",
        _                => ".jpg"
    };

    private async Task<(string OutputPath, IReadOnlyList<string> Warnings)> ProcessFileAsync(
        string inputPath,
        string outputPath,
        BatchOperation operation,
        Action<double>? subProgressCallback,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            AudioConvertBatchOperation op   => await ProcessAudioConvertAsync(inputPath, outputPath, op, subProgressCallback, cancellationToken).ConfigureAwait(false),
            AudioCompressBatchOperation op  => await ProcessAudioCompressAsync(inputPath, outputPath, op, subProgressCallback, cancellationToken).ConfigureAwait(false),
            AudioNormalizeBatchOperation op => await ProcessAudioNormalizeAsync(inputPath, outputPath, op, subProgressCallback, cancellationToken).ConfigureAwait(false),

            VideoChangeContainerBatchOperation op => await ProcessVideoChangeContainerAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),
            VideoChangeCodecBatchOperation op     => await ProcessVideoChangeCodecAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),
            VideoCompressBatchOperation op        => await ProcessVideoCompressAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),
            VideoResizeBatchOperation op          => await ProcessVideoResizeAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),
            VideoExtractAudioBatchOperation op    => await ProcessVideoExtractAudioAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),
            VideoSubtitlesBatchOperation op       => await ProcessVideoSubtitlesAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),

            DocumentConvertToPdfBatchOperation op => await ProcessDocumentConvertAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),

            PdfCompressBatchOperation  => await ProcessPdfRepairAsync(inputPath, outputPath, cancellationToken).ConfigureAwait(false),
            PdfRepairBatchOperation    => await ProcessPdfRepairAsync(inputPath, outputPath, cancellationToken).ConfigureAwait(false),
            PdfOcrBatchOperation op    => await ProcessPdfOcrAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),

            ImageConvertFormatBatchOperation op => await ProcessImageConvertAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),
            ImageCompressBatchOperation op      => await ProcessImageCompressAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),
            ImageResizeBatchOperation op        => await ProcessImageResizeAsync(inputPath, outputPath, op, cancellationToken).ConfigureAwait(false),

            _ => throw new NotSupportedException($"Unsupported batch operation kind: {operation.Kind}")
        };
    }

    // ── Audio dispatch ────────────────────────────────────────────────────────

    private async Task<(string, IReadOnlyList<string>)> ProcessAudioConvertAsync(
        string input, string output, AudioConvertBatchOperation op,
        Action<double>? subProgress, CancellationToken ct)
    {
        var result = await _audio.ConvertAsync(
            input, output, op.Options,
            CreateAudioProgressAdapter(subProgress), ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessAudioCompressAsync(
        string input, string output, AudioCompressBatchOperation op,
        Action<double>? subProgress, CancellationToken ct)
    {
        await _audio.CompressAsync(
            input, output, op.Options,
            CreateAudioProgressAdapter(subProgress), ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessAudioNormalizeAsync(
        string input, string output, AudioNormalizeBatchOperation op,
        Action<double>? subProgress, CancellationToken ct)
    {
        await _audio.NormalizeAsync(
            input, output, op.Options,
            CreateAudioProgressAdapter(subProgress), ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    // ── Video dispatch ────────────────────────────────────────────────────────

    private async Task<(string, IReadOnlyList<string>)> ProcessVideoChangeContainerAsync(
        string input, string output, VideoChangeContainerBatchOperation op, CancellationToken ct)
    {
        await _video.ChangeContainerAsync(input, output, op.TargetContainer, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessVideoChangeCodecAsync(
        string input, string output, VideoChangeCodecBatchOperation op, CancellationToken ct)
    {
        await _video.ChangeCodecAsync(input, output, op.Options, null, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessVideoCompressAsync(
        string input, string output, VideoCompressBatchOperation op, CancellationToken ct)
    {
        await _video.CompressAsync(input, output, op.Options, null, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessVideoResizeAsync(
        string input, string output, VideoResizeBatchOperation op, CancellationToken ct)
    {
        await _video.ResizeAsync(input, output, op.Options, null, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessVideoExtractAudioAsync(
        string input, string output, VideoExtractAudioBatchOperation op, CancellationToken ct)
    {
        await _video.ExtractAudioAsync(input, output, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessVideoSubtitlesAsync(
        string input, string output, VideoSubtitlesBatchOperation op, CancellationToken ct)
    {
        // Write the subtitle file next to the output video in a temp path.
        var subtitleExt    = op.UseAdvancedAss ? ".ass" : ".srt";
        var subtitlePath   = Path.Combine(
            Path.GetDirectoryName(output) ?? Path.GetTempPath(),
            Path.GetFileNameWithoutExtension(output) + subtitleExt);

        var postprocessingOptions = op.UseAdvancedAss
            ? new SubtitlePostprocessingOptions
              {
                  MaximumDuration   = TimeSpan.FromSeconds(op.MaxSectionSeconds),
                  MaxWordsPerSection = op.MaxWordsPerSection
              }
            : null;

        var subtitlesService = new SubtitlesService();

        if (op.UseAdvancedAss)
        {
            if (op.UseKaraoke)
                await subtitlesService.GenerateKaraokeAssAsync(input, subtitlePath, postprocessingOptions, op.StylePreset, op.Placement, cancellationToken: ct).ConfigureAwait(false);
            else
                await subtitlesService.GenerateStyledAssAsync(input, subtitlePath, postprocessingOptions, op.StylePreset, op.Placement, cancellationToken: ct).ConfigureAwait(false);
        }
        else
            await subtitlesService.GenerateSrtAsync(input, subtitlePath, ct).ConfigureAwait(false);

        var muxOptions = new MuxSubtitleOptions
        {
            SubtitlePath = subtitlePath,
            Mode         = op.MuxMode,
            Language     = op.Language,
            Title        = op.Title,
            SetAsDefault = op.SetAsDefault,
            Placement    = op.Placement
        };

        await _video.CombineWithSubtitlesAsync(input, output, muxOptions, cancellationToken: ct).ConfigureAwait(false);

        return (output, Array.Empty<string>());
    }

    // ── Document dispatch ─────────────────────────────────────────────────────

    private async Task<(string, IReadOnlyList<string>)> ProcessDocumentConvertAsync(
        string input, string output, DocumentConvertToPdfBatchOperation op, CancellationToken ct)
    {
        await _document.ConvertToPdfAsync(input, output, op.Options, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    // ── PDF dispatch ──────────────────────────────────────────────────────────

    private async Task<(string, IReadOnlyList<string>)> ProcessPdfRepairAsync(
        string input, string output, CancellationToken ct)
    {
        await _pdf.RepairAsync(input, output, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessPdfOcrAsync(
        string input, string output, PdfOcrBatchOperation op, CancellationToken ct)
    {
        await _pdf.OcrAsync(input, output, op.Options, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    // ── Image dispatch ────────────────────────────────────────────────────────

    private async Task<(string, IReadOnlyList<string>)> ProcessImageConvertAsync(
        string input, string output, ImageConvertFormatBatchOperation op, CancellationToken ct)
    {
        await _image.ConvertFormatAsync(input, output, op.TargetFormat, op.OutputOptions, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessImageCompressAsync(
        string input, string output, ImageCompressBatchOperation op, CancellationToken ct)
    {
        await _image.CompressAsync(input, output, op.Options, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    private async Task<(string, IReadOnlyList<string>)> ProcessImageResizeAsync(
        string input, string output, ImageResizeBatchOperation op, CancellationToken ct)
    {
        await _image.ResizeAsync(input, output, op.Options, op.OutputOptions, ct).ConfigureAwait(false);
        return (output, Array.Empty<string>());
    }

    // ── Sub-progress adapters ─────────────────────────────────────────────────

    private static IProgress<AudioProcessProgress>? CreateAudioProgressAdapter(Action<double>? callback)
    {
        if (callback is null) return null;
        return new DelegateProgress<AudioProcessProgress>(p => callback(p.OverallPercent));
    }

    // ── Result builder ────────────────────────────────────────────────────────

    private static BatchResult BuildResult(
        List<BatchFileResult> fileResults,
        List<BatchFileEntry> skipped,
        bool wasCancelled)
    {
        var skippedResults = skipped.Select(e => new BatchFileResult
        {
            InputPath = e.InputPath,
            IsSuccess = false
        }).ToList();

        var allResults = fileResults.Concat(skippedResults).ToList();

        return new BatchResult
        {
            FileResults   = allResults,
            SuccessCount  = allResults.Count(r => r.IsSuccess),
            FailureCount  = allResults.Count(r => !r.IsSuccess && r.Error is not null),
            SkippedCount  = skippedResults.Count,
            WasCancelled  = wasCancelled
        };
    }

    // ── Progress reporting ────────────────────────────────────────────────────

    private static void Report(
        IProgress<BatchProcessProgress>? progress,
        BatchProcessStage stage,
        double overallFraction,
        int currentFileIndex,
        int totalFileCount,
        string? currentFilePath,
        double? fileProgress,
        string description)
    {
        progress?.Report(new BatchProcessProgress
        {
            Stage            = stage,
            OverallFraction  = overallFraction,
            CurrentFileIndex = currentFileIndex,
            TotalFileCount   = totalFileCount,
            CurrentFilePath  = currentFilePath,
            FileProgress     = fileProgress,
            StageDescription = description
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class DelegateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        internal DelegateProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}
