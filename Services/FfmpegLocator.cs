using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Files_Tools.Services;

/// <summary>
/// Central resolver for FFmpeg and FFprobe executables.
/// <para>
/// Resolution order per executable:
/// <list type="number">
///   <item>Bundled binary — DevEnvy.FFmpeg.Binaries.LGPL NuGet package for win-x64, linux-x64,
///         linux-arm64, osx-x64 and osx-arm64; the <c>Binaries\ffmpeg\win-arm64\</c> project
///         folder (copied by the <c>CopyFfmpegArm64Binaries</c> build target) for win-arm64.</item>
///   <item>Executable name only — resolved through the system PATH.</item>
/// </list>
/// </para>
/// <para>
/// The first candidate that passes a quick <c>-version</c> probe is cached for the lifetime
/// of the process so that subsequent calls pay no I/O cost.
/// </para>
/// </summary>
internal static class FfmpegLocator
{
    private static readonly ConcurrentDictionary<string, string> VerifiedExecutableCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns an ordered list of candidate executable paths to try for the given tool name
    /// (e.g. <c>"ffmpeg"</c> or <c>"ffprobe"</c>, without extension).
    /// <para>
    /// When a previously verified candidate is cached the list contains only that single entry.
    /// When no candidate has been verified yet, the list is pre-filtered to the first launchable
    /// one if possible; otherwise all candidates are returned so the caller can surface the right
    /// error message.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ResolveExecutableCandidates(string executableNameWithoutExtension)
    {
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? executableNameWithoutExtension + ".exe"
            : executableNameWithoutExtension;

        if (VerifiedExecutableCache.TryGetValue(executableName, out var cachedExecutable))
            return [cachedExecutable];

        var rid = GetCurrentRid();
        var candidates = new List<string>(2);

        // 1. Bundled binary — DevEnvy.FFmpeg.Binaries.LGPL for most platforms;
        //    Binaries\ffmpeg\win-arm64\ (copied by the CopyFfmpegArm64Binaries build target)
        //    for win-arm64.
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", rid, executableName);
        if (File.Exists(bundledPath))
            candidates.Add(bundledPath);

        // 2. System PATH fallback.
        candidates.Add(executableName);

        foreach (var candidate in candidates)
        {
            if (!CanLaunchExecutable(candidate))
                continue;

            VerifiedExecutableCache[executableName] = candidate;
            return [candidate];
        }

        // No candidate passed the probe — return all of them so the caller can attempt each and
        // surface a meaningful error (e.g. "ffmpeg not found" vs "ffmpeg exited with code 1").
        return candidates;
    }

    /// <summary>Returns the .NET runtime identifier for the current process.</summary>
    public static string GetCurrentRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "win-x64",
                Architecture.X86   => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported Windows architecture '{RuntimeInformation.ProcessArchitecture}'.")
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported macOS architecture '{RuntimeInformation.ProcessArchitecture}'.")
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported Linux architecture '{RuntimeInformation.ProcessArchitecture}'.")
            };

        throw new PlatformNotSupportedException("Current operating system is not supported.");
    }

    /// <summary>
    /// Runs the binary with <c>-version</c> and returns <c>true</c> if it exits with code 0
    /// within 5 seconds. Silently catches all exceptions so discovery never throws.
    /// </summary>
    private static bool CanLaunchExecutable(string binaryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = binaryPath,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = Path.IsPathRooted(binaryPath)
                ? (Path.GetDirectoryName(binaryPath) ?? AppContext.BaseDirectory)
                : AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("-version");

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return false;

            process.WaitForExit(5_000);

            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
