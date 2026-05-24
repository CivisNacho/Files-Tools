# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Files Tools is a WinUI (Windows App SDK) desktop application for media processing including audio transcription, subtitle generation, video editing, and document processing. The codebase uses C# 12 with .NET 8.0 targeting Windows 10+.

## Architecture

### High-Level Structure

```
Services/                    # Core business logic
  AudioTranscriptionService    # Whisper-based transcription via Whisper.net
  SubtitlesService             # Subtitle generation, styling, ASS/SRT output
  VideoProcessingService       # FFmpeg-based video operations
  AudioProcessingService       # Audio denoising and preparation
  DocumentProcessingService    # LibreOffice-based document conversion
  ...

Pages/                       # WinUI pages
  VideoEditorPage              # Video editing with subtitle support
  AudioEditorPage              # Audio transcription and subtitle generation
  DocumentEditorPage           # Document viewing and processing
  ...

Assets/Models/               # ML models (ONNX files)
  Dtln/                        # DTLN denoising model
```

### Key Architectural Patterns

#### Subtitle Styling System

The subtitle system separates **styling** from **rendering**:

1. **Styling Presets** define visual properties (fonts, colors, animations):
   - `StyledSubtitlePresets` - For standard `.ass` subtitles (SocialImpact, CleanSans, CaptionBox, BroadcastLowerThird)
   - `KaraokeSubtitlePresets` - For karaoke/word-by-word subtitles (NeonKaraoke, Punch)

2. **Rendering Pipeline**:
   - `SubtitleStylePreset` model → `KaraokeRenderPreset` (internal)
   - Colors converted to ASS BGRA hex format (`&HAABBGGRR&`)
   - `BuildKaraokeAss()` / `BuildStyledAss()` produce final `.ass` files

3. **Color Format**: SubtitleColor uses (Alpha, Red, Green, Blue) bytes, converted to BGRA in ASS files:
   ```csharp
   White: new(0, 255, 255, 255) → &H00FFFFFF&
   Orange: new(0, 255, 100, 0) → &H000064FF&
   ```

#### Service Responsibilities

- **AudioTranscriptionService**: Whisper model installation, audio preparation (16 kHz mono), transcription with word-level timing
- **SubtitlesService**: All subtitle operations - postprocessing, styling, karaoke construction, ASS/SRT/karaoke output
- **VideoProcessingService**: FFmpeg-based operations - rendering, muxing, burning subtitles
- The two services are **intentionally separated**: transcription produces raw timing; subtitles handles all styling and output formats

## Development

### Common Commands

```bash
# Build the project
dotnet build

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "Name=RenderKaraokeAss_WithPunch_UsesWhiteBaseAndOrangeHighlight"

# Run tests for a specific service
dotnet test --filter "SubtitlesServiceTests"

# Build and see warnings/errors only
dotnet build 2>&1 | grep -E "error|warning"
```

### Testing

Tests are in `Files.Tools.Tests/`. Key test classes:
- `SubtitlesServiceTests` - Subtitle generation, styling, karaoke output
- `AudioTranscriptionServiceTests` - Transcription and timing
- `VideoProcessingServiceTests` - Video rendering operations

When verifying color output in ASS files, look for style definitions like:
```
Style: Punch,Arial Black,72,&H00FFFFFF&,&H000064FF&,...
         └─────────────────────────────┬──────────────┘
            Primary (base)   Secondary (highlight)
```

## Adding New Subtitle Presets

### New Karaoke Preset

1. Add `CreateYourPreset()` method to `KaraokeSubtitlePresets` class
2. Return `SubtitleStylePreset` with:
   - Unique `Name` and `AssStyleName`
   - `PrimaryFontFamily` (distinct from other presets)
   - `FillColor` (typically white for readability)
   - `KaraokeHighlightColor` (the accent/emphasis color)
   - `PresentationAnimation` (None for instant fill, Pop for scale animation, Fade/FadePop for transitions)
3. Add public property: `public static SubtitleStylePreset YourPreset => CreateYourPreset();`
4. Update `VideoEditorPage.xaml.cs`:
   - Add enum value to `KaraokeSubtitleBasePreset`
   - Update dropdown population in `ConfigureAdvancedSubtitlesButton_Click()`
   - Add switch case in `CreateAdvancedSubtitleStylePresetFromConfiguration()`
   - Add font mapping in `GetDefaultFontFamilyForPreset()`

### New Styled (Non-Karaoke) Preset

Similar process but:
- Use `StyledSubtitlePresets` class
- Set `TextTransform` property (None, Uppercase, etc.)
- Configure `MaxCharsPerLine` and `MaxLines`
- Use `StyledSubtitleBasePreset` enum in VideoEditorPage

### Color Guidelines

- Base colors should be high-contrast (usually white)
- Accent colors should be distinct from other presets for visual differentiation
- Test colors in ASS format to ensure proper conversion:
  - Format: `&H{Alpha:X2}{Blue:X2}{Green:X2}{Red:X2}&`
  - Example: orange (R=255, G=100, B=0) → `&H000064FF&`

## Important Files and Documentation

### Core Service Files
- `Services/SubtitlesService.cs` - All subtitle operations, presets, styling
- `Services/AudioTranscriptionService.cs` - Transcription, timing, model management
- `Services/VideoProcessingService.cs` - FFmpeg operations
- `docs/subtitles-service.md` - Detailed subtitle system documentation
- `docs/audio-transcription-service.md` - Detailed transcription documentation

### UI Integration
- `Pages/VideoEditorPage.xaml.cs` - Karaoke and styled subtitle configuration
- `Pages/AudioEditorPage.xaml.cs` - Audio transcription and simple SRT generation
- Look for `ConfigureAdvancedSubtitlesButton_Click()` for preset selection logic

### Models & Records
- `SubtitleStylePreset` - Style definition with all visual properties
- `SubtitleColor` - BGRA color representation
- `SubtitlePresentationAnimation` - Animation enum (None, Fade, Pop, FadePop)
- `KaraokeSubtitlePresets` - Karaoke preset factory
- `StyledSubtitlePresets` - Styled subtitle preset factory

## Known Patterns & Best Practices

### Preset Selection Flow

1. User selects preset in UI → stored in `AdvancedSubtitlePresetConfiguration`
2. `CreateAdvancedSubtitleStylePresetFromConfiguration()` instantiates the actual `SubtitleStylePreset`
3. For karaoke: `CreateDefaultKaraokePreset()` wraps preset in internal `KaraokeRenderPreset` with proper color/font mapping
4. Rendering methods use the preset to build ASS output with correct colors and animations

### Color Application

- `FillColor` → ASS PrimaryColour (base text color)
- `KaraokeHighlightColor` → ASS SecondaryColour (active word highlight in karaoke)
- `OutlineColor` → ASS OutlineColour
- `ShadowColor` → ASS BackColour (shadow effect)

### Animation Implementation

- `SubtitlePresentationAnimation.None` → Instant fill with `\k` tags (Punch preset)
- `SubtitlePresentationAnimation.Pop` → Scale animation (112% entry, 100% finish)
- `SubtitlePresentationAnimation.Fade` → Fade-in/out
- `SubtitlePresentationAnimation.FadePop` → Combined fade and pop

## Testing Subtitle Changes

When modifying subtitle styling or adding presets:

```bash
# Run the specific test
dotnet test --filter "Name=YourTestName"

# Look at the test output for actual ASS content to verify colors/fonts
# The test failure message will show the generated ASS file content
```

Example test structure for color verification:
```csharp
StringAssert.Contains(ass, "Style: YourPreset");
StringAssert.Contains(ass, "&H00FFFFFF&");  // Verify white base
StringAssert.Contains(ass, "&H000064FF&");  // Verify orange highlight
StringAssert.Contains(ass, "YourFont");     // Verify font
```

## Project Configuration

- Target: `net8.0-windows10.0.19041.0`
- Language version: C# 12
- Nullable: enabled
- Key dependencies:
  - `Whisper.net` 1.9.0 (transcription)
  - `DevEnvy.FFmpeg.Binaries.LGPL` (video/audio processing)
  - `NetVips` 3.2.0 (image processing)
  - `Microsoft.WindowsAppSDK` 2.0.1 (WinUI framework)

LibreOffice is **not bundled** - downloaded on-demand when first needed.
