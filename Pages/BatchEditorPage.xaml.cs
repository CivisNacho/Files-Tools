using Files_Tools.Services;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

namespace Files_Tools.Pages
{
    public sealed partial class BatchEditorPage : Page
    {
        private const double MinimumTwoColumnBreakpoint = 980;
        private const double OptionsColumnRatio = 0.3;
        private const double OptionsPanelMinimumWidth = 320;

        private enum StyledSubtitleBasePreset { SocialImpact, CleanSans, CaptionBox, BroadcastLowerThird }
        private enum KaraokeSubtitleBasePreset { NeonKaraoke, Punch, Bubbly }

        private sealed class SubtitlePresetConfiguration
        {
            public StyledSubtitleBasePreset StyledPreset { get; set; } = StyledSubtitleBasePreset.SocialImpact;
            public KaraokeSubtitleBasePreset KaraokePreset { get; set; } = KaraokeSubtitleBasePreset.NeonKaraoke;
            public string FontFamily { get; set; } = "Impact";
            public double FontSize { get; set; } = 72d;
            public bool Bold { get; set; } = true;
            public SubtitleTextTransform TextTransform { get; set; } = SubtitleTextTransform.Uppercase;
            public double OutlineWidth { get; set; } = 5d;
            public int MarginVertical { get; set; } = 90;
            public SubtitleColor KaraokeHighlightColor { get; set; } = new(0, 255, 110, 0);

            public static SubtitlePresetConfiguration CreateDefault() => new()
            {
                StyledPreset = StyledSubtitleBasePreset.SocialImpact,
                KaraokePreset = KaraokeSubtitleBasePreset.NeonKaraoke,
                FontFamily = "Impact",
                KaraokeHighlightColor = new SubtitleColor(0, 255, 110, 0)
            };
        }

        private readonly IBatchProcessingService _batchService;
        private readonly ObservableCollection<BatchFileItem> _fileItems = new();
        private BatchAnalysis? _currentAnalysis;
        private CancellationTokenSource? _processingCts;
        private bool _isProcessing;
        private string? _lastOutputDirectory;
        private DateTimeOffset _processingStartTime;
        private SubtitlePresetConfiguration _batchAdvancedSubtitlePresetConfiguration = SubtitlePresetConfiguration.CreateDefault();
        private readonly IReadOnlyList<string> _batchInstalledFontFamilies = LoadInstalledFontFamilies();
        private DispatcherTimer? _elapsedTimer;

        // Sliding-window samples for smoothed throughput / ETA.
        private readonly Queue<(DateTimeOffset Time, double Fraction)> _progressSamples = new();
        private const int MaxSamples = 30;
        private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(15);
        private double _smoothedRemainingSec = -1;

        public BatchEditorPage()
        {
            InitializeComponent();

            _batchService = new BatchProcessingService(
                new AudioProcessingService(),
                new VideoProcessingService(),
                new DocumentService(),
                new PdfService(),
                new ImageProcessingService());

            FileListView.ItemsSource = _fileItems;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is BatchNavigationRequest request && request.Files.Count > 0)
            {
                AddFiles(request.Files);
            }
        }

        // ── Responsive layout ─────────────────────────────────────────────────

        private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
        }

        private void ApplyResponsiveLayout(double width)
        {
            if (width <= 0) return;

            var effectiveWidth = Math.Min(width, 1440);
            var availableForColumns = effectiveWidth - 64;

            if (availableForColumns < MinimumTwoColumnBreakpoint)
            {
                LeftColumn.Width  = new GridLength(1, GridUnitType.Star);
                RightColumn.Width = new GridLength(0);
                OptionsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                var optionsWidth = Math.Max(availableForColumns * OptionsColumnRatio, OptionsPanelMinimumWidth);
                var remainderWidth = availableForColumns - optionsWidth - 20;
                LeftColumn.Width  = new GridLength(remainderWidth, GridUnitType.Star);
                RightColumn.Width = new GridLength(optionsWidth, GridUnitType.Star);
                OptionsPanel.Visibility = Visibility.Visible;
            }
        }

        // ── File management ───────────────────────────────────────────────────

        private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is null) return;

            var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files is null || files.Count == 0) return;

            AddFiles(files);
        }

        private void ClearFilesButton_Click(object sender, RoutedEventArgs e)
        {
            _fileItems.Clear();
            _currentAnalysis = null;
            _lastOutputDirectory = null;
            RefreshUiAfterFileChange();
        }

        private void FileDropSurface_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (e.DragUIOverride is { } ui)
            {
                ui.Caption = "Add to batch";
                ui.IsCaptionVisible = true;
                ui.IsGlyphVisible = true;
            }
        }

        private async void FileDropSurface_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            var items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<StorageFile>().ToList();
            if (files.Count == 0) return;

            AddFiles(files);
        }

        private void AddFiles(IEnumerable<StorageFile> files)
        {
            var existingPaths = new HashSet<string>(
                _fileItems.Select(f => f.InputPath),
                StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                if (existingPaths.Add(file.Path))
                {
                    _fileItems.Add(new BatchFileItem
                    {
                        InputPath = file.Path,
                        FileName  = file.Name,
                        FileType  = FileTypeRegistry.Classify(file.FileType)
                    });
                }
            }

            RefreshUiAfterFileChange();
        }

        private void RefreshUiAfterFileChange()
        {
            var hasFiles = _fileItems.Count > 0;

            DropHintPanel.Visibility    = hasFiles ? Visibility.Collapsed : Visibility.Visible;
            FileListView.Visibility     = hasFiles ? Visibility.Visible   : Visibility.Collapsed;
            ClearFilesButton.Visibility = hasFiles ? Visibility.Visible   : Visibility.Collapsed;

            if (!hasFiles)
            {
                FileCountTextBlock.Text       = "Drop files below or click Add to get started.";
                ProcessStatusTextBlock.Text   = "No files loaded yet.";
                ProcessButton.IsEnabled       = false;
                UnknownFilesWarning.Visibility = Visibility.Collapsed;
                UpdateNavVisibility(null);
                // Refresh the subtitle of the currently visible panel to reflect "no files".
                RefreshCurrentPanelSubtitle();
                return;
            }

            _currentAnalysis = _batchService.AnalyzeBatch(
                _fileItems.Select(f => f.InputPath).ToList());

            UpdateNavVisibility(_currentAnalysis);

            var unknownCount = _currentAnalysis.UnknownFiles.Count;
            UnknownFilesWarning.Visibility = unknownCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (unknownCount > 0)
                UnknownFilesWarningText.Text =
                    $"{unknownCount} file{(unknownCount == 1 ? "" : "s")} with unrecognized extensions will be skipped.";

            var knownCount = _fileItems.Count - unknownCount;
            FileCountTextBlock.Text = knownCount > 0
                ? $"{_fileItems.Count} file{(_fileItems.Count == 1 ? "" : "s")} added · {_currentAnalysis.TypeGroups.Count} type group{(_currentAnalysis.TypeGroups.Count == 1 ? "" : "s")}"
                : $"{_fileItems.Count} file{(_fileItems.Count == 1 ? "" : "s")} added (none recognized)";

            ProcessButton.IsEnabled     = knownCount > 0 && !_isProcessing;
            ProcessStatusTextBlock.Text = "Ready to process.";

            RefreshCurrentPanelSubtitle();
        }

        private void UpdateNavVisibility(BatchAnalysis? analysis)
        {
            if (App.MainWindow is not MainWindow mainWindow) return;

            var activeTypes = analysis?.TypeGroups.Select(g => g.FileType).ToList()
                           ?? new List<BatchFileType>();
            mainWindow.UpdateBatchNavigationVisibility(activeTypes);
        }

        // ── Navigation-driven panel switching ─────────────────────────────────

        private string _currentTag = string.Empty;

        /// <summary>
        /// Called by <see cref="MainWindow"/> when a batch navigation item is selected.
        /// Shows the matching operation panel and hides the rest.
        /// </summary>
        public void ApplyOptionSelection(string tag)
        {
            _currentTag = tag;
            HideAllOperationPanels();
            NoSelectionHint.Visibility = Visibility.Collapsed;

            FrameworkElement? panel = tag switch
            {
                "Batch:Output"          => BatchOutputPanel,
                "BatchAudio:Format"     => BatchAudioFormatPanel,
                "BatchAudio:Compress"   => BatchAudioCompressPanel,
                "BatchAudio:Normalize"  => BatchAudioNormalizePanel,
                "BatchVideo:Container"  => BatchVideoContainerPanel,
                "BatchVideo:Codec"      => BatchVideoCodecPanel,
                "BatchVideo:Compress"   => BatchVideoCompressPanel,
                "BatchVideo:Resize"     => BatchVideoResizePanel,
                "BatchVideo:Extract"      => BatchVideoExtractPanel,
                "BatchVideo:Subtitles"   => BatchVideoSubtitlesPanel,
                "BatchDocument:Convert"  => BatchDocumentConvertPanel,
                "BatchPdf:Compress"     => BatchPdfCompressPanel,
                "BatchPdf:Repair"       => BatchPdfRepairPanel,
                "BatchPdf:OCR"          => BatchPdfOcrPanel,
                "BatchPdf:Merge"        => BatchPdfMergePanel,
                "BatchImage:Format"     => BatchImageFormatPanel,
                "BatchImage:Compress"   => BatchImageCompressPanel,
                "BatchImage:Resize"     => BatchImageResizePanel,
                _                       => null
            };

            if (panel is null)
            {
                NoSelectionHint.Visibility = Visibility.Visible;
                SelectedOperationHeaderTextBlock.Text    = "Select an operation";
                SelectedOperationSubtitleTextBlock.Text  = string.Empty;
                return;
            }

            panel.Visibility = Visibility.Visible;
            SelectedOperationHeaderTextBlock.Text   = GetPanelHeader(tag);
            SelectedOperationSubtitleTextBlock.Text = GetPanelSubtitle(tag);

            if (tag == "BatchVideo:Subtitles")
                RefreshTranscriptionDownloadUi();
        }

        private void HideAllOperationPanels()
        {
            BatchOutputPanel.Visibility         = Visibility.Collapsed;
            BatchAudioFormatPanel.Visibility     = Visibility.Collapsed;
            BatchAudioCompressPanel.Visibility   = Visibility.Collapsed;
            BatchAudioNormalizePanel.Visibility  = Visibility.Collapsed;
            BatchVideoContainerPanel.Visibility  = Visibility.Collapsed;
            BatchVideoCodecPanel.Visibility      = Visibility.Collapsed;
            BatchVideoCompressPanel.Visibility   = Visibility.Collapsed;
            BatchVideoResizePanel.Visibility     = Visibility.Collapsed;
            BatchVideoExtractPanel.Visibility       = Visibility.Collapsed;
            BatchVideoSubtitlesPanel.Visibility     = Visibility.Collapsed;
            BatchDocumentConvertPanel.Visibility = Visibility.Collapsed;
            BatchPdfCompressPanel.Visibility     = Visibility.Collapsed;
            BatchPdfRepairPanel.Visibility       = Visibility.Collapsed;
            BatchPdfOcrPanel.Visibility          = Visibility.Collapsed;
            BatchPdfMergePanel.Visibility        = Visibility.Collapsed;
            BatchImageFormatPanel.Visibility     = Visibility.Collapsed;
            BatchImageCompressPanel.Visibility   = Visibility.Collapsed;
            BatchImageResizePanel.Visibility     = Visibility.Collapsed;
        }

        private static string GetPanelHeader(string tag) => tag switch
        {
            "Batch:Output"          => "Output settings",
            "BatchAudio:Format"     => "Audio · Format",
            "BatchAudio:Compress"   => "Audio · Compress",
            "BatchAudio:Normalize"  => "Audio · Normalize",
            "BatchVideo:Container"  => "Video · Container",
            "BatchVideo:Codec"      => "Video · Codec",
            "BatchVideo:Compress"   => "Video · Compress",
            "BatchVideo:Resize"     => "Video · Resize",
            "BatchVideo:Extract"      => "Video · Extract audio",
            "BatchVideo:Subtitles"   => "Video · Subtitles",
            "BatchDocument:Convert"  => "Documents · Convert to PDF",
            "BatchPdf:Compress"     => "PDF · Compress",
            "BatchPdf:Repair"       => "PDF · Repair",
            "BatchPdf:OCR"          => "PDF · OCR",
            "BatchPdf:Merge"        => "PDF · Merge",
            "BatchImage:Format"     => "Images · Format",
            "BatchImage:Compress"   => "Images · Compress",
            "BatchImage:Resize"     => "Images · Resize",
            _                       => "Select an operation"
        };

        private string GetPanelSubtitle(string tag)
        {
            if (tag == "Batch:Output") return string.Empty;

            var type = tag switch
            {
                "BatchAudio:Format" or "BatchAudio:Compress" or "BatchAudio:Normalize"
                    => BatchFileType.Audio,
                "BatchVideo:Container" or "BatchVideo:Codec" or "BatchVideo:Compress"
                    or "BatchVideo:Resize" or "BatchVideo:Extract" or "BatchVideo:Subtitles"
                    => BatchFileType.Video,
                "BatchDocument:Convert" => BatchFileType.Document,
                "BatchPdf:Compress" or "BatchPdf:Repair" or "BatchPdf:OCR" or "BatchPdf:Merge"
                    => BatchFileType.Pdf,
                "BatchImage:Format" or "BatchImage:Compress" or "BatchImage:Resize"
                    => BatchFileType.Image,
                _   => (BatchFileType?)null
            };

            if (type is null) return string.Empty;

            var group = _currentAnalysis?.TypeGroups.FirstOrDefault(g => g.FileType == type.Value);
            if (group is null) return "No files of this type in batch";
            var count = group.Files.Count;
            return count == 1 ? "1 file in batch" : $"{count} files in batch";
        }

        private void RefreshCurrentPanelSubtitle()
        {
            if (string.IsNullOrEmpty(_currentTag)) return;
            SelectedOperationSubtitleTextBlock.Text = GetPanelSubtitle(_currentTag);
        }

        private void ImageQualitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ImageQualityTextBlock is not null)
                ImageQualityTextBlock.Text = $"Quality: {(int)e.NewValue}%";
        }

        private void RefreshTranscriptionDownloadUi()
        {
            var isInstalled = new AudioTranscriptionService().IsInstalled();
            var downloadVisible = isInstalled ? Visibility.Collapsed : Visibility.Visible;

            BatchDownloadTranscriptionButton.Visibility          = downloadVisible;
            BatchTranscriptionDownloadProgressBar.Visibility     = Visibility.Collapsed;
            BatchTranscriptionDownloadStatusTextBlock.Visibility = downloadVisible;
        }

        private void BatchSubtitleGenerationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BatchAdvancedSubtitleOptionsPanel is null) return;
            BatchAdvancedSubtitleOptionsPanel.Visibility =
                BatchSubtitleGenerationModeComboBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BatchSubtitleSectionDurationSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (BatchSubtitleSectionDurationTextBlock is null || BatchSubtitleSectionDurationNumberBox is null) return;
            BatchSubtitleSectionDurationNumberBox.Value = e.NewValue;
            BatchSubtitleSectionDurationTextBlock.Text = $"Section length: {e.NewValue:F1} s max";
        }

        private void BatchSubtitleSectionDurationNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (BatchSubtitleSectionDurationSlider is null || BatchSubtitleSectionDurationTextBlock is null) return;
            if (double.IsNaN(args.NewValue)) return;
            BatchSubtitleSectionDurationSlider.Value = args.NewValue;
            BatchSubtitleSectionDurationTextBlock.Text = $"Section length: {args.NewValue:F1} s max";
        }

        private void BatchEnableSubtitleWordCapCheckBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (BatchSubtitleWordCapNumberBox is null) return;
            BatchSubtitleWordCapNumberBox.IsEnabled = BatchEnableSubtitleWordCapCheckBox.IsChecked == true;
        }

        private async void BatchDownloadTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            BatchDownloadTranscriptionButton.IsEnabled = false;
            BatchTranscriptionDownloadProgressBar.Visibility = Visibility.Visible;
            BatchTranscriptionDownloadStatusTextBlock.Text = "Downloading transcription feature...";

            try
            {
                var transcriptionService = new AudioTranscriptionService();
                var progress = new Progress<AudioTranscriptionInstallProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        BatchTranscriptionDownloadProgressBar.Value = p.FractionComplete;
                        BatchTranscriptionDownloadStatusTextBlock.Text = p.Stage;
                    });
                });
                await transcriptionService.InstallAsync(progress);
                RefreshTranscriptionDownloadUi();
            }
            catch (Exception ex)
            {
                BatchTranscriptionDownloadStatusTextBlock.Text = $"Download failed: {ex.Message}";
                BatchDownloadTranscriptionButton.IsEnabled = true;
            }
            finally
            {
                BatchTranscriptionDownloadProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void BatchAdvancedSubtitleTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BatchUpdateAdvancedSubtitlePresetSummary();
        }

        private async void BatchConfigureAdvancedSubtitlesButton_Click(object sender, RoutedEventArgs e)
        {
            var isKaraokeMode = BatchIsKaraokeAdvancedSubtitleTypeSelected();

            var basePresetComboBox = new ComboBox { Header = "Base preset" };
            if (isKaraokeMode)
            {
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "NeonKaraoke" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "Punch" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "Bubbly" });
                basePresetComboBox.SelectedIndex = _batchAdvancedSubtitlePresetConfiguration.KaraokePreset switch
                {
                    KaraokeSubtitleBasePreset.Punch  => 1,
                    KaraokeSubtitleBasePreset.Bubbly => 2,
                    _                                => 0
                };
            }
            else
            {
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "SocialImpact" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "CleanSans" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "CaptionBox" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "BroadcastLowerThird" });
                basePresetComboBox.SelectedIndex = _batchAdvancedSubtitlePresetConfiguration.StyledPreset switch
                {
                    StyledSubtitleBasePreset.CleanSans           => 1,
                    StyledSubtitleBasePreset.CaptionBox          => 2,
                    StyledSubtitleBasePreset.BroadcastLowerThird => 3,
                    _                                            => 0
                };
            }

            var fontSizeNumberBox = new NumberBox
            {
                Header = "Font size", Minimum = 24, Maximum = 160, SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _batchAdvancedSubtitlePresetConfiguration.FontSize
            };
            var fontFamilyComboBox = new ComboBox
            {
                Header = "System font",
                ItemsSource = _batchInstalledFontFamilies.ToList(),
                PlaceholderText = "Select an installed font"
            };
            if (_batchInstalledFontFamilies.Contains(_batchAdvancedSubtitlePresetConfiguration.FontFamily, StringComparer.OrdinalIgnoreCase))
                fontFamilyComboBox.SelectedItem = _batchInstalledFontFamilies.First(n => string.Equals(n, _batchAdvancedSubtitlePresetConfiguration.FontFamily, StringComparison.OrdinalIgnoreCase));

            var outlineNumberBox = new NumberBox
            {
                Header = "Outline width", Minimum = 0, Maximum = 20, SmallChange = 0.5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _batchAdvancedSubtitlePresetConfiguration.OutlineWidth
            };
            var marginVerticalNumberBox = new NumberBox
            {
                Header = "Vertical margin", Minimum = 0, Maximum = 400, SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _batchAdvancedSubtitlePresetConfiguration.MarginVertical
            };
            var boldCheckBox = new CheckBox { Content = "Bold text", IsChecked = _batchAdvancedSubtitlePresetConfiguration.Bold };
            var textTransformComboBox = new ComboBox { Header = "Text transform" };
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = "Original case" });
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = "UPPERCASE" });
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = "lowercase" });
            textTransformComboBox.SelectedIndex = _batchAdvancedSubtitlePresetConfiguration.TextTransform switch
            {
                SubtitleTextTransform.Uppercase => 1,
                SubtitleTextTransform.Lowercase => 2,
                _                               => 0
            };
            var karaokeAccentColorPicker = new ColorPicker
            {
                Color = ToUiColor(_batchAdvancedSubtitlePresetConfiguration.KaraokeHighlightColor),
                Visibility = isKaraokeMode ? Visibility.Visible : Visibility.Collapsed,
                IsEnabled  = isKaraokeMode
            };
            var karaokeAccentLabel = new TextBlock
            {
                Text = "Karaoke highlight color",
                Visibility = isKaraokeMode ? Visibility.Visible : Visibility.Collapsed
            };

            basePresetComboBox.SelectionChanged += (_, _) =>
            {
                var defaultFont = GetBatchDefaultFontFamilyForPreset(basePresetComboBox.SelectedIndex, isKaraokeMode);
                if (_batchInstalledFontFamilies.Contains(defaultFont, StringComparer.OrdinalIgnoreCase))
                    fontFamilyComboBox.SelectedItem = _batchInstalledFontFamilies.First(n => string.Equals(n, defaultFont, StringComparison.OrdinalIgnoreCase));

                SubtitleStylePreset defaults = isKaraokeMode
                    ? basePresetComboBox.SelectedIndex switch
                    {
                        1 => KaraokeSubtitlePresets.CreatePunch(),
                        2 => KaraokeSubtitlePresets.CreateBubbly(),
                        _ => KaraokeSubtitlePresets.CreateNeonKaraoke()
                    }
                    : basePresetComboBox.SelectedIndex switch
                    {
                        1 => StyledSubtitlePresets.CreateCleanSans(),
                        2 => StyledSubtitlePresets.CreateCaptionBox(),
                        3 => StyledSubtitlePresets.CreateBroadcastLowerThird(),
                        _ => StyledSubtitlePresets.CreateSocialImpact()
                    };

                fontSizeNumberBox.Value = defaults.FontSize;
                outlineNumberBox.Value  = defaults.OutlineWidth;
                marginVerticalNumberBox.Value = defaults.MarginVertical;
                boldCheckBox.IsChecked  = defaults.Bold;
                textTransformComboBox.SelectedIndex = defaults.TextTransform switch
                {
                    SubtitleTextTransform.Uppercase => 1,
                    SubtitleTextTransform.Lowercase => 2,
                    _                               => 0
                };
            };

            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = "Choose a preset and tune the typography, presentation, and karaoke accent used for advanced subtitle rendering.",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.76
            });
            content.Children.Add(basePresetComboBox);
            content.Children.Add(fontFamilyComboBox);
            content.Children.Add(fontSizeNumberBox);
            content.Children.Add(outlineNumberBox);
            content.Children.Add(marginVerticalNumberBox);
            content.Children.Add(boldCheckBox);
            content.Children.Add(textTransformComboBox);
            content.Children.Add(karaokeAccentLabel);
            content.Children.Add(karaokeAccentColorPicker);

            var dialog = new ContentDialog
            {
                Title = "Configure advanced subtitles",
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = 560, MinWidth = 420, Content = content
                },
                PrimaryButtonText   = "Save",
                SecondaryButtonText = "Reset defaults",
                CloseButtonText     = "Cancel",
                XamlRoot            = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                _batchAdvancedSubtitlePresetConfiguration = SubtitlePresetConfiguration.CreateDefault();
                BatchUpdateAdvancedSubtitlePresetSummary();
                return;
            }
            if (result != ContentDialogResult.Primary) return;

            var newConfig = SubtitlePresetConfiguration.CreateDefault();
            newConfig.FontFamily     = (fontFamilyComboBox.SelectedItem as string) ?? GetBatchDefaultFontFamilyForPreset(basePresetComboBox.SelectedIndex, isKaraokeMode);
            newConfig.FontSize       = Math.Clamp(double.IsNaN(fontSizeNumberBox.Value) ? 72d : fontSizeNumberBox.Value, 24d, 160d);
            newConfig.OutlineWidth   = Math.Clamp(double.IsNaN(outlineNumberBox.Value) ? 5d : outlineNumberBox.Value, 0d, 20d);
            newConfig.MarginVertical = Math.Clamp(double.IsNaN(marginVerticalNumberBox.Value) ? 90 : (int)Math.Round(marginVerticalNumberBox.Value), 0, 400);
            newConfig.Bold           = boldCheckBox.IsChecked == true;
            newConfig.TextTransform  = textTransformComboBox.SelectedIndex switch
            {
                1 => SubtitleTextTransform.Uppercase,
                2 => SubtitleTextTransform.Lowercase,
                _ => SubtitleTextTransform.None
            };
            newConfig.KaraokeHighlightColor = FromUiColor(karaokeAccentColorPicker.Color);

            if (isKaraokeMode)
                newConfig.KaraokePreset = basePresetComboBox.SelectedIndex switch
                {
                    1 => KaraokeSubtitleBasePreset.Punch,
                    2 => KaraokeSubtitleBasePreset.Bubbly,
                    _ => KaraokeSubtitleBasePreset.NeonKaraoke
                };
            else
                newConfig.StyledPreset = basePresetComboBox.SelectedIndex switch
                {
                    1 => StyledSubtitleBasePreset.CleanSans,
                    2 => StyledSubtitleBasePreset.CaptionBox,
                    3 => StyledSubtitleBasePreset.BroadcastLowerThird,
                    _ => StyledSubtitleBasePreset.SocialImpact
                };

            _batchAdvancedSubtitlePresetConfiguration = newConfig;
            BatchUpdateAdvancedSubtitlePresetSummary();
        }

        private void BatchUpdateAdvancedSubtitlePresetSummary()
        {
            if (BatchAdvancedSubtitlePresetSummaryTextBlock is null) return;
            var isKaraokeMode = BatchIsKaraokeAdvancedSubtitleTypeSelected();
            var (presetName, presentation) = isKaraokeMode
                ? (_batchAdvancedSubtitlePresetConfiguration.KaraokePreset.ToString(), _batchAdvancedSubtitlePresetConfiguration.KaraokePreset switch
                {
                    KaraokeSubtitleBasePreset.Punch  => "Punch karaoke",
                    KaraokeSubtitleBasePreset.Bubbly => "Bubbly karaoke",
                    _                                => "Neon karaoke"
                })
                : (_batchAdvancedSubtitlePresetConfiguration.StyledPreset.ToString(), _batchAdvancedSubtitlePresetConfiguration.StyledPreset switch
                {
                    StyledSubtitleBasePreset.CaptionBox          => "Boxed caption",
                    StyledSubtitleBasePreset.BroadcastLowerThird => "Lower third",
                    StyledSubtitleBasePreset.CleanSans           => "Clean fade",
                    _                                            => "Impact pop"
                });
            var textTransform = _batchAdvancedSubtitlePresetConfiguration.TextTransform switch
            {
                SubtitleTextTransform.Uppercase => "Uppercase",
                SubtitleTextTransform.Lowercase => "Lowercase",
                _                               => "Original case"
            };
            var fontWeight = _batchAdvancedSubtitlePresetConfiguration.Bold ? "Bold" : "Regular";
            BatchAdvancedSubtitlePresetSummaryTextBlock.Text =
                $"Preset: {presetName} ({presentation}) | {_batchAdvancedSubtitlePresetConfiguration.FontFamily} {_batchAdvancedSubtitlePresetConfiguration.FontSize:0.#} | {fontWeight} | {textTransform} | Outline {_batchAdvancedSubtitlePresetConfiguration.OutlineWidth:0.#}";
        }

        private void BatchResetSubtitlePlacementButton_Click(object sender, RoutedEventArgs e)
        {
            if (BatchSubtitlePlacementXNumberBox is not null) BatchSubtitlePlacementXNumberBox.Value = 50;
            if (BatchSubtitlePlacementYNumberBox is not null) BatchSubtitlePlacementYNumberBox.Value = 88;
        }

        private bool BatchIsKaraokeAdvancedSubtitleTypeSelected() =>
            (BatchAdvancedSubtitleTypeComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Karaoke ASS";

        private SubtitleStylePreset CreateBatchSubtitleStylePreset()
        {
            var isKaraokeMode = BatchIsKaraokeAdvancedSubtitleTypeSelected();
            var basePreset = isKaraokeMode
                ? _batchAdvancedSubtitlePresetConfiguration.KaraokePreset switch
                {
                    KaraokeSubtitleBasePreset.Punch  => KaraokeSubtitlePresets.CreatePunch(),
                    KaraokeSubtitleBasePreset.Bubbly => KaraokeSubtitlePresets.CreateBubbly(),
                    _                                => KaraokeSubtitlePresets.CreateNeonKaraoke()
                }
                : _batchAdvancedSubtitlePresetConfiguration.StyledPreset switch
                {
                    StyledSubtitleBasePreset.CleanSans           => StyledSubtitlePresets.CreateCleanSans(),
                    StyledSubtitleBasePreset.CaptionBox          => StyledSubtitlePresets.CreateCaptionBox(),
                    StyledSubtitleBasePreset.BroadcastLowerThird => StyledSubtitlePresets.CreateBroadcastLowerThird(),
                    _                                            => StyledSubtitlePresets.CreateSocialImpact()
                };

            return new SubtitleStylePreset
            {
                Name = basePreset.Name,
                AssStyleName = basePreset.AssStyleName,
                ScriptTitle = basePreset.ScriptTitle,
                PlayResX = basePreset.PlayResX,
                PlayResY = basePreset.PlayResY,
                WrapStyle = basePreset.WrapStyle,
                ScaledBorderAndShadow = basePreset.ScaledBorderAndShadow,
                PrimaryFontFamily = _batchAdvancedSubtitlePresetConfiguration.FontFamily,
                FontFamilyFallbacks = BuildBatchFontFallbacks(_batchAdvancedSubtitlePresetConfiguration.FontFamily, basePreset.FontFamilyFallbacks),
                FontSize = _batchAdvancedSubtitlePresetConfiguration.FontSize,
                Bold = _batchAdvancedSubtitlePresetConfiguration.Bold,
                Italic = basePreset.Italic,
                TextTransform = _batchAdvancedSubtitlePresetConfiguration.TextTransform,
                FillColor = basePreset.FillColor,
                OutlineColor = basePreset.OutlineColor,
                ShadowColor = basePreset.ShadowColor,
                KaraokeHighlightColor = _batchAdvancedSubtitlePresetConfiguration.KaraokeHighlightColor,
                UseBackgroundBox = basePreset.UseBackgroundBox,
                PresentationAnimation = basePreset.PresentationAnimation,
                EntryFadeMilliseconds = basePreset.EntryFadeMilliseconds,
                ExitFadeMilliseconds = basePreset.ExitFadeMilliseconds,
                IntroScale = basePreset.IntroScale,
                OutlineWidth = _batchAdvancedSubtitlePresetConfiguration.OutlineWidth,
                ShadowDepth = basePreset.ShadowDepth,
                Alignment = basePreset.Alignment,
                MarginLeft = basePreset.MarginLeft,
                MarginRight = basePreset.MarginRight,
                MarginVertical = _batchAdvancedSubtitlePresetConfiguration.MarginVertical,
                PositionX = basePreset.PositionX,
                PositionY = basePreset.PositionY,
                MaxLines = basePreset.MaxLines,
                MaxCharsPerLine = basePreset.MaxCharsPerLine
            };
        }

        private static string GetBatchDefaultFontFamilyForPreset(int basePresetIndex, bool isKaraokeMode)
        {
            if (isKaraokeMode)
                return basePresetIndex switch { 1 => "Arial Black", 2 => "Bahnschrift", _ => "Segoe UI Semibold" };
            return basePresetIndex switch { 1 => "Segoe UI", 2 => "Arial", 3 => "Segoe UI Semibold", _ => "Impact" };
        }

        private static IReadOnlyList<string> BuildBatchFontFallbacks(string primary, IReadOnlyList<string> existingFallbacks)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(primary)) result.Add(primary.Trim());
            foreach (var fallback in existingFallbacks)
            {
                if (!string.IsNullOrWhiteSpace(fallback) && !result.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                    result.Add(fallback);
            }
            if (result.Count == 0) result.Add("Arial");
            return result;
        }

        private static IReadOnlyList<string> LoadInstalledFontFamilies()
        {
            try
            {
                var names = CanvasTextFormat.GetSystemFontFamilies()
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (names.Length > 0) return names;
            }
            catch { }
            return new[] { "Impact", "Segoe UI", "Arial" };
        }

        private static Color ToUiColor(SubtitleColor color) =>
            Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

        private static SubtitleColor FromUiColor(Color color) =>
            new(color.A, color.R, color.G, color.B);

        // ── Processing ────────────────────────────────────────────────────────

        private async void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalysis is null || _isProcessing) return;

            var plan = BuildBatchPlan();
            if (plan is null)
            {
                ProcessStatusTextBlock.Text = "Configure at least one type group operation.";
                return;
            }

            // ── Ask where to save the output ZIP ─────────────────────────────
            if (App.MainWindow is null) return;

            var savePicker = new FileSavePicker { SuggestedFileName = "batch_output" };
            savePicker.FileTypeChoices.Add("ZIP archive", new List<string> { ".zip" });

            WinRT.Interop.InitializeWithWindow.Initialize(
                savePicker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var zipStorageFile = await savePicker.PickSaveFileAsync();
            if (zipStorageFile is null) return; // user cancelled

            var outputZipPath = zipStorageFile.Path;
            var tempOutputDir = Path.Combine(Path.GetTempPath(), $"batch_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempOutputDir);

            // ── Lock UI and start progress display ───────────────────────────
            _isProcessing = true;
            _lastOutputDirectory = null;
            _processingCts = new CancellationTokenSource();

            ProcessButton.IsEnabled           = false;
            CancelButton.Visibility           = Visibility.Visible;
            AddFilesButton.IsEnabled          = false;
            ClearFilesButton.IsEnabled        = false;
            OpenOutputFolderButton.Visibility = Visibility.Collapsed;

            _processingStartTime             = DateTimeOffset.UtcNow;
            _progressSamples.Clear();
            _smoothedRemainingSec            = -1;
            ProcessProgressBar.IsIndeterminate = true;
            ProcessProgressBar.Value           = 0;
            ProcessProgressLabel.Text          = "Preparing…";
            ProcessProgressPercentTextBlock.Text = "—";
            ProcessCurrentFileTextBlock.Text   = string.Empty;
            ProcessElapsedTextBlock.Text       = "Elapsed 0s";
            ProcessEtaTextBlock.Text           = "Estimating…";
            ProcessFileProgressBar.Visibility  = Visibility.Collapsed;
            ProcessingProgressPanel.Visibility = Visibility.Visible;
            ProcessStatusTextBlock.Visibility  = Visibility.Collapsed;

            StartElapsedTimer();

            foreach (var item in _fileItems)
                item.Status = BatchFileItemStatus.Pending;

            var options = new BatchOptions
            {
                OutputDirectory        = tempOutputDir,
                OutputSuffix           = OutputSuffixTextBox.Text is { Length: > 0 } s ? s : "_processed",
                MaxDegreeOfParallelism = (int)MaxParallelismNumberBox.Value
            };

            var progress = new Progress<BatchProcessProgress>(OnBatchProgress);

            BatchResult? result = null;
            try
            {
                result = await _batchService.ProcessBatchAsync(
                    plan, options, progress, _processingCts.Token);
            }
            catch (OperationCanceledException)
            {
                ProcessStatusTextBlock.Text = "Batch cancelled.";
            }
            catch (Exception ex)
            {
                ProcessStatusTextBlock.Text = $"Unexpected error: {ex.Message}";
            }
            finally
            {
                _isProcessing = false;
                _processingCts.Dispose();
                _processingCts = null;

                StopElapsedTimer();
                ProcessProgressBar.IsIndeterminate = false;
                ProcessingProgressPanel.Visibility = Visibility.Collapsed;
                ProcessStatusTextBlock.Visibility  = Visibility.Visible;
                CancelButton.Visibility            = Visibility.Collapsed;
                ProcessButton.IsEnabled            = true;
                AddFilesButton.IsEnabled           = true;
                ClearFilesButton.IsEnabled         = true;
            }

            if (result is null)
            {
                TryDeleteDirectory(tempOutputDir);
                return;
            }

            ApplyResultsToItems(result);

            var successText = $"{result.SuccessCount} succeeded";
            var failText    = result.FailureCount > 0 ? $", {result.FailureCount} failed"  : string.Empty;
            var skipText    = result.SkippedCount > 0 ? $", {result.SkippedCount} skipped" : string.Empty;

            // ── Nothing succeeded or was cancelled → skip zip ─────────────────
            if (result.WasCancelled || result.SuccessCount == 0)
            {
                ProcessStatusTextBlock.Text = result.WasCancelled
                    ? $"Cancelled. {successText}{failText}{skipText}."
                    : $"Done. {successText}{failText}{skipText}.";
                TryDeleteDirectory(tempOutputDir);
                return;
            }

            // ── ZIP the processed files ───────────────────────────────────────
            ProcessStatusTextBlock.Text = "Creating ZIP archive…";
            try
            {
                if (File.Exists(outputZipPath))
                    File.Delete(outputZipPath);

                await Task.Run(() => ZipFile.CreateFromDirectory(tempOutputDir, outputZipPath));

                _lastOutputDirectory = Path.GetDirectoryName(outputZipPath);
                OpenOutputFolderButton.Visibility = Visibility.Visible;

                var zipName = Path.GetFileName(outputZipPath);
                ProcessStatusTextBlock.Text = $"{successText}{failText}{skipText} → {zipName}";
            }
            catch (Exception ex)
            {
                ProcessStatusTextBlock.Text =
                    $"{successText}{failText}{skipText} — ZIP failed: {ex.Message}";
            }
            finally
            {
                TryDeleteDirectory(tempOutputDir);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch { /* best effort */ }
        }

        private void OnBatchProgress(BatchProcessProgress progress)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var isAnalyzing = progress.Stage == BatchProcessStage.Analyzing
                                  || progress.Stage == BatchProcessStage.Queued;

                // ── Overall bar + percentage ──────────────────────────────────
                if (isAnalyzing && progress.OverallFraction <= 0)
                {
                    ProcessProgressBar.IsIndeterminate = true;
                    ProcessProgressPercentTextBlock.Text = "—";
                }
                else
                {
                    ProcessProgressBar.IsIndeterminate = false;
                    ProcessProgressBar.Value = progress.OverallFraction;
                    var pct = (int)Math.Round(progress.OverallFraction * 100);
                    ProcessProgressPercentTextBlock.Text = $"{pct}%";
                }

                // ── Label: "File X of Y" or stage description ─────────────────
                if (progress.CurrentFilePath is { } path && progress.TotalFileCount > 0)
                {
                    ProcessProgressLabel.Text    = $"File {progress.CurrentFileIndex} of {progress.TotalFileCount}";
                    ProcessCurrentFileTextBlock.Text = Path.GetFileName(path);
                }
                else
                {
                    ProcessProgressLabel.Text        = progress.StageDescription;
                    ProcessCurrentFileTextBlock.Text = string.Empty;
                }

                // ── Sample throughput and update ETA ──────────────────────────
                UpdateEtaFromSample(progress.OverallFraction);

                // ── Per-file sub-progress ─────────────────────────────────────
                if (progress.FileProgress is { } fp)
                {
                    ProcessFileProgressBar.Visibility = Visibility.Visible;
                    ProcessFileProgressBar.Value      = fp;
                }
                else
                {
                    ProcessFileProgressBar.Visibility = Visibility.Collapsed;
                }

                // ── Mark list-item as Processing ──────────────────────────────
                if (progress.CurrentFilePath is not null)
                {
                    var item = _fileItems.FirstOrDefault(f =>
                        string.Equals(f.InputPath, progress.CurrentFilePath, StringComparison.OrdinalIgnoreCase));
                    if (item is not null && item.Status == BatchFileItemStatus.Pending)
                        item.Status = BatchFileItemStatus.Processing;
                }
            });
        }

        // ── Progress: ETA smoothing + elapsed timer ──────────────────────────

        private void StartElapsedTimer()
        {
            StopElapsedTimer();
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += OnElapsedTimerTick;
            _elapsedTimer.Start();
        }

        private void StopElapsedTimer()
        {
            if (_elapsedTimer is null) return;
            _elapsedTimer.Stop();
            _elapsedTimer.Tick -= OnElapsedTimerTick;
            _elapsedTimer = null;
        }

        private void OnElapsedTimerTick(object? sender, object e)
        {
            var elapsed = DateTimeOffset.UtcNow - _processingStartTime;
            ProcessElapsedTextBlock.Text = $"Elapsed {FormatDuration(elapsed)}";

            // Decay the smoothed remaining estimate so it ticks down between samples.
            if (_smoothedRemainingSec > 0)
            {
                _smoothedRemainingSec = Math.Max(0, _smoothedRemainingSec - 1);
                ProcessEtaTextBlock.Text = $"{FormatDuration(TimeSpan.FromSeconds(_smoothedRemainingSec))} left";
            }
        }

        private void UpdateEtaFromSample(double fraction)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _processingStartTime;

            if (fraction <= 0 || fraction >= 1)
            {
                if (fraction >= 1) ProcessEtaTextBlock.Text = "Done";
                return;
            }

            _progressSamples.Enqueue((now, fraction));
            while (_progressSamples.Count > MaxSamples
                   || (_progressSamples.Count > 2 && now - _progressSamples.Peek().Time > SampleWindow))
            {
                _progressSamples.Dequeue();
            }

            // Need ≥ 2 samples and ≥ 1 s of data to estimate; also gate on overall progress.
            if (_progressSamples.Count < 2 || elapsed.TotalSeconds < 2 || fraction < 0.02)
            {
                ProcessEtaTextBlock.Text = "Estimating…";
                return;
            }

            var first = _progressSamples.Peek();
            var deltaFraction = fraction - first.Fraction;
            var deltaSec = (now - first.Time).TotalSeconds;

            double remainSec;
            if (deltaFraction > 1e-4 && deltaSec > 0.5)
            {
                // Recent throughput: how many seconds per remaining unit of progress.
                var ratePerSec = deltaFraction / deltaSec;
                remainSec = (1.0 - fraction) / ratePerSec;
            }
            else
            {
                // Fallback to linear projection from start.
                remainSec = elapsed.TotalSeconds * (1.0 - fraction) / fraction;
            }

            // Exponential smoothing to reduce jitter between samples.
            const double alpha = 0.35;
            _smoothedRemainingSec = _smoothedRemainingSec < 0
                ? remainSec
                : alpha * remainSec + (1 - alpha) * _smoothedRemainingSec;

            ProcessEtaTextBlock.Text = $"{FormatDuration(TimeSpan.FromSeconds(_smoothedRemainingSec))} left";
        }

        private static string FormatDuration(TimeSpan t)
        {
            if (t.TotalSeconds < 1) return "0s";
            if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds}s";
            if (t.TotalMinutes < 60) return t.Seconds == 0 ? $"{(int)t.TotalMinutes}m" : $"{(int)t.TotalMinutes}m {t.Seconds}s";
            return t.Minutes == 0 ? $"{(int)t.TotalHours}h" : $"{(int)t.TotalHours}h {t.Minutes}m";
        }

        private void ApplyResultsToItems(BatchResult result)
        {
            var resultsByPath = result.FileResults.ToDictionary(
                r => r.InputPath,
                r => r,
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in _fileItems)
            {
                if (!resultsByPath.TryGetValue(item.InputPath, out var fileResult)) continue;

                if (fileResult.OutputPath is null && fileResult.Error is null && !fileResult.IsSuccess)
                {
                    item.Status = BatchFileItemStatus.Skipped;
                }
                else if (fileResult.IsSuccess)
                {
                    item.Status = BatchFileItemStatus.Success;
                    item.OutputPath = fileResult.OutputPath;
                }
                else
                {
                    item.Status = BatchFileItemStatus.Failed;
                    item.ErrorMessage = fileResult.Error?.Message ?? "Unknown error";
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _processingCts?.Cancel();
        }

        private async void OpenOutputFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastOutputDirectory is null || !Directory.Exists(_lastOutputDirectory)) return;
            await Launcher.LaunchFolderPathAsync(_lastOutputDirectory);
        }

        // ── Plan builder ──────────────────────────────────────────────────────

        private BatchPlan? BuildBatchPlan()
        {
            if (_currentAnalysis is null) return null;

            var groupPlans = new List<BatchTypeGroupPlan>();

            // ── Audio ──────────────────────────────────────────────────────────
            if (_currentAnalysis.TypeGroups.Any(g => g.FileType == BatchFileType.Audio))
            {
                if (EnableAudioFormatCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Audio, Operation = BuildAudioFormatOp() });
                if (EnableAudioCompressCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Audio, Operation = BuildAudioCompressOp() });
                if (EnableAudioNormalizeCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Audio, Operation = BuildAudioNormalizeOp() });
            }

            // ── Video ──────────────────────────────────────────────────────────
            if (_currentAnalysis.TypeGroups.Any(g => g.FileType == BatchFileType.Video))
            {
                if (EnableVideoContainerCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Video, Operation = BuildVideoContainerOp() });
                if (EnableVideoCodecCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Video, Operation = BuildVideoCodecOp() });
                if (EnableVideoCompressCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Video, Operation = BuildVideoCompressOp() });
                if (EnableVideoResizeCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Video, Operation = BuildVideoResizeOp() });
                if (EnableVideoExtractCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Video, Operation = BuildVideoExtractOp() });
                if (EnableVideoSubtitlesCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Video, Operation = BuildVideoSubtitlesOp() });
            }

            // ── Documents ──────────────────────────────────────────────────────
            if (_currentAnalysis.TypeGroups.Any(g => g.FileType == BatchFileType.Document))
            {
                if (EnableDocumentConvertCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Document, Operation = BuildDocumentConvertOp() });
            }

            // ── PDF (PdfMerge is exclusive of other PDF operations) ────────────
            if (_currentAnalysis.TypeGroups.Any(g => g.FileType == BatchFileType.Pdf))
            {
                if (EnablePdfMergeCheckBox.IsChecked == true)
                {
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Pdf, Operation = BuildPdfMergeOp() });
                }
                else
                {
                    if (EnablePdfCompressCheckBox.IsChecked == true)
                        groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Pdf, Operation = new PdfCompressBatchOperation() });
                    if (EnablePdfRepairCheckBox.IsChecked == true)
                        groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Pdf, Operation = new PdfRepairBatchOperation() });
                    if (EnablePdfOcrCheckBox.IsChecked == true)
                    {
                        var ocrOp = BuildPdfOcrOp();
                        if (ocrOp is not null)
                            groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Pdf, Operation = ocrOp });
                    }
                }
            }

            // ── Images ─────────────────────────────────────────────────────────
            if (_currentAnalysis.TypeGroups.Any(g => g.FileType == BatchFileType.Image))
            {
                if (EnableImageFormatCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Image, Operation = BuildImageFormatOp() });
                if (EnableImageCompressCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Image, Operation = BuildImageCompressOp() });
                if (EnableImageResizeCheckBox.IsChecked == true)
                    groupPlans.Add(new BatchTypeGroupPlan { FileType = BatchFileType.Image, Operation = BuildImageResizeOp() });
            }

            if (groupPlans.Count == 0) return null;

            return new BatchPlan { Analysis = _currentAnalysis, TypeGroupPlans = groupPlans };
        }

        // ── Individual operation builders ─────────────────────────────────────

        private BatchOperation BuildAudioFormatOp() => new AudioConvertBatchOperation
        {
            OutputExtension = GetComboBoxTag(AudioConvertFormatComboBox) ?? ".mp3",
            Options = new AudioConversionOptions()
        };

        private BatchOperation BuildAudioCompressOp() => new AudioCompressBatchOperation
        {
            Options = new AudioCompressionOptions
            {
                Mode              = AudioCompressModeComboBox.SelectedIndex == 1 ? AudioCompressionMode.Lossless : AudioCompressionMode.Lossy,
                TargetBitrateKbps = (int)AudioCompressBitrateNumberBox.Value
            }
        };

        private BatchOperation BuildAudioNormalizeOp() => new AudioNormalizeBatchOperation
        {
            Options = new AudioNormalizationOptions
            {
                Mode         = AudioNormalizeModeComboBox.SelectedIndex == 1 ? AudioNormalizationMode.Peak : AudioNormalizationMode.Lufs,
                TargetLufs   = AudioNormalizeModeComboBox.SelectedIndex == 0 ? (double?)AudioNormalizeTargetNumberBox.Value : null,
                TargetPeakDb = AudioNormalizeModeComboBox.SelectedIndex == 1 ? (double?)AudioNormalizeTargetNumberBox.Value : null
            }
        };

        private BatchOperation BuildVideoContainerOp() => new VideoChangeContainerBatchOperation
        {
            TargetContainer = VideoContainerComboBox.SelectedIndex switch
            {
                1 => VideoContainerFormat.Mkv,
                2 => VideoContainerFormat.Mov,
                3 => VideoContainerFormat.Avi,
                4 => VideoContainerFormat.Webm,
                _ => VideoContainerFormat.Mp4
            }
        };

        private BatchOperation BuildVideoCodecOp() => new VideoChangeCodecBatchOperation
        {
            Options = new CodecChangeOptions
            {
                VideoCodec = VideoCodecComboBox.SelectedIndex switch
                {
                    1 => VideoCodec.H265,
                    2 => VideoCodec.Av1,
                    3 => VideoCodec.Vp9,
                    _ => VideoCodec.H264
                },
                AudioCodec = VideoAudioCodecComboBox.SelectedIndex switch
                {
                    1 => AudioCodec.Opus,
                    2 => AudioCodec.Mp3,
                    3 => AudioCodec.Flac,
                    _ => AudioCodec.Aac
                }
            }
        };

        private BatchOperation BuildVideoCompressOp() => new VideoCompressBatchOperation
        {
            Options = new VideoCompressionOptions
            {
                Preset = VideoCompressPresetComboBox.SelectedIndex switch
                {
                    0 => CompressionPreset.VeryHigh,
                    2 => CompressionPreset.Balanced,
                    3 => CompressionPreset.SmallSize,
                    _ => CompressionPreset.High
                }
            }
        };

        private BatchOperation BuildVideoResizeOp() => new VideoResizeBatchOperation
        {
            Options = new VideoResizeOptions
            {
                Width  = (int)VideoResizeWidthNumberBox.Value,
                Height = (int)VideoResizeHeightNumberBox.Value
            }
        };

        private BatchOperation BuildVideoExtractOp() => new VideoExtractAudioBatchOperation
        {
            OutputExtension = GetComboBoxTag(VideoExtractAudioFormatComboBox) ?? ".mp3"
        };

        private BatchOperation BuildVideoSubtitlesOp()
        {
            var isAdvanced = BatchSubtitleGenerationModeComboBox.SelectedIndex == 1;
            var isKaraoke  = isAdvanced && BatchIsKaraokeAdvancedSubtitleTypeSelected();
            var maxDurationSec = BatchSubtitleSectionDurationNumberBox.Value;
            int? wordCap = BatchEnableSubtitleWordCapCheckBox.IsChecked == true
                ? (int?)BatchSubtitleWordCapNumberBox.Value
                : null;

            var placementX = BatchSubtitlePlacementXNumberBox?.Value ?? 50d;
            var placementY = BatchSubtitlePlacementYNumberBox?.Value ?? 88d;
            var placement = new SubtitlePlacementOptions
            {
                NormalizedX = double.IsNaN(placementX) ? 0.5d : placementX / 100d,
                NormalizedY = double.IsNaN(placementY) ? 0.88d : placementY / 100d
            };

            return new VideoSubtitlesBatchOperation
            {
                UseAdvancedAss     = isAdvanced,
                UseKaraoke         = isKaraoke,
                MaxSectionSeconds  = double.IsNaN(maxDurationSec) ? 6.5 : maxDurationSec,
                MaxWordsPerSection = wordCap,
                StylePreset        = isAdvanced ? CreateBatchSubtitleStylePreset() : null,
                Placement          = placement,
                MuxMode            = BatchSubtitleMuxModeComboBox.SelectedIndex == 1
                                         ? SubtitleMode.BurnIn
                                         : SubtitleMode.SoftMux,
                Language           = BatchSubtitleLanguageTextBox.Text?.Trim() is { Length: > 0 } lang ? lang : null,
                Title              = BatchSubtitleTitleTextBox.Text?.Trim() is { Length: > 0 } title ? title : null,
                SetAsDefault       = BatchSetDefaultSubtitleCheckBox.IsChecked == true
            };
        }

        private BatchOperation BuildDocumentConvertOp() => new DocumentConvertToPdfBatchOperation
        {
            Options = new DocumentConversionOptions
            {
                Variant = DocumentPdfVariantComboBox.SelectedIndex switch
                {
                    1 => PdfOutputVariant.PdfA1b,
                    2 => PdfOutputVariant.PdfA2b,
                    3 => PdfOutputVariant.PdfA3b,
                    _ => PdfOutputVariant.Standard
                }
            }
        };

        private BatchOperation BuildPdfMergeOp() => new PdfMergeBatchOperation
        {
            MergedFileName = PdfMergeFilenameTextBox.Text is { Length: > 0 } fn ? fn : "merged.pdf"
        };

        private PdfOcrBatchOperation? BuildPdfOcrOp()
        {
            var tessDataPath = PdfOcrTessDataTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(tessDataPath)) return null;
            return new PdfOcrBatchOperation
            {
                Options = new PdfOcrOptions
                {
                    TessDataPath = tessDataPath,
                    Languages    = PdfOcrLanguagesTextBox.Text is { Length: > 0 } l ? l : "eng",
                    Dpi          = (int)PdfOcrDpiNumberBox.Value
                }
            };
        }

        private BatchOperation BuildImageFormatOp() => new ImageConvertFormatBatchOperation
        {
            TargetFormat = ImageConvertFormatComboBox.SelectedIndex switch
            {
                0 => ImageFormat.Jpeg,
                1 => ImageFormat.Png,
                3 => ImageFormat.Avif,
                4 => ImageFormat.Tiff,
                5 => ImageFormat.Heif,
                6 => ImageFormat.Gif,
                _ => ImageFormat.Webp
            }
        };

        private BatchOperation BuildImageCompressOp() => new ImageCompressBatchOperation
        {
            Options = new CompressionOptions
            {
                MaintainOriginalQuality = false,
                Quality                 = (int)ImageQualitySlider.Value
            }
        };

        private BatchOperation BuildImageResizeOp() => new ImageResizeBatchOperation
        {
            Options = new ResizeOptions
            {
                Width  = (int)ImageResizeWidthNumberBox.Value,
                Height = (int)ImageResizeHeightNumberBox.Value
            }
        };

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Used by x:Bind to convert nullable CheckBox.IsChecked to a plain bool for IsEnabled.</summary>
        public bool IsCheckedToBool(bool? value) => value == true;

        private static string? GetComboBoxTag(ComboBox comboBox) =>
            (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;
    }

    // ── BatchFileItem ─────────────────────────────────────────────────────────

    public sealed class BatchFileItem : INotifyPropertyChanged
    {
        private static readonly SolidColorBrush AudioBadgeBrush    = new(Color.FromArgb(255, 59,  130, 246));
        private static readonly SolidColorBrush VideoBadgeBrush     = new(Color.FromArgb(255, 239, 68,  68));
        private static readonly SolidColorBrush DocumentBadgeBrush  = new(Color.FromArgb(255, 34,  197, 94));
        private static readonly SolidColorBrush PdfBadgeBrush       = new(Color.FromArgb(255, 249, 115, 22));
        private static readonly SolidColorBrush ImageBadgeBrush     = new(Color.FromArgb(255, 6,   182, 212));
        private static readonly SolidColorBrush UnknownBadgeBrush   = new(Color.FromArgb(255, 148, 163, 184));

        private static readonly SolidColorBrush SuccessBrush    = new(Color.FromArgb(255, 34,  197, 94));
        private static readonly SolidColorBrush FailedBrush     = new(Color.FromArgb(255, 239, 68,  68));
        private static readonly SolidColorBrush ProcessingBrush = new(Color.FromArgb(255, 59,  130, 246));
        private static readonly SolidColorBrush NeutralBrush    = new(Color.FromArgb(255, 148, 163, 184));

        public event PropertyChangedEventHandler? PropertyChanged;

        public required string InputPath { get; init; }
        public required string FileName  { get; init; }
        public required BatchFileType FileType { get; init; }

        public string? OutputPath   { get; set; }
        public string? ErrorMessage { get; set; }

        private BatchFileItemStatus _status = BatchFileItemStatus.Pending;
        public BatchFileItemStatus Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
        }

        // Computed display properties (all notify via null/all-properties above)

        public string TypeBadgeText => FileType switch
        {
            BatchFileType.Audio    => "AUD",
            BatchFileType.Video    => "VID",
            BatchFileType.Document => "DOC",
            BatchFileType.Pdf      => "PDF",
            BatchFileType.Image    => "IMG",
            _                      => "???"
        };

        public SolidColorBrush TypeBadgeBackground => FileType switch
        {
            BatchFileType.Audio    => AudioBadgeBrush,
            BatchFileType.Video    => VideoBadgeBrush,
            BatchFileType.Document => DocumentBadgeBrush,
            BatchFileType.Pdf      => PdfBadgeBrush,
            BatchFileType.Image    => ImageBadgeBrush,
            _                      => UnknownBadgeBrush
        };

        /// <summary>Shows the ProgressRing only while the file is being processed.</summary>
        public Visibility ProcessingVisibility => Status == BatchFileItemStatus.Processing ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>Shows the static icon for every state except Processing.</summary>
        public Visibility IconVisibility       => Status != BatchFileItemStatus.Processing ? Visibility.Visible : Visibility.Collapsed;

        public string StatusGlyph => Status switch
        {
            BatchFileItemStatus.Pending    => "",
            BatchFileItemStatus.Processing => "",
            BatchFileItemStatus.Success    => "",
            BatchFileItemStatus.Failed     => "",
            BatchFileItemStatus.Skipped    => "",
            _                              => ""
        };

        public SolidColorBrush StatusForeground => Status switch
        {
            BatchFileItemStatus.Success    => SuccessBrush,
            BatchFileItemStatus.Failed     => FailedBrush,
            BatchFileItemStatus.Processing => ProcessingBrush,
            _                              => NeutralBrush
        };

        public string StatusText => Status switch
        {
            BatchFileItemStatus.Pending    => "Waiting",
            BatchFileItemStatus.Processing => "Processing…",
            BatchFileItemStatus.Success    => OutputPath is not null
                                                 ? $"→ {Path.GetFileName(OutputPath)}"
                                                 : "Done",
            BatchFileItemStatus.Failed     => ErrorMessage ?? "Failed",
            BatchFileItemStatus.Skipped    => "Skipped",
            _                              => ""
        };
    }

    public enum BatchFileItemStatus
    {
        Pending,
        Processing,
        Success,
        Failed,
        Skipped
    }
}
