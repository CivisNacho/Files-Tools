using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using QPdfNet;

namespace Files_Tools.Services;

/// <summary>
/// ARM64 fallback for the PDF native libraries. QPdfNet and TesseractOCR only ship x86/x64
/// in-process DLLs, which an ARM64 process cannot load. Windows on ARM runs whole x64
/// *processes* fine under emulation, so on ARM64 the same operations are routed through the
/// official x64 CLI tools bundled under <c>qpdf\win-x64\</c> and <c>tesseract\win-x64\</c>
/// (populated from <c>Binaries\</c> by the csproj copy targets, mirroring the win-arm64
/// FFmpeg arrangement):
/// <list type="bullet">
///   <item>qpdf jobs are serialized to qpdf's job-JSON (the exact format QPdfNet feeds to
///   <c>qpdfjob_initialize_from_json</c>) and run via <c>qpdf.exe --job-json-file</c>.</item>
///   <item>OCR runs <c>tesseract.exe</c> with its PDF renderer per page.</item>
/// </list>
/// </summary>
internal static class PdfNativeCli
{
    /// <summary>Test hook: forces the CLI path on x64 so the fallback can be exercised in tests.</summary>
    internal static bool ForceForTesting { get; set; }

    /// <summary>Whether PDF operations must go through the x64 CLI tools.</summary>
    public static bool UseCli =>
        ForceForTesting || RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    public static string QpdfExePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "qpdf", "win-x64", "qpdf.exe");

    public static string TesseractExePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "tesseract", "win-x64", "tesseract.exe");

    /// <summary>
    /// Serializes a QPdfNet <see cref="Job"/> to qpdf job-JSON using the same serializer settings
    /// QPdfNet's own <c>InternalRun</c> uses, so the CLI sees an identical job description.
    /// </summary>
    public static string SerializeJob(Job job)
    {
        var settings = new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };
        settings.Converters.Add(new StringEnumConverter());
        return JsonConvert.SerializeObject(job, settings);
    }

    /// <summary>
    /// Runs <c>qpdf.exe --job-json-file</c> for the given job and returns the raw stdout bytes
    /// (qpdf writes <c>--json</c>/<c>--show-attachment</c> payloads to stdout) plus the exit code.
    /// Exit codes match QPdfNet's <c>ExitCode</c> enum (0 = success, 3 = warnings, file processed).
    /// </summary>
    public static (int ExitCode, byte[] StdOut, string StdErr) RunQpdfJob(string jobJson)
    {
        EnsureExists(QpdfExePath, "qpdf");
        var jsonPath = Path.Combine(Path.GetTempPath(), $"ft_qpdfjob_{Guid.NewGuid():N}.json");
        // qpdf's JSON parser rejects a UTF-8 BOM — write without one.
        File.WriteAllText(jsonPath, jobJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            return RunProcess(QpdfExePath, ["--job-json-file=" + jsonPath]);
        }
        finally
        {
            try { File.Delete(jsonPath); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Runs <c>tesseract.exe</c> on a rasterized page image with the PDF renderer, producing
    /// <c><paramref name="outputBase"/>.pdf</c> — the CLI equivalent of the in-process
    /// Engine + PdfRenderer path.
    /// </summary>
    public static void RunTesseractPdf(
        string imagePath, string outputBase, string tessDataPath, string languages, int engineMode, int dpi)
    {
        EnsureExists(TesseractExePath, "tesseract");
        // The PDF renderer is enabled via -c rather than the 'pdf' config name: config files live
        // in tessdata\configs, which the app's downloaded tessdata (bare .traineddata files)
        // doesn't have — and a missing config only logs to stderr while still exiting 0.
        var (exitCode, _, stdErr) = RunProcess(TesseractExePath,
        [
            imagePath, outputBase,
            "--tessdata-dir", tessDataPath,
            "-l", languages,
            "--oem", engineMode.ToString(),
            "--dpi", dpi.ToString(),
            "-c", "tessedit_create_pdf=1",
        ]);

        if (exitCode != 0)
        {
            throw new PdfOperationException($"tesseract exited with code {exitCode}: {Tail(stdErr)}");
        }

        if (!File.Exists(outputBase + ".pdf"))
        {
            throw new PdfOperationException(
                $"tesseract did not produce '{outputBase}.pdf': {Tail(stdErr)}");
        }
    }

    private static (int ExitCode, byte[] StdOut, string StdErr) RunProcess(string exePath, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi)
            ?? throw new PdfOperationException($"Failed to start '{exePath}'.");

        // Read stdout as raw bytes (it can be binary PDF/attachment data) while draining
        // stderr on another thread so neither pipe blocks the process.
        var stdErrTask = process.StandardError.ReadToEndAsync();
        using var stdOut = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(stdOut);
        process.WaitForExit();
        return (process.ExitCode, stdOut.ToArray(), stdErrTask.GetAwaiter().GetResult());
    }

    private static void EnsureExists(string path, string tool)
    {
        if (!File.Exists(path))
        {
            throw new PdfOperationException(
                $"The bundled x64 {tool} tool was not found at '{path}'. " +
                "It is required for PDF operations on ARM64 builds.");
        }
    }

    private static string Tail(string s)
    {
        s = s.Trim();
        return s.Length <= 400 ? s : s[^400..];
    }
}
