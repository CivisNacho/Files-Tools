using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Files_Tools.Services.Presets;

/// <summary>
/// Loads subtitle presets from JSON files and merges them into catalog entries.
///
/// Two sources are merged, in order:
/// <list type="number">
///   <item>built-in presets shipped under <c>Assets/Presets/*.json</c> (read-only), and</item>
///   <item>user presets under <c>%LOCALAPPDATA%\FilesTools\Presets\*.json</c>.</item>
/// </list>
/// When a user file declares the same id as a built-in, the user file wins (last-writer-wins by id),
/// so a user can override a shipped look without editing the app.
///
/// This type deliberately uses plain <see cref="System.IO"/> + <see cref="Environment"/> rather than
/// WinRT <c>ApplicationData</c>, so it is testable and works whether the app runs packaged or not.
/// </summary>
public sealed class SubtitlePresetLoader
{
    private readonly string _builtInDirectory;
    private readonly string _userDirectory;

    public SubtitlePresetLoader()
        : this(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Presets"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesTools", "Presets"))
    {
    }

    /// <summary>Test-friendly constructor that targets explicit directories.</summary>
    public SubtitlePresetLoader(string builtInDirectory, string userDirectory)
    {
        _builtInDirectory = builtInDirectory ?? throw new ArgumentNullException(nameof(builtInDirectory));
        _userDirectory = userDirectory ?? throw new ArgumentNullException(nameof(userDirectory));
    }

    /// <summary>
    /// Reads and parses every preset file from both directories and returns the merged catalog
    /// entries in a stable order: built-ins first (by file name), then any user-only presets.
    /// Files that fail to parse or are missing required fields are skipped and surfaced via
    /// <paramref name="errors"/> rather than aborting the whole load.
    /// </summary>
    public IReadOnlyList<SubtitleStyleCatalogEntry> Load(out IReadOnlyList<SubtitlePresetLoadError> errors)
    {
        var collectedErrors = new List<SubtitlePresetLoadError>();
        var byId = new Dictionary<string, SubtitleStyleCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var path in EnumeratePresetFiles(_builtInDirectory))
        {
            AddOrReplace(path, byId, order, collectedErrors);
        }

        foreach (var path in EnumeratePresetFiles(_userDirectory))
        {
            AddOrReplace(path, byId, order, collectedErrors);
        }

        errors = collectedErrors;
        var result = new List<SubtitleStyleCatalogEntry>(order.Count);
        foreach (var id in order)
        {
            result.Add(byId[id]);
        }

        return result;
    }

    /// <summary>Convenience overload that discards parse errors.</summary>
    public IReadOnlyList<SubtitleStyleCatalogEntry> Load() => Load(out _);

    private static IEnumerable<string> EnumeratePresetFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static void AddOrReplace(
        string path,
        Dictionary<string, SubtitleStyleCatalogEntry> byId,
        List<string> order,
        List<SubtitlePresetLoadError> errors)
    {
        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, SubtitlePresetJsonContext.Default.SubtitlePresetDto);
            if (dto is null)
            {
                errors.Add(new SubtitlePresetLoadError(path, "Preset file deserialized to null."));
                return;
            }

            var entry = SubtitlePresetMapper.ToCatalogEntry(dto);
            if (!byId.ContainsKey(entry.Id))
            {
                order.Add(entry.Id);
            }

            byId[entry.Id] = entry;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            errors.Add(new SubtitlePresetLoadError(path, ex.Message));
        }
    }
}

/// <summary>A preset file that could not be loaded, with the reason. Used for diagnostics.</summary>
public sealed record SubtitlePresetLoadError(string Path, string Message);
