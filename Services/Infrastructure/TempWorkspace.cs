using System;
using System.IO;

namespace Files_Tools.Services.Infrastructure;

/// <summary>
/// Creates isolated per-job temporary directories under the system temp folder using the
/// <c>files-tools-*</c> naming convention. Each call returns a new unique path.
/// </summary>
internal static class TempWorkspace
{
    /// <summary>
    /// Creates a new unique temporary directory under <c>%TEMP%\{appSubfolder}\{guid}</c>,
    /// ensures the directory exists, and returns its full path.
    /// </summary>
    /// <param name="appSubfolder">
    /// Service-specific subfolder name, e.g. <c>"files-tools-audio"</c>.
    /// </param>
    public static string CreateDirectory(string appSubfolder)
    {
        var path = Path.Combine(Path.GetTempPath(), appSubfolder, Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
}
