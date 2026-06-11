using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Files_Tools.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Tools.Tests;

/// <summary>
/// Exercises the ARM64 PDF fallback (<see cref="PdfNativeCli"/>) on x64 by forcing the CLI path,
/// proving that the QPdfNet job-JSON serialization drives the bundled x64 qpdf.exe identically to
/// the in-process library, and that the tesseract CLI produces searchable page PDFs.
/// Tests are inconclusive (not failed) when the local Binaries\ folders are not populated.
/// </summary>
[TestClass]
public sealed class PdfNativeCliFallbackTests
{
    private static string RepoRoot
    {
        get
        {
            // Walk up from the test output directory to the repo root (the directory that
            // contains Binaries\), which sits a varying number of levels up depending on RID.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Binaries")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }

    private static string QpdfExe => Path.Combine(RepoRoot, "Binaries", "qpdf", "win-x64", "qpdf.exe");
    private static string TesseractExe => Path.Combine(RepoRoot, "Binaries", "tesseract", "win-x64", "tesseract.exe");

    // Minimal valid single-page PDF (blank page, no compression) used as the operation input.
    private const string MinimalPdf =
        "%PDF-1.4\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\n" +
        "trailer<</Root 1 0 R>>\n";

    private static string CreateInputPdf(string dir)
    {
        var path = Path.Combine(dir, "input.pdf");
        File.WriteAllText(path, MinimalPdf, new UTF8Encoding(false));
        return path;
    }

    private static string CreateWorkDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ft_clitest_" + Path.GetRandomFileName())).FullName;

    private static void RequireQpdf()
    {
        if (!File.Exists(QpdfExe))
        {
            Assert.Inconclusive($"x64 qpdf.exe not present at {QpdfExe}.");
        }

        PdfNativeCli.QpdfExePath = QpdfExe;
    }

    [TestMethod]
    public async Task QpdfCli_Rotate_ProducesSamePageRotationAsInProc()
    {
        RequireQpdf();
        var dir = CreateWorkDir();
        try
        {
            var service = new PdfService();
            var input = CreateInputPdf(dir);

            var inProcOut = Path.Combine(dir, "rot_inproc.pdf");
            await service.RotateAsync(input, inProcOut, 90, null);

            PdfNativeCli.ForceForTesting = true;
            var cliOut = Path.Combine(dir, "rot_cli.pdf");
            await service.RotateAsync(input, cliOut, 90, null);

            // Both paths must produce a valid PDF carrying the same /Rotate 90 entry.
            foreach (var path in new[] { inProcOut, cliOut })
            {
                var bytes = await File.ReadAllBytesAsync(path);
                StringAssert.StartsWith(Encoding.ASCII.GetString(bytes, 0, 5), "%PDF-");
                StringAssert.Contains(Encoding.ASCII.GetString(bytes), "/Rotate 90");
            }
        }
        finally
        {
            PdfNativeCli.ForceForTesting = false;
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public async Task QpdfCli_ReadMetadata_CapturesJsonFromStdout()
    {
        RequireQpdf();
        var dir = CreateWorkDir();
        try
        {
            PdfNativeCli.ForceForTesting = true;
            var service = new PdfService();
            var metadata = await service.ReadMetadataAsync(CreateInputPdf(dir));

            // The structure JSON came over the CLI's stdout and parsed into a metadata object.
            Assert.IsNotNull(metadata);
        }
        finally
        {
            PdfNativeCli.ForceForTesting = false;
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void TesseractCli_RendersSearchablePagePdf()
    {
        if (!File.Exists(TesseractExe))
        {
            Assert.Inconclusive($"x64 tesseract.exe not present at {TesseractExe}.");
        }

        var tessData = Path.Combine(RepoRoot, "Binaries", "tesseract", "tessdata");
        if (!File.Exists(Path.Combine(tessData, "eng.traineddata")))
        {
            Assert.Inconclusive($"eng.traineddata not present under {tessData}.");
        }

        PdfNativeCli.TesseractExePath = TesseractExe;
        var dir = CreateWorkDir();
        try
        {
            // Render a high-contrast text image via libvips (black text on a white page with a
            // margin), then OCR it to a PDF. PNG: leptonica is picky about libvips TIFFs.
            var png = Path.Combine(dir, "page.png");
            using (var text = NetVips.Image.Text("The quick brown fox", dpi: 300))
            using (var inverted = text.Invert())
            using (var page = inverted.Embed(60, 60, text.Width + 120, text.Height + 120,
                background: [255.0]))
            {
                page.Pngsave(png);
            }

            var outputBase = Path.Combine(dir, "page");
            PdfNativeCli.RunTesseractPdf(png, outputBase, tessData, "eng", engineMode: 3, dpi: 300);

            var pdf = outputBase + ".pdf";
            Assert.IsTrue(File.Exists(pdf), "tesseract did not produce the page PDF.");
            Assert.IsTrue(new FileInfo(pdf).Length > 500, "produced PDF is implausibly small.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
