using Windows.ApplicationModel.Resources;

namespace Files_Tools.Helpers;

/// <summary>
/// Thin wrapper around <see cref="ResourceLoader"/> for use in code-behind.
/// Call <c>Strings.Get("Key")</c> instead of hard-coding English text.
/// </summary>
internal static class Strings
{
    private static readonly ResourceLoader _loader = new();

    /// <summary>Returns the localised string for <paramref name="key"/>,
    /// falling back to <paramref name="key"/> itself when the resource is missing.</summary>
    public static string Get(string key)
    {
        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    /// <summary>
    /// Returns the localised nav-section label for a tag segment such as
    /// "Media", "Transform", "Adjust", "Organization", "Security", etc.
    /// These map to the <c>Nav{tag}.Content</c> resources already used by XAML x:Uid.
    /// </summary>
    public static string GetNavLabel(string sectionTag) =>
        Get($"Nav{sectionTag}.Content");
}
