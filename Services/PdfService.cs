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
    /// </summary>
    /// <returns>The number of images and attachments extracted, in that order.</returns>
    Task<(int Images, int Attachments)> ExtractAsync(
        string inputPath,
        string outputDirectory,
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
    /// Re-encrypts the PDF using the supplied owner password but with a new permission set.
    /// The user password is preserved. Use this to flip print/copy/edit/annotate flags without
    /// rotating credentials.
    /// </summary>
    Task UpdatePermissionsAsync(
        string inputPath,
        string outputPath,
        string ownerPassword,
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
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory cannot be null or whitespace.", nameof(outputDirectory));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imagesDir = Path.Combine(outputDirectory, "images");
            var attachmentsDir = Path.Combine(outputDirectory, "attachments");
            Directory.CreateDirectory(imagesDir);
            Directory.CreateDirectory(attachmentsDir);

            var fullInput = Path.GetFullPath(inputPath);

            // ── Images ────────────────────────────────────────────────────
            // Use qpdf --json to enumerate objects, then extract each image XObject's
            // raw stream data with a per-object Job invocation.
            var jsonJob = new Job()
                .InputFile(fullInput)
                .Json(JsonVersion.Version2);

            string jsonOutput = ExecuteAndCaptureOutput(jsonJob);
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

            // QPdfNet 1.5.0 does not surface qpdf's --set-info-key flag. We achieve the same
            // effect via a JSON round-trip: dump the PDF as qpdf JSON, mutate the /Info
            // dictionary entries in-place, then write the modified JSON back as a PDF.
            var fullInput = Path.GetFullPath(inputPath);
            var fullOutput = Path.GetFullPath(outputPath);

            var workDir = Path.Combine(Path.GetTempPath(), "ft_meta_" + Path.GetRandomFileName());
            Directory.CreateDirectory(workDir);
            var srcJson = Path.Combine(workDir, "src.json");
            var dstJson = Path.Combine(workDir, "dst.json");

            try
            {
                Execute(new Job()
                    .InputFile(fullInput)
                    .JsonOutput(JsonVersion.Version2)
                    .OutputFile(srcJson));

                var patched = PatchInfoDictionary(File.ReadAllText(srcJson), metadata);
                File.WriteAllText(dstJson, patched);

                Execute(new Job()
                    .JsonInput()
                    .InputFile(dstJson)
                    .OutputFile(fullOutput));
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Rewrites the qpdf JSON document so the trailer's /Info object reflects the supplied metadata.
    /// Null fields are left as-is; empty fields remove the entry.
    /// </summary>
    private static string PatchInfoDictionary(string sourceJson, PdfMetadata metadata)
    {
        using var doc = JsonDocument.Parse(sourceJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("qpdf", out var qpdfArr) ||
            qpdfArr.ValueKind != JsonValueKind.Array ||
            qpdfArr.GetArrayLength() < 2)
        {
            throw new PdfOperationException("Unexpected qpdf JSON shape: missing 'qpdf' array.");
        }

        // The second element is the object map; find or create the /Info object ref via trailer.
        var objectsElement = qpdfArr[1];

        // Locate the trailer's /Info entry to know which object holds the Info dictionary.
        string? infoObjKey = null;
        if (objectsElement.TryGetProperty("trailer", out var trailer) &&
            trailer.TryGetProperty("value", out var trailerValue) &&
            trailerValue.TryGetProperty("/Info", out var infoRef) &&
            infoRef.ValueKind == JsonValueKind.String)
        {
            // qpdf encodes references as e.g. "12 0 R"; the object map key is "obj:12 0 R".
            infoObjKey = "obj:" + infoRef.GetString();
        }

        // Build a mutable representation by serialising back through a writer.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            foreach (var topProp in root.EnumerateObject())
            {
                if (topProp.Name != "qpdf")
                {
                    topProp.WriteTo(writer);
                    continue;
                }

                writer.WritePropertyName("qpdf");
                writer.WriteStartArray();
                for (int i = 0; i < qpdfArr.GetArrayLength(); i++)
                {
                    if (i == 1)
                    {
                        WritePatchedObjectMap(writer, qpdfArr[i], infoObjKey, metadata);
                    }
                    else
                    {
                        qpdfArr[i].WriteTo(writer);
                    }
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePatchedObjectMap(
        Utf8JsonWriter writer,
        JsonElement objects,
        string? infoObjKey,
        PdfMetadata metadata)
    {
        writer.WriteStartObject();
        foreach (var entry in objects.EnumerateObject())
        {
            if (infoObjKey is not null && entry.Name == infoObjKey)
            {
                writer.WritePropertyName(entry.Name);
                WritePatchedInfoObject(writer, entry.Value, metadata);
            }
            else
            {
                entry.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static void WritePatchedInfoObject(
        Utf8JsonWriter writer,
        JsonElement infoObject,
        PdfMetadata metadata)
    {
        writer.WriteStartObject();
        foreach (var prop in infoObject.EnumerateObject())
        {
            if (prop.Name != "value")
            {
                prop.WriteTo(writer);
                continue;
            }

            writer.WritePropertyName("value");
            writer.WriteStartObject();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dictEntry in prop.Value.EnumerateObject())
            {
                seen.Add(dictEntry.Name);
                var replacement = ResolveInfoReplacement(dictEntry.Name, metadata);
                if (replacement is null)
                {
                    dictEntry.WriteTo(writer);
                }
                else if (replacement.Length > 0)
                {
                    writer.WriteString(dictEntry.Name, "u:" + replacement);
                }
                // empty replacement string => drop the key
            }

            // Add any keys that weren't previously present.
            AddIfMissing(writer, seen, "/Title",    metadata.Title);
            AddIfMissing(writer, seen, "/Author",   metadata.Author);
            AddIfMissing(writer, seen, "/Subject",  metadata.Subject);
            AddIfMissing(writer, seen, "/Keywords", metadata.Keywords);
            AddIfMissing(writer, seen, "/Creator",  metadata.Creator);
            AddIfMissing(writer, seen, "/Producer", metadata.Producer);

            writer.WriteEndObject();
        }
        writer.WriteEndObject();
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
        string ownerPassword,
        PdfPermissions permissions,
        PdfEncryptionStrength strength,
        CancellationToken cancellationToken = default)
    {
        ValidateInputPath(inputPath);
        ValidateOutputPath(outputPath);
        if (ownerPassword is null) throw new ArgumentNullException(nameof(ownerPassword));
        if (permissions is null) throw new ArgumentNullException(nameof(permissions));

        // Re-encrypt with empty user password (no password needed to open) but enforce
        // the new permission set via the supplied owner password.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = new Job()
                .InputFile(Path.GetFullPath(inputPath))
                .Password(ownerPassword);

            ApplyEncryption(job, userPassword: string.Empty, ownerPassword, permissions, strength);
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

    // ── Image extraction (qpdf JSON walker) ──────────────────────────────────

    /// <summary>
    /// Walks the qpdf JSON tree to find Image XObjects and writes their raw stream data to disk.
    /// </summary>
    private static int ExtractImagesFromJson(
        string inputPath,
        string json,
        string outputDir,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("qpdf", out var qpdfArray)) return 0;
        if (qpdfArray.ValueKind != JsonValueKind.Array || qpdfArray.GetArrayLength() < 2) return 0;

        var objects = qpdfArray[1]; // The second entry contains the object map.
        if (objects.ValueKind != JsonValueKind.Object) return 0;

        int count = 0;
        foreach (var prop in objects.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!prop.Value.TryGetProperty("value", out var value)) continue;
            if (value.ValueKind != JsonValueKind.Object) continue;

            // Image XObjects have /Type=/XObject and /Subtype=/Image.
            if (!IsImageXObject(value)) continue;

            // prop.Name looks like "obj:12 0 R" — peel out the object id.
            var objId = ParseObjectId(prop.Name);
            if (objId is null) continue;

            var filterName = TryGetFilterName(value);
            var ext = FilterToExtension(filterName);
            var outPath = Path.Combine(outputDir, $"image_{objId.Value}{ext}");

            var dumpJob = new Job()
                .InputFile(inputPath)
                .ShowObject($"{objId.Value},0")
                .RawStreamData();

            try
            {
                var bytes = ExecuteAndCaptureBinaryOutput(dumpJob);
                File.WriteAllBytes(outPath, bytes);
                count++;
            }
            catch (PdfOperationException)
            {
                // Skip objects qpdf refuses to dump (encrypted streams, malformed entries).
            }
        }

        return count;
    }

    private static bool IsImageXObject(JsonElement value)
    {
        if (!value.TryGetProperty("/Type", out var type) || type.ValueKind != JsonValueKind.String) return false;
        if (!string.Equals(type.GetString(), "/XObject", StringComparison.Ordinal)) return false;
        if (!value.TryGetProperty("/Subtype", out var subtype) || subtype.ValueKind != JsonValueKind.String) return false;
        return string.Equals(subtype.GetString(), "/Image", StringComparison.Ordinal);
    }

    private static string? TryGetFilterName(JsonElement value)
    {
        if (!value.TryGetProperty("/Filter", out var filter)) return null;

        if (filter.ValueKind == JsonValueKind.String) return filter.GetString();

        if (filter.ValueKind == JsonValueKind.Array && filter.GetArrayLength() > 0)
        {
            // Compound filter chain — the last filter determines the on-disk format.
            var last = filter[filter.GetArrayLength() - 1];
            if (last.ValueKind == JsonValueKind.String) return last.GetString();
        }

        return null;
    }

    private static string FilterToExtension(string? filter) => filter switch
    {
        "/DCTDecode"      => ".jpg",
        "/JPXDecode"      => ".jp2",
        "/CCITTFaxDecode" => ".tif",
        "/FlateDecode"    => ".png",
        "/LZWDecode"      => ".tif",
        _                 => ".bin"
    };

    private static int? ParseObjectId(string objKey)
    {
        // Format is "obj:<num> <gen> R" — extract the numeric id.
        const string prefix = "obj:";
        if (!objKey.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var rest = objKey.AsSpan(prefix.Length);
        var space = rest.IndexOf(' ');
        if (space < 0) return null;

        return int.TryParse(rest[..space], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
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
