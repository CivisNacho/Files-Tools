using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetVips;
using QPdfNet;
using QPdfNet.Enums;
using TesseractOCR;
using TesseractOCR.Enums;

namespace Files_Tools.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Public types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Defines PDF manipulation operations backed by qpdf (via QPdfNet) and Tesseract OCR.
/// </summary>
public interface IPdfService
{
    /// <summary>
    /// Concatenates the input PDFs, in the supplied order, into a single output file.
    /// </summary>
    /// <param name="inputPaths">Absolute paths of the source PDFs. Must contain at least one entry.</param>
    /// <param name="outputPath">Absolute path where the merged PDF will be written.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task MergeAsync(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Splits a PDF either into one file per page or by an explicit set of page ranges.
    /// </summary>
    /// <param name="inputPath">Absolute path to the source PDF.</param>
    /// <param name="outputDirectory">Absolute directory where the resulting parts will be written.</param>
    /// <param name="options">Split mode — either one file per page or a set of page-range groups.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The absolute paths of the produced part files in deterministic order.</returns>
    Task<IReadOnlyList<string>> SplitAsync(
        string inputPath,
        string outputDirectory,
        PdfSplitOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts all raster image XObjects and all embedded file attachments from a PDF
    /// into <paramref name="outputDirectory"/>.
    /// <para>
    /// Images are written under <c>images/</c> using the original PDF object number as filename and
    /// a best-effort extension inferred from the stream filter (<c>.jpg</c> for DCTDecode,
    /// <c>.png</c> for FlateDecode, <c>.tif</c> for CCITTFaxDecode, otherwise <c>.bin</c>).
    /// </para>
    /// <para>
    /// Embedded attachments are written under <c>attachments/</c> using their original names.
    /// </para>
    /// <para>
    /// The image and attachment sub-folder names can be overridden (e.g. for localization) via
    /// <paramref name="imagesFolderName"/> and <paramref name="attachmentsFolderName"/>.
    /// </para>
    /// </summary>
    /// <returns>The number of images and attachments extracted, in that order.</returns>
    Task<(int Images, int Attachments)> ExtractAsync(
        string inputPath,
        string outputDirectory,
        string imagesFolderName = "images",
        string attachmentsFolderName = "attachments",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces a new PDF whose pages appear in the order given by <paramref name="pageOrder"/>.
    /// Page numbers are 1-based and may include repetitions to duplicate pages.
    /// </summary>
    Task ReorderAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<int> pageOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the selected pages by the given angle (multiple of 90).
    /// <paramref name="pageRange"/> uses qpdf range syntax (e.g. <c>"1-3,5,8-z"</c>).
    /// Pass <c>null</c> or empty to rotate every page.
    /// </summary>
    Task RotateAsync(
        string inputPath,
        string outputPath,
        int angle,
        string? pageRange,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the PDF Info dictionary metadata. Properties set to <c>null</c> are left unchanged;
    /// properties set to an empty string clear the corresponding key.
    /// </summary>
    Task UpdateMetadataAsync(
        string inputPath,
        string outputPath,
        PdfMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current PDF Info dictionary metadata. Missing fields are returned as <c>null</c>.
    /// </summary>
    Task<PdfMetadata> ReadMetadataAsync(
        string inputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts a PDF with the given passwords and permission set.
    /// If <paramref name="ownerPassword"/> is null or empty, <paramref name="userPassword"/> is reused.
    /// </summary>
    Task EncryptAsync(
        string inputPath,
        string outputPath,
        string userPassword,
        string? ownerPassword,
        PdfPermissions? permissions,
        PdfEncryptionStrength strength,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes encryption from a PDF. The current owner password (or user password when the file
    /// has no separate owner password) must be supplied.
    /// </summary>
    Task RemovePasswordAsync(
        string inputPath,
        string outputPath,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the user/owner passwords of an already-encrypted PDF. Convenience helper that
    /// decrypts in memory and re-encrypts with the new credentials, preserving the previous
    /// permission set and encryption strength.
    /// </summary>
    Task ChangePasswordAsync(
        string inputPath,
        string outputPath,
        string currentPassword,
        string newUserPassword,
        string? newOwnerPassword,
        PdfEncryptionStrength strength,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a permission set to the PDF by (re-)encrypting it. The user password is left empty
    /// so the document still opens without a prompt; permission flags are enforced via encryption.
    /// <para>
    /// <paramref name="ownerPassword"/> is optional. When null or empty, an empty owner password is
    /// used — the restrictions are written but can be lifted by any tool. Supply an owner password to
    /// make the restrictions removable only with that password (and to open an already-encrypted input).
    /// </para>
    /// </summary>
    Task UpdatePermissionsAsync(
        string inputPath,
        string outputPath,
        string? ownerPassword,
        PdfPermissions permissions,
        PdfEncryptionStrength strength,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Repairs a PDF by performing a full qpdf load-and-rewrite cycle. Object streams are
    /// rebuilt, the cross-reference table is regenerated, and trailing junk is discarded.
    /// </summary>
    Task RepairAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs OCR on every page of the PDF using Tesseract and produces a searchable PDF
    /// at <paramref name="outputPath"/>. Each page is rasterized via libvips at the requested
    /// DPI, OCRed, and rebuilt into a single output PDF.
    /// </summary>
    /// <param name="options">OCR options including languages, tessdata path, and rasterization DPI.</param>
    Task OcrAsync(
        string inputPath,
        string outputPath,
        PdfOcrOptions options,
        CancellationToken cancellationToken = default);
}

// ── Options & enums ─────────────────────────────────────────────────────────

/// <summary>
/// Controls how <see cref="IPdfService.SplitAsync"/> chunks the input PDF.
/// </summary>
public sealed class PdfSplitOptions
{
    /// <summary>
    /// When set, each page becomes its own output file. Mutually exclusive with <see cref="Ranges"/>.
    /// </summary>
    public bool OnePagePerFile { get; init; }

    /// <summary>
    /// Explicit page-range groups in qpdf syntax (e.g. <c>["1-3", "4,6", "7-z"]</c>).
    /// Each entry becomes one output file. Ignored when <see cref="OnePagePerFile"/> is true.
    /// </summary>
    public IReadOnlyList<string>? Ranges { get; init; }

    /// <summary>Optional filename prefix for the produced parts. Defaults to the input basename.</summary>
    public string? OutputPrefix { get; init; }
}

/// <summary>Subset of Info dictionary fields updatable via <see cref="IPdfService.UpdateMetadataAsync"/>.</summary>
public sealed class PdfMetadata
{
    public string? Title    { get; init; }
    public string? Author   { get; init; }
    public string? Subject  { get; init; }
    public string? Keywords { get; init; }
    public string? Creator  { get; init; }
    public string? Producer { get; init; }
}

/// <summary>Encryption key length used by <see cref="IPdfService.EncryptAsync"/>.</summary>
public enum PdfEncryptionStrength
{
    /// <summary>RC4 40-bit. Legacy; only for compatibility with very old readers.</summary>
    Rc4_40 = 40,

    /// <summary>RC4 128-bit. Acrobat 5 era.</summary>
    Rc4_128 = 128,

    /// <summary>AES 256-bit. Recommended default.</summary>
    Aes_256 = 256
}

/// <summary>Permission bits applied alongside encryption.</summary>
public sealed class PdfPermissions
{
    public bool AllowPrint        { get; init; } = true;
    public bool AllowHighResPrint { get; init; } = true;
    public bool AllowModify       { get; init; } = true;
    public bool AllowExtract      { get; init; } = true;
    public bool AllowAnnotate     { get; init; } = true;
    public bool AllowAssemble     { get; init; } = true;
    public bool AllowFormFill     { get; init; } = true;
}

/// <summary>Options controlling <see cref="IPdfService.OcrAsync"/>.</summary>
public sealed class PdfOcrOptions
{
    /// <summary>
    /// Absolute path of the Tesseract tessdata directory containing the requested language packs.
    /// Required: Tesseract cannot run without it.
    /// </summary>
    public required string TessDataPath { get; init; }

    /// <summary>
    /// Languages to load, joined with '+' (e.g. <c>"eng"</c>, <c>"eng+spa"</c>).
    /// Defaults to English. Each language must have a <c>&lt;lang&gt;.traineddata</c>
    /// file under <see cref="TessDataPath"/>.
    /// </summary>
    public string Languages { get; init; } = "eng";

    /// <summary>Rasterization DPI used when rendering PDF pages for OCR. 300 is the usual sweet spot.</summary>
    public int Dpi { get; init; } = 300;

    /// <summary>Engine mode passed to Tesseract. Default is the LSTM neural engine.</summary>
    public EngineMode EngineMode { get; init; } = EngineMode.Default;
}

/// <summary>Rich qpdf failure information used for debugging PDF operations.</summary>
public sealed class PdfOperationException : InvalidOperationException
{
    public PdfOperationException(string message, ExitCode? exitCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }

    /// <summary>qpdf exit code when the operation reached qpdf and returned a non-success status.</summary>
    public ExitCode? ExitCode { get; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// qpdf + Tesseract-backed implementation of <see cref="IPdfService"/>.
/// <para>
/// All qpdf operations use the QPdfNet <see cref="Job"/> builder, which invokes the qpdf native
/// library in-process. No external process is spawned. Job execution is synchronous; the wrappers
/// here marshal work onto the thread pool so the public surface stays async-friendly.
/// </para>
/// <para>
/// OCR rasterizes each page via libvips (NetVips) at the requested DPI, runs Tesseract over the
/// bitmap with its built-in PDF renderer, then re-merges the per-page searchable PDFs back into
/// the final output via qpdf.
/// </para>
/// </summary>
public sealed class PdfService : IPdfService
{
    // ── Merge ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task MergeAsync(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (inputPaths is null || inputPaths.Count == 0)
            throw new ArgumentException("At least one input PDF is required.", nameof(inputPaths));

        foreach (var p in inputPaths) ValidateInputPath(p);
        ValidateOutputPath(outputPath);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // qpdf --empty --pages file1 file2 ... -- output.pdf
            var job = new Job().Empty();

            foreach (var input in inputPaths)
            {
                job.Pages(Path.GetFullPath(input), "1-z", null!);
            }

            job.OutputFile(Path.GetFullPath(outputPath));
            Execute(job);
        }, cancellationToken);
    }

    // ── Split ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> SplitAsync(
        string inputPath,
        string outputDirectory,
        PdfSplitOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory cannot be null or whitespace.", nameof(outputDirectory));
        if (options is null) throw new ArgumentNullException(nameof(options));

        if (options.OnePagePerFile && options.Ranges is { Count: > 0 })
        {
            throw new ArgumentException(
                $"{nameof(PdfSplitOptions.OnePagePerFile)} and {nameof(PdfSplitOptions.Ranges)} are mutually exclusive.",
                nameof(options));
        }

        if (!options.OnePagePerFile && (options.Ranges is null || options.Ranges.Count == 0))
        {
            throw new ArgumentException(
                $"Either {nameof(PdfSplitOptions.OnePagePerFile)} must be true or {nameof(PdfSplitOptions.Ranges)} must be supplied.",
                nameof(options));
        }

        return Task.Run<IReadOnlyList<string>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputDirectory);

            var prefix = options.OutputPrefix ?? Path.GetFileNameWithoutExtension(inputPath);
            var fullInput = Path.GetFullPath(inputPath);
            var produced = new List<string>();

            if (options.OnePagePerFile)
            {
                // qpdf input.pdf --split-pages=1 outdir/prefix-%d.pdf
                var template = Path.Combine(outputDirectory, prefix + "-%d.pdf");
                var job = new Job()
                    .InputFile(fullInput)
                    .SplitPages(1)
                    .OutputFile(template);

                Execute(job);

                // qpdf names parts with zero-padded indices based on the page count.
                foreach (var file in Directory.EnumerateFiles(outputDirectory, prefix + "-*.pdf"))
                {
                    produced.Add(file);
                }
                produced.Sort(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                int index = 1;
                foreach (var range in options.Ranges!)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var outFile = Path.Combine(
                        outputDirectory,
                        $"{prefix}-{index.ToString(CultureInfo.InvariantCulture)}.pdf");

                    var job = new Job()
                        .Empty()
                        .Pages(fullInput, range, null!)
                        .OutputFile(outFile);

                    Execute(job);
                    produced.Add(outFile);
                    index++;
                }
            }

            return produced;
        }, cancellationToken);
    }

    // ── Extract images + attachments ─────────────────────────────────────────

    /// <inheritdoc />
    public Task<(int Images, int Attachments)> ExtractAsync(
        string inputPath,
        string outputDirectory,
        string imagesFolderName = "images",
        string attachmentsFolderName = "attachments",
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory cannot be null or whitespace.", nameof(outputDirectory));

        var imagesFolder = string.IsNullOrWhiteSpace(imagesFolderName) ? "images" : SafeFileName(imagesFolderName);
        var attachmentsFolder = string.IsNullOrWhiteSpace(attachmentsFolderName) ? "attachments" : SafeFileName(attachmentsFolderName);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imagesDir = Path.Combine(outputDirectory, imagesFolder);
            var attachmentsDir = Path.Combine(outputDirectory, attachmentsFolder);
            Directory.CreateDirectory(imagesDir);
            Directory.CreateDirectory(attachmentsDir);

            var fullInput = Path.GetFullPath(inputPath);

            // ── Images ────────────────────────────────────────────────────
            // Use qpdf --json to enumerate objects, then extract each image XObject's
            // raw stream data with a per-object Job invocation.
            var jsonJob = new Job()
                .InputFile(fullInput)
                .Json(JsonVersion.Version2);

            byte[] jsonBytes = ExecuteAndCaptureBinaryOutput(jsonJob);
            string jsonOutput = Encoding.UTF8.GetString(jsonBytes);
            int imageCount = ExtractImagesFromJson(fullInput, jsonOutput, imagesDir, cancellationToken);

            // ── Attachments ───────────────────────────────────────────────
            int attachmentCount = 0;
            try
            {
                var listJob = new Job()
                    .InputFile(fullInput)
                    .ListAttachments();

                string attachmentList = ExecuteAndCaptureOutput(listJob);

                foreach (var key in ParseAttachmentKeys(attachmentList))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var outPath = Path.Combine(attachmentsDir, SafeFileName(key));
                    var showJob = new Job()
                        .InputFile(fullInput)
                        .ShowAttachment(key);

                    // ShowAttachment writes the raw bytes to stdout; capture to file.
                    byte[] data = ExecuteAndCaptureBinaryOutput(showJob);
                    File.WriteAllBytes(outPath, data);
                    attachmentCount++;
                }
            }
            catch (PdfOperationException)
            {
                // No attachments present — qpdf returns a non-success status. Swallow.
            }

            return (imageCount, attachmentCount);
        }, cancellationToken);
    }

    // ── Reorder ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task ReorderAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<int> pageOrder,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (pageOrder is null || pageOrder.Count == 0)
            throw new ArgumentException("Page order cannot be empty.", nameof(pageOrder));

        foreach (var p in pageOrder)
        {
            if (p < 1) throw new ArgumentOutOfRangeException(nameof(pageOrder), "Page numbers must be 1-based positive integers.");
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var range = string.Join(",", pageOrder.Select(p => p.ToString(CultureInfo.InvariantCulture)));
            var job = new Job()
                .Empty()
                .Pages(Path.GetFullPath(inputPath), range, null!)
                .OutputFile(Path.GetFullPath(outputPath));

            Execute(job);
        }, cancellationToken);
    }

    // ── Rotate ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task RotateAsync(
        string inputPath,
        string outputPath,
        int angle,
        string? pageRange,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);

        if (angle % 90 != 0)
            throw new ArgumentOutOfRangeException(nameof(angle), "Rotation angle must be a multiple of 90.");

        var normalized = ((angle % 360) + 360) % 360;
        var rotation = normalized switch
        {
            0   => QPdfNet.Enums.Rotation.Rotate0,
            90  => QPdfNet.Enums.Rotation.Rotate90,
            180 => QPdfNet.Enums.Rotation.Rotate180,
            270 => QPdfNet.Enums.Rotation.Rotate270,
            _   => throw new ArgumentOutOfRangeException(nameof(angle))
        };

        var effectiveRange = string.IsNullOrWhiteSpace(pageRange) ? "1-z" : pageRange.Trim();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = new Job()
                .InputFile(Path.GetFullPath(inputPath))
                .Rotate(rotation, effectiveRange)
                .OutputFile(Path.GetFullPath(outputPath));

            Execute(job);
        }, cancellationToken);
    }

    // ── Metadata ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task UpdateMetadataAsync(
        string inputPath,
        string outputPath,
        PdfMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Strategy: dump the PDF structure as qpdf JSON (no stream data needed), locate the
            // /Info object, write a MINIMAL patch JSON with only the /Info changes, then use
            // UpdateFromJson so qpdf merges the patch into the original PDF — supplying all stream
            // data itself. This avoids the fragile full-JSON round-trip.
            var fullInput = Path.GetFullPath(inputPath);
            var fullOutput = Path.GetFullPath(outputPath);

            var workDir = Path.Combine(Path.GetTempPath(), "ft_meta_" + Path.GetRandomFileName());
            Directory.CreateDirectory(workDir);
            var patchJson = Path.Combine(workDir, "patch.json");

            try
            {
                // Dump structure only (no stream data; UpdateFromJson reads streams from the original PDF).
                var jsonBytes = ExecuteAndCaptureBinaryOutput(new Job()
                    .InputFile(fullInput)
                    .Json(JsonVersion.Version2));
                var structureJson = Encoding.UTF8.GetString(jsonBytes);

                var patch = BuildInfoPatchJson(structureJson, metadata);
                // qpdf's JSON parser rejects a UTF-8 BOM ("offset 0: unexpected character"),
                // and Encoding.UTF8 emits one — write without a BOM.
                File.WriteAllText(patchJson, patch, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                Execute(new Job()
                    .InputFile(fullInput)
                    .UpdateFromJson(patchJson)
                    .OutputFile(fullOutput));
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Builds a minimal qpdf patch JSON that contains only the /Info object update.
    /// qpdf's UpdateFromJson merges this into the original PDF object-by-object, so only
    /// objects present in the patch are changed; everything else (including streams) is
    /// preserved from the input PDF.
    /// </summary>
    private static string BuildInfoPatchJson(string structureJson, PdfMetadata metadata)
    {
        using var doc = JsonDocument.Parse(structureJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("qpdf", out var qpdfArr) ||
            qpdfArr.ValueKind != JsonValueKind.Array ||
            qpdfArr.GetArrayLength() < 2)
            throw new PdfOperationException("Unexpected qpdf JSON shape: missing 'qpdf' array.");

        var header = qpdfArr[0];
        var objects = qpdfArr[1];

        // Locate the /Info object reference from the trailer.
        string? infoObjKey = null;
        if (objects.TryGetProperty("trailer", out var trailer) &&
            trailer.TryGetProperty("value", out var trailerValue) &&
            trailerValue.TryGetProperty("/Info", out var infoRef) &&
            infoRef.ValueKind == JsonValueKind.String)
        {
            infoObjKey = "obj:" + infoRef.GetString();
        }

        // Read existing /Info fields so we can preserve anything we're not overwriting.
        JsonElement existingInfoDict = default;
        if (infoObjKey is not null &&
            objects.TryGetProperty(infoObjKey, out var infoObj) &&
            infoObj.TryGetProperty("value", out var infoValue) &&
            infoValue.ValueKind == JsonValueKind.Object)
        {
            existingInfoDict = infoValue;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WritePropertyName("qpdf");
            w.WriteStartArray();

            // Header element — qpdf validates this on update and uses "maxobjectid"
            // to allocate any new objects, so it must carry the original jsonversion,
            // pdfversion, and maxobjectid. An empty object here makes qpdf reject the
            // patch with ErrorsFoundFileNotProcessed, so echo the original verbatim.
            header.WriteTo(w);

            // Object map — we include only the /Info object (all others untouched).
            w.WriteStartObject();

            if (infoObjKey is not null)
            {
                w.WritePropertyName(infoObjKey);
                w.WriteStartObject();
                w.WritePropertyName("value");
                w.WriteStartObject();

                var seen = new HashSet<string>(StringComparer.Ordinal);
                if (existingInfoDict.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in existingInfoDict.EnumerateObject())
                    {
                        seen.Add(entry.Name);
                        var replacement = ResolveInfoReplacement(entry.Name, metadata);
                        if (replacement is null)
                            entry.WriteTo(w);
                        else if (replacement.Length > 0)
                            w.WriteString(entry.Name, "u:" + replacement);
                        // empty string → drop the key
                    }
                }

                AddIfMissing(w, seen, "/Title",    metadata.Title);
                AddIfMissing(w, seen, "/Author",   metadata.Author);
                AddIfMissing(w, seen, "/Subject",  metadata.Subject);
                AddIfMissing(w, seen, "/Keywords", metadata.Keywords);
                AddIfMissing(w, seen, "/Creator",  metadata.Creator);
                AddIfMissing(w, seen, "/Producer", metadata.Producer);

                w.WriteEndObject(); // value
                w.WriteEndObject(); // info object
            }

            w.WriteEndObject(); // object map
            w.WriteEndArray();  // qpdf
            w.WriteEndObject(); // root
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? ResolveInfoReplacement(string key, PdfMetadata metadata) => key switch
    {
        "/Title"    => metadata.Title,
        "/Author"   => metadata.Author,
        "/Subject"  => metadata.Subject,
        "/Keywords" => metadata.Keywords,
        "/Creator"  => metadata.Creator,
        "/Producer" => metadata.Producer,
        _ => null
    };

    private static void AddIfMissing(Utf8JsonWriter writer, HashSet<string> seen, string key, string? value)
    {
        if (value is null || value.Length == 0 || seen.Contains(key)) return;
        writer.WriteString(key, "u:" + value);
    }

    /// <inheritdoc />
    public Task<PdfMetadata> ReadMetadataAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullInput = Path.GetFullPath(inputPath);
            var jsonBytes = ExecuteAndCaptureBinaryOutput(new Job()
                .InputFile(fullInput)
                .Json(JsonVersion.Version2));
            var structureJson = Encoding.UTF8.GetString(jsonBytes);
            return ParseInfoMetadata(structureJson);
        }, cancellationToken);
    }

    private static PdfMetadata ParseInfoMetadata(string structureJson)
    {
        using var doc = JsonDocument.Parse(structureJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("qpdf", out var qpdfArr) ||
            qpdfArr.ValueKind != JsonValueKind.Array ||
            qpdfArr.GetArrayLength() < 2)
            return new PdfMetadata();

        var objects = qpdfArr[1];

        string? infoObjKey = null;
        if (objects.TryGetProperty("trailer", out var trailer) &&
            trailer.TryGetProperty("value", out var trailerValue) &&
            trailerValue.TryGetProperty("/Info", out var infoRef) &&
            infoRef.ValueKind == JsonValueKind.String)
        {
            infoObjKey = "obj:" + infoRef.GetString();
        }

        if (infoObjKey is null ||
            !objects.TryGetProperty(infoObjKey, out var infoObj) ||
            !infoObj.TryGetProperty("value", out var info) ||
            info.ValueKind != JsonValueKind.Object)
            return new PdfMetadata();

        return new PdfMetadata
        {
            Title    = ReadInfoString(info, "/Title"),
            Author   = ReadInfoString(info, "/Author"),
            Subject  = ReadInfoString(info, "/Subject"),
            Keywords = ReadInfoString(info, "/Keywords"),
            Creator  = ReadInfoString(info, "/Creator"),
            Producer = ReadInfoString(info, "/Producer"),
        };
    }

    private static string? ReadInfoString(JsonElement info, string key)
    {
        if (!info.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String) return null;
        return DecodeQpdfString(el.GetString());
    }

    /// <summary>
    /// Decodes a qpdf JSON v2 string value. qpdf prefixes strings with "u:" (UTF-8 text) or
    /// "b:" (hex-encoded raw bytes).
    /// </summary>
    private static string? DecodeQpdfString(string? raw)
    {
        if (raw is null) return null;
        if (raw.StartsWith("u:", StringComparison.Ordinal)) return raw[2..];
        if (raw.StartsWith("b:", StringComparison.Ordinal))
        {
            try { return Encoding.UTF8.GetString(Convert.FromHexString(raw[2..])); }
            catch { return raw[2..]; }
        }
        return raw;
    }

    // ── Encryption ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task EncryptAsync(
        string inputPath,
        string outputPath,
        string userPassword,
        string? ownerPassword,
        PdfPermissions? permissions,
        PdfEncryptionStrength strength,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (userPassword is null) throw new ArgumentNullException(nameof(userPassword));

        var owner = string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword;
        var perms = permissions ?? new PdfPermissions();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = new Job().InputFile(Path.GetFullPath(inputPath));
            ApplyEncryption(job, userPassword, owner, perms, strength);
            job.OutputFile(Path.GetFullPath(outputPath));
            Execute(job);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemovePasswordAsync(
        string inputPath,
        string outputPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (password is null) throw new ArgumentNullException(nameof(password));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = new Job()
                .InputFile(Path.GetFullPath(inputPath))
                .Password(password)
                .Decrypt()
                .OutputFile(Path.GetFullPath(outputPath));

            Execute(job);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(
        string inputPath,
        string outputPath,
        string currentPassword,
        string newUserPassword,
        string? newOwnerPassword,
        PdfEncryptionStrength strength,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (currentPassword is null) throw new ArgumentNullException(nameof(currentPassword));
        if (newUserPassword is null) throw new ArgumentNullException(nameof(newUserPassword));

        // Decrypt into a temp file, then re-encrypt with the new credentials.
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");

        try
        {
            await RemovePasswordAsync(inputPath, tempPath, currentPassword, cancellationToken).ConfigureAwait(false);
            await EncryptAsync(
                tempPath,
                outputPath,
                newUserPassword,
                newOwnerPassword,
                permissions: null,
                strength,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    /// <inheritdoc />
    public Task UpdatePermissionsAsync(
        string inputPath,
        string outputPath,
        string? ownerPassword,
        PdfPermissions permissions,
        PdfEncryptionStrength strength,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (permissions is null) throw new ArgumentNullException(nameof(permissions));

        // An empty owner password is valid: qpdf writes the permission flags without requiring a
        // password to open or to lift them. A non-empty password also doubles as the credential
        // needed to open an already-encrypted input.
        var owner = ownerPassword ?? string.Empty;

        // Re-encrypt with empty user password (no password needed to open) but enforce
        // the new permission set via the owner password.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = new Job().InputFile(Path.GetFullPath(inputPath));
            if (owner.Length > 0) job.Password(owner);

            ApplyEncryption(job, userPassword: string.Empty, owner, permissions, strength);
            job.OutputFile(Path.GetFullPath(outputPath));
            Execute(job);
        }, cancellationToken);
    }

    private static void ApplyEncryption(
        Job job,
        string userPassword,
        string ownerPassword,
        PdfPermissions perms,
        PdfEncryptionStrength strength)
    {
        var modify = ResolveModify(perms);
        var print  = ResolvePrint(perms);

        switch (strength)
        {
            case PdfEncryptionStrength.Rc4_40:
                job.Encrypt(userPassword, ownerPassword, new Encryption40Bit(
                    annotate: perms.AllowAnnotate,
                    extract:  perms.AllowExtract,
                    modify:   modify,
                    print:    print));
                break;

            case PdfEncryptionStrength.Rc4_128:
                job.Encrypt(userPassword, ownerPassword, new Encryption128Bit(
                    accessibility:     true,
                    annotate:          perms.AllowAnnotate,
                    assemble:          perms.AllowAssemble,
                    extract:           perms.AllowExtract,
                    form:              perms.AllowFormFill,
                    modifyOther:       perms.AllowModify,
                    modify:            modify,
                    print:             print,
                    cleartextMetaData: false));
                break;

            case PdfEncryptionStrength.Aes_256:
            default:
                job.Encrypt(userPassword, ownerPassword, new Encryption256Bit(
                    accessibility:     true,
                    annotate:          perms.AllowAnnotate,
                    assemble:          perms.AllowAssemble,
                    extract:           perms.AllowExtract,
                    form:              perms.AllowFormFill,
                    modifyOther:       perms.AllowModify,
                    modify:            modify,
                    print:             print,
                    cleartextMetaData: false));
                break;
        }
    }

    private static QPdfNet.Enums.Modify ResolveModify(PdfPermissions perms)
    {
        if (!perms.AllowModify && !perms.AllowAnnotate && !perms.AllowAssemble && !perms.AllowFormFill)
            return QPdfNet.Enums.Modify.None;
        if (perms.AllowModify) return QPdfNet.Enums.Modify.All;
        if (perms.AllowFormFill) return QPdfNet.Enums.Modify.Form;
        if (perms.AllowAnnotate) return QPdfNet.Enums.Modify.Annotate;
        return QPdfNet.Enums.Modify.Assembly;
    }

    private static QPdfNet.Enums.Print ResolvePrint(PdfPermissions perms)
    {
        if (!perms.AllowPrint) return QPdfNet.Enums.Print.None;
        return perms.AllowHighResPrint ? QPdfNet.Enums.Print.Full : QPdfNet.Enums.Print.Low;
    }

    // ── Repair ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task RepairAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A bare qpdf in→out invocation with linearization rewrites the entire structure,
            // which is qpdf's de-facto repair mode.
            var job = new Job()
                .InputFile(Path.GetFullPath(inputPath))
                .Linearize()
                .OutputFile(Path.GetFullPath(outputPath));

            Execute(job);
        }, cancellationToken);
    }

    // ── OCR ──────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task OcrAsync(
        string inputPath,
        string outputPath,
        PdfOcrOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.TessDataPath))
            throw new ArgumentException("TessDataPath is required.", nameof(options));
        if (!Directory.Exists(options.TessDataPath))
            throw new DirectoryNotFoundException($"Tessdata directory was not found: {options.TessDataPath}");
        if (options.Dpi < 72 || options.Dpi > 1200)
            throw new ArgumentOutOfRangeException(nameof(options), "DPI must be between 72 and 1200.");

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workDir = Path.Combine(Path.GetTempPath(), "ft_ocr_" + Path.GetRandomFileName());
            Directory.CreateDirectory(workDir);

            try
            {
                int pageCount = GetPageCount(Path.GetFullPath(inputPath));
                var perPagePdfs = new List<string>(pageCount);

                using var engine = new Engine(options.TessDataPath, options.Languages, options.EngineMode);

                for (int page = 0; page < pageCount; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Rasterize the page to a TIFF on disk via libvips' PDF loader.
                    var tiffPath = Path.Combine(workDir, $"page_{page:D5}.tif");
                    RasterizePdfPage(Path.GetFullPath(inputPath), page, options.Dpi, tiffPath);

                    // Run Tesseract with its PDF renderer to produce a searchable page PDF.
                    var pageBaseName = Path.Combine(workDir, $"page_{page:D5}");
                    using var renderer = TesseractOCR.Renderers.Result.CreatePdfRenderer(
                        pageBaseName,
                        options.TessDataPath,
                        textonly: false);

                    using var img = TesseractOCR.Pix.Image.LoadFromFile(tiffPath);
                    using (renderer.BeginDocument(Path.GetFileNameWithoutExtension(inputPath)))
                    {
                        using var ocrPage = engine.Process(img);
                        renderer.AddPage(ocrPage);
                    }

                    perPagePdfs.Add(pageBaseName + ".pdf");
                }

                // Merge all per-page PDFs into the final output.
                MergeSync(perPagePdfs, Path.GetFullPath(outputPath));
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { }
            }
        }, cancellationToken);
    }

    private static void RasterizePdfPage(string pdfPath, int pageIndex, int dpi, string outputTiffPath)
    {
        // libvips renders PDF at the requested DPI when the dpi load option is set.
        using var image = Image.Pdfload(pdfPath, page: pageIndex, n: 1, dpi: dpi);
        image.Tiffsave(outputTiffPath, compression: NetVips.Enums.ForeignTiffCompression.Lzw);
    }

    private static int GetPageCount(string pdfPath)
    {
        // libvips exposes the total page count of the PDF as the "n-pages" metadata field.
        using var image = Image.Pdfload(pdfPath, page: 0, n: 1);
        return (int)image.Get("n-pages");
    }

    private static void MergeSync(IReadOnlyList<string> inputs, string outputPath)
    {
        var job = new Job().Empty();
        foreach (var input in inputs) job.Pages(input, "1-z");
        job.OutputFile(outputPath);
        Execute(job);
    }

    // ── qpdf execution helpers ───────────────────────────────────────────────

    private static void Execute(Job job)
    {
        ExitCode code;
        try
        {
            code = job.Run(out _);
        }
        catch (Exception ex) when (ex is not PdfOperationException)
        {
            throw new PdfOperationException("qpdf threw while executing the job: " + ex.Message, null, ex);
        }

        if (!IsSuccess(code))
        {
            throw new PdfOperationException($"qpdf returned non-success exit code: {code}.", code);
        }
    }

    private static string ExecuteAndCaptureOutput(Job job)
    {
        try
        {
            var code = job.Run(out var output);
            if (!IsSuccess(code))
                throw new PdfOperationException($"qpdf returned non-success exit code: {code}.", code);
            return output ?? string.Empty;
        }
        catch (PdfOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PdfOperationException("qpdf threw while executing the job: " + ex.Message, null, ex);
        }
    }

    private static byte[] ExecuteAndCaptureBinaryOutput(Job job)
    {
        try
        {
            var code = job.Run(out _, out var data);
            if (!IsSuccess(code))
                throw new PdfOperationException($"qpdf returned non-success exit code: {code}.", code);
            return data ?? Array.Empty<byte>();
        }
        catch (PdfOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PdfOperationException("qpdf threw while executing the job: " + ex.Message, null, ex);
        }
    }

    private static bool IsSuccess(ExitCode code) =>
        code == ExitCode.Success || code == ExitCode.WarningsWereFoundFileProcessed;

    // ── Image extraction (qpdf JSON image enumeration) ───────────────────────

    /// <summary>
    /// Enumerates the images qpdf reports under <c>pages[].images[]</c> and writes each one to disk
    /// as a viewable file. JPEG/JPEG2000 streams are written verbatim (their raw bytes already are a
    /// standalone image); other rasters are decoded with qpdf's filtered-stream-data and rebuilt into
    /// a PNG/TIFF via libvips using the reported width/height/colorspace. Anything that can't be
    /// reconstructed is written as raw <c>.bin</c> so nothing is silently dropped.
    /// </summary>
    private static int ExtractImagesFromJson(
        string inputPath,
        string json,
        string outputDir,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            return 0;

        var seen = new HashSet<int>();
        int count = 0;

        foreach (var page in pages.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!page.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var image in images.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!image.TryGetProperty("object", out var objRef) || objRef.ValueKind != JsonValueKind.String)
                    continue;

                var objId = ParseObjectRef(objRef.GetString());
                if (objId is null || !seen.Add(objId.Value)) continue; // de-dupe shared images

                if (TryExtractImage(inputPath, image, objId.Value, outputDir))
                    count++;
            }
        }

        return count;
    }

    private static bool TryExtractImage(string inputPath, JsonElement image, int objId, string outputDir)
    {
        var lastFilter = LastFilterName(image);

        try
        {
            // DCTDecode / JPXDecode streams are already complete image files — copy the raw bytes.
            if (lastFilter is "/DCTDecode" or "/JPXDecode")
            {
                var ext = lastFilter == "/DCTDecode" ? ".jpg" : ".jp2";
                var raw = DumpStream(inputPath, objId, filtered: false);
                File.WriteAllBytes(Path.Combine(outputDir, $"image_{objId}{ext}"), raw);
                return true;
            }

            int width  = GetInt(image, "width");
            int height = GetInt(image, "height");
            int bpc    = GetInt(image, "bitspercomponent");

            // Try to rebuild a viewable raster from decoded samples.
            if (width > 0 && height > 0 && bpc == 8)
            {
                var decoded = DumpStream(inputPath, objId, filtered: true);
                long pixels = (long)width * height;
                int? bands = ResolveBands(image, decoded.Length, pixels);

                if (bands is 1 or 3)
                {
                    using var img = Image.NewFromMemory(decoded, width, height, bands.Value, NetVips.Enums.BandFormat.Uchar);
                    img.Pngsave(Path.Combine(outputDir, $"image_{objId}.png"));
                    return true;
                }
                if (bands is 4)
                {
                    using var img = Image.NewFromMemory(decoded, width, height, 4, NetVips.Enums.BandFormat.Uchar);
                    img.Tiffsave(Path.Combine(outputDir, $"image_{objId}.tif"));
                    return true;
                }
            }

            // Fallback: write the raw stream so the image is at least recoverable.
            var rawFallback = DumpStream(inputPath, objId, filtered: false);
            File.WriteAllBytes(Path.Combine(outputDir, $"image_{objId}.bin"), rawFallback);
            return true;
        }
        catch (Exception)
        {
            // Encrypted, malformed, or undecodable stream — skip it rather than fail the whole export.
            return false;
        }
    }

    private static byte[] DumpStream(string inputPath, int objId, bool filtered)
    {
        var job = new Job().InputFile(inputPath).ShowObject($"{objId},0");
        job = filtered ? job.FilteredStreamData() : job.RawStreamData();
        return ExecuteAndCaptureBinaryOutput(job);
    }

    /// <summary>Picks the band count from the colorspace name, or infers it from the decoded length.</summary>
    private static int? ResolveBands(JsonElement image, int decodedLength, long pixels)
    {
        if (image.TryGetProperty("colorspace", out var cs) && cs.ValueKind == JsonValueKind.String)
        {
            switch (cs.GetString())
            {
                case "/DeviceGray": return 1;
                case "/DeviceRGB":  return 3;
                case "/DeviceCMYK": return 4;
            }
        }

        // Unknown/ICC colorspace: infer bands when the decoded buffer divides evenly into the pixels.
        if (pixels > 0 && decodedLength % pixels == 0)
        {
            long b = decodedLength / pixels;
            if (b is 1 or 3 or 4) return (int)b;
        }

        return null;
    }

    private static string? LastFilterName(JsonElement image)
    {
        if (!image.TryGetProperty("filter", out var filter)) return null;

        if (filter.ValueKind == JsonValueKind.String) return filter.GetString();

        if (filter.ValueKind == JsonValueKind.Array && filter.GetArrayLength() > 0)
        {
            var last = filter[filter.GetArrayLength() - 1];
            if (last.ValueKind == JsonValueKind.String) return last.GetString();
        }

        return null;
    }

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : 0;

    private static int? ParseObjectRef(string? objRef)
    {
        // Format is "<num> <gen> R" (e.g. "12 0 R").
        if (string.IsNullOrEmpty(objRef)) return null;
        var space = objRef.IndexOf(' ');
        var head = space < 0 ? objRef : objRef[..space];
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    // ── Attachment listing parser ────────────────────────────────────────────

    /// <summary>
    /// Parses the output of <c>qpdf --list-attachments</c>. Each line begins with the
    /// attachment key followed by a tab and metadata; we keep only the key.
    /// </summary>
    private static IEnumerable<string> ParseAttachmentKeys(string listing)
    {
        if (string.IsNullOrWhiteSpace(listing)) yield break;

        foreach (var rawLine in listing.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var tab = line.IndexOf('\t');
            yield return tab < 0 ? line : line[..tab].Trim();
        }
    }

    // ── Validation & misc ────────────────────────────────────────────────────

    private static void ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Input path cannot be null or whitespace.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Input PDF was not found.", path);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Only .pdf files are supported. Got '{Path.GetExtension(path)}'.");
    }

    private static void ValidateOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(path));

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.Length == 0 ? "attachment.bin" : sb.ToString();
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
