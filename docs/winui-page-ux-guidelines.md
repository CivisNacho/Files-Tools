# WinUI Page UX Guidelines

This document defines the page-level UX patterns we use for editor and tool pages in this project.
It exists so new pages follow the same structure, interaction model, and WinUI visual language as the current image and video editors.

## Reference Pages

Use these pages as the primary implementation references:

- `Pages/ImageEditorPage.xaml`
- `Pages/ImageEditorPage.xaml.cs`
- `Pages/VideoEditorPage.xaml`
- `Pages/VideoEditorPage.xaml.cs`

## External WinUI Guidance

These project rules align with the following WinUI guidance:

- Layering: <https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/layering>
- Color: <https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color>
- Layout: <https://learn.microsoft.com/en-us/windows/apps/design/layout/>

## Core Page Structure

Every new tool page should use the same high-level shell:

1. A scrollable page root.
2. A centered content container with a max width.
3. A page header with title and short supporting description.
4. A two-panel editor layout:
   - left: preview or primary working surface
   - right: options and settings
5. A window-level, left-side navigation rail hosted in `MainWindow` for editor option grouping.
6. A responsive collapse path for narrower widths.

Recommended structure:

- `ScrollViewer`
- `Grid x:Name="PageRoot"` with outer page padding
- `Grid x:Name="ContentRoot"` with `HorizontalAlignment="Center"` and `MaxWidth="1440"`
- `Grid x:Name="EditorGrid"` with left and right columns
- `Border x:Name="PreviewSection"`
- `Border x:Name="OptionsPanel"`
- `NavigationView x:Name="ImageEditorOptionsNavigationView"` in `MainWindow.xaml`

## Layout Rules

### Page spacing

Use these as the baseline unless the page has a strong reason to differ:

- page padding: `32,24,32,28`
- major vertical spacing: `20`
- editor grid column spacing: `20`
- card padding: `16`
- section padding: `20`
- surface corner radius: `14` or `16`

### Width and alignment

- `ContentRoot` should be centered and use `MaxWidth="1440"`.
- `PreviewSection` should use `VerticalAlignment="Top"` so it fits its content instead of stretching full height.
- The preview side should feel visually dominant. Current pages use a roughly `7:3` or `8:4` left/right ratio.

### Responsive behavior

For narrow widths, move the options panel below the preview instead of trying to compress both columns too aggressively.

The existing editor pattern is:

- wide layout: preview left, options right
- narrow layout: preview first, options below

When implementing a new page, add a page-size handler and a helper similar to:

- `PageRoot_SizeChanged(...)`
- `ApplyResponsiveLayout(double width)`

## Layering Rules

Use WinUI layering rather than heavy custom coloring.

Preferred brushes:

- outer cards: `{ThemeResource CardBackgroundFillColorDefaultBrush}`
- inner working surfaces: `{ThemeResource LayerFillColorDefaultBrush}`
- borders: `{ThemeResource CardStrokeColorDefaultBrush}`
- accent highlights only for selected or active states: `{ThemeResource AccentFillColorDefaultBrush}`

Avoid:

- large custom solid backgrounds when a theme brush fits
- repeated bright accent outlines around every group
- deep nested borders that make the page feel busy

## Typography Rules

Use consistent hierarchy:

- page title: `38`, `SemiBold`
- section titles like `Preview` or `Options`: `24`, `SemiBold`
- subpanel titles like `Format and codecs`: `18`, `SemiBold`
- supporting copy: normal body text with reduced opacity, usually `0.72` to `0.78`

Copy should be short and task-oriented.
Describe what the user can do in the page, not the underlying implementation.

## Shared Card Pattern

Option groups should be placed inside a shared style similar to `SettingsCardStyle`.

Baseline card style:

- `Padding="16"`
- `CornerRadius="12"`
- `BorderThickness="1"`
- `BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"`
- `Background="{ThemeResource LayerFillColorDefaultBrush}"`
- `Margin="0,0,0,12"`

Use one card per logical group.
Examples:

- format and codecs
- resize
- crop
- subtitles
- repair
- output folder

Do not put all controls into one giant settings stack without grouping.

## Color Schema

Use color intentionally and sparingly so the app stays WinUI-native rather than decorative.

### Primary action color

- Use a shared blue accent for the main apply/action button on editor pages.
- Current reference value: `#3B82F6` with a darker companion `#2563EB`.
- Apply buttons should use white text on the accent background.
- Keep this color consistent across the main editor flows unless there is a strong reason to differentiate a page.

### Category accents

Use soft tinted surfaces and matching glyph colors for high-level categories or empty-state hints:

- images: blue tint
- video: teal tint
- documents: amber tint
- audio: purple tint

### Usage rules

- Prefer tinted backgrounds behind built-in WinUI icons instead of replacing the icons with custom artwork.
- Use the accent color on only a few important surfaces: main action buttons, landing cards, and prominent empty states.
- Avoid giving every control its own color. Secondary buttons and standard inputs should stay mostly theme-driven.
- Validation text should remain a warning color and not compete with the primary accents.

## Preview Section Rules

The preview area is the primary work surface and should follow a stable pattern.

### Preview section anatomy

`PreviewSection` should usually contain:

1. a title and helper text
2. an upload/drop surface
3. a bottom action row with status on the left and the main action button on the right
4. optional progress or task state below the action row

### Preview section sizing

- `PreviewSection` should fit content vertically using `VerticalAlignment="Top"`.
- `UploadSurface` should use `HorizontalAlignment="Stretch"` and `VerticalAlignment="Top"`.
- `UploadSurface` should have a bounded but flexible size.
- Do not make the preview area follow the full height of the options panel.

Typical bounds used in current pages:

- `MinHeight="240"`
- `MaxHeight="560"`
- `Padding="16"`
- `CornerRadius="14"`

### Drag and drop

Drag and drop should look and behave the same across pages.

Required behaviors:

- entire preview surface is clickable to open picker
- entire preview surface accepts file drop
- centered empty-state hint when no file is loaded
- loaded preview replaces the drop hint cleanly

Recommended empty-state pattern:

- centered icon
- short strong title like `Drop an image here` or `Drop a video here`
- one paragraph explaining click-to-browse and key available edits

The empty-state hint should be centered and width-limited, usually with `MaxWidth="360"`.

## Navigation Rail Rules

The canonical pattern for editor option switching is now a `NavigationView` integrated at the window/title shell level (similar to WinUI Gallery), not a page-local `ListBox`.

### Placement and ownership

- Host the rail in `MainWindow.xaml` as `ImageEditorOptionsNavigationView`.
- Place it in a dedicated left column that spans title/content rows.
- Show it only for pages that need editor option grouping (for example `ImageEditorPage`).
- Keep page content itself as a 2-column layout: preview left, options right.

### Interaction model

- Use hierarchical `NavigationViewItem` nodes for grouping.
- Parent items represent domains (for example `Media`, `Transform`, `Adjust`).
- Child items represent concrete option panels (for example `Format`, `Crop`, `Resize`).
- Do not auto-select a default option when entering the page.
- Wait for explicit user selection before showing section-specific content.

### Icon rules

- Every `NavigationViewItem` must have a meaningful WinUI icon.
- Parent and child icons should communicate action intent, not decoration.
- Avoid empty icon slots.

## Options Panel Rules

The options panel is secondary to the preview.
It should feel structured, scannable, and consistent across pages.

### Options panel anatomy

`OptionsPanel` should usually contain:

1. title and helper text
2. a selected-section header (for example `Media`, `Transform`)
3. a scrollable content area with grouped cards

### Section selector

Use the window-level `NavigationView` rail for top-level and subgroup selection.

Current pattern:

- `Media -> Format`
- `Transform -> Crop / Resize / Upscale / Rotate`
- `Adjust -> Color`

Rules:

- keep labels short
- keep hierarchy shallow and concrete
- route selections to the page via explicit tag keys (for example `Transform:Resize`)
- options content should switch panels without changing the panel width

### Content rules

- Keep related controls together in a single card.
- Use clear headers.
- Keep validation messages near the relevant section.
- Prefer enabling and disabling related controls over hiding too much state.
- Keep `OptionsPanel` width stable while switching sections.
- Prefer fixed `MinWidth` plus responsive breakpoint logic over content-measured width.

Example already used in this project:

- repair sub-options are dimmed and disabled when `EnableRepairCheckBox` is off

### Subgroup selector

Subgroup selection should be handled through child `NavigationViewItem` entries instead of nested page-local selectors.

Implementation pattern:

- one window-level `NavigationView` hierarchy
- one top-level `StackPanel` per major section in `OptionsPanel`
- one child `StackPanel` per subgroup
- page methods such as `ApplyOptionSelection(...)`, `ShowMediaSubgroup(...)`, `ShowTransformSubgroup(...)`

Reference implementations:

- `Pages/VideoEditorPage.xaml`
- `Pages/VideoEditorPage.xaml.cs`
- `Pages/ImageEditorPage.xaml`
- `Pages/ImageEditorPage.xaml.cs`

## Long-Running Operation Rules

If a page starts a background job such as image processing, video processing, conversion, or export, it should expose task state directly in the preview section.

### Placement

Show progress at the bottom of `PreviewSection`, below the action row.
This keeps status close to the work surface and avoids detached status elsewhere in the page.

### Required progress UI

Use a compact stack with:

- current task label
- ETA text on the right
- progress bar
- detail text such as processed time or current phase

### Behavior

- show progress only while work is active
- disable the main apply button while the job is running
- reset the progress area after success or failure unless there is a strong reason to preserve the finished state
- use real service progress when available instead of a fake timer

### Video-specific rule

For FFmpeg-backed pages, parse and surface actual encoding progress instead of estimating only from wall time.

## Media-Specific Interaction Rules

### Image pages

Image editing pages may place direct manipulation overlays inside the preview area when the operation is spatial.
Examples:

- crop rectangle
- drag handles
- preview overlays

Rules:

- overlays should remain inside the preview bounds
- handles must remain visible on top of the preview
- preview layout should not jump when overlays appear

### Video pages

Video pages may place timeline or trim controls inside the preview area when the operation is preview-driven.
Examples:

- trim handles below the player
- range highlight
- preview seek while dragging

Rules:

- the media player must stay inside `UploadSurface`
- trim controls appear only when relevant state is enabled and media metadata is ready
- validation must use the same backing state that the drag UI updates

## Validation Rules

Every page should have clear local validation.

Rules:

- keep validation scoped to the section where the issue belongs
- disable the main action button when required inputs are missing or invalid
- use short actionable messages
- do not rely on service exceptions as the main user-facing validation layer

Validation should be refreshed after:

- file load
- option changes
- section toggles
- direct-manipulation updates that affect export state

## Interaction Rules

- Primary actions belong at the bottom-right of the preview section.
- File or selection status belongs on the left side of that same row.
- The preview surface itself should handle browse interaction where practical.
- Avoid placing duplicate upload buttons in the preview header unless the page has a strong accessibility or workflow reason.

## Naming Conventions

Use these names when the page follows the shared editor pattern:

- `PageRoot`
- `ContentRoot`
- `EditorGrid`
- `PreviewSection`
- `UploadSurface`
- `PreviewHost`
- `DropHintPanel`
- `OptionsPanel`
- `ImageEditorOptionsNavigationView` (window-level)
- `SelectedOptionHeaderTextBlock`
- `ApplyButton`

Using the same names makes it easier to:

- copy known-good behavior between pages
- share documentation and review comments
- recognize layout responsibilities quickly

## New Page Checklist

When building a new page, confirm all of the following:

1. `ContentRoot` is centered and width-bounded.
2. `PreviewSection` uses `VerticalAlignment="Top"`.
3. `OptionsPanel` uses the same card and spacing language as the editor pages.
4. `ImageEditorOptionsNavigationView` is hosted in `MainWindow` and visible only on relevant editor pages.
5. Navigation uses hierarchical items with meaningful icons on every item.
6. Drag and drop uses the full preview surface.
7. Empty state uses centered icon, title, and helper text.
8. Main action button sits bottom-right of the preview section.
9. Status text sits bottom-left of the preview section.
10. Long-running operations show progress and ETA in the preview section.
11. Narrow-width layout moves options below the preview.
12. `OptionsPanel` width remains stable while switching navigation options.
13. Validation is section-local and disables the main action when needed.
14. Theme brushes are used instead of custom hard-coded panel colors where possible.

## When to Deviate

These rules are the default, not a prison.
A page can deviate if:

- the task is not preview-driven
- the page needs a more document-style flow
- the page is a wizard rather than an editor
- the interaction model would become worse if forced into the editor pattern

If a page deviates, document the reason in the PR or in a short code comment near the unusual layout logic.

## Recommendation for Future Pages

Before creating a new editor page, start by copying the shell structure from either:

- `Pages/ImageEditorPage.xaml` for image-like direct manipulation pages
- `Pages/VideoEditorPage.xaml` for media-preview and progress-driven pages

Then rename controls and remove unused cards rather than building the shell from scratch.
