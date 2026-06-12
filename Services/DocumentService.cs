using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Public types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Defines document format conversion operations backed by LibreOffice CLI.
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// Converts an Office or LibreOffice document to PDF using LibreOffice headless mode.
    /// <para>
    /// Supported input formats: .docx, .doc, .odt (text), .pptx, .ppt, .odp (presentation),
    /// .xlsx, .xls, .ods, .csv (spreadsheet).
    /// </para>
    /// </summary>
    /// <param name="inputPath">Absolute path to the source document file.</param>
    /// <param name="outputPath">Absolute path where the resulting PDF will be written.</param>
    /// <param name="options">
    /// Optional conversion settings (PDF variant, quality, page range). When null, all defaults apply.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation and kill the LibreOffice process.</param>
    /// <exception cref="ArgumentException">Thrown when a path argument is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="inputPath"/> does not exist.</exception>
    /// <exception cref="NotSupportedException">Thrown when the input extension is not in the supported format list.</exception>
    /// <exception cref="DocumentConversionException">Thrown when LibreOffice exits with a non-zero code or cannot be launched.</exception>
    Task ConvertToPdfAsync(
        string inputPath,
        string outputPath,
        DocumentConversionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts all images embedded in a document and packs them into a single ZIP archive.
    /// <para>
    /// Supported input formats: .docx, .doc, .odt (text), .pptx, .ppt, .odp (presentation),
    /// .xlsx, .xls, .ods (spreadsheet). CSV is not supported because it cannot contain embedded images.
    /// </para>
    /// <para>
    /// Modern ZIP-based formats (.docx, .pptx, .xlsx, .odt, .odp, .ods) are read directly — no
    /// LibreOffice invocation is needed. Legacy binary formats (.doc, .ppt, .xls) are first
    /// converted to their modern equivalent via LibreOffice headless, then extracted; the
    /// intermediate file is discarded automatically.
    /// </para>
    /// <para>
    /// Images are stored at the root of the output ZIP with their original filenames. If two
    /// images share the same name a numeric suffix is appended to avoid collisions.
    /// </para>
    /// </summary>
    /// <param name="inputPath">Absolute path to the source document file.</param>
    /// <param name="outputZipPath">Absolute path where the resulting ZIP archive will be written.</param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation. For legacy formats, also kills the LibreOffice process.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when a path argument is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="inputPath"/> does not exist.</exception>
    /// <exception cref="NotSupportedException">Thrown when the input extension does not support image extraction (e.g. .csv).</exception>
    /// <exception cref="InvalidOperationException">Thrown when the document contains no embedded images.</exception>
    /// <exception cref="DocumentConversionException">
    /// Thrown when a required LibreOffice conversion (legacy formats only) fails or LibreOffice cannot be launched.
    /// </exception>
    Task ExtractImagesAsync(
        string inputPath,
        string outputZipPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to repair a document by performing a full load-and-save cycle through LibreOffice.
    /// <para>
    /// During load, LibreOffice parses the file using its internal recovery logic, which rebuilds
    /// broken XML structures, drops orphaned media references, and resolves inconsistent internal
    /// state. During save, it produces a freshly serialised, well-formed output file.
    /// </para>
    /// <para>
    /// The output format is determined by the extension of <paramref name="outputPath"/>. When it
    /// matches the input extension the operation is a pure repair. When they differ (e.g. input
    /// <c>.doc</c>, output <c>.docx</c>) the repair also performs a format upgrade. All formats
    /// supported by <see cref="ConvertToPdfAsync"/> are valid for both input and output.
    /// </para>
    /// <para>
    /// Passing the same path for both <paramref name="inputPath"/> and <paramref name="outputPath"/>
    /// is safe and performs an in-place repair: LibreOffice reads the original before the destination
    /// file is touched.
    /// </para>
    /// <para>
    /// Note: severely corrupted files that LibreOffice cannot parse at all will cause a
    /// <see cref="DocumentConversionException"/> rather than producing partial output.
    /// </para>
    /// </summary>
    /// <param name="inputPath">Absolute path to the source document to repair.</param>
    /// <param name="outputPath">
    /// Absolute path where the repaired document will be written.
    /// The output format is inferred from the file extension.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation and kill the LibreOffice process.</param>
    /// <exception cref="ArgumentException">Thrown when a path argument is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="inputPath"/> does not exist.</exception>
    /// <exception cref="NotSupportedException">Thrown when the input or output extension is not in the supported format list.</exception>
    /// <exception cref="DocumentConversionException">Thrown when LibreOffice exits with a non-zero code or cannot be launched.</exception>
    Task RepairAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Target PDF standard for the output file.
/// </summary>
public enum PdfOutputVariant
{
    /// <summary>Standard PDF 1.4 output. No archival conformance requirements.</summary>
    Standard = 0,

    /// <summary>
    /// PDF/A-1b — ISO 19005-1. Ensures long-term preservation; requires all fonts embedded,
    /// prohibits encryption, transparency, and external content.
    /// </summary>
    PdfA1b = 1,

    /// <summary>
    /// PDF/A-2b — ISO 19005-2. Extends PDF/A-1 with JPEG 2000, optional content, and transparency support.
    /// </summary>
    PdfA2b = 2,

    /// <summary>
    /// PDF/A-3b — ISO 19005-3. Same as PDF/A-2 but permits attaching arbitrary file formats as embedded files.
    /// </summary>
    PdfA3b = 3
}

/// <summary>
/// Controls how raster images inside the document are resampled and compressed in the PDF output.
/// </summary>
public enum PdfImageCompression
{
    /// <summary>LibreOffice default compression — typically lossless for most image types.</summary>
    Default,

    /// <summary>
    /// Lossless compression (DEFLATE/PNG). Larger files, no quality loss.
    /// Not compatible with PDF/A variants that prohibit certain compression types.
    /// </summary>
    Lossless,

    /// <summary>
    /// JPEG compression. Smaller files with configurable quality loss.
    /// Use <see cref="DocumentConversionOptions.JpegQuality"/> to control the trade-off.
    /// </summary>
    Jpeg
}

/// <summary>
/// Options that control the PDF output produced by <see cref="IDocumentService.ConvertToPdfAsync"/>.
/// <para>
/// Advanced options (PDF variant, quality, page range, image compression) are passed to LibreOffice
/// as filter data properties in the <c>--convert-to</c> argument. They require LibreOffice 7.0 or later;
/// on older versions the basic conversion still succeeds but these settings may be silently ignored.
/// </para>
/// </summary>
public sealed class DocumentConversionOptions
{
    /// <summary>
    /// PDF standard to target. Defaults to <see cref="PdfOutputVariant.Standard"/>.
    /// </summary>
    public PdfOutputVariant Variant { get; init; } = PdfOutputVariant.Standard;

    /// <summary>
    /// Image compression mode applied to raster images inside the output PDF.
    /// Defaults to <see cref="PdfImageCompression.Default"/>.
    /// </summary>
    public PdfImageCompression ImageCompression { get; init; } = PdfImageCompression.Default;

    /// <summary>
    /// JPEG quality for raster images when <see cref="ImageCompression"/> is <see cref="PdfImageCompression.Jpeg"/>.
    /// Valid range: 1–100. Null uses LibreOffice's built-in default (90).
    /// Has no effect when <see cref="ImageCompression"/> is not <see cref="PdfImageCompression.Jpeg"/>.
    /// </summary>
    public int? JpegQuality { get; init; }

    /// <summary>
    /// Subset of document pages to include in the PDF output.
    /// Accepts a comma-separated list of individual pages and inclusive ranges, e.g. <c>"1-3,5,8-10"</c>.
    /// Null or empty string exports all pages.
    /// <para>
    /// For presentations and spreadsheets, page numbers correspond to slides and sheets respectively.
    /// </para>
    /// </summary>
    public string? PageRange { get; init; }

    /// <summary>
    /// Returns true when all properties match their defaults, meaning no extra filter data
    /// needs to be appended to the <c>--convert-to</c> argument.
    /// </summary>
    internal bool IsDefault =>
        Variant == PdfOutputVariant.Standard &&
        ImageCompression == PdfImageCompression.Default &&
        JpegQuality is null &&
        string.IsNullOrWhiteSpace(PageRange);
}

/// <summary>
/// Rich LibreOffice failure information used for debugging conversion operations.
/// </summary>
public sealed class DocumentConversionException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception for a failed LibreOffice invocation.
    /// </summary>
    public DocumentConversionException(
        string message,
        string binaryPath,
        string commandLine,
        int? exitCode,
        string standardOutput,
        string standardError,
        Exception? innerException = null)
        : base(message, innerException)
    {
        BinaryPath = binaryPath;
        CommandLine = commandLine;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>Executed binary path.</summary>
    public string BinaryPath { get; }

    /// <summary>Fully formatted command line used for the failed process.</summary>
    public string CommandLine { get; }

    /// <summary>Process exit code when available.</summary>
    public int? ExitCode { get; }

    /// <summary>Captured standard output.</summary>
    public string StandardOutput { get; }

    /// <summary>Captured standard error.</summary>
    public string StandardError { get; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Document service
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// LibreOffice CLI-backed implementation for document-to-PDF conversion, image extraction, and repair.
/// <para>
/// <b>PDF conversion</b> — LibreOffice is invoked in headless mode:
/// <code>soffice --headless --convert-to "pdf:FilterName[:FilterData]" --outdir &lt;tempDir&gt; &lt;inputPath&gt;</code>
/// The output filter is selected automatically from the input extension:
/// <list type="bullet">
///   <item>Text documents (.docx, .doc, .odt) → <c>writer_pdf_Export</c></item>
///   <item>Presentations (.pptx, .ppt, .odp) → <c>impress_pdf_Export</c></item>
///   <item>Spreadsheets (.xlsx, .xls, .ods, .csv) → <c>calc_pdf_Export</c></item>
/// </list>
/// </para>
/// <para>
/// <b>Image extraction</b> — Modern Office and ODF formats are ZIP archives whose embedded images
/// live in predictable subfolders. The service reads those entries directly and streams them into
/// an output ZIP without loading all images into memory at once. Legacy binary formats (.doc, .ppt,
/// .xls) are converted to their modern equivalent first via LibreOffice, then extracted.
/// </para>
/// <para>
/// Because LibreOffice always names its output file after the input basename, converted files are
/// written to a temporary directory and moved to the caller-supplied path when done.
/// </para>
/// <para>
/// The service probes for <c>soffice</c> / <c>soffice.exe</c> at well-known installation paths
/// before falling back to a system PATH lookup. LibreOffice must be installed on the host machine;
/// this service does not bundle the binary.
/// </para>
/// </summary>
public sealed class DocumentService : IDocumentService
{
    private static readonly string[] WriterExtensions   = [".docx", ".doc", ".odt"];
    private static readonly string[] ImpressExtensions  = [".pptx", ".ppt", ".odp"];
    private static readonly string[] CalcExtensions     = [".xlsx", ".xls", ".ods", ".csv"];

    // CSV excluded: it is a plain-text format and cannot contain embedded images.
    private static readonly string[] ImageExtractionExtensions =
        [".docx", ".doc", ".odt", ".pptx", ".ppt", ".odp", ".xlsx", ".xls", ".ods"];

    // ── Public API ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task ConvertToPdfAsync(
        string inputPath,
        string outputPath,
        DocumentConversionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        ValidateOptions(options);

        var ext = Path.GetExtension(inputPath);
        if (!IsSupportedExtension(ext))
        {
            throw new NotSupportedException(
                $"Input format '{ext}' is not supported. " +
                $"Supported formats: {string.Join(", ", AllSupportedExtensions())}.");
        }

        await ConvertToOutputPathAsync(
            inputPath, outputPath, BuildConvertToArgument(ext, options), ".pdf", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ExtractImagesAsync(
        string inputPath,
        string outputZipPath,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputZipPath);

        var ext = Path.GetExtension(inputPath);
        if (!ImageExtractionExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Image extraction is not supported for '{ext}' files. " +
                $"Supported formats: {string.Join(", ", ImageExtractionExtensions)}.");
        }

        // Legacy binary formats (.doc, .ppt, .xls) are not ZIP archives.
        // Convert to the modern equivalent first so we can read the ZIP structure.
        string? tempDir = null;
        var sourceForExtraction = Path.GetFullPath(inputPath);
        var effectiveExt = ext;

        try
        {
            if (IsLegacyBinaryFormat(ext))
            {
                tempDir = CreateTempDirectory();
                var modernFormat = GetModernConversionFormat(ext);

                sourceForExtraction = await RunConversionAsync(
                    inputPath, modernFormat, tempDir, "." + modernFormat, cancellationToken)
                    .ConfigureAwait(false);
                effectiveExt = "." + modernFormat;
            }

            var mediaPrefix = GetMediaFolderPrefix(effectiveExt);
            var imageCount = PackImagesToZip(sourceForExtraction, mediaPrefix, outputZipPath);

            if (imageCount == 0)
            {
                // Remove the empty archive so the caller is not left with a useless file.
                SetupHelpers.TryDeleteFile(outputZipPath);
                throw new InvalidOperationException(
                    "No embedded images were found in the document.");
            }
        }
        finally
        {
            if (tempDir is not null)
                SetupHelpers.TryDeleteDirectory(tempDir);
        }
    }

    /// <inheritdoc />
    public async Task RepairAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);

        var inputExt  = Path.GetExtension(inputPath);
        var outputExt = Path.GetExtension(outputPath);

        if (!IsSupportedExtension(inputExt))
        {
            throw new NotSupportedException(
                $"Input format '{inputExt}' is not supported for repair. " +
                $"Supported formats: {string.Join(", ", AllSupportedExtensions())}.");
        }

        if (!IsSupportedExtension(outputExt))
        {
            throw new NotSupportedException(
                $"Output format '{outputExt}' is not supported. " +
                $"Supported formats: {string.Join(", ", AllSupportedExtensions())}.");
        }

        // The repair is a full load-and-save cycle: the --convert-to format string is simply
        // the lowercased output extension; LibreOffice resolves the save filter from it.
        await ConvertToOutputPathAsync(
            inputPath, outputPath, outputExt.TrimStart('.').ToLowerInvariant(), outputExt, cancellationToken)
            .ConfigureAwait(false);
    }

    // ── Conversion plumbing ──────────────────────────────────────────────────

    /// <summary>
    /// Runs a LibreOffice conversion through a private temp directory and moves the produced
    /// file to the exact <paramref name="outputPath"/>. LibreOffice always names its output
    /// after the input basename inside <c>--outdir</c>, so the temp-dir hop is what lets the
    /// caller pick an arbitrary destination — including in-place repair where input == output.
    /// </summary>
    private static async Task ConvertToOutputPathAsync(
        string inputPath,
        string outputPath,
        string convertToArg,
        string outputExtension,
        CancellationToken cancellationToken)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var producedPath = await RunConversionAsync(
                inputPath, convertToArg, tempDir, outputExtension, cancellationToken)
                .ConfigureAwait(false);

            var fullOutputPath = Path.GetFullPath(outputPath);
            EnsureParentDirectoryExists(fullOutputPath);
            File.Move(producedPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            // Best-effort cleanup; the OS will sweep temp files eventually.
            SetupHelpers.TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Invokes <c>soffice --headless --convert-to &lt;convertToArg&gt; --outdir &lt;outDir&gt;</c>
    /// and returns the path of the produced file (input basename + <paramref name="outputExtension"/>).
    /// Throws <see cref="DocumentConversionException"/> if the expected file is missing afterwards.
    /// </summary>
    private static async Task<string> RunConversionAsync(
        string inputPath,
        string convertToArg,
        string outDir,
        string outputExtension,
        CancellationToken cancellationToken)
    {
        var candidates = ResolveExecutableCandidates();
        var args = new List<string>
        {
            "--headless",
            "--convert-to",
            convertToArg,
            "--outdir",
            outDir,
            Path.GetFullPath(inputPath)
        };

        await RunProcessWithFallbackAsync(candidates, args, cancellationToken).ConfigureAwait(false);

        var expectedName = Path.GetFileNameWithoutExtension(inputPath) + outputExtension;
        var producedPath = Path.Combine(outDir, expectedName);

        if (!File.Exists(producedPath))
        {
            throw new DocumentConversionException(
                $"LibreOffice completed without producing the expected output file '{expectedName}'.",
                candidates.Count > 0 ? candidates[0] : string.Empty,
                string.Empty,
                null,
                string.Empty,
                string.Empty);
        }

        return producedPath;
    }

    // ── Filter argument builder ──────────────────────────────────────────────

    /// <summary>
    /// Builds the value for the <c>--convert-to</c> argument.
    /// <para>
    /// Format: <c>pdf:FilterName</c> for default output, or
    /// <c>pdf:FilterName:key=value key=value …</c> when filter data properties are needed.
    /// </para>
    /// <para>
    /// Filter data properties are passed as space-separated <c>key=value</c> pairs — the format
    /// accepted by LibreOffice 7.0+ when processing the third colon-delimited field of
    /// <c>--convert-to</c> for PDF export filters.
    /// </para>
    /// </summary>
    private static string BuildConvertToArgument(string extension, DocumentConversionOptions? options)
    {
        var filterName = ResolveFilterName(extension);

        if (options is null || options.IsDefault)
        {
            return $"pdf:{filterName}";
        }

        var filterData = new StringBuilder();

        // PDF/A variant — SelectPdfVersion values match LibreOffice's XPDFFilterData enum:
        // 0 = standard PDF 1.4, 1 = PDF/A-1b, 2 = PDF/A-2b, 3 = PDF/A-3b.
        if (options.Variant != PdfOutputVariant.Standard)
        {
            filterData.Append($"SelectPdfVersion={(int)options.Variant} ");

            // PDF/A requires all fonts to be embedded.
            filterData.Append("EmbedStandardFonts=true ");
        }

        // Image compression and JPEG quality.
        switch (options.ImageCompression)
        {
            case PdfImageCompression.Jpeg:
                filterData.Append("UseTaggedPDF=true ");
                var quality = options.JpegQuality ?? 90;
                filterData.Append($"Quality={quality} ");
                break;

            case PdfImageCompression.Lossless:
                // Force lossless by setting quality to 100 and disabling JPEG.
                filterData.Append("Quality=100 ");
                break;
        }

        // Page range — passed as a string like "1-3,5,8-10".
        // LibreOffice interprets this per document type: pages for Writer,
        // slides for Impress, sheets (by index) for Calc.
        if (!string.IsNullOrWhiteSpace(options.PageRange))
        {
            filterData.Append($"PageRange={options.PageRange.Trim()} ");
        }

        var filterDataStr = filterData.ToString().TrimEnd();
        return string.IsNullOrEmpty(filterDataStr)
            ? $"pdf:{filterName}"
            : $"pdf:{filterName}:{filterDataStr}";
    }

    /// <summary>
    /// Maps the input file extension to the correct LibreOffice PDF export filter name.
    /// </summary>
    private static string ResolveFilterName(string extension)
    {
        if (MatchesAny(extension, WriterExtensions))  return "writer_pdf_Export";
        if (MatchesAny(extension, ImpressExtensions)) return "impress_pdf_Export";
        if (MatchesAny(extension, CalcExtensions))    return "calc_pdf_Export";

        // Should not happen after the extension check, but safer than throwing here.
        return "writer_pdf_Export";
    }

    // ── Executable resolution ────────────────────────────────────────────────

    /// <summary>
    /// Returns the ordered list of soffice executable paths to try, in priority order.
    /// <list type="number">
    ///   <item>
    ///     <b>Downloaded copy</b> — installed on-demand by <see cref="LibreOfficeSetupService"/>
    ///     at <c>%LOCALAPPDATA%\FilesTools\libreoffice\&lt;rid&gt;\program\soffice[.exe]</c>.
    ///     This is the primary path in a normal packaged release.
    ///   </item>
    ///   <item>
    ///     <b>Bundled copy</b> — <c>&lt;AppDir&gt;/libreoffice/&lt;rid&gt;/program/soffice[.exe]</c>.
    ///     Used when a developer manually places a LibreOffice tree next to the executable.
    ///   </item>
    ///   <item>
    ///     <b>System install</b> (Windows only, dev-time) — probes
    ///     <c>%ProgramFiles%\LibreOffice*\program\soffice.exe</c> so that developers can
    ///     run the service against a normally installed LibreOffice without needing a
    ///     downloaded or bundled copy.
    ///   </item>
    ///   <item>
    ///     <b>PATH lookup</b> — bare <c>soffice[.exe]</c> as a last resort.
    ///   </item>
    /// </list>
    /// </summary>
    private static List<string> ResolveExecutableCandidates()
    {
        var executableName = SetupHelpers.SofficeExecutableName;
        var candidates     = new List<string>();

        // 1. On-demand downloaded copy — installed by LibreOfficeSetupService.
        if (LibreOfficeSetupService.IsAvailable)
            candidates.Add(LibreOfficeSetupService.ExecutablePath);

        // 2. Manually bundled copy in the app directory (developer convenience).
        var rid         = SetupHelpers.GetCurrentRid();
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "libreoffice", rid, "program", executableName);
        if (File.Exists(bundledPath))
            candidates.Add(bundledPath);

        // 3. System install — dev-time convenience on Windows.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root, "LibreOffice*", SearchOption.TopDirectoryOnly)
                                                 .OrderByDescending(static d => d, StringComparer.OrdinalIgnoreCase))
                    {
                        var candidate = Path.Combine(dir, "program", executableName);
                        if (File.Exists(candidate))
                            candidates.Add(candidate);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Directory inaccessible — skip.
                }
            }
        }

        // 4. PATH lookup — last resort.
        candidates.Add(executableName);

        return candidates;
    }

    // ── Process execution ────────────────────────────────────────────────────

    private static async Task RunProcessWithFallbackAsync(
        List<string> candidates,
        List<string> arguments,
        CancellationToken cancellationToken)
    {
        DocumentConversionException? lastException = null;

        foreach (var candidate in candidates)
        {
            // Skip absolute paths that are known not to exist; always try bare names (PATH lookup).
            if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
            {
                continue;
            }

            try
            {
                await RunProcessAsync(candidate, arguments, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DocumentConversionException ex)
            {
                lastException = ex;
            }
        }

        var expectedPath = Path.Combine(
            AppContext.BaseDirectory, "libreoffice", SetupHelpers.GetCurrentRid(), "program",
            SetupHelpers.SofficeExecutableName);

        throw lastException ?? new DocumentConversionException(
            $"The bundled LibreOffice executable was not found. " +
            $"Expected at: {expectedPath}",
            expectedPath,
            string.Empty,
            null,
            string.Empty,
            string.Empty);
    }

    private static async Task RunProcessAsync(
        string binaryPath,
        List<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // LibreOffice resolves its own DLLs and resource files relative to the program\
            // directory at startup. Setting WorkingDirectory to that directory is required
            // for the bundled copy to initialise correctly.
            WorkingDirectory = Path.IsPathRooted(binaryPath)
                ? (Path.GetDirectoryName(binaryPath) ?? AppContext.BaseDirectory)
                : AppContext.BaseDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new DocumentConversionException(
                    "Unable to start LibreOffice process.",
                    binaryPath,
                    FormatCommandLine(binaryPath, arguments),
                    null,
                    string.Empty,
                    string.Empty);
            }
        }
        catch (DocumentConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DocumentConversionException(
                "Failed to start LibreOffice process.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                null,
                string.Empty,
                ex.Message,
                ex);
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort kill on cancellation.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new DocumentConversionException(
                "LibreOffice exited with a non-zero exit code.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                process.ExitCode,
                stdout,
                stderr);
        }
    }

    // ── Validation ───────────────────────────────────────────────────────────

    private static void ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Input path cannot be null or whitespace.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException("Input file was not found.", path);
    }

    private static void ValidateOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(path));
    }

    private static void ValidateOptions(DocumentConversionOptions? options)
    {
        if (options is null)
        {
            return;
        }

        if (options.JpegQuality.HasValue && (options.JpegQuality.Value < 1 || options.JpegQuality.Value > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.JpegQuality),
                "JPEG quality must be between 1 and 100.");
        }

        if (options.JpegQuality.HasValue && options.ImageCompression != PdfImageCompression.Jpeg)
        {
            throw new ArgumentException(
                $"{nameof(options.JpegQuality)} can only be set when {nameof(options.ImageCompression)} is {nameof(PdfImageCompression.Jpeg)}.",
                nameof(options.JpegQuality));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool MatchesAny(string extension, string[] extensions) =>
        extensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));

    private static bool IsSupportedExtension(string ext) =>
        MatchesAny(ext, WriterExtensions) ||
        MatchesAny(ext, ImpressExtensions) ||
        MatchesAny(ext, CalcExtensions);

    private static IEnumerable<string> AllSupportedExtensions() =>
        WriterExtensions.Concat(ImpressExtensions).Concat(CalcExtensions);

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void EnsureParentDirectoryExists(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    // ── Image extraction helpers ─────────────────────────────────────────────

    /// <summary>
    /// Streams all image entries from <paramref name="sourceZipPath"/> that live under
    /// <paramref name="mediaPrefix"/> directly into a new ZIP at <paramref name="outputZipPath"/>.
    /// Returns the number of images written.
    /// <para>
    /// Entries are piped one at a time — no image is fully loaded into memory — so the method
    /// stays efficient even for documents with large embedded images.
    /// </para>
    /// </summary>
    private static int PackImagesToZip(string sourceZipPath, string mediaPrefix, string outputZipPath)
    {
        EnsureParentDirectoryExists(Path.GetFullPath(outputZipPath));

        // Open both archives simultaneously to stream entries directly.
        using var sourceArchive = ZipFile.OpenRead(sourceZipPath);
        using var outputArchive = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (var sourceEntry in sourceArchive.Entries)
        {
            // Ignore directory markers and entries outside the media folder.
            if (string.IsNullOrEmpty(sourceEntry.Name))
            {
                continue;
            }

            if (!sourceEntry.FullName.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryName = DeduplicateEntryName(sourceEntry.Name, usedNames);
            usedNames.Add(entryName);

            // CompressionLevel.NoCompression preserves the original compression from the source
            // archive; images are already compressed and re-compressing wastes CPU for no gain.
            var destEntry = outputArchive.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var sourceStream = sourceEntry.Open();
            using var destStream = destEntry.Open();
            sourceStream.CopyTo(destStream);

            count++;
        }

        return count;
    }

    /// <summary>
    /// Returns the ZIP subfolder prefix where embedded images are stored for the given file extension.
    /// <list type="table">
    ///   <item><term>.docx</term><description><c>word/media/</c></description></item>
    ///   <item><term>.pptx</term><description><c>ppt/media/</c></description></item>
    ///   <item><term>.xlsx</term><description><c>xl/media/</c></description></item>
    ///   <item><term>.odt / .odp / .ods</term><description><c>Pictures/</c></description></item>
    /// </list>
    /// </summary>
    private static string GetMediaFolderPrefix(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".docx" => "word/media/",
            ".pptx" => "ppt/media/",
            ".xlsx" => "xl/media/",
            ".odt" or ".odp" or ".ods" => "Pictures/",
            _ => throw new NotSupportedException($"Cannot resolve media folder for extension '{extension}'.")
        };

    /// <summary>
    /// Returns true for legacy binary formats that must be converted to a modern ZIP-based
    /// equivalent before their embedded images can be extracted.
    /// </summary>
    private static bool IsLegacyBinaryFormat(string extension) =>
        extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".ppt", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the <c>--convert-to</c> format string used to upgrade a legacy binary format
    /// to its modern Open XML equivalent so the ZIP structure can be read.
    /// </summary>
    private static string GetModernConversionFormat(string legacyExtension) =>
        legacyExtension.ToLowerInvariant() switch
        {
            ".doc" => "docx",
            ".ppt" => "pptx",
            ".xls" => "xlsx",
            _ => throw new ArgumentException($"'{legacyExtension}' is not a known legacy binary format.", nameof(legacyExtension))
        };

    /// <summary>
    /// Returns <paramref name="name"/> unchanged if it is not already in <paramref name="usedNames"/>,
    /// otherwise appends an incrementing numeric suffix before the extension until a unique name is found.
    /// </summary>
    private static string DeduplicateEntryName(string name, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(name))
        {
            return name;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var counter = 2;

        string candidate;
        do
        {
            candidate = $"{baseName}_{counter++}{ext}";
        }
        while (usedNames.Contains(candidate));

        return candidate;
    }

    private static string FormatCommandLine(string binaryPath, List<string> arguments)
    {
        var sb = new StringBuilder();
        sb.Append('"').Append(binaryPath).Append('"');
        foreach (var arg in arguments)
        {
            sb.Append(" \"").Append(arg.Replace("\"", "\\\"")).Append('"');
        }
        return sb.ToString();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// LibreOffice setup — progress types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stage reported while LibreOffice is being downloaded and installed.
/// </summary>
public enum LibreOfficeSetupStage
{
    /// <summary>Downloading the installer from the LibreOffice foundation server.</summary>
    Downloading,

    /// <summary>
    /// Unpacking the MSI payload via <c>msiexec /a</c> (administrative install).
    /// Progress percentage is -1 (indeterminate) during this stage.
    /// </summary>
    Extracting,

    /// <summary>Copying the extracted files to the local install directory.</summary>
    Copying,

    /// <summary>Installation finished successfully. <see cref="LibreOfficeSetupService.IsAvailable"/> is now <c>true</c>.</summary>
    Complete
}

/// <summary>
/// Progress snapshot reported through <see cref="IProgress{T}"/> during
/// <see cref="LibreOfficeSetupService.DownloadAndInstallAsync"/>.
/// </summary>
public sealed class LibreOfficeSetupProgress
{
    /// <summary>Current installation stage.</summary>
    public LibreOfficeSetupStage Stage { get; init; }

    /// <summary>
    /// Completion percentage 0–100.
    /// <c>-1</c> indicates that the current stage is indeterminate (no progress can be calculated).
    /// </summary>
    public double Percentage { get; init; }

    /// <summary>Human-readable status line suitable for display in the UI.</summary>
    public string StatusText { get; init; } = "";

    /// <summary>Bytes downloaded so far (only meaningful during <see cref="LibreOfficeSetupStage.Downloading"/>).</summary>
    public long BytesDownloaded { get; init; }

    /// <summary>Total bytes to download (only meaningful during <see cref="LibreOfficeSetupStage.Downloading"/>).</summary>
    public long TotalBytes { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// LibreOffice setup — service
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages the on-demand download and local installation of the LibreOffice CLI.
/// <para>
/// LibreOffice is downloaded once from the official LibreOffice Foundation distribution
/// servers and stored in the user's local application data folder. It persists across
/// app updates and does not need to be re-downloaded unless the user removes it.
/// </para>
/// <para>
/// Install location: <c>%LOCALAPPDATA%\FilesTools\libreoffice\&lt;rid&gt;\</c>
/// where <c>&lt;rid&gt;</c> is the current runtime identifier (e.g. <c>win-x64</c>).
/// </para>
/// <para>
/// The download is approximately 350–450 MB depending on platform. Extraction uses
/// <c>msiexec /a</c> (administrative install), which unpacks the MSI payload to a
/// temporary directory without writing to the system registry or requiring elevation.
/// </para>
/// </summary>
public static class LibreOfficeSetupService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string AppDataFolderName = "FilesTools";

    /// <summary>
    /// Fallback version used when the stable-directory lookup fails.
    /// Keep this in sync with the latest confirmed stable LibreOffice release.
    /// </summary>
    private const string FallbackVersion = "26.2.3";

    /// <summary>
    /// LibreOffice stable-releases directory index.
    /// Returns an HTML page whose links include every currently hosted version folder,
    /// e.g. <c>26.2.3/</c>. We parse these to discover the highest available version.
    /// </summary>
    private const string StableDirectoryUrl =
        "https://download.documentfoundation.org/libreoffice/stable/";

    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout           = TimeSpan.FromSeconds(30)
    });

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// Root directory containing all per-RID LibreOffice installations.
    /// Resolves to <c>%LOCALAPPDATA%\FilesTools\libreoffice\</c>.
    /// </summary>
    public static string InstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName, "libreoffice");

    /// <summary>
    /// Directory where the LibreOffice build matching the current architecture is installed.
    /// Resolves to <c>%LOCALAPPDATA%\FilesTools\libreoffice\win-x64\</c> on a 64-bit Windows machine.
    /// </summary>
    public static string InstallDirectory =>
        Path.Combine(InstallRoot, SetupHelpers.GetCurrentRid());

    /// <summary>
    /// Full path to the LibreOffice entry-point binary.
    /// <c>soffice.exe</c> on Windows, <c>soffice</c> on Unix.
    /// The file exists when <see cref="IsAvailable"/> returns <c>true</c>.
    /// </summary>
    public static string ExecutablePath =>
        Path.Combine(InstallDirectory, "program", SetupHelpers.SofficeExecutableName);

    /// <summary>
    /// Returns <c>true</c> when the LibreOffice executable has been downloaded and is ready.
    /// </summary>
    public static bool IsAvailable => File.Exists(ExecutablePath);

    /// <summary>
    /// Approximate installer size in bytes for the current platform.
    /// Used as a progress denominator before the HTTP Content-Length header is received.
    /// </summary>
    public static long EstimatedDownloadBytes => GetEstimatedBytes(SetupHelpers.GetCurrentRid());

    /// <summary>
    /// Fallback LibreOffice version string shown in the UI before the live version is resolved.
    /// The actual download always uses the version returned by the update-check endpoint.
    /// </summary>
    public static string DisplayVersion => FallbackVersion;

    // ── Download & install ────────────────────────────────────────────────────

    /// <summary>
    /// Downloads LibreOffice from the official LibreOffice Foundation server and
    /// installs it to <see cref="InstallDirectory"/>.
    /// <para>
    /// Progress is reported through four stages in order:
    /// <list type="number">
    ///   <item><see cref="LibreOfficeSetupStage.Downloading"/> — live byte-count progress.</item>
    ///   <item><see cref="LibreOfficeSetupStage.Extracting"/>  — MSI unpack (indeterminate).</item>
    ///   <item><see cref="LibreOfficeSetupStage.Copying"/>     — file copy with count progress.</item>
    ///   <item><see cref="LibreOfficeSetupStage.Complete"/>    — <see cref="IsAvailable"/> becomes <c>true</c>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// If the operation is cancelled or fails, any partial installation is removed
    /// so that <see cref="IsAvailable"/> stays <c>false</c>.
    /// </para>
    /// </summary>
    /// <param name="progress">Optional progress receiver; called from a thread-pool thread.</param>
    /// <param name="cancellationToken">Token that cancels the download and cleans up partial files.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="HttpRequestException">Thrown when the download request fails.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when extraction fails or the expected binary is absent after installation.
    /// </exception>
    public static async Task DownloadAndInstallAsync(
        IProgress<LibreOfficeSetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var rid = SetupHelpers.GetCurrentRid();

        // ── Resolve the current stable version from the update-check endpoint ──
        // download.documentfoundation.org/stable/ only serves the live release;
        // any hardcoded version older than today's stable returns 404.
        Report(progress, LibreOfficeSetupStage.Downloading, -1, "Resolving current LibreOffice version…");
        var version    = await ResolveCurrentVersionAsync(cancellationToken).ConfigureAwait(false);
        var msiUrl     = GetDownloadUrl(rid, version);
        var msiPath    = Path.Combine(Path.GetTempPath(), $"LibreOffice_{version}_{rid}.msi");
        var extractDir = Path.Combine(Path.GetTempPath(), $"lo-extract-{Guid.NewGuid():N}");

        try
        {
            // ── Stage 1: Download ─────────────────────────────────────────────
            await DownloadFileAsync(msiUrl, msiPath, progress, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            // ── Stage 2: Extract via msiexec administrative install ───────────
            Report(progress, LibreOfficeSetupStage.Extracting, -1,
                "Extracting LibreOffice — this may take a minute…");

            Directory.CreateDirectory(extractDir);
            await ExtractMsiAsync(msiPath, extractDir, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            // ── Stage 3: Locate installation root and copy ───────────────────
            var loRoot = FindLibreOfficeRoot(extractDir)
                ?? throw new InvalidOperationException(
                    "Extraction completed but soffice.exe was not found inside the extracted payload. " +
                    $"Extract directory: {extractDir}");

            await CopyDirectoryAsync(loRoot, InstallDirectory, progress, cancellationToken)
                .ConfigureAwait(false);

            // ── Stage 4: Verify ───────────────────────────────────────────────
            if (!File.Exists(ExecutablePath))
                throw new InvalidOperationException(
                    $"Installation completed but soffice.exe is missing at the expected path: {ExecutablePath}");

            Report(progress, LibreOfficeSetupStage.Complete, 100, "LibreOffice is ready.");
        }
        catch
        {
            // Roll back any partial installation so IsAvailable stays false.
            SetupHelpers.TryDeleteDirectory(InstallDirectory);
            throw;
        }
        finally
        {
            SetupHelpers.TryDeleteFile(msiPath);
            SetupHelpers.TryDeleteDirectory(extractDir);
        }
    }

    /// <summary>
    /// Removes the downloaded LibreOffice installation from <see cref="InstallDirectory"/>.
    /// Safe to call when LibreOffice has not been downloaded yet.
    /// </summary>
    public static void Remove() => SetupHelpers.TryDeleteDirectory(InstallDirectory);

    // ── Private: download ─────────────────────────────────────────────────────

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<LibreOfficeSetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SharedHttpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? EstimatedDownloadBytes;
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var dest   = File.Create(destination);

        var buffer    = new byte[81_920];
        long received = 0;
        int  read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            var pct = totalBytes > 0 ? received * 100.0 / totalBytes : -1;
            progress?.Report(new LibreOfficeSetupProgress
            {
                Stage           = LibreOfficeSetupStage.Downloading,
                Percentage      = pct,
                StatusText      = $"Downloading — {FormatBytes(received)} / {FormatBytes(totalBytes)}",
                BytesDownloaded = received,
                TotalBytes      = totalBytes
            });
        }
    }

    // ── Private: extraction ───────────────────────────────────────────────────

    private static async Task ExtractMsiAsync(
        string msiPath,
        string targetDir,
        CancellationToken cancellationToken)
    {
        // msiexec /a (administrative install) unpacks the MSI file tree to targetDir
        // without writing registry entries or requiring elevation.
        var psi = new ProcessStartInfo
        {
            FileName               = "msiexec.exe",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };
        psi.ArgumentList.Add("/a");
        psi.ArgumentList.Add(msiPath);
        psi.ArgumentList.Add("/qn");
        psi.ArgumentList.Add($"TARGETDIR={targetDir}");

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("msiexec.exe failed to start.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not launch msiexec.exe: {ex.Message}", ex);
        }

        using var _ = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"msiexec /a exited with code {process.ExitCode}." +
                (string.IsNullOrWhiteSpace(stderr) ? "" : $" Error: {stderr.Trim()}"));
        }
    }

    /// <summary>
    /// Recursively searches <paramref name="extractDir"/> for <c>program\soffice.exe</c>
    /// and returns the parent of the <c>program\</c> directory (the LibreOffice root).
    /// Returns <c>null</c> if not found.
    /// </summary>
    private static string? FindLibreOfficeRoot(string extractDir)
    {
        foreach (var soffice in Directory.EnumerateFiles(
            extractDir, SetupHelpers.SofficeExecutableName, SearchOption.AllDirectories))
        {
            var programDir = Path.GetDirectoryName(soffice);
            if (programDir is null) continue;
            var loRoot = Path.GetDirectoryName(programDir);
            if (loRoot is not null) return loRoot;
        }
        return null;
    }

    // ── Private: copy ─────────────────────────────────────────────────────────

    private static async Task CopyDirectoryAsync(
        string sourceDir,
        string destinationDir,
        IProgress<LibreOfficeSetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        var total    = allFiles.Length;
        var copied   = 0;

        Directory.CreateDirectory(destinationDir);

        foreach (var sourceFile in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
            var destFile     = Path.Combine(destinationDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(sourceFile, destFile, overwrite: true);
            copied++;

            if (copied % 100 == 0 || copied == total)
            {
                progress?.Report(new LibreOfficeSetupProgress
                {
                    Stage      = LibreOfficeSetupStage.Copying,
                    Percentage = total > 0 ? copied * 100.0 / total : -1,
                    StatusText = $"Installing — {copied:N0} / {total:N0} files"
                });
            }
        }
    }

    // ── Private: version resolution & URL ────────────────────────────────────

    /// <summary>
    /// Discovers the highest available LibreOffice version by parsing the stable-releases
    /// directory index at <see cref="StableDirectoryUrl"/>.
    /// <para>
    /// The directory lists every currently hosted version folder (e.g. <c>26.2.3/</c>).
    /// We collect all <c>MAJOR.MINOR.PATCH</c> matches, pick the numerically highest one,
    /// and use that as the download version. This avoids hardcoding a version that will
    /// go stale as new LibreOffice releases are published.
    /// </para>
    /// Falls back to <see cref="FallbackVersion"/> when the directory is unreachable or
    /// contains no parseable version strings.
    /// </summary>
    private static async Task<string> ResolveCurrentVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Short timeout — the directory index is a small HTML document.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var html    = await SharedHttpClient.GetStringAsync(StableDirectoryUrl, cts.Token).ConfigureAwait(false);
            var matches = Regex.Matches(html, @"(\d+\.\d+\.\d+)/");

            Version? best = null;
            foreach (Match m in matches)
            {
                if (Version.TryParse(m.Groups[1].Value, out var v) && (best is null || v > best))
                    best = v;
            }

            if (best is not null)
                return best.ToString();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout from the inner CTS — fall through to fallback.
        }
        catch
        {
            // Network error, parse failure — fall through to fallback.
        }

        return FallbackVersion;
    }

    private static string GetDownloadUrl(string rid, string version) => rid switch
    {
        "win-x64"   => $"https://download.documentfoundation.org/libreoffice/stable/{version}/win/x86_64/LibreOffice_{version}_Win_x86-64.msi",
        "win-arm64" => $"https://download.documentfoundation.org/libreoffice/stable/{version}/win/aarch64/LibreOffice_{version}_Win_aarch64.msi",
        "win-x86"   => $"https://download.documentfoundation.org/libreoffice/stable/{version}/win/x86/LibreOffice_{version}_Win_x86.msi",
        _ => throw new PlatformNotSupportedException($"No LibreOffice download URL available for RID '{rid}'.")
    };

    private static long GetEstimatedBytes(string rid) => rid switch
    {
        "win-x64"   => 420_000_000L,
        "win-arm64" => 350_000_000L,
        "win-x86"   => 390_000_000L,
        _           => 400_000_000L
    };

    // ── Private: utilities ────────────────────────────────────────────────────

    private static void Report(
        IProgress<LibreOfficeSetupProgress>? progress,
        LibreOfficeSetupStage stage,
        double percentage,
        string text)
    {
        progress?.Report(new LibreOfficeSetupProgress
        {
            Stage      = stage,
            Percentage = percentage,
            StatusText = text
        });
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_000_000_000L => $"{bytes / 1_000_000_000.0:F1} GB",
        >= 1_000_000L     => $"{bytes / 1_000_000.0:F0} MB",
        >= 1_000L         => $"{bytes / 1_000.0:F0} KB",
        _                 => $"{bytes} B"
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Shared helpers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Small platform/file utilities shared by <see cref="DocumentService"/> and
/// <see cref="LibreOfficeSetupService"/>.
/// </summary>
internal static class SetupHelpers
{
    /// <summary>Platform-specific LibreOffice entry-point binary name.</summary>
    internal static string SofficeExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "soffice.exe" : "soffice";

    /// <summary>
    /// Returns the runtime identifier matching the current OS and process architecture
    /// (e.g. <c>win-x64</c>), used to locate the correct LibreOffice binary subfolder.
    /// </summary>
    internal static string GetCurrentRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "win-x64",
                Architecture.X86   => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported Windows architecture: {RuntimeInformation.ProcessArchitecture}")
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported macOS architecture: {RuntimeInformation.ProcessArchitecture}")
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}")
            };

        throw new PlatformNotSupportedException("Current operating system is not supported.");
    }

    /// <summary>Deletes a file, swallowing any error (best-effort cleanup).</summary>
    internal static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    /// <summary>Deletes a directory tree, swallowing any error (best-effort cleanup).</summary>
    internal static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
