using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Files_Tools.ImageEditing;
using Files_Tools.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Files_Tools.Pages
{
    public sealed partial class ImageEditorPage : Page
    {
        private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
        private int? _originalImageWidth;
        private int? _originalImageHeight;
        private StorageFile? _sourceImageFile;
        private string? _outputFolderPath;
        private bool _syncingResizeDimensions;
        private bool _syncingUpscaleDimensions;
        private readonly IImageProcessingService _imageProcessingService = new ImageProcessingService();

        public ImageEditorPage()
        {
            InitializeComponent();
            UpdateOptionUiState();
            RefreshValidation();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame?.CanGoBack == true)
            {
                Frame.GoBack(new DrillInNavigationTransitionInfo());
            }
        }

        private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
        }

        private void ApplyResponsiveLayout(double width)
        {
            var isNarrow = width < 980;

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
            _sourceImageFile = file;
            _outputFolderPath = Path.GetDirectoryName(file.Path);
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            DropHintPanel.Visibility = Visibility.Collapsed;
            LoadedImageInfoTextBlock.Text = $"Loaded image: {_originalImageWidth} x {_originalImageHeight} px";
            OutputFolderTextBlock.Text = _outputFolderPath ?? "Same folder as input image";
            if (_originalImageWidth.HasValue && _originalImageHeight.HasValue)
            {
                ApplyDimensionBounds(_originalImageWidth.Value, _originalImageHeight.Value);
                ResizeWidthNumberBox.Value = _originalImageWidth.Value;
                ResizeHeightNumberBox.Value = _originalImageHeight.Value;
                UpscaleWidthNumberBox.Value = _originalImageWidth.Value;
                UpscaleHeightNumberBox.Value = _originalImageHeight.Value;
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

        private void QualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (QualityPercentTextBlock is not null && QualitySlider is not null)
            {
                QualityPercentTextBlock.Text = $"Output quality: {(int)Math.Round(QualitySlider.Value)}%";
            }

            RefreshValidation();
        }

        private void RgbSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
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

            if (!_originalImageWidth.HasValue || !_originalImageHeight.HasValue || _originalImageWidth.Value <= 0 || _originalImageHeight.Value <= 0)
            {
                RefreshValidation();
                return;
            }

            var sourceRatio = (double)_originalImageWidth.Value / _originalImageHeight.Value;
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

            if (!_originalImageWidth.HasValue || !_originalImageHeight.HasValue || _originalImageWidth.Value <= 0 || _originalImageHeight.Value <= 0)
            {
                RefreshValidation();
                return;
            }

            var sourceRatio = (double)_originalImageWidth.Value / _originalImageHeight.Value;
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

        private void OptionsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateOptionUiState();
            RefreshValidation();
        }

        private void OptionsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshValidation();
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceImageFile is null)
            {
                return;
            }

            var options = BuildCurrentOptions();
            var validationErrors = ImageEditOptionsValidator.Validate(options, _originalImageWidth, _originalImageHeight);
            if (validationErrors.Count > 0)
            {
                RefreshValidation(validationErrors);
                return;
            }

            try
            {
                var outputFile = await ProcessImageAsync(_sourceImageFile, options);
                var dialog = new ContentDialog
                {
                    Title = "Done",
                    Content = $"Image saved to:\n{outputFile.Path}",
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
            SetPanelInteractive(QualitySelectorPanel, !(PreserveOriginalQualityCheckBox?.IsChecked ?? false));
            SetPanelInteractive(ResizeInputsGrid, EnableResizeCheckBox?.IsChecked ?? false);

            if (PreserveAspectRatioCheckBox is not null)
            {
                PreserveAspectRatioCheckBox.IsEnabled = EnableResizeCheckBox?.IsChecked ?? false;
            }

            SetPanelInteractive(UpscaleInputsGrid, EnableUpscaleCheckBox?.IsChecked ?? false);
            if (PreserveUpscaleAspectRatioCheckBox is not null)
            {
                PreserveUpscaleAspectRatioCheckBox.IsEnabled = EnableUpscaleCheckBox?.IsChecked ?? false;
            }

            if (RotationComboBox is not null)
            {
                RotationComboBox.IsEnabled = EnableRotationCheckBox?.IsChecked ?? false;
            }

            SetPanelInteractive(RgbControlsPanel, EnableRgbAdjustmentCheckBox?.IsChecked ?? false);
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
                _originalImageHeight);

            var upscaleErrors = currentErrors
                .Where(error => error.Contains("Upscale", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (UpscaleValidationTextBlock is not null)
            {
                UpscaleValidationTextBlock.Text = string.Join("\n", upscaleErrors);
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

            return new ImageEditOptions
            {
                OutputFormat = (ImageFileFormat)Math.Max(0, OutputFormatComboBox?.SelectedIndex ?? 0),
                PreserveOriginalQuality = PreserveOriginalQualityCheckBox?.IsChecked ?? true,
                QualityPercent = ToPercentage(QualitySlider?.Value ?? 90),
                EnableResize = EnableResizeCheckBox?.IsChecked ?? false,
                ResizeWidth = ToDimensionFromDouble(ResizeWidthNumberBox?.Value, 1),
                ResizeHeight = ToDimensionFromDouble(ResizeHeightNumberBox?.Value, 1),
                PreserveAspectRatio = PreserveAspectRatioCheckBox?.IsChecked ?? true,
                EnableUpscale = EnableUpscaleCheckBox?.IsChecked ?? false,
                UpscaleWidth = ToDimensionFromDouble(UpscaleWidthNumberBox?.Value, 1),
                UpscaleHeight = ToDimensionFromDouble(UpscaleHeightNumberBox?.Value, 1),
                EnableRotation = EnableRotationCheckBox?.IsChecked ?? false,
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
            if (!_originalImageWidth.HasValue || !_originalImageHeight.HasValue)
            {
                return;
            }

            ResizeWidthNumberBox.Value = Math.Clamp(ResizeWidthNumberBox.Value, 1, _originalImageWidth.Value);
            ResizeHeightNumberBox.Value = Math.Clamp(ResizeHeightNumberBox.Value, 1, _originalImageHeight.Value);
        }

        private void ClampUpscaleNumberBoxesToBounds()
        {
            if (!_originalImageWidth.HasValue || !_originalImageHeight.HasValue)
            {
                return;
            }

            UpscaleWidthNumberBox.Value = Math.Clamp(UpscaleWidthNumberBox.Value, _originalImageWidth.Value, 20000);
            UpscaleHeightNumberBox.Value = Math.Clamp(UpscaleHeightNumberBox.Value, _originalImageHeight.Value, 20000);
        }

        private static int ToPercentage(double value)
        {
            return Math.Clamp((int)Math.Round(value), 0, 200);
        }

        private static string BuildOptionsSummary(ImageEditOptions options)
        {
            return
                $"Format: {options.OutputFormat}\n" +
                $"Preserve quality: {options.PreserveOriginalQuality}\n" +
                $"Quality: {options.QualityPercent}%\n" +
                $"Resize enabled: {options.EnableResize} ({options.ResizeWidth} x {options.ResizeHeight})\n" +
                $"Preserve aspect ratio: {options.PreserveAspectRatio}\n" +
                $"Upscale enabled: {options.EnableUpscale} ({options.UpscaleWidth} x {options.UpscaleHeight})\n" +
                $"Rotation enabled: {options.EnableRotation} ({options.RotationDegrees} degrees)\n" +
                $"Mirror H/V: {options.MirrorHorizontally}/{options.MirrorVertically}\n" +
                $"RGB enabled: {options.EnableRgbAdjustments} ({options.RedPercent}% / {options.GreenPercent}% / {options.BluePercent}%)";
        }

        private async void SelectOutputFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            _outputFolderPath = folder.Path;
            OutputFolderTextBlock.Text = _outputFolderPath;
        }

        private async Task<StorageFile> ProcessImageAsync(StorageFile sourceFile, ImageEditOptions options)
        {
            var outputDirectory = _outputFolderPath ?? Path.GetDirectoryName(sourceFile.Path) ?? Path.GetTempPath();
            Directory.CreateDirectory(outputDirectory);
            var extension = GetOutputExtension(options.OutputFormat, sourceFile.FileType);
            var outputPath = Path.Combine(
                outputDirectory,
                $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}_edited_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");

            var processOptions = new ProcessImageOptions
            {
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

            await _imageProcessingService.ProcessImageAsync(sourceFile.Path, outputPath, processOptions, CancellationToken.None);
            return await StorageFile.GetFileFromPathAsync(outputPath);
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

        private static string GetOutputExtension(ImageFileFormat format, string sourceExtension)
        {
            return format switch
            {
                ImageFileFormat.KeepOriginal => sourceExtension,
                ImageFileFormat.Jpeg => ".jpg",
                ImageFileFormat.Png => ".png",
                ImageFileFormat.WebP => ".png",
                ImageFileFormat.Avif => ".avif",
                ImageFileFormat.Tiff => ".tiff",
                ImageFileFormat.Heif => ".heif",
                ImageFileFormat.Gif => ".gif",
                _ => ".png"
            };
        }
    }
}
