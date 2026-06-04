using System.Text.Json;
using Files_Tools.Services;
using Files_Tools.Services.Presets;

namespace Files.Tools.Tests;

[TestClass]
public class SubtitlePresetTests
{
    private string _tempRoot = null!;
    private string _builtInDir = null!;
    private string _userDir = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "files-tools-preset-tests", Guid.NewGuid().ToString("N"));
        _builtInDir = Path.Combine(_tempRoot, "builtin");
        _userDir = Path.Combine(_tempRoot, "user");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_userDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SubtitleStyleCatalog.ResetRegistrations();
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    [TestMethod]
    public void Color_RoundTrips_PreservingAssAlphaConvention()
    {
        // ASS alpha: 00 = opaque, FF = transparent. The converter must preserve all four channels.
        var dto = new SubtitlePresetDto
        {
            Id = "x",
            ShadowColor = new SubtitleColor(0xC8, 0x18, 0x20, 0x38)
        };

        var json = JsonSerializer.Serialize(dto, SubtitlePresetJsonContext.Default.SubtitlePresetDto);
        StringAssert.Contains(json, "#C8182038");

        var restored = JsonSerializer.Deserialize(json, SubtitlePresetJsonContext.Default.SubtitlePresetDto)!;
        Assert.AreEqual(dto.ShadowColor, restored.ShadowColor);
    }

    [TestMethod]
    public void Dto_RoundTrips_ThroughPresetAndBack()
    {
        // Start from a built-in preset, project it to a DTO, serialize, deserialize, map back, and
        // assert the key fields survive the trip. This locks the JSON <-> domain mapping.
        var original = KaraokeSubtitlePresets.CreateWordPop();
        var dto = new SubtitlePresetDto
        {
            Id = original.Name,
            DisplayName = "Word Pop",
            Kind = SubtitleStyleKind.Karaoke,
            AssStyleName = original.AssStyleName,
            ScriptTitle = original.ScriptTitle,
            PrimaryFontFamily = original.PrimaryFontFamily,
            FontFamilyFallbacks = original.FontFamilyFallbacks.ToList(),
            FontSize = original.FontSize,
            TextTransform = original.TextTransform,
            FillColor = original.FillColor,
            OutlineColor = original.OutlineColor,
            ShadowColor = original.ShadowColor,
            KaraokeHighlightColor = original.KaraokeHighlightColor,
            Alignment = original.Alignment,
            MaxWordsPerChunk = original.MaxWordsPerChunk,
            WrapStyle = original.WrapStyle,
            OutlineWidth = original.OutlineWidth,
            Effects = original.Effects!
                .Select(e => new SubtitleEffectDto { Kind = e.Kind, DurationMs = e.DurationMs, Scale = e.Scale })
                .ToList()
        };

        var json = JsonSerializer.Serialize(dto, SubtitlePresetJsonContext.Default.SubtitlePresetDto);
        var restoredDto = JsonSerializer.Deserialize(json, SubtitlePresetJsonContext.Default.SubtitlePresetDto)!;
        var mapped = SubtitlePresetMapper.ToPreset(restoredDto);

        Assert.AreEqual(original.Name, mapped.Name);
        Assert.AreEqual(original.AssStyleName, mapped.AssStyleName);
        Assert.AreEqual(original.PrimaryFontFamily, mapped.PrimaryFontFamily);
        CollectionAssert.AreEqual(original.FontFamilyFallbacks.ToList(), mapped.FontFamilyFallbacks.ToList());
        Assert.AreEqual(original.FontSize, mapped.FontSize);
        Assert.AreEqual(original.TextTransform, mapped.TextTransform);
        Assert.AreEqual(original.FillColor, mapped.FillColor);
        Assert.AreEqual(original.KaraokeHighlightColor, mapped.KaraokeHighlightColor);
        Assert.AreEqual(original.Alignment, mapped.Alignment);
        Assert.AreEqual(original.MaxWordsPerChunk, mapped.MaxWordsPerChunk);
        Assert.AreEqual(original.WrapStyle, mapped.WrapStyle);
        Assert.IsNotNull(mapped.Effects);
        Assert.AreEqual(original.Effects!.Count, mapped.Effects!.Count);
        Assert.AreEqual(original.Effects[1].Kind, mapped.Effects[1].Kind);
        Assert.AreEqual(original.Effects[1].Scale, mapped.Effects[1].Scale);
    }

    [TestMethod]
    public void Mapper_Throws_WhenIdMissing()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => SubtitlePresetMapper.ToPreset(new SubtitlePresetDto()));
    }

    [TestMethod]
    public void Mapper_FallsBack_DisplayNameAndAssStyleName_ToId()
    {
        var entry = SubtitlePresetMapper.ToCatalogEntry(new SubtitlePresetDto { Id = "MyStyle" });
        Assert.AreEqual("MyStyle", entry.Id);
        Assert.AreEqual("MyStyle", entry.DisplayName);
        Assert.AreEqual("MyStyle", entry.Factory().AssStyleName);
    }

    [TestMethod]
    public void Loader_MergesUserOverBuiltIn_ById()
    {
        WritePreset(_builtInDir, "a.json", "Shared", "Built-in Shared", SubtitleStyleKind.Styled);
        WritePreset(_builtInDir, "b.json", "BuiltOnly", "Built Only", SubtitleStyleKind.Styled);
        WritePreset(_userDir, "shared.json", "Shared", "User Shared", SubtitleStyleKind.Karaoke);
        WritePreset(_userDir, "c.json", "UserOnly", "User Only", SubtitleStyleKind.Karaoke);

        var loader = new SubtitlePresetLoader(_builtInDir, _userDir);
        var entries = loader.Load(out var errors);

        Assert.AreEqual(0, errors.Count);
        Assert.AreEqual(3, entries.Count);

        var shared = entries.Single(e => e.Id == "Shared");
        Assert.AreEqual("User Shared", shared.DisplayName, "User file should override the built-in by id.");
        Assert.AreEqual(SubtitleStyleKind.Karaoke, shared.Kind);

        Assert.IsTrue(entries.Any(e => e.Id == "BuiltOnly"));
        Assert.IsTrue(entries.Any(e => e.Id == "UserOnly"));
    }

    [TestMethod]
    public void Loader_SkipsMalformedFile_AndReportsError()
    {
        WritePreset(_builtInDir, "good.json", "Good", "Good", SubtitleStyleKind.Styled);
        File.WriteAllText(Path.Combine(_builtInDir, "bad.json"), "{ not valid json ");

        var loader = new SubtitlePresetLoader(_builtInDir, _userDir);
        var entries = loader.Load(out var errors);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Good", entries[0].Id);
        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains(errors[0].Path, "bad.json");
    }

    [TestMethod]
    public void Catalog_RegisterPresets_AppendsNew_AndOverridesBuiltInInPlace()
    {
        var builtInCount = SubtitleStyleCatalog.Entries.Count;
        var builtInIds = SubtitleStyleCatalog.Entries.Select(e => e.Id).ToList();

        var overrideEntry = SubtitlePresetMapper.ToCatalogEntry(
            new SubtitlePresetDto { Id = "CleanSans", DisplayName = "Overridden Clean Sans", Kind = SubtitleStyleKind.Styled });
        var newEntry = SubtitlePresetMapper.ToCatalogEntry(
            new SubtitlePresetDto { Id = "Brand New", DisplayName = "Brand New", Kind = SubtitleStyleKind.Karaoke });

        SubtitleStyleCatalog.RegisterPresets([overrideEntry, newEntry]);

        var merged = SubtitleStyleCatalog.Entries;
        Assert.AreEqual(builtInCount + 1, merged.Count, "One override replaces in place; one new entry appends.");
        Assert.AreEqual("Overridden Clean Sans", merged.Single(e => e.Id == "CleanSans").DisplayName);
        // Override keeps the built-in's position.
        Assert.AreEqual(builtInIds.IndexOf("CleanSans"), merged.ToList().FindIndex(e => e.Id == "CleanSans"));
        // New entry is appended last.
        Assert.AreEqual("Brand New", merged[^1].Id);
    }

    private static void WritePreset(string dir, string fileName, string id, string displayName, SubtitleStyleKind kind)
    {
        var dto = new SubtitlePresetDto { Id = id, DisplayName = displayName, Kind = kind };
        var json = JsonSerializer.Serialize(dto, SubtitlePresetJsonContext.Default.SubtitlePresetDto);
        File.WriteAllText(Path.Combine(dir, fileName), json);
    }
}
