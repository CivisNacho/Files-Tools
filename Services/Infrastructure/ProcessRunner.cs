using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Tools.Services.Infrastructure;

/// <summary>
/// Implemented by domain exceptions that carry a process exit code so that
/// <see cref="ProcessRunner"/> can decide whether a fallback to the next candidate is appropriate
/// without resorting to reflection.
/// </summary>
internal interface IHasExitCode
{
    /// <summary>
    /// Process exit code when the process started and exited. <see langword="null"/> when the
    /// process could not be started at all.
    /// </summary>
    int? ExitCode { get; }
}

/// <summary>
/// Result returned by <see cref="ProcessRunner"/> after a process exits successfully.
/// </summary>
internal sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs a process from a prioritised candidate list (e.g. from
/// <see cref="FfmpegLocator.ResolveExecutableCandidates"/>) with unified stdout/stderr capture and
/// cancellation support. Each service supplies its own exception factory so that the correct
/// domain exception type bubbles up.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Tries each candidate in order. Falls back to the next when the current one fails to start
    /// (exit code is <see langword="null"/> in the exception) and more candidates remain.
    /// </summary>
    /// <typeparam name="TException">Domain exception type thrown by this service.</typeparam>
    /// <param name="binaryCandidates">Ordered list of executable paths to try.</param>
    /// <param name="arguments">Arguments forwarded to the process verbatim.</param>
    /// <param name="cancellationToken">Cancellation token; kills the process when triggered.</param>
    /// <param name="standardErrorLineObserver">
    /// Optional callback invoked for each stderr line while the process runs (e.g. for progress parsing).
    /// </param>
    /// <param name="exceptionFactory">
    /// Factory called with (message, binaryPath, commandLine, exitCode?, stdout, stderr) to produce the
    /// domain exception when the process fails or cannot be started.
    /// </param>
    public static async Task<ProcessRunResult> RunWithFallbackAsync<TException>(
        IReadOnlyList<string> binaryCandidates,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? standardErrorLineObserver,
        Func<string, string, string, int?, string, string, TException> exceptionFactory)
        where TException : Exception
    {
        TException? lastException = null;

        foreach (var candidate in binaryCandidates)
        {
            try
            {
                return await RunAsync(candidate, arguments, cancellationToken, standardErrorLineObserver, exceptionFactory)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TException ex) when (CanFallback(candidate, binaryCandidates, ex))
            {
                lastException = ex;
            }
        }

        throw lastException
            ?? exceptionFactory("No executable candidates were available.", string.Empty, string.Empty, null, string.Empty, string.Empty);
    }

    private static async Task<ProcessRunResult> RunAsync<TException>(
        string binaryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? standardErrorLineObserver,
        Func<string, string, string, int?, string, string, TException> exceptionFactory)
        where TException : Exception
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(binaryPath) ?? AppContext.BaseDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex)
        {
            throw exceptionFactory(
                "Failed to start process.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                null,
                string.Empty,
                ex.Message);
        }

        if (!started)
        {
            // process.Start() returning false is an OS-level failure unrelated to the binary path
            // being wrong. Pass a non-null sentinel exit code (-1) so CanFallback does NOT treat
            // this as a "binary not found" case and attempt other candidates with a misleading error.
            throw exceptionFactory(
                $"The operating system could not start process '{binaryPath}' (Process.Start returned false).",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                -1,
                string.Empty,
                string.Empty);
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
                // Best effort kill on cancellation.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                stderrBuilder.AppendLine(line);
                standardErrorLineObserver?.Invoke(line);
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);
        var stderr = stderrBuilder.ToString();

        if (process.ExitCode != 0)
        {
            throw exceptionFactory(
                "Process exited with a non-zero code.",
                binaryPath,
                FormatCommandLine(binaryPath, arguments),
                process.ExitCode,
                stdout,
                stderr);
        }

        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the failed candidate was a rooted path (not just a name)
    /// and the exception indicates the process could not be started (null exit code via
    /// <see cref="IHasExitCode"/>), so a fallback to the next candidate is warranted.
    /// </summary>
    private static bool CanFallback<TException>(string candidate, IReadOnlyList<string> candidates, TException ex)
        where TException : Exception
    {
        // Domain exceptions must implement IHasExitCode for a reliable fallback check.
        // A non-null ExitCode means the process launched and exited with an error — don't fall through.
        if (ex is IHasExitCode hasExitCode && hasExitCode.ExitCode is not null)
        {
            return false;
        }

        return candidates.Count > 1 && Path.IsPathRooted(candidate);
    }

    /// <summary>Formats a process invocation for inclusion in exception messages.</summary>
    public static string FormatCommandLine(string binaryPath, IReadOnlyList<string> arguments)
    {
        return string.Join(" ", new[] { Quote(binaryPath) }.Concat(arguments.Select(Quote)));
    }

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
