using Files_Tools.Services;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Files.Tools.Tests;

[TestClass]
public class DocumentProcessingServiceTests
{
    private string _tempRoot = null!;
    private DocumentService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-doc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _service = new DocumentService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
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
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Diagnostics — runs first, fails loudly with a full environment report
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Probes the bundled LibreOffice path and reports everything relevant to
    /// diagnosing "Failed to start LibreOffice process" exceptions.
    ///
    /// This test intentionally calls Assert.Fail() with the full report so that
    /// the output is always visible in the test results, regardless of outcome.
    /// Comment out the final Assert.Fail() once the binary probe passes.
    /// </summary>
    [TestMethod]
    [TestCategory("Diagnostics")]
    public async Task Diagnostics_LibreOfficeBinaryProbe()
    {
        var report = new StringBuilder();
        report.AppendLine("=== LibreOffice Binary Probe ===");
        report.AppendLine();

        // ── Environment ───────────────────────────────────────────────────────
        var rid        = GetRid();
        var baseDir    = AppContext.BaseDirectory;
        var exeName    = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "soffice.exe" : "soffice";
        var bundledDir = Path.Combine(baseDir, "libreoffice", rid, "program");
        var bundledExe = Path.Combine(bundledDir, exeName);

        report.AppendLine($"AppContext.BaseDirectory : {baseDir}");
        report.AppendLine($"Current RID             : {rid}");
        report.AppendLine($"Expected bundled exe    : {bundledExe}");
        report.AppendLine($"Bundled exe exists      : {File.Exists(bundledExe)}");
        report.AppendLine();

        // ── Directory listing: AppDir root ────────────────────────────────────
        report.AppendLine("--- AppDir top-level entries ---");
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(baseDir).OrderBy(x => x))
                report.AppendLine($"  {Path.GetRelativePath(baseDir, entry)}");
        }
        catch (Exception ex)
        {
            report.AppendLine($"  (error listing AppDir: {ex.Message})");
        }

        report.AppendLine();

        // ── Directory listing: libreoffice/ subtree ───────────────────────────
        var loRoot = Path.Combine(baseDir, "libreoffice");
        report.AppendLine($"--- libreoffice/ subtree (exists: {Directory.Exists(loRoot)}) ---");
        if (Directory.Exists(loRoot))
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(loRoot, "*", SearchOption.AllDirectories)
                                               .OrderBy(x => x))
                {
                    report.AppendLine($"  {Path.GetRelativePath(loRoot, entry)}");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"  (error listing libreoffice/: {ex.Message})");
            }
        }

        report.AppendLine();

        // ── System-install fallback candidates (dev-time) ─────────────────────
        report.AppendLine("--- System-install candidates (Program Files probe) ---");
        var systemCandidates = new List<string>();
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "LibreOffice*", SearchOption.TopDirectoryOnly)
                                             .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var c = Path.Combine(dir, "program", exeName);
                    report.AppendLine($"  {c}  [exists: {File.Exists(c)}]");
                    if (File.Exists(c)) systemCandidates.Add(c);
                }
            }
            catch { /* skip inaccessible dirs */ }
        }
        if (systemCandidates.Count == 0)
            report.AppendLine("  (none — LibreOffice is not installed in Program Files)");

        report.AppendLine();

        // ── Version probe — try bundled first, then first system candidate ────
        var probeExe    = File.Exists(bundledExe) ? bundledExe
                        : systemCandidates.Count > 0 ? systemCandidates[0]
                        : null;
        var probeWorkDir = probeExe is not null ? (Path.GetDirectoryName(probeExe) ?? string.Empty) : string.Empty;

        report.AppendLine("--- soffice --version probe ---");

        if (probeExe is null)
        {
            report.AppendLine("  SKIPPED — no soffice.exe found (bundled or system).");
        }
        else
        {
            report.AppendLine($"  Probing : {probeExe}");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = probeExe,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                    WorkingDirectory       = probeWorkDir
                };
                psi.ArgumentList.Add("--version");

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                report.AppendLine($"  Exit code : {proc.ExitCode}");
                report.AppendLine($"  stdout    : {stdout.Trim()}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    report.AppendLine($"  stderr    : {stderr.Trim()}");
            }
            catch (Exception ex)
            {
                report.AppendLine($"  EXCEPTION : [{ex.GetType().Name}] {ex.Message}");
                if (ex.InnerException is not null)
                    report.AppendLine($"  INNER     : [{ex.InnerException.GetType().Name}] {ex.InnerException.Message}");
            }
        }

        report.AppendLine();
        report.AppendLine("=== End of probe ===");

        // Always surface the full report in the test output.
        // Once the probe shows success, remove or comment out this Assert.Fail().
        Assert.Fail(report.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Input validation — no LibreOffice required
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ConvertToPdf_ThrowsFileNotFound_WhenInputMissing()
    {
        var missing = Path.Combine(_tempRoot, "nope.docx");
        var output  = Path.Combine(_tempRoot, "out.pdf");

        await AssertThrowsAsync<FileNotFoundException>(
            () => _service.ConvertToPdfAsync(missing, output));
    }

    [TestMethod]
    public async Task ConvertToPdf_ThrowsArgumentException_WhenInputPathBlank()
    {
        await AssertThrowsAsync<ArgumentException>(
            () => _service.ConvertToPdfAsync("  ", Path.Combine(_tempRoot, "out.pdf")));
    }

    [TestMethod]
    public async Task ConvertToPdf_ThrowsArgumentException_WhenOutputPathBlank()
    {
        var input = CreateMinimalDocx("source.docx");

        await AssertThrowsAsync<ArgumentException>(
            () => _service.ConvertToPdfAsync(input, "  "));
    }

    [TestMethod]
    public async Task ConvertToPdf_ThrowsNotSupported_WhenExtensionUnknown()
    {
        var input  = Path.Combine(_tempRoot, "file.xyz");
        File.WriteAllText(input, "dummy");
        var output = Path.Combine(_tempRoot, "out.pdf");

        await AssertThrowsAsync<NotSupportedException>(
            () => _service.ConvertToPdfAsync(input, output));
    }

    [TestMethod]
    public async Task ConvertToPdf_ThrowsArgumentOutOfRange_WhenJpegQualityOutOfRange()
    {
        var input = CreateMinimalDocx("source.docx");
        var output = Path.Combine(_tempRoot, "out.pdf");

        await AssertThrowsAsync<ArgumentOutOfRangeException>(
            () => _service.ConvertToPdfAsync(input, output, new DocumentConversionOptions
            {
                ImageCompression = PdfImageCompression.Jpeg,
                JpegQuality      = 0   // below valid range 1-100
            }));
    }

    [TestMethod]
    public async Task ConvertToPdf_ThrowsArgumentException_WhenJpegQualitySetWithoutJpegMode()
    {
        var input  = CreateMinimalDocx("source.docx");
        var output = Path.Combine(_tempRoot, "out.pdf");

        await AssertThrowsAsync<ArgumentException>(
            () => _service.ConvertToPdfAsync(input, output, new DocumentConversionOptions
            {
                ImageCompression = PdfImageCompression.Default,
                JpegQuality      = 80
            }));
    }

    [TestMethod]
    public async Task ExtractImages_ThrowsFileNotFound_WhenInputMissing()
    {
        var missing = Path.Combine(_tempRoot, "nope.docx");
        var output  = Path.Combine(_tempRoot, "images.zip");

        await AssertThrowsAsync<FileNotFoundException>(
            () => _service.ExtractImagesAsync(missing, output));
    }

    [TestMethod]
    public async Task ExtractImages_ThrowsNotSupported_ForCsvInput()
    {
        var csv    = Path.Combine(_tempRoot, "data.csv");
        File.WriteAllText(csv, "a,b,c\n1,2,3");
        var output = Path.Combine(_tempRoot, "images.zip");

        await AssertThrowsAsync<NotSupportedException>(
            () => _service.ExtractImagesAsync(csv, output));
    }

    [TestMethod]
    public async Task RepairAsync_ThrowsFileNotFound_WhenInputMissing()
    {
        var missing = Path.Combine(_tempRoot, "nope.docx");
        var output  = Path.Combine(_tempRoot, "repaired.docx");

        await AssertThrowsAsync<FileNotFoundException>(
            () => _service.RepairAsync(missing, output));
    }

    [TestMethod]
    public async Task RepairAsync_ThrowsNotSupported_WhenInputExtensionUnknown()
    {
        var input = Path.Combine(_tempRoot, "file.xyz");
        File.WriteAllText(input, "dummy");
        var output = Path.Combine(_tempRoot, "repaired.docx");

        await AssertThrowsAsync<NotSupportedException>(
            () => _service.RepairAsync(input, output));
    }

    [TestMethod]
    public async Task RepairAsync_ThrowsNotSupported_WhenOutputExtensionUnknown()
    {
        var input  = CreateMinimalDocx("source.docx");
        var output = Path.Combine(_tempRoot, "repaired.xyz");

        await AssertThrowsAsync<NotSupportedException>(
            () => _service.RepairAsync(input, output));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Image extraction — uses real ZIP archives, no LibreOffice required
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExtractImages_ExtractsCorrectCount_FromDocx()
    {
        var docx   = CreateDocxWithImages("doc-with-images.docx", imageCount: 3);
        var outZip = Path.Combine(_tempRoot, "images.zip");

        await _service.ExtractImagesAsync(docx, outZip);

        Assert.IsTrue(File.Exists(outZip), "Output ZIP should exist.");
        using var zip = ZipFile.OpenRead(outZip);
        Assert.AreEqual(3, zip.Entries.Count, "Should extract exactly 3 images.");
    }

    [TestMethod]
    public async Task ExtractImages_ExtractsCorrectCount_FromPptx()
    {
        var pptx   = CreatePptxWithImages("deck-with-images.pptx", imageCount: 2);
        var outZip = Path.Combine(_tempRoot, "images.zip");

        await _service.ExtractImagesAsync(pptx, outZip);

        Assert.IsTrue(File.Exists(outZip), "Output ZIP should exist.");
        using var zip = ZipFile.OpenRead(outZip);
        Assert.AreEqual(2, zip.Entries.Count, "Should extract exactly 2 images.");
    }

    [TestMethod]
    public async Task ExtractImages_ExtractsCorrectCount_FromXlsx()
    {
        var xlsx   = CreateXlsxWithImages("sheet-with-images.xlsx", imageCount: 1);
        var outZip = Path.Combine(_tempRoot, "images.zip");

        await _service.ExtractImagesAsync(xlsx, outZip);

        Assert.IsTrue(File.Exists(outZip), "Output ZIP should exist.");
        using var zip = ZipFile.OpenRead(outZip);
        Assert.AreEqual(1, zip.Entries.Count, "Should extract exactly 1 image.");
    }

    [TestMethod]
    public async Task ExtractImages_ExtractsCorrectCount_FromOdt()
    {
        var odt    = CreateOdtWithImages("doc-with-images.odt", imageCount: 4);
        var outZip = Path.Combine(_tempRoot, "images.zip");

        await _service.ExtractImagesAsync(odt, outZip);

        Assert.IsTrue(File.Exists(outZip), "Output ZIP should exist.");
        using var zip = ZipFile.OpenRead(outZip);
        Assert.AreEqual(4, zip.Entries.Count, "Should extract exactly 4 images.");
    }

    [TestMethod]
    public async Task ExtractImages_DeduplicatesEntryNames_WhenDocxHasSameImageName()
    {
        var docx   = CreateDocxWithDuplicateImageNames("dup-names.docx");
        var outZip = Path.Combine(_tempRoot, "images.zip");

        await _service.ExtractImagesAsync(docx, outZip);

        Assert.IsTrue(File.Exists(outZip), "Output ZIP should exist.");
        using var zip = ZipFile.OpenRead(outZip);

        Assert.AreEqual(2, zip.Entries.Count, "Should contain 2 distinct entries.");

        var names = zip.Entries.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.IsTrue(names.Contains("image1.png"),   "First entry should keep original name.");
        Assert.IsTrue(names.Contains("image1_2.png"), "Duplicate should have _2 suffix.");
    }

    [TestMethod]
    public async Task ExtractImages_ThrowsInvalidOperation_WhenDocxHasNoImages()
    {
        var docx   = CreateMinimalDocx("no-images.docx");
        var outZip = Path.Combine(_tempRoot, "images.zip");

        await AssertThrowsAsync<InvalidOperationException>(
            () => _service.ExtractImagesAsync(docx, outZip));

        Assert.IsFalse(File.Exists(outZip), "Empty output ZIP should be deleted on failure.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Integration — require a working LibreOffice binary
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ConvertToPdf_ProducesValidPdf_FromDocx()
    {
        var input  = CreateMinimalDocx("source.docx");
        var output = Path.Combine(_tempRoot, "output.pdf");

        await _service.ConvertToPdfAsync(input, output);

        Assert.IsTrue(File.Exists(output), "Output PDF should be created.");
        Assert.IsTrue(IsPdfFile(output), "Output should start with %PDF- magic bytes.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ConvertToPdf_ProducesValidPdf_FromOdt()
    {
        var input  = CreateMinimalOdt("source.odt");
        var output = Path.Combine(_tempRoot, "output.pdf");

        await _service.ConvertToPdfAsync(input, output);

        Assert.IsTrue(File.Exists(output), "Output PDF should be created.");
        Assert.IsTrue(IsPdfFile(output), "Output should start with %PDF- magic bytes.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ConvertToPdf_ProducesValidPdf_FromPptx()
    {
        var input  = CreateMinimalPptx("source.pptx");
        var output = Path.Combine(_tempRoot, "output.pdf");

        await _service.ConvertToPdfAsync(input, output);

        Assert.IsTrue(File.Exists(output), "Output PDF should be created.");
        Assert.IsTrue(IsPdfFile(output), "Output should start with %PDF- magic bytes.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ConvertToPdf_ProducesValidPdf_FromXlsx()
    {
        var input  = CreateMinimalXlsx("source.xlsx");
        var output = Path.Combine(_tempRoot, "output.pdf");

        await _service.ConvertToPdfAsync(input, output);

        Assert.IsTrue(File.Exists(output), "Output PDF should be created.");
        Assert.IsTrue(IsPdfFile(output), "Output should start with %PDF- magic bytes.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ConvertToPdf_PdfA1b_ProducesNonEmptyFile()
    {
        var input  = CreateMinimalDocx("source.docx");
        var output = Path.Combine(_tempRoot, "output-pdfa.pdf");

        await _service.ConvertToPdfAsync(input, output, new DocumentConversionOptions
        {
            Variant = PdfOutputVariant.PdfA1b
        });

        Assert.IsTrue(File.Exists(output), "Output PDF/A file should be created.");
        Assert.IsTrue(IsPdfFile(output), "Output should start with %PDF- magic bytes.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ConvertToPdf_CreatesOutputDirectory_WhenItDoesNotExist()
    {
        var input    = CreateMinimalDocx("source.docx");
        var newDir   = Path.Combine(_tempRoot, "new-subdir", "nested");
        var output   = Path.Combine(newDir, "output.pdf");

        await _service.ConvertToPdfAsync(input, output);

        Assert.IsTrue(File.Exists(output), "Output PDF should be created inside the new directory.");
        Assert.IsTrue(IsPdfFile(output));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task RepairAsync_ProducesOutputFile_InPlace()
    {
        var input = CreateMinimalDocx("damaged.docx");

        await _service.RepairAsync(input, input);   // in-place

        Assert.IsTrue(File.Exists(input), "Repaired file should still exist at the same path.");
        Assert.IsTrue(new FileInfo(input).Length > 0, "Repaired file should not be empty.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ConvertToPdf_ThrowsOnCancellation()
    {
        var input  = CreateMinimalDocx("source.docx");
        var output = Path.Combine(_tempRoot, "output.pdf");
        using var cts = new CancellationTokenSource();
        cts.Cancel();   // already cancelled before we start

        await AssertThrowsAsync<OperationCanceledException>(
            () => _service.ConvertToPdfAsync(input, output, cancellationToken: cts.Token));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Minimal document factories
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a minimal well-formed DOCX file.</summary>
    private string CreateMinimalDocx(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="xml"  ContentType="application/xml"/>""" +
            """<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>""" +
            """</Types>""");

        WriteZipEntry(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "word/_rels/document.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>""");

        WriteZipEntry(zip, "word/document.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<w:document xmlns:wpc="http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas" """ +
            """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""" +
            """<w:body><w:p><w:r><w:t>Hello World</w:t></w:r></w:p></w:body></w:document>""");

        return path;
    }

    /// <summary>Creates a minimal well-formed ODT file.</summary>
    private string CreateMinimalOdt(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "mimetype", "application/vnd.oasis.opendocument.text");

        WriteZipEntry(zip, "META-INF/manifest.xml",
            """<?xml version="1.0" encoding="UTF-8"?>""" +
            """<manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0">""" +
            """<manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/>""" +
            """<manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>""" +
            """</manifest:manifest>""");

        WriteZipEntry(zip, "content.xml",
            """<?xml version="1.0" encoding="UTF-8"?>""" +
            """<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" """ +
            """xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" office:version="1.3">""" +
            """<office:body><office:text><text:p>Hello World</text:p></office:text></office:body>""" +
            """</office:document-content>""");

        return path;
    }

    /// <summary>Creates a minimal well-formed PPTX file.</summary>
    private string CreateMinimalPptx(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="xml" ContentType="application/xml"/>""" +
            """<Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>""" +
            """<Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>""" +
            """</Types>""");

        WriteZipEntry(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "ppt/_rels/presentation.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "ppt/slides/_rels/slide1.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>""");

        WriteZipEntry(zip, "ppt/presentation.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" """ +
            """xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" """ +
            """xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""" +
            """<p:sldMasterIdLst/><p:sldSz cx="9144000" cy="6858000"/><p:notesSz cx="6858000" cy="9144000"/>""" +
            """<p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst>""" +
            """</p:presentation>""");

        WriteZipEntry(zip, "ppt/slides/slide1.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" """ +
            """xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">""" +
            """<p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Hello</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld>""" +
            """</p:sld>""");

        return path;
    }

    /// <summary>Creates a minimal well-formed XLSX file.</summary>
    private string CreateMinimalXlsx(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="xml" ContentType="application/xml"/>""" +
            """<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""" +
            """<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""" +
            """</Types>""");

        WriteZipEntry(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "xl/_rels/workbook.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "xl/workbook.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" """ +
            """xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""" +
            """<sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>""" +
            """</workbook>""");

        WriteZipEntry(zip, "xl/worksheets/sheet1.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""" +
            """<sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Hello</t></is></c></row></sheetData>""" +
            """</worksheet>""");

        return path;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Image-bearing document factories
    // ─────────────────────────────────────────────────────────────────────────

    private string CreateDocxWithImages(string fileName, int imageCount)
    {
        var path = Path.Combine(_tempRoot, fileName);
        var png  = MinimalPng();

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="xml"  ContentType="application/xml"/>""" +
            """<Default Extension="png"  ContentType="image/png"/>""" +
            """<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>""" +
            """</Types>""");

        WriteZipEntry(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "word/_rels/document.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>""");

        WriteZipEntry(zip, "word/document.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""" +
            """<w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p></w:body></w:document>""");

        for (int i = 1; i <= imageCount; i++)
        {
            var entry = zip.CreateEntry($"word/media/image{i}.png", CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(png);
        }

        return path;
    }

    private string CreatePptxWithImages(string fileName, int imageCount)
    {
        var path = Path.Combine(_tempRoot, fileName);
        var png  = MinimalPng();

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="xml"  ContentType="application/xml"/>""" +
            """<Default Extension="png"  ContentType="image/png"/>""" +
            """<Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>""" +
            """</Types>""");

        WriteZipEntry(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "ppt/_rels/presentation.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>""");

        WriteZipEntry(zip, "ppt/presentation.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">""" +
            """<p:sldMasterIdLst/><p:sldSz cx="9144000" cy="6858000"/><p:notesSz cx="6858000" cy="9144000"/><p:sldIdLst/>""" +
            """</p:presentation>""");

        for (int i = 1; i <= imageCount; i++)
        {
            var entry = zip.CreateEntry($"ppt/media/image{i}.png", CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(png);
        }

        return path;
    }

    private string CreateXlsxWithImages(string fileName, int imageCount)
    {
        var path = Path.Combine(_tempRoot, fileName);
        var png  = MinimalPng();

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="xml"  ContentType="application/xml"/>""" +
            """<Default Extension="png"  ContentType="image/png"/>""" +
            """<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""" +
            """</Types>""");

        WriteZipEntry(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "xl/_rels/workbook.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>""");

        WriteZipEntry(zip, "xl/workbook.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" """ +
            """xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""" +
            """<sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>""" +
            """</workbook>""");

        for (int i = 1; i <= imageCount; i++)
        {
            var entry = zip.CreateEntry($"xl/media/image{i}.png", CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(png);
        }

        return path;
    }

    private string CreateOdtWithImages(string fileName, int imageCount)
    {
        var path = Path.Combine(_tempRoot, fileName);
        var png  = MinimalPng();

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "mimetype", "application/vnd.oasis.opendocument.text");

        WriteZipEntry(zip, "META-INF/manifest.xml",
            """<?xml version="1.0" encoding="UTF-8"?>""" +
            """<manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0">""" +
            """<manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/>""" +
            """<manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>""" +
            """</manifest:manifest>""");

        WriteZipEntry(zip, "content.xml",
            """<?xml version="1.0" encoding="UTF-8"?>""" +
            """<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" """ +
            """xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" office:version="1.3">""" +
            """<office:body><office:text><text:p>Hello</text:p></office:text></office:body>""" +
            """</office:document-content>""");

        for (int i = 1; i <= imageCount; i++)
        {
            var entry = zip.CreateEntry($"Pictures/image{i}.png", CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(png);
        }

        return path;
    }

    /// <summary>
    /// Creates a DOCX containing two images that share the same filename ("image1.png"),
    /// to verify that <see cref="DocumentService"/> deduplicates entry names.
    /// </summary>
    private string CreateDocxWithDuplicateImageNames(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        var png  = MinimalPng();

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="xml"  ContentType="application/xml"/>""" +
            """<Default Extension="png"  ContentType="image/png"/>""" +
            """<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>""" +
            """</Types>""");

        WriteZipEntry(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>""" +
            """</Relationships>""");

        WriteZipEntry(zip, "word/_rels/document.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>""");

        WriteZipEntry(zip, "word/document.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
            """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""" +
            """<w:body><w:p><w:r><w:t>Dup</w:t></w:r></w:p></w:body></w:document>""");

        // Two entries with the same leaf name inside the media folder.
        // A real DOCX wouldn't do this, but the ZIP format allows it and our
        // deduplication logic should handle it regardless.
        foreach (var entryName in new[] { "word/media/image1.png", "word/media/image1.png" })
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(png);
        }

        return path;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns a 1×1 transparent PNG — smallest valid PNG payload.</summary>
    private static ReadOnlySpan<byte> MinimalPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>Writes a text string into a new ZIP archive entry using UTF-8 encoding.</summary>
    private static void WriteZipEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Returns true when the file starts with the PDF magic bytes <c>%PDF-</c>.</summary>
    private static bool IsPdfFile(string path)
    {
        Span<byte> header = stackalloc byte[5];
        using var fs = File.OpenRead(path);
        var read = fs.Read(header);
        return read == 5 &&
               header[0] == '%' && header[1] == 'P' && header[2] == 'D' &&
               header[3] == 'F' && header[4] == '-';
    }

    /// <summary>Returns the runtime identifier for the current process architecture.</summary>
    private static string GetRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "win-x64",
                Architecture.X86   => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _                  => "win-unknown"
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _                  => "osx-unknown"
            };
        }

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64   => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            _                  => "linux-unknown"
        };
    }

    /// <summary>Asserts that the async delegate throws an exception of type <typeparamref name="T"/>.</summary>
    private static async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected {typeof(T).Name} to be thrown, but no exception was raised.");
        }
        catch (T)
        {
            // Expected — test passes.
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}");
        }
    }
}
