using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Files_Tools.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Microsoft.UI.Xaml.Navigation;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace Files_Tools.Pages
{
    public sealed partial class ImageEditorPage : Page
    {
        private const double MinimumTwoColumnBreakpoint = 980;
        private const double ContentMaxWidth = 1440;
        private const double OuterHorizontalPadding = 64;
        private const double WideColumnSpacing = 18;
        private const double OptionsColumnRatio = 0.3;
        private const double OptionsPanelMinimumWidth = 360;
        private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
        private int? _originalImageWidth;
        private int? _originalImageHeight;
        private int? _previewImageWidth;
        private int? _previewImageHeight;
        private (int Left, int Top, int Width, int Height)? _committedCropPixels;
        private StorageFile? _sourceImageFile;
        private bool _syncingResizeDimensions;
        private bool _syncingUpscaleDimensions;
        private bool _updatingLivePreview;
        private const double MinimumCropDisplaySize = 24;
        private Rect _displayedImageRect;
        private Rect _cropRect;
        private Rect _dragStartCropRect;
        private Point _dragStartPoint;
        private CropDragMode _cropDragMode = CropDragMode.None;
        private readonly IImageProcessingService _imageProcessingService = new ImageProcessingService();

        private enum CropDragMode
        {
            None,
            Move,
            Left,
            Top,
            Right,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        public ImageEditorPage()
        {
            InitializeComponent();
            UpdateOptionUiState();
            RefreshValidation();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is FileNavigationRequest navigationRequest &&
                IsSupportedImageFile(navigationRequest.File))
            {
                await LoadImagePreviewAsync(navigationRequest.File);
            }
        }

        public bool IsCropEnabled => EnableCropCheckBox?.IsChecked ?? false;

        public void CancelCrop()
        {
            if (!(EnableCropCheckBox?.IsChecked ?? false))
            {
                return;
            }

            EnableCropCheckBox.IsChecked = false;
        }

        private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
        }

        private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && (EnableCropCheckBox?.IsChecked ?? false))
            {
                e.Handled = true;
                await CommitCurrentCropAsync();
            }
            else if (e.Key == VirtualKey.Escape && (EnableCropCheckBox?.IsChecked ?? false))
            {
                e.Handled = true;
                CancelCrop();
            }
        }

        private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCropOverlayForCurrentPreview();
        }

        private void ApplyResponsiveLayout(double width)
        {
            var isNarrow = ShouldUseSingleColumnLayout(width);

            if (isNarrow)
            {
                LeftColumn.Width = new GridLength(1, GridUnitType.Star);
                RightColumn.Width = new GridLength(0);
                Grid.SetColumn(PreviewSection, 0);
                Grid.SetRow(PreviewSection, 0);
                Grid.SetColumn(OptionsPanel, 0);
                Grid.SetRow(OptionsPanel, 2);
                Grid.SetRowSpan(OptionsPanel, 1);
                EditorGrid.ColumnSpacing = 0;
                EditorGrid.RowSpacing = 14;
                return;
            }

            LeftColumn.Width = new GridLength(7, GridUnitType.Star);
            RightColumn.Width = new GridLength(3, GridUnitType.Star);
            Grid.SetColumn(PreviewSection, 0);
            Grid.SetRow(PreviewSection, 0);
            Grid.SetColumn(OptionsPanel, 1);
            Grid.SetRow(OptionsPanel, 0);
            Grid.SetRowSpan(OptionsPanel, 3);
            EditorGrid.ColumnSpacing = 18;
            EditorGrid.RowSpacing = 14;
        }

        private bool ShouldUseSingleColumnLayout(double width)
        {
            if (width < MinimumTwoColumnBreakpoint)
            {
                return true;
            }

            var contentWidth = Math.Min(ContentMaxWidth, Math.Max(0, width - OuterHorizontalPadding));
            var availableOptionsWidth = Math.Max(0, (contentWidth * OptionsColumnRatio) - WideColumnSpacing);
            return availableOptionsWidth < OptionsPanelMinimumWidth;
        }

        private async void UploadSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (PreviewImage.Source is not null)
            {
                return;
            }

            await PickImageFromExplorerAsync();
        }

        private void UploadSurface_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop image to load";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void UploadSurface_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            var imageFile = items.OfType<StorageFile>().FirstOrDefault(IsSupportedImageFile);

            if (imageFile is null)
            {
                return;
            }

            await LoadImagePreviewAsync(imageFile);
        }

        private async Task PickImageFromExplorerAsync()
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var picker = new FileOpenPicker();
            foreach (var extension in SupportedExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null)
            {
                return;
            }

            await LoadImagePreviewAsync(selectedFile);
        }

        private async Task LoadImagePreviewAsync(StorageFile file)
        {
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            _originalImageWidth = (int)decoder.OrientedPixelWidth;
            _originalImageHeight = (int)decoder.OrientedPixelHeight;
            _previewImageWidth = _originalImageWidth;
            _previewImageHeight = _originalImageHeight;
            _committedCropPixels = null;
            _sourceImageFile = file;
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            DropHintPanel.Visibility = Visibility.Collapsed;
            ResetCropState();
            LoadedImageInfoTextBlock.Text = BuildLoadedImageInfoText();
            if (_originalImageWidth.HasValue && _originalImageHeight.HasValue)
            {
                ApplyDimensionBounds(_previewImageWidth.Value, _previewImageHeight.Value);
                UpdateDimensionBoundsForWorkingImage(resetDisabledInputs: true);
            }
            RefreshValidation();
        }

        private static bool IsSupportedImageFile(StorageFile file)
        {
            return SupportedExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase);
        }

        private void PreserveQualityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SetPanelInteractive(QualitySelectorPanel, !(PreserveOriginalQualityCheckBox.IsChecked ?? false));
            RefreshValidation();
        }

        public void ApplyOptionSelection(string optionTag)
        {
            if (!TryParseNavigationTag(optionTag, out var section, out var subgroup))
            {
                return;
            }

            if (SelectedOptionHeaderTextBlock is null)
            {
                return;
            }

            SelectedOptionHeaderTextBlock.Text = section;
            ShowOptionPanel(section);

            if (!string.IsNullOrWhiteSpace(subgroup))
            {
                switch (section)
                {
                    case "Media":
                        ShowMediaSubgroup(subgroup);
                        break;
                    case "Transform":
                        ShowTransformSubgroup(subgroup);
                        break;
                    case "Adjust":
                        ShowAdjustSubgroup(subgroup);
                        break;
                }
            }
        }

        private void ShowOptionPanel(string selected)
        {
            MediaPanel.Visibility = selected == "Media" ? Visibility.Visible : Visibility.Collapsed;
            TransformPanel.Visibility = selected == "Transform" ? Visibility.Visible : Visibility.Collapsed;
            AdjustPanel.Visibility = selected == "Adjust" ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool TryParseNavigationTag(string tag, out string section, out string subgroup)
        {
            var parts = tag.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                section = string.Empty;
                subgroup = string.Empty;
                return false;
            }

            section = parts[0];
            subgroup = parts.Length > 1 ? parts[1] : string.Empty;
            return true;
        }

        private void ShowMediaSubgroup(string selected)
        {
            MediaFormatPanel.Visibility = selected == "Format" ? Visibility.Visible : Visibility.Collapsed;
            MediaOutputPanel.Visibility = selected == "Output" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowTransformSubgroup(string selected)
        {
            TransformCropPanel.Visibility = selected == "Crop" ? Visibility.Visible : Visibility.Collapsed;
            TransformResizePanel.Visibility = selected == "Resize" ? Visibility.Visible : Visibility.Collapsed;
            TransformUpscalePanel.Visibility = selected == "Upscale" ? Visibility.Visible : Visibility.Collapsed;
            TransformRotatePanel.Visibility = selected == "Rotate" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowAdjustSubgroup(string selected)
        {
            AdjustColorPanel.Visibility = selected == "Color" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void QualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (QualityPercentTextBlock is not null && QualitySlider is not null)
            {
                QualityPercentTextBlock.Text = $"Output quality: {(int)Math.Round(QualitySlider.Value)}%";
            }

            RefreshValidation();
        }

        private async void RgbSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (RedSliderTextBlock is not null && RedSlider is not null)
            {
                RedSliderTextBlock.Text = $"Red: {(int)Math.Round(RedSlider.Value)}%";
            }

            if (GreenSliderTextBlock is not null && GreenSlider is not null)
            {
                GreenSliderTextBlock.Text = $"Green: {(int)Math.Round(GreenSlider.Value)}%";
            }

            if (BlueSliderTextBlock is not null && BlueSlider is not null)
            {
                BlueSliderTextBlock.Text = $"Blue: {(int)Math.Round(BlueSlider.Value)}%";
            }

            await UpdatePreviewFromLiveTransformsAsync();
            RefreshValidation();
        }

        private void DimensionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshValidation();
        }

        private void UpscaleDimensionNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ClampUpscaleNumberBoxesToBounds();

            if (_syncingUpscaleDimensions || !(PreserveUpscaleAspectRatioCheckBox?.IsChecked ?? false))
            {
                RefreshValidation();
                return;
            }

            if (!_previewImageWidth.HasValue || !_previewImageHeight.HasValue || _previewImageWidth.Value <= 0 || _previewImageHeight.Value <= 0)
            {
                RefreshValidation();
                return;
            }

            var sourceRatio = (double)_previewImageWidth.Value / _previewImageHeight.Value;
            _syncingUpscaleDimensions = true;
            try
            {
                if (ReferenceEquals(sender, UpscaleWidthNumberBox))
                {
                    var width = Math.Max(1, (int)Math.Round(UpscaleWidthNumberBox.Value));
                    var computedHeight = Math.Max(1, (int)Math.Round(width / sourceRatio));
                    UpscaleHeightNumberBox.Value = computedHeight;
                }
                else if (ReferenceEquals(sender, UpscaleHeightNumberBox))
                {
                    var height = Math.Max(1, (int)Math.Round(UpscaleHeightNumberBox.Value));
                    var computedWidth = Math.Max(1, (int)Math.Round(height * sourceRatio));
                    UpscaleWidthNumberBox.Value = computedWidth;
                }
            }
            finally
            {
                _syncingUpscaleDimensions = false;
            }

            RefreshValidation();
        }

        private void ResizeDimensionNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ClampResizeNumberBoxesToBounds();

            if (_syncingResizeDimensions || !(PreserveAspectRatioCheckBox?.IsChecked ?? false))
            {
                RefreshValidation();
                return;
            }

            if (!_previewImageWidth.HasValue || !_previewImageHeight.HasValue || _previewImageWidth.Value <= 0 || _previewImageHeight.Value <= 0)
            {
                RefreshValidation();
                return;
            }

            var sourceRatio = (double)_previewImageWidth.Value / _previewImageHeight.Value;
            _syncingResizeDimensions = true;
            try
            {
                if (ReferenceEquals(sender, ResizeWidthNumberBox))
                {
                    var width = Math.Max(1, (int)Math.Round(ResizeWidthNumberBox.Value));
                    var computedHeight = Math.Max(1, (int)Math.Round(width / sourceRatio));
                    ResizeHeightNumberBox.Value = computedHeight;
                }
                else if (ReferenceEquals(sender, ResizeHeightNumberBox))
                {
                    var height = Math.Max(1, (int)Math.Round(ResizeHeightNumberBox.Value));
                    var computedWidth = Math.Max(1, (int)Math.Round(height * sourceRatio));
                    ResizeWidthNumberBox.Value = computedWidth;
                }
            }
            finally
            {
                _syncingResizeDimensions = false;
            }

            RefreshValidation();
        }

        private async void OptionsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateOptionUiState();
            await UpdatePreviewFromLiveTransformsAsync();
            RefreshValidation();
        }

        private void CropCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (EnableCropCheckBox?.IsChecked ?? false)
            {
                EnableResizeCheckBox.IsChecked = false;
                EnableUpscaleCheckBox.IsChecked = false;
                MirrorHorizontalCheckBox.IsChecked = false;
                MirrorVerticalCheckBox.IsChecked = false;
                InitializeCropToFullImage();
            }
            else
            {
                _cropDragMode = CropDragMode.None;
                CropOverlayCanvas.Visibility = Visibility.Collapsed;
            }

            UpdateOptionUiState();
            RefreshValidation();
        }

        private async void ApplyCropButton_Click(object sender, RoutedEventArgs e)
        {
            await CommitCurrentCropAsync();
        }

        private async void OptionsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await UpdatePreviewFromLiveTransformsAsync();
            RefreshValidation();
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceImageFile is null)
            {
                return;
            }

            var options = BuildCurrentOptions();
            var validationErrors = ImageEditOptionsValidator.Validate(options, _originalImageWidth, _originalImageHeight, _previewImageWidth, _previewImageHeight);
            if (validationErrors.Count > 0)
            {
                RefreshValidation(validationErrors);
                return;
            }

            try
            {
                var outputPath = await PickOutputPathAsync(_sourceImageFile, options);
                if (outputPath is null)
                {
                    return;
                }

                var processOptions = BuildProcessOptions(options);
                await _imageProcessingService.ProcessImageAsync(_sourceImageFile.Path, outputPath, processOptions, CancellationToken.None);

                var dialog = new ContentDialog
                {
                    Title = "Done",
                    Content = $"Image saved to:\n{outputPath}",
                    PrimaryButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                _ = await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Processing error",
                    Content = $"Could not process image with the selected options.\n\nDetails: {ex.Message}",
                    PrimaryButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                _ = await dialog.ShowAsync();
            }
        }

        private void UpdateOptionUiState()
        {
            var cropEnabled = EnableCropCheckBox?.IsChecked ?? false;

            if (cropEnabled)
            {
                EnableResizeCheckBox.IsChecked = false;
                EnableUpscaleCheckBox.IsChecked = false;
                MirrorHorizontalCheckBox.IsChecked = false;
                MirrorVerticalCheckBox.IsChecked = false;
            }

            SetPanelInteractive(QualitySelectorPanel, !(PreserveOriginalQualityCheckBox?.IsChecked ?? false));
            SetPanelInteractive(ResizeInputsGrid, !cropEnabled && (EnableResizeCheckBox?.IsChecked ?? false));

            if (EnableResizeCheckBox is not null)
            {
                EnableResizeCheckBox.IsEnabled = !cropEnabled;
            }

            if (PreserveAspectRatioCheckBox is not null)
            {
                PreserveAspectRatioCheckBox.IsEnabled = !cropEnabled && (EnableResizeCheckBox?.IsChecked ?? false);
            }

            SetPanelInteractive(UpscaleInputsGrid, !cropEnabled && (EnableUpscaleCheckBox?.IsChecked ?? false));
            if (EnableUpscaleCheckBox is not null)
            {
                EnableUpscaleCheckBox.IsEnabled = !cropEnabled;
            }

            if (PreserveUpscaleAspectRatioCheckBox is not null)
            {
                PreserveUpscaleAspectRatioCheckBox.IsEnabled = !cropEnabled && (EnableUpscaleCheckBox?.IsChecked ?? false);
            }

            if (RotationComboBox is not null)
            {
                RotationComboBox.IsEnabled = !cropEnabled;
            }

            if (MirrorHorizontalCheckBox is not null)
            {
                MirrorHorizontalCheckBox.IsEnabled = !cropEnabled;
            }

            if (MirrorVerticalCheckBox is not null)
            {
                MirrorVerticalCheckBox.IsEnabled = !cropEnabled;
            }

            if (ApplyCropButton is not null)
            {
                ApplyCropButton.IsEnabled = cropEnabled && PreviewImage?.Source is not null;
            }

            SetPanelInteractive(RgbControlsPanel, EnableRgbAdjustmentCheckBox?.IsChecked ?? false);
            CropOverlayCanvas.Visibility = cropEnabled && PreviewImage?.Source is not null ? Visibility.Visible : Visibility.Collapsed;
            UpdateCropOverlayForCurrentPreview();
        }

        private static void SetPanelInteractive(UIElement? panel, bool enabled)
        {
            if (panel is null)
            {
                return;
            }

            panel.IsHitTestVisible = enabled;
            panel.Opacity = enabled ? 1.0 : 0.55;
        }

        private void RefreshValidation(IReadOnlyList<string>? errors = null)
        {
            var currentErrors = errors ?? ImageEditOptionsValidator.Validate(
                BuildCurrentOptions(),
                _originalImageWidth,
                _originalImageHeight,
                _previewImageWidth,
                _previewImageHeight);

            var upscaleErrors = currentErrors
                .Where(error => error.Contains("Upscale", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var cropErrors = currentErrors
                .Where(error => error.Contains("Crop", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (UpscaleValidationTextBlock is not null)
            {
                UpscaleValidationTextBlock.Text = string.Join("\n", upscaleErrors);
            }

            if (CropValidationTextBlock is not null)
            {
                CropValidationTextBlock.Text = string.Join("\n", cropErrors);
            }

            if (ApplyButton is not null)
            {
                ApplyButton.IsEnabled = PreviewImage?.Source is not null && currentErrors.Count == 0;
            }
        }

        private ImageEditOptions BuildCurrentOptions()
        {
            var rotationTag = (RotationComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0";
            _ = int.TryParse(rotationTag, out var rotationDegrees);
            var crop = GetEffectiveCropForProcessing();
            var cropPixels = crop ?? (0, 0, _originalImageWidth ?? 1, _originalImageHeight ?? 1);

            return new ImageEditOptions
            {
                OutputFormat = (ImageFileFormat)Math.Max(0, OutputFormatComboBox?.SelectedIndex ?? 0),
                PreserveOriginalQuality = PreserveOriginalQualityCheckBox?.IsChecked ?? true,
                QualityPercent = ToPercentage(QualitySlider?.Value ?? 90),
                EnableCrop = crop.HasValue,
                CropLeft = cropPixels.Item1,
                CropTop = cropPixels.Item2,
                CropWidth = cropPixels.Item3,
                CropHeight = cropPixels.Item4,
                EnableResize = EnableResizeCheckBox?.IsChecked ?? false,
                ResizeWidth = ToDimensionFromDouble(ResizeWidthNumberBox?.Value, 1),
                ResizeHeight = ToDimensionFromDouble(ResizeHeightNumberBox?.Value, 1),
                PreserveAspectRatio = PreserveAspectRatioCheckBox?.IsChecked ?? true,
                EnableUpscale = EnableUpscaleCheckBox?.IsChecked ?? false,
                UpscaleWidth = ToDimensionFromDouble(UpscaleWidthNumberBox?.Value, 1),
                UpscaleHeight = ToDimensionFromDouble(UpscaleHeightNumberBox?.Value, 1),
                EnableRotation = rotationDegrees != 0,
                RotationDegrees = rotationDegrees,
                MirrorHorizontally = MirrorHorizontalCheckBox?.IsChecked ?? false,
                MirrorVertically = MirrorVerticalCheckBox?.IsChecked ?? false,
                EnableRgbAdjustments = EnableRgbAdjustmentCheckBox?.IsChecked ?? false,
                RedPercent = ToPercentage(RedSlider?.Value ?? 100),
                GreenPercent = ToPercentage(GreenSlider?.Value ?? 100),
                BluePercent = ToPercentage(BlueSlider?.Value ?? 100)
            };
        }

        private static int ToDimension(string? value, int fallback)
        {
            if (!int.TryParse(value, out var parsed))
            {
                return fallback;
            }

            return Math.Max(1, parsed);
        }

        private static int ToDimensionFromDouble(double? value, int fallback)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                return fallback;
            }

            return Math.Max(1, (int)Math.Round(value.Value));
        }

        private void ApplyDimensionBounds(int originalWidth, int originalHeight)
        {
            // Resize must not exceed original size.
            ResizeWidthNumberBox.Minimum = 1;
            ResizeHeightNumberBox.Minimum = 1;
            ResizeWidthNumberBox.Maximum = originalWidth;
            ResizeHeightNumberBox.Maximum = originalHeight;

            // Upscale must not go below original size.
            UpscaleWidthNumberBox.Minimum = originalWidth;
            UpscaleHeightNumberBox.Minimum = originalHeight;
            UpscaleWidthNumberBox.Maximum = 20000;
            UpscaleHeightNumberBox.Maximum = 20000;
        }

        private void ClampResizeNumberBoxesToBounds()
        {
            if (!_previewImageWidth.HasValue || !_previewImageHeight.HasValue)
            {
                return;
            }

            ResizeWidthNumberBox.Value = Math.Clamp(ResizeWidthNumberBox.Value, 1, _previewImageWidth.Value);
            ResizeHeightNumberBox.Value = Math.Clamp(ResizeHeightNumberBox.Value, 1, _previewImageHeight.Value);
        }

        private void ClampUpscaleNumberBoxesToBounds()
        {
            if (!_previewImageWidth.HasValue || !_previewImageHeight.HasValue)
            {
                return;
            }

            UpscaleWidthNumberBox.Value = Math.Clamp(UpscaleWidthNumberBox.Value, _previewImageWidth.Value, 20000);
            UpscaleHeightNumberBox.Value = Math.Clamp(UpscaleHeightNumberBox.Value, _previewImageHeight.Value, 20000);
        }

        private static int ToPercentage(double value)
        {
            return Math.Clamp((int)Math.Round(value), 0, 100);
        }

        private static string BuildOptionsSummary(ImageEditOptions options)
        {
            return
                $"Format: {options.OutputFormat}\n" +
                $"Preserve quality: {options.PreserveOriginalQuality}\n" +
                $"Quality: {options.QualityPercent}%\n" +
                $"Crop enabled: {options.EnableCrop} ({options.CropLeft}, {options.CropTop}, {options.CropWidth} x {options.CropHeight})\n" +
                $"Resize enabled: {options.EnableResize} ({options.ResizeWidth} x {options.ResizeHeight})\n" +
                $"Preserve aspect ratio: {options.PreserveAspectRatio}\n" +
                $"Upscale enabled: {options.EnableUpscale} ({options.UpscaleWidth} x {options.UpscaleHeight})\n" +
                $"Rotation: {options.RotationDegrees} degrees\n" +
                $"Mirror H/V: {options.MirrorHorizontally}/{options.MirrorVertically}\n" +
                $"RGB enabled: {options.EnableRgbAdjustments} ({options.RedPercent}% / {options.GreenPercent}% / {options.BluePercent}%)";
        }

        private void ResetCropState()
        {
            _cropDragMode = CropDragMode.None;
            _displayedImageRect = default;
            _cropRect = default;
            _committedCropPixels = null;
            if (EnableCropCheckBox is not null)
            {
                EnableCropCheckBox.IsChecked = false;
            }

            if (CropOverlayCanvas is not null)
            {
                CropOverlayCanvas.Visibility = Visibility.Collapsed;
            }

            UpdateCropInfoText();
        }

        private async Task CommitCurrentCropAsync()
        {
            if (_sourceImageFile is null || !(EnableCropCheckBox?.IsChecked ?? false))
            {
                return;
            }

            var validationErrors = ImageEditOptionsValidator.Validate(
                BuildCurrentOptions(),
                _originalImageWidth,
                _originalImageHeight,
                _previewImageWidth,
                _previewImageHeight);

            if (validationErrors.Count > 0)
            {
                RefreshValidation(validationErrors);
                return;
            }

            try
            {
                ApplyCropButton.IsEnabled = false;
                var pendingCrop = GetCurrentCropPixelRect();
                _committedCropPixels = ComposeCommittedCrop(pendingCrop);
                await UpdatePreviewFromLiveTransformsAsync();

                EnableCropCheckBox.IsChecked = false;
                _cropDragMode = CropDragMode.None;
                _cropRect = default;
                CropOverlayCanvas.Visibility = Visibility.Collapsed;

                if (_previewImageWidth.HasValue && _previewImageHeight.HasValue)
                {
                    UpdateDimensionBoundsForWorkingImage(resetDisabledInputs: true);
                }

                UpdateCropInfoText();
                UpdateOptionUiState();
                RefreshValidation();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Crop error",
                    Content = $"Could not apply the selected crop.\n\nDetails: {ex.Message}",
                    PrimaryButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                _ = await dialog.ShowAsync();
            }
            finally
            {
                ApplyCropButton.IsEnabled = (EnableCropCheckBox?.IsChecked ?? false) && PreviewImage?.Source is not null;
            }
        }

        private (int Left, int Top, int Width, int Height) ComposeCommittedCrop((int Left, int Top, int Width, int Height) pendingCrop)
        {
            if (_committedCropPixels is not { } committedCrop)
            {
                return pendingCrop;
            }

            return (
                committedCrop.Left + pendingCrop.Left,
                committedCrop.Top + pendingCrop.Top,
                pendingCrop.Width,
                pendingCrop.Height);
        }

        private async Task UpdatePreviewFromLiveTransformsAsync()
        {
            if (_sourceImageFile is null || _updatingLivePreview)
            {
                return;
            }

            _updatingLivePreview = true;
            try
            {
                var previewOptions = BuildCurrentOptions();
                var processOptions = BuildProcessOptions(previewOptions);
                var previewProcessOptions = new ProcessImageOptions
                {
                    Crop = processOptions.Crop,
                    Resize = processOptions.Resize,
                    Upscale = processOptions.Upscale,
                    Rotate = processOptions.Rotate,
                    Mirror = processOptions.Mirror,
                    RgbAdjust = processOptions.RgbAdjust,
                    Output = new OutputOptions
                    {
                        Format = Services.ImageFormat.Png,
                        QualityMode = ImageQualityMode.MaintainOriginal,
                        Quality = null,
                        KeepMetadata = false
                    }
                };

                var previewOutputPath = Path.Combine(Path.GetTempPath(), $"files-tools-preview-{Guid.NewGuid():N}.png");

                try
                {
                    await _imageProcessingService.ProcessImageAsync(_sourceImageFile.Path, previewOutputPath, previewProcessOptions, CancellationToken.None);

                    var previewStorageFile = await StorageFile.GetFileFromPathAsync(previewOutputPath);
                    using var previewStream = await previewStorageFile.OpenAsync(FileAccessMode.Read);
                    var previewDecoder = await BitmapDecoder.CreateAsync(previewStream);
                    _previewImageWidth = (int)previewDecoder.OrientedPixelWidth;
                    _previewImageHeight = (int)previewDecoder.OrientedPixelHeight;
                    previewStream.Seek(0);

                    var previewBitmap = new BitmapImage();
                    await previewBitmap.SetSourceAsync(previewStream);
                    PreviewImage.Source = previewBitmap;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(previewOutputPath))
                        {
                            File.Delete(previewOutputPath);
                        }
                    }
                    catch
                    {
                        // Best-effort temp cleanup.
                    }
                }

                UpdateDimensionBoundsForWorkingImage();
                LoadedImageInfoTextBlock.Text = BuildLoadedImageInfoText();
                UpdateCropOverlayForCurrentPreview();
            }
            finally
            {
                _updatingLivePreview = false;
            }
        }

        private void UpdateDimensionBoundsForWorkingImage(bool resetDisabledInputs = false)
        {
            if (!_previewImageWidth.HasValue || !_previewImageHeight.HasValue)
            {
                return;
            }

            ApplyDimensionBounds(_previewImageWidth.Value, _previewImageHeight.Value);

            if (resetDisabledInputs || !(EnableResizeCheckBox?.IsChecked ?? false))
            {
                ResizeWidthNumberBox.Value = _previewImageWidth.Value;
                ResizeHeightNumberBox.Value = _previewImageHeight.Value;
            }
            else
            {
                ClampResizeNumberBoxesToBounds();
            }

            if (resetDisabledInputs || !(EnableUpscaleCheckBox?.IsChecked ?? false))
            {
                UpscaleWidthNumberBox.Value = _previewImageWidth.Value;
                UpscaleHeightNumberBox.Value = _previewImageHeight.Value;
            }
            else
            {
                ClampUpscaleNumberBoxesToBounds();
            }
        }

        private string BuildLoadedImageInfoText()
        {
            if (!_originalImageWidth.HasValue || !_originalImageHeight.HasValue)
            {
                return "No image loaded yet.";
            }

            if (_previewImageWidth.HasValue &&
                _previewImageHeight.HasValue &&
                (_previewImageWidth.Value != _originalImageWidth.Value ||
                 _previewImageHeight.Value != _originalImageHeight.Value ||
                 _committedCropPixels.HasValue ||
                 GetSelectedRotationDegrees() != 0 ||
                 (MirrorHorizontalCheckBox?.IsChecked ?? false) ||
                 (MirrorVerticalCheckBox?.IsChecked ?? false)))
            {
                return $"Loaded image: {_originalImageWidth} x {_originalImageHeight} px. Working image: {_previewImageWidth} x {_previewImageHeight} px";
            }

            return $"Loaded image: {_originalImageWidth} x {_originalImageHeight} px";
        }

        private int GetSelectedRotationDegrees()
        {
            var rotationTag = (RotationComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0";
            return int.TryParse(rotationTag, out var rotationDegrees) ? rotationDegrees : 0;
        }

        private void InitializeCropToFullImage()
        {
            UpdateDisplayedImageRect();
            if (_displayedImageRect.Width <= 0 || _displayedImageRect.Height <= 0)
            {
                return;
            }

            _cropRect = _displayedImageRect;
            UpdateCropOverlayVisuals();
        }

        private void UpdateCropOverlayForCurrentPreview()
        {
            if (CropOverlayCanvas is null)
            {
                return;
            }

            var cropPixels = GetCurrentCropPixelRect();
            UpdateDisplayedImageRect();

            CropOverlayCanvas.Width = Math.Max(0, PreviewHost.ActualWidth);
            CropOverlayCanvas.Height = Math.Max(0, PreviewHost.ActualHeight);

            if ((EnableCropCheckBox?.IsChecked ?? false) && _displayedImageRect.Width > 0 && _displayedImageRect.Height > 0)
            {
                SetCropRectFromPixels(cropPixels);
            }

            UpdateCropOverlayVisuals();
        }

        private void UpdateDisplayedImageRect()
        {
            if (!_previewImageWidth.HasValue ||
                !_previewImageHeight.HasValue ||
                _previewImageWidth.Value <= 0 ||
                _previewImageHeight.Value <= 0 ||
                PreviewHost.ActualWidth <= 0 ||
                PreviewHost.ActualHeight <= 0)
            {
                _displayedImageRect = default;
                return;
            }

            var scale = Math.Min(
                PreviewHost.ActualWidth / _previewImageWidth.Value,
                PreviewHost.ActualHeight / _previewImageHeight.Value);
            var width = _previewImageWidth.Value * scale;
            var height = _previewImageHeight.Value * scale;
            var left = (PreviewHost.ActualWidth - width) / 2;
            var top = (PreviewHost.ActualHeight - height) / 2;

            _displayedImageRect = new Rect(left, top, width, height);
        }

        private (int Left, int Top, int Width, int Height) GetCurrentCropPixelRect()
        {
            if (!_previewImageWidth.HasValue || !_previewImageHeight.HasValue)
            {
                return (0, 0, 1, 1);
            }

            if (_displayedImageRect.Width <= 0 || _displayedImageRect.Height <= 0 || _cropRect.Width <= 0 || _cropRect.Height <= 0)
            {
                return (0, 0, _previewImageWidth.Value, _previewImageHeight.Value);
            }

            var scaleX = _previewImageWidth.Value / _displayedImageRect.Width;
            var scaleY = _previewImageHeight.Value / _displayedImageRect.Height;
            var left = Math.Clamp((int)Math.Round((_cropRect.Left - _displayedImageRect.Left) * scaleX), 0, _previewImageWidth.Value - 1);
            var top = Math.Clamp((int)Math.Round((_cropRect.Top - _displayedImageRect.Top) * scaleY), 0, _previewImageHeight.Value - 1);
            var right = Math.Clamp((int)Math.Round((_cropRect.Right - _displayedImageRect.Left) * scaleX), left + 1, _previewImageWidth.Value);
            var bottom = Math.Clamp((int)Math.Round((_cropRect.Bottom - _displayedImageRect.Top) * scaleY), top + 1, _previewImageHeight.Value);

            return (left, top, right - left, bottom - top);
        }

        private (int Left, int Top, int Width, int Height)? GetEffectiveCropForProcessing()
        {
            if (EnableCropCheckBox?.IsChecked ?? false)
            {
                return ComposeCommittedCrop(GetCurrentCropPixelRect());
            }

            return _committedCropPixels;
        }

        private void SetCropRectFromPixels((int Left, int Top, int Width, int Height) cropPixels)
        {
            if (!_previewImageWidth.HasValue ||
                !_previewImageHeight.HasValue ||
                _displayedImageRect.Width <= 0 ||
                _displayedImageRect.Height <= 0)
            {
                return;
            }

            var scaleX = _displayedImageRect.Width / _previewImageWidth.Value;
            var scaleY = _displayedImageRect.Height / _previewImageHeight.Value;
            var left = _displayedImageRect.Left + cropPixels.Left * scaleX;
            var top = _displayedImageRect.Top + cropPixels.Top * scaleY;
            var width = cropPixels.Width * scaleX;
            var height = cropPixels.Height * scaleY;

            _cropRect = ClampCropRect(new Rect(left, top, width, height));
        }

        private void UpdateCropOverlayVisuals()
        {
            if (CropOverlayCanvas is null)
            {
                return;
            }

            if (!(EnableCropCheckBox?.IsChecked ?? false) || PreviewImage?.Source is null || _displayedImageRect.Width <= 0 || _displayedImageRect.Height <= 0)
            {
                CropOverlayCanvas.Visibility = Visibility.Collapsed;
                UpdateCropInfoText();
                return;
            }

            CropOverlayCanvas.Visibility = Visibility.Visible;
            _cropRect = ClampCropRect(_cropRect.Width <= 0 || _cropRect.Height <= 0 ? _displayedImageRect : _cropRect);

            SetRectangle(CropDimTopRectangle, _displayedImageRect.Left, _displayedImageRect.Top, _displayedImageRect.Width, Math.Max(0, _cropRect.Top - _displayedImageRect.Top));
            SetRectangle(CropDimLeftRectangle, _displayedImageRect.Left, _cropRect.Top, Math.Max(0, _cropRect.Left - _displayedImageRect.Left), _cropRect.Height);
            SetRectangle(CropDimRightRectangle, _cropRect.Right, _cropRect.Top, Math.Max(0, _displayedImageRect.Right - _cropRect.Right), _cropRect.Height);
            SetRectangle(CropDimBottomRectangle, _displayedImageRect.Left, _cropRect.Bottom, _displayedImageRect.Width, Math.Max(0, _displayedImageRect.Bottom - _cropRect.Bottom));

            SetElementBounds(CropSelectionBorder, _cropRect.Left, _cropRect.Top, _cropRect.Width, _cropRect.Height);
            PositionCropHandle(CropHandleTopLeft, _cropRect.Left, _cropRect.Top);
            PositionCropHandle(CropHandleTop, _cropRect.Left + _cropRect.Width / 2, _cropRect.Top);
            PositionCropHandle(CropHandleTopRight, _cropRect.Right, _cropRect.Top);
            PositionCropHandle(CropHandleRight, _cropRect.Right, _cropRect.Top + _cropRect.Height / 2);
            PositionCropHandle(CropHandleBottomRight, _cropRect.Right, _cropRect.Bottom);
            PositionCropHandle(CropHandleBottom, _cropRect.Left + _cropRect.Width / 2, _cropRect.Bottom);
            PositionCropHandle(CropHandleBottomLeft, _cropRect.Left, _cropRect.Bottom);
            PositionCropHandle(CropHandleLeft, _cropRect.Left, _cropRect.Top + _cropRect.Height / 2);
            UpdateCropInfoText();
        }

        private static void SetRectangle(Rectangle rectangle, double left, double top, double width, double height)
        {
            rectangle.Width = width;
            rectangle.Height = height;
            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
        }

        private static void SetElementBounds(FrameworkElement element, double left, double top, double width, double height)
        {
            element.Width = width;
            element.Height = height;
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
        }

        private static void PositionCropHandle(FrameworkElement handle, double centerX, double centerY)
        {
            Canvas.SetLeft(handle, centerX - handle.Width / 2);
            Canvas.SetTop(handle, centerY - handle.Height / 2);
        }

        private Rect ClampCropRect(Rect rect)
        {
            if (_displayedImageRect.Width <= 0 || _displayedImageRect.Height <= 0)
            {
                return rect;
            }

            var minWidth = Math.Min(MinimumCropDisplaySize, _displayedImageRect.Width);
            var minHeight = Math.Min(MinimumCropDisplaySize, _displayedImageRect.Height);
            var width = Math.Clamp(rect.Width, minWidth, _displayedImageRect.Width);
            var height = Math.Clamp(rect.Height, minHeight, _displayedImageRect.Height);
            var maxLeft = Math.Max(_displayedImageRect.Left, _displayedImageRect.Right - width);
            var maxTop = Math.Max(_displayedImageRect.Top, _displayedImageRect.Bottom - height);
            var left = Math.Clamp(rect.Left, _displayedImageRect.Left, maxLeft);
            var top = Math.Clamp(rect.Top, _displayedImageRect.Top, maxTop);

            return new Rect(left, top, width, height);
        }

        private void UpdateCropInfoText()
        {
            if (CropInfoTextBlock is null)
            {
                return;
            }

            if (!(EnableCropCheckBox?.IsChecked ?? false) || PreviewImage?.Source is null)
            {
                CropInfoTextBlock.Text = _committedCropPixels is { } committedCrop
                    ? $"Applied crop: {committedCrop.Width} x {committedCrop.Height} px at {committedCrop.Left}, {committedCrop.Top}"
                    : "Crop disabled";
                return;
            }

            var crop = GetCurrentCropPixelRect();
            CropInfoTextBlock.Text = $"Crop: {crop.Width} x {crop.Height} px at {crop.Left}, {crop.Top}";
        }

        private void CropHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!(EnableCropCheckBox?.IsChecked ?? false))
            {
                return;
            }

            _cropDragMode = ParseCropDragMode((sender as FrameworkElement)?.Tag?.ToString());
            _dragStartPoint = e.GetCurrentPoint(CropOverlayCanvas).Position;
            _dragStartCropRect = _cropRect;
            CropOverlayCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void CropOverlayCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_cropDragMode == CropDragMode.None)
            {
                return;
            }

            var currentPoint = e.GetCurrentPoint(CropOverlayCanvas).Position;
            var deltaX = currentPoint.X - _dragStartPoint.X;
            var deltaY = currentPoint.Y - _dragStartPoint.Y;
            _cropRect = GetDraggedCropRect(_dragStartCropRect, deltaX, deltaY, _cropDragMode);
            UpdateCropOverlayVisuals();
            RefreshValidation();
            e.Handled = true;
        }

        private void CropOverlayCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_cropDragMode == CropDragMode.None)
            {
                return;
            }

            _cropDragMode = CropDragMode.None;
            CropOverlayCanvas.ReleasePointerCapture(e.Pointer);
            RefreshValidation();
            e.Handled = true;
        }

        private Rect GetDraggedCropRect(Rect startRect, double deltaX, double deltaY, CropDragMode mode)
        {
            var left = startRect.Left;
            var top = startRect.Top;
            var right = startRect.Right;
            var bottom = startRect.Bottom;
            var minWidth = Math.Min(MinimumCropDisplaySize, _displayedImageRect.Width);
            var minHeight = Math.Min(MinimumCropDisplaySize, _displayedImageRect.Height);

            if (mode == CropDragMode.Move)
            {
                return ClampCropRect(new Rect(startRect.Left + deltaX, startRect.Top + deltaY, startRect.Width, startRect.Height));
            }

            if (mode is CropDragMode.Left or CropDragMode.TopLeft or CropDragMode.BottomLeft)
            {
                left = Math.Clamp(startRect.Left + deltaX, _displayedImageRect.Left, right - minWidth);
            }

            if (mode is CropDragMode.Right or CropDragMode.TopRight or CropDragMode.BottomRight)
            {
                right = Math.Clamp(startRect.Right + deltaX, left + minWidth, _displayedImageRect.Right);
            }

            if (mode is CropDragMode.Top or CropDragMode.TopLeft or CropDragMode.TopRight)
            {
                top = Math.Clamp(startRect.Top + deltaY, _displayedImageRect.Top, bottom - minHeight);
            }

            if (mode is CropDragMode.Bottom or CropDragMode.BottomLeft or CropDragMode.BottomRight)
            {
                bottom = Math.Clamp(startRect.Bottom + deltaY, top + minHeight, _displayedImageRect.Bottom);
            }

            return ClampCropRect(new Rect(left, top, right - left, bottom - top));
        }

        private static CropDragMode ParseCropDragMode(string? tag)
        {
            return Enum.TryParse<CropDragMode>(tag, out var mode) ? mode : CropDragMode.None;
        }

        private async Task<string?> PickOutputPathAsync(StorageFile sourceFile, ImageEditOptions options)
        {
            if (App.MainWindow is null)
            {
                return null;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}_edited_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var extension = GetImageOutputExtension(options.OutputFormat, sourceFile.FileType);
            picker.FileTypeChoices.Add(GetImageSaveChoiceLabel(options.OutputFormat), new List<string> { extension });
            picker.DefaultFileExtension = extension;

            var outputFile = await picker.PickSaveFileAsync();
            return outputFile?.Path;
        }

        private static string GetImageSaveChoiceLabel(ImageFileFormat format)
        {
            return format switch
            {
                ImageFileFormat.KeepOriginal => "Original format",
                ImageFileFormat.Jpeg => "JPEG",
                ImageFileFormat.Png => "PNG",
                ImageFileFormat.WebP => "WebP",
                ImageFileFormat.Avif => "AVIF",
                ImageFileFormat.Tiff => "TIFF",
                ImageFileFormat.Heif => "HEIF",
                ImageFileFormat.Gif => "GIF",
                _ => "Image"
            };
        }

        private static ProcessImageOptions BuildProcessOptions(ImageEditOptions options)
        {
            return new ProcessImageOptions
            {
                Crop = options.EnableCrop
                    ? new CropOptions
                    {
                        Left = options.CropLeft,
                        Top = options.CropTop,
                        Width = options.CropWidth,
                        Height = options.CropHeight
                    }
                    : null,
                Resize = options.EnableResize ? new ResizeOptions { Width = options.ResizeWidth, Height = options.ResizeHeight } : null,
                Upscale = options.EnableUpscale ? new UpscaleOptions { Width = options.UpscaleWidth, Height = options.UpscaleHeight } : null,
                Rotate = options.EnableRotation && options.RotationDegrees != 0
                    ? new RotateOptions { Angle = options.RotationDegrees }
                    : null,
                Mirror = (options.MirrorHorizontally || options.MirrorVertically)
                    ? new MirrorOptions { Horizontal = options.MirrorHorizontally, Vertical = options.MirrorVertically }
                    : null,
                RgbAdjust = options.EnableRgbAdjustments
                    ? new RgbAdjustOptions
                    {
                        RedScale = options.RedPercent / 100.0,
                        GreenScale = options.GreenPercent / 100.0,
                        BlueScale = options.BluePercent / 100.0
                    }
                    : null,
                Output = new OutputOptions
                {
                    Format = MapOutputFormat(options.OutputFormat),
                    QualityMode = options.PreserveOriginalQuality ? ImageQualityMode.MaintainOriginal : ImageQualityMode.ExplicitQuality,
                    Quality = options.PreserveOriginalQuality ? null : options.QualityPercent,
                    KeepMetadata = true
                }
            };
        }

        private static Services.ImageFormat? MapOutputFormat(ImageFileFormat format)
        {
            return format switch
            {
                ImageFileFormat.KeepOriginal => null,
                ImageFileFormat.Jpeg => Services.ImageFormat.Jpeg,
                ImageFileFormat.Png => Services.ImageFormat.Png,
                ImageFileFormat.WebP => Services.ImageFormat.Webp,
                ImageFileFormat.Avif => Services.ImageFormat.Avif,
                ImageFileFormat.Tiff => Services.ImageFormat.Tiff,
                ImageFileFormat.Heif => Services.ImageFormat.Heif,
                ImageFileFormat.Gif => Services.ImageFormat.Gif,
                _ => null
            };
        }

        private static string GetImageOutputExtension(ImageFileFormat format, string sourceExtension)
        {
            return format switch
            {
                ImageFileFormat.KeepOriginal => sourceExtension,
                ImageFileFormat.Jpeg => ".jpg",
                ImageFileFormat.Png => ".png",
                ImageFileFormat.WebP => ".webp",
                ImageFileFormat.Avif => ".avif",
                ImageFileFormat.Tiff => ".tiff",
                ImageFileFormat.Heif => ".heif",
                ImageFileFormat.Gif => ".gif",
                _ => ".png"
            };
        }
    }
}
