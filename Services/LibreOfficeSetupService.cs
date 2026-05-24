using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Progress types
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
// Service
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
        Path.Combine(InstallRoot, GetCurrentRid());

    /// <summary>
    /// Full path to the LibreOffice entry-point binary.
    /// <c>soffice.exe</c> on Windows, <c>soffice</c> on Unix.
    /// The file exists when <see cref="IsAvailable"/> returns <c>true</c>.
    /// </summary>
    public static string ExecutablePath =>
        Path.Combine(InstallDirectory, "program",
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "soffice.exe" : "soffice");

    /// <summary>
    /// Returns <c>true</c> when the LibreOffice executable has been downloaded and is ready.
    /// </summary>
    public static bool IsAvailable => File.Exists(ExecutablePath);

    /// <summary>
    /// Approximate installer size in bytes for the current platform.
    /// Used as a progress denominator before the HTTP Content-Length header is received.
    /// </summary>
    public static long EstimatedDownloadBytes => GetEstimatedBytes(GetCurrentRid());

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
        var rid = GetCurrentRid();

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
            TryDeleteDirectory(InstallDirectory);
            throw;
        }
        finally
        {
            TryDeleteFile(msiPath);
            TryDeleteDirectory(extractDir);
        }
    }

    /// <summary>
    /// Removes the downloaded LibreOffice installation from <see cref="InstallDirectory"/>.
    /// Safe to call when LibreOffice has not been downloaded yet.
    /// </summary>
    public static void Remove() => TryDeleteDirectory(InstallDirectory);

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
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "soffice.exe" : "soffice";
        foreach (var soffice in Directory.EnumerateFiles(extractDir, exeName, SearchOption.AllDirectories))
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

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
