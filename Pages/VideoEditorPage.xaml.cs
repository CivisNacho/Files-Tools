using Files_Tools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Navigation;

namespace Files_Tools.Pages
{
    public sealed partial class VideoEditorPage : Page
    {
        private const double MinimumTwoColumnBreakpoint = 980;
        private const double ContentMaxWidth = 1440;
        private const double OuterHorizontalPadding = 64;
        private const double WideColumnSpacing = 18;
        private const double OptionsColumnRatio = 0.3;
        private const double OptionsPanelMinimumWidth = 360;
        private const double TrimHandleWidth = 16;
        private const double TrimRailTop = 15;
        private const double TrimHandleTop = 6;
        private const double MinimumTrimGapMilliseconds = 100;
        private const int MaximumAudioSyncOffsetMilliseconds = 5000;

        private static readonly string[] SupportedVideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v", ".gif"];
        private static readonly string[] SupportedAudioExtensions = [".mp3", ".aac", ".m4a", ".wav", ".flac", ".opus", ".ogg", ".ac3"];
        private static readonly string[] SupportedAudioExtractionExtensions = [".mp3", ".aac", ".m4a", ".wav", ".flac", ".opus", ".ogg"];
        private static readonly string[] SupportedSubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt"];
        private static readonly Dictionary<VideoContainerFormat, HashSet<string>> SupportedVideoCodecNamesByContainer = new()
        {
            [VideoContainerFormat.Mp4] = new HashSet<string>(StringComparer.Ordinal) { "H264", "H265", "AV1", "MPEG4" },
            [VideoContainerFormat.Webm] = new HashSet<string>(StringComparer.Ordinal) { "VP8", "VP9", "AV1" },
            [VideoContainerFormat.Gif] = new HashSet<string>(StringComparer.Ordinal),
            [VideoContainerFormat.Mkv] = new HashSet<string>(StringComparer.Ordinal) { "H264", "H265", "AV1", "VP8", "VP9", "MPEG4" },
            [VideoContainerFormat.Mov] = new HashSet<string>(StringComparer.Ordinal) { "H264", "H265", "AV1", "MPEG4" },
            [VideoContainerFormat.Avi] = new HashSet<string>(StringComparer.Ordinal) { "H264", "MPEG4" }
        };

        private static readonly Dictionary<VideoContainerFormat, HashSet<string>> SupportedAudioCodecNamesByContainer = new()
        {
            [VideoContainerFormat.Mp4] = new HashSet<string>(StringComparer.Ordinal) { "AAC", "MP3", "AC3", "FLAC" },
            [VideoContainerFormat.Webm] = new HashSet<string>(StringComparer.Ordinal) { "Opus", "Vorbis" },
            [VideoContainerFormat.Gif] = new HashSet<string>(StringComparer.Ordinal),
            [VideoContainerFormat.Mkv] = new HashSet<string>(StringComparer.Ordinal) { "AAC", "Opus", "Vorbis", "MP3", "AC3", "FLAC", "PCM_S16LE" },
            [VideoContainerFormat.Mov] = new HashSet<string>(StringComparer.Ordinal) { "AAC", "MP3", "AC3", "FLAC", "PCM_S16LE" },
            [VideoContainerFormat.Avi] = new HashSet<string>(StringComparer.Ordinal) { "MP3", "AC3", "PCM_S16LE" }
        };

        private readonly IVideoProcessingService _videoProcessingService = new VideoProcessingService();
        private StorageFile? _sourceVideoFile;
        private TimeSpan? _videoDuration;
        private TimeSpan _trimStart = TimeSpan.Zero;
        private TimeSpan _trimEnd = TimeSpan.Zero;
        private TrimDragHandle _activeTrimHandle = TrimDragHandle.None;
        private bool _isProcessing;

        private enum TrimDragHandle
        {
            None,
            Start,
            End
        }

        public VideoEditorPage()
        {
            InitializeComponent();

            InitializeDefaults();

            RefreshValidationAndState();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is FileNavigationRequest navigationRequest &&
                IsSupportedVideoFile(navigationRequest.File))
            {
                await LoadVideoPreviewAsync(navigationRequest.File);
            }
        }

        private void InitializeDefaults()
        {
            ApplyButton.IsEnabled = false;

            OutputContainerComboBox.SelectedIndex = 0;
            VideoCodecComboBox.SelectedIndex = 0;
            AudioCodecComboBox.SelectedIndex = 0;
            CompressionPresetComboBox.SelectedIndex = 2;
            SubtitleModeComboBox.SelectedIndex = 0;
            ResizeModeComboBox.SelectedIndex = 0;
            RotationComboBox.SelectedIndex = 0;
            RepairModeComboBox.SelectedIndex = 0;
            AudioSyncDirectionComboBox.SelectedIndex = 0;
            UpdateCodecOptionsForSelectedContainer();
        }

        private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
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
            if (_sourceVideoFile is not null)
            {
                return;
            }

            await PickVideoFromExplorerAsync();
        }

        private void UploadSurface_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop video to load";
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
            var videoFile = items.OfType<StorageFile>().FirstOrDefault(IsSupportedVideoFile);
            if (videoFile is null)
            {
                return;
            }

            await LoadVideoPreviewAsync(videoFile);
        }

        private async Task PickVideoFromExplorerAsync()
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var picker = new FileOpenPicker();
            foreach (var extension in SupportedVideoExtensions)
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

            await LoadVideoPreviewAsync(selectedFile);
        }

        private async Task LoadVideoPreviewAsync(StorageFile file)
        {
            _sourceVideoFile = file;
            ResetTrimState();

            VideoPlayer.Source = MediaSource.CreateFromStorageFile(file);
            VideoPlayer.Visibility = Visibility.Visible;
            DropHintPanel.Visibility = Visibility.Collapsed;
            AttachVideoOpenedHandler();

            var basicProperties = await file.GetBasicPropertiesAsync();
            LoadedVideoInfoTextBlock.Text = $"Loaded video: {file.Name} ({basicProperties.Size / (1024.0 * 1024.0):0.00} MB)";

            RefreshValidationAndState();
        }

        private void AttachVideoOpenedHandler()
        {
            if (VideoPlayer.MediaPlayer is null)
            {
                return;
            }

            VideoPlayer.MediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
            VideoPlayer.MediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
        }

        private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
        {
            var duration = sender.PlaybackSession.NaturalDuration;
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                _videoDuration = duration;
                if (EnableTrimCheckBox?.IsChecked ?? false)
                {
                    EnsureTrimRangeInitialized();
                }

                UpdateTrimUiState();
            });
        }

        private static bool IsSupportedVideoFile(StorageFile file)
        {
            return SupportedVideoExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase);
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceVideoFile is null || _isProcessing)
            {
                return;
            }

            var (options, errors) = BuildOptionsFromUi();
            if (errors.Count > 0)
            {
                RefreshValidationAndState(errors);
                return;
            }

            try
            {
                var selectedOutputPath = await PickOutputPathAsync(_sourceVideoFile, options.Output.Format);
                if (selectedOutputPath is null)
                {
                    return;
                }

                var outputPath = NormalizeVideoOutputPath(selectedOutputPath, options.Output.Format, _sourceVideoFile.FileType);
                var estimate = await _videoProcessingService.EstimateProcessAsync(_sourceVideoFile.Path, options);

                var preflightDialog = new ContentDialog
                {
                    Title = "Processing estimate",
                    Content = BuildEstimateSummary(estimate),
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = XamlRoot
                };

                var preflightResult = await preflightDialog.ShowAsync();
                if (preflightResult != ContentDialogResult.Primary)
                {
                    return;
                }

                _isProcessing = true;
                ShowProcessingState();
                RefreshValidationAndState();

                var progress = new Progress<VideoProcessingProgress>(UpdateProcessingProgress);
                await _videoProcessingService.ProcessVideoAsync(_sourceVideoFile.Path, outputPath, options, progress);

                var doneDialog = new ContentDialog
                {
                    Title = "Done",
                    Content = $"Video saved to:\n{outputPath}",
                    PrimaryButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                _ = await doneDialog.ShowAsync();
                ResetProcessingState();
            }
            catch (Exception ex)
            {
                ResetProcessingState();

                var errorDialog = new ContentDialog
                {
                    Title = "Processing error",
                    Content = ex.Message,
                    PrimaryButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                _ = await errorDialog.ShowAsync();
            }
            finally
            {
                _isProcessing = false;
                RefreshValidationAndState();
            }
        }

        private void ShowOptionPanel(string selected)
        {
            MediaPanel.Visibility = selected == "Media" ? Visibility.Visible : Visibility.Collapsed;
            TransformPanel.Visibility = selected == "Transform" ? Visibility.Visible : Visibility.Collapsed;
            AdvancedPanel.Visibility = selected == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
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
            switch (section)
            {
                case "Media":
                    ShowMediaSubgroup(subgroup);
                    break;
                case "Transform":
                    ShowTransformSubgroup(subgroup);
                    break;
                case "Advanced":
                    ShowAdvancedSubgroup(subgroup);
                    break;
            }
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
            var showFormat = selected == "Format";
            MediaFormatPanel.Visibility = showFormat ? Visibility.Visible : Visibility.Collapsed;
            MediaCompressionPanel.Visibility = showFormat ? Visibility.Visible : Visibility.Collapsed;
            MediaAudioPanel.Visibility = selected == "Audio" ? Visibility.Visible : Visibility.Collapsed;
            MediaSubtitlesPanel.Visibility = selected == "Subtitles" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowTransformSubgroup(string selected)
        {
            TransformResizePanel.Visibility = selected == "Resize" ? Visibility.Visible : Visibility.Collapsed;
            TransformTrimPanel.Visibility = selected == "Trim" ? Visibility.Visible : Visibility.Collapsed;
            TransformRotatePanel.Visibility = selected == "Rotate" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowAdvancedSubgroup(string selected)
        {
            AdvancedMetadataPanel.Visibility = selected == "Metadata" ? Visibility.Visible : Visibility.Collapsed;
            AdvancedRepairPanel.Visibility = selected == "Repair" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AnyOptionChanged_CheckChanged(object sender, RoutedEventArgs e) => RefreshValidationAndState();
        private void AnyOptionChanged_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshValidationAndState();
        private void AnyOptionChanged_TextChanged(object sender, TextChangedEventArgs e) => RefreshValidationAndState();
        private void AnyOptionChanged_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => RefreshValidationAndState();

        private void OutputContainerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCodecOptionsForSelectedContainer();
            RefreshValidationAndState();
        }

        private void UpdateCodecOptionsForSelectedContainer()
        {
            var format = ParseContainer(OutputContainerComboBox);
            if (!format.HasValue)
            {
                SetAllCodecItemsEnabled(VideoCodecComboBox);
                SetAllCodecItemsEnabled(AudioCodecComboBox);
                return;
            }

            var videoSupported = SupportedVideoCodecNamesByContainer[format.Value];
            var audioSupported = SupportedAudioCodecNamesByContainer[format.Value];

            UpdateCodecComboItems(VideoCodecComboBox, videoSupported, allowRemoveAudio: false);
            UpdateCodecComboItems(AudioCodecComboBox, audioSupported, allowRemoveAudio: true);
        }

        private static void SetAllCodecItemsEnabled(ComboBox? comboBox)
        {
            if (comboBox is null)
            {
                return;
            }

            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                item.IsEnabled = true;
            }
        }

        private static void UpdateCodecComboItems(ComboBox? comboBox, HashSet<string> supportedCodecs, bool allowRemoveAudio)
        {
            if (comboBox is null)
            {
                return;
            }

            ComboBoxItem? firstEnabled = null;
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                var content = item.Content?.ToString() ?? string.Empty;
                var isKeepOriginal = string.Equals(content, "Keep original", StringComparison.Ordinal);
                var isRemoveAudio = allowRemoveAudio && string.Equals(content, "Remove audio", StringComparison.Ordinal);
                var isSupportedCodec = supportedCodecs.Contains(content);
                item.IsEnabled = isKeepOriginal || isRemoveAudio || isSupportedCodec;

                if (item.IsEnabled && firstEnabled is null)
                {
                    firstEnabled = item;
                }
            }

            if (comboBox.SelectedItem is ComboBoxItem selectedItem && !selectedItem.IsEnabled)
            {
                comboBox.SelectedItem = firstEnabled;
            }
        }

        private void AudioVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (AudioVolumeTextBlock is not null && AudioVolumeSlider is not null)
            {
                AudioVolumeTextBlock.Text = $"Volume: {(int)Math.Round(AudioVolumeSlider.Value)}%";
            }

            RefreshValidationAndState();
        }

        private void AudioSyncDirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAudioSyncOffsetText();
            RefreshValidationAndState();
        }

        private void AudioSyncOffsetSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateAudioSyncOffsetText();
            RefreshValidationAndState();
        }

        private async void ExtractAudioButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceVideoFile is null || App.MainWindow is null)
            {
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(_sourceVideoFile.Name)
            };

            picker.FileTypeChoices.Add("MP3 audio", [".mp3"]);
            picker.FileTypeChoices.Add("AAC audio", [".aac"]);
            picker.FileTypeChoices.Add("M4A audio", [".m4a"]);
            picker.FileTypeChoices.Add("WAV audio", [".wav"]);
            picker.FileTypeChoices.Add("FLAC audio", [".flac"]);
            picker.FileTypeChoices.Add("Opus audio", [".opus"]);
            picker.FileTypeChoices.Add("Ogg audio", [".ogg"]);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            var extension = Path.GetExtension(file.Path);
            if (!SupportedAudioExtractionExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                await ShowSimpleDialogAsync("Audio extraction error", $"Unsupported audio extraction extension '{extension}'.");
                return;
            }

            try
            {
                await _videoProcessingService.ExtractAudioAsync(_sourceVideoFile.Path, file.Path);
                await ShowSimpleDialogAsync("Audio extracted", $"Audio saved to:\n{file.Path}");
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync("Audio extraction error", ex.Message);
            }
        }

        private async void BrowseAudioMuxButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickSingleFileAsync(SupportedAudioExtensions);
            if (file is null)
            {
                return;
            }

            AudioMuxPathTextBox.Text = file.Path;
            EnableAudioMuxCheckBox.IsChecked = true;
            RefreshValidationAndState();
        }

        private async void BrowseSubtitleMuxButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickSingleFileAsync(SupportedSubtitleExtensions);
            if (file is null)
            {
                return;
            }

            SubtitlePathTextBox.Text = file.Path;
            EnableSubtitleMuxCheckBox.IsChecked = true;
            RefreshValidationAndState();
        }

        private async void ShowNotImplementedButton_Click(object sender, RoutedEventArgs e)
        {
            var text = "Not implemented yet in this phase:\n\n" +
                "- FPS / VFR / CFR conversion\n" +
                "- Split/join/segment workflows\n" +
                "- Multi-track audio mixing\n" +
                "- Subtitle conversion/replacement/removal workflows\n" +
                "- Chapters add/edit/remove\n" +
                "- Thumbnails, contact sheets, preview strips\n" +
                "- Streaming preset profiles (Discord/YouTube/mobile)\n" +
                "- Visual filters (denoise, deinterlace, watermark, color correction)\n" +
                "- Batch queue, retry, pause/resume\n" +
                "- Explicit hardware encoder selection (NVENC/AMF/QSV)\n" +
                "- Animated WebP helpers";

            var dialog = new ContentDialog
            {
                Title = "Roadmap not implemented yet",
                Content = text,
                PrimaryButtonText = "OK",
                XamlRoot = XamlRoot
            };

            _ = await dialog.ShowAsync();
        }

        private void RefreshValidationAndState(IReadOnlyList<string>? forcedErrors = null)
        {
            if (ApplyButton is null ||
                MediaValidationTextBlock is null ||
                TransformValidationTextBlock is null ||
                AdvancedValidationTextBlock is null)
            {
                return;
            }

            var errors = forcedErrors?.ToList() ?? BuildOptionsFromUi().Errors;

            var mediaErrors = errors.Where(e => e.StartsWith("Media:", StringComparison.Ordinal)).Select(e => e[6..].Trim()).ToArray();
            var transformErrors = errors.Where(e => e.StartsWith("Transform:", StringComparison.Ordinal)).Select(e => e[10..].Trim()).ToArray();
            var advancedErrors = errors.Where(e => e.StartsWith("Advanced:", StringComparison.Ordinal)).Select(e => e[9..].Trim()).ToArray();

            MediaValidationTextBlock.Text = string.Join("\n", mediaErrors);
            TransformValidationTextBlock.Text = string.Join("\n", transformErrors);
            AdvancedValidationTextBlock.Text = string.Join("\n", advancedErrors);

            UpdateOptionUiState();
            ApplyButton.IsEnabled = !_isProcessing && _sourceVideoFile is not null && errors.Count == 0;
        }

        private void UpdateOptionUiState()
        {
            TrySyncDurationFromPlayer();

            if ((EnableTrimCheckBox?.IsChecked ?? false) && _sourceVideoFile is not null)
            {
                EnsureTrimRangeInitialized();
            }

            UpdateTrimUiState();

            SetDependentOptionsState(AudioMuxOptionsPanel, EnableAudioMuxCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(CompressionOptionsPanel, EnableCompressionCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(ResizeOptionsPanel, EnableResizeCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(SubtitleMuxOptionsPanel, EnableSubtitleMuxCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(
                AudioVolumePanel,
                (EnableAudioVolumeCheckBox?.IsChecked ?? false) && !ShouldDisableOutputAudioVolume());
            SetDependentOptionsState(
                NormalizeAudioPanel,
                (EnableNormalizeAudioCheckBox?.IsChecked ?? false) && !ShouldDisableOutputAudioVolume());
            SetDependentOptionsState(
                AudioSyncPanel,
                (EnableAudioSyncCheckBox?.IsChecked ?? false) && !ShouldDisableOutputAudioVolume());

            if (ExtractAudioButton is not null)
            {
                ExtractAudioButton.IsEnabled = _sourceVideoFile is not null && !_isProcessing;
            }

            if (RepairOptionsPanel is not null)
            {
                var repairEnabled = EnableRepairCheckBox?.IsChecked ?? false;
                RepairOptionsPanel.IsEnabled = repairEnabled;
                RepairOptionsPanel.Opacity = repairEnabled ? 1d : 0.5d;
            }
        }

        private static void SetDependentOptionsState(UIElement? panel, bool isEnabled)
        {
            if (panel is null)
            {
                return;
            }

            if (panel is Control control)
            {
                control.IsEnabled = isEnabled;
            }

            panel.IsHitTestVisible = isEnabled;
            panel.Opacity = isEnabled ? 1d : 0.5d;
        }

        private (ProcessVideoOptions Options, List<string> Errors) BuildOptionsFromUi()
        {
            var errors = new List<string>();

            var output = new VideoOutputOptions
            {
                Format = ParseContainer(OutputContainerComboBox),
                VideoCodec = ParseVideoCodec(VideoCodecComboBox),
                AudioCodec = ParseAudioCodec(AudioCodecComboBox)
            };
            var removeAudio = IsRemoveAudioSelected() && !(EnableAudioMuxCheckBox?.IsChecked ?? false);

            AudioAdjustOptions? audioAdjust = null;
            var volumePercent = Math.Clamp((int)Math.Round(AudioVolumeSlider?.Value ?? 100), 0, 200);
            var syncOffset = GetSignedAudioSyncOffsetMilliseconds();
            if (((EnableAudioVolumeCheckBox?.IsChecked ?? false) && volumePercent != 100 ||
                 (EnableNormalizeAudioCheckBox?.IsChecked ?? false) ||
                 (EnableAudioSyncCheckBox?.IsChecked ?? false && syncOffset != 0)) &&
                !ShouldDisableOutputAudioVolume())
            {
                audioAdjust = new AudioAdjustOptions
                {
                    VolumePercent = (EnableAudioVolumeCheckBox?.IsChecked ?? false) ? volumePercent : 100,
                    NormalizeLoudness = EnableNormalizeAudioCheckBox?.IsChecked ?? false,
                    SyncOffsetMilliseconds = (EnableAudioSyncCheckBox?.IsChecked ?? false) ? syncOffset : 0
                };
            }

            var resize = (EnableResizeCheckBox?.IsChecked ?? false)
                ? new VideoResizeOptions
                {
                    Width = Math.Max(1, (int)Math.Round(ResizeWidthNumberBox?.Value ?? 1)),
                    Height = Math.Max(1, (int)Math.Round(ResizeHeightNumberBox?.Value ?? 1)),
                    Mode = ParseResizeMode(ResizeModeComboBox),
                    PadColor = string.IsNullOrWhiteSpace(PadColorTextBox?.Text) ? "black" : PadColorTextBox.Text.Trim()
                }
                : null;

            var compression = (EnableCompressionCheckBox?.IsChecked ?? false)
                ? new VideoCompressionOptions
                {
                    Preset = ParseCompressionPreset(CompressionPresetComboBox)
                }
                : null;

            TrimOptions? trim = null;
            if (EnableTrimCheckBox?.IsChecked ?? false)
            {
                TrySyncDurationFromPlayer();
                EnsureTrimRangeInitialized();

                if (_videoDuration is null)
                {
                    errors.Add("Transform: Trim range is not available until video metadata is loaded.");
                }
                else if (_trimEnd <= _trimStart)
                {
                    errors.Add("Transform: Trim end must be greater than start.");
                }
                else
                {
                    trim = new TrimOptions { Start = _trimStart, End = _trimEnd };
                }
            }

            var rotation = ParseRotation(RotationComboBox);
            var mirrorH = MirrorHorizontalCheckBox?.IsChecked ?? false;
            var mirrorV = MirrorVerticalCheckBox?.IsChecked ?? false;
            var transform = (rotation != 0 || mirrorH || mirrorV)
                ? new TransformOptions { RotationDegrees = rotation, MirrorHorizontal = mirrorH, MirrorVertical = mirrorV }
                : null;

            MuxAudioOptions? audioMux = null;
            if (EnableAudioMuxCheckBox?.IsChecked ?? false)
            {
                if (string.IsNullOrWhiteSpace(AudioMuxPathTextBox?.Text) || !File.Exists(AudioMuxPathTextBox.Text))
                {
                    errors.Add("Media: External audio file is required when audio mux is enabled.");
                }
                else
                {
                    audioMux = new MuxAudioOptions
                    {
                        AudioPath = AudioMuxPathTextBox.Text.Trim(),
                        AudioCodec = ParseAudioCodec(AudioCodecComboBox),
                        ReplaceExistingAudio = ReplaceAudioCheckBox?.IsChecked ?? true,
                        UseShortestDuration = UseShortestCheckBox?.IsChecked ?? true,
                        SetAsDefault = SetDefaultAudioCheckBox?.IsChecked ?? true
                    };
                }
            }

            MuxSubtitleOptions? subtitleMux = null;
            if (EnableSubtitleMuxCheckBox?.IsChecked ?? false)
            {
                if (string.IsNullOrWhiteSpace(SubtitlePathTextBox?.Text) || !File.Exists(SubtitlePathTextBox.Text))
                {
                    errors.Add("Media: Subtitle file is required when subtitle mux is enabled.");
                }
                else
                {
                    subtitleMux = new MuxSubtitleOptions
                    {
                        SubtitlePath = SubtitlePathTextBox.Text.Trim(),
                        Mode = ParseSubtitleMode(SubtitleModeComboBox),
                        Language = string.IsNullOrWhiteSpace(SubtitleLanguageTextBox?.Text) ? null : SubtitleLanguageTextBox.Text.Trim(),
                        Title = string.IsNullOrWhiteSpace(SubtitleTitleTextBox?.Text) ? null : SubtitleTitleTextBox.Text.Trim(),
                        SetAsDefault = SetDefaultSubtitleCheckBox?.IsChecked ?? false
                    };
                }
            }

            RepairOptions? repair = null;
            if (EnableRepairCheckBox?.IsChecked ?? false)
            {
                repair = new RepairOptions
                {
                    Mode = ParseRepairMode(RepairModeComboBox),
                    RegeneratePresentationTimestamps = RegeneratePtsCheckBox?.IsChecked ?? true,
                    IgnoreRecoverableErrors = IgnoreRecoverableErrorsCheckBox?.IsChecked ?? true,
                    DropNonEssentialStreams = DropNonEssentialStreamsCheckBox?.IsChecked ?? true,
                    RemoveMetadata = RepairRemoveMetadataCheckBox?.IsChecked ?? true
                };
            }

            var options = new ProcessVideoOptions
            {
                Output = output,
                Resize = resize,
                Compression = compression,
                Trim = trim,
                Transform = transform,
                RemoveAudio = removeAudio,
                RemoveMetadata = RemoveMetadataCheckBox?.IsChecked ?? false,
                AudioMux = audioMux,
                AudioAdjust = audioAdjust,
                SubtitleMux = subtitleMux,
                Repair = repair,
                CodecChange = (output.VideoCodec.HasValue || output.AudioCodec.HasValue)
                    ? new CodecChangeOptions { VideoCodec = output.VideoCodec, AudioCodec = output.AudioCodec }
                    : null
            };

            if ((EnableResizeCheckBox?.IsChecked ?? false) && resize is not null)
            {
                if (resize.Width < 1 || resize.Height < 1)
                {
                    errors.Add("Transform: Resize dimensions must be greater than 0.");
                }
            }

            if ((EnableCompressionCheckBox?.IsChecked ?? false) && output.Format == VideoContainerFormat.Gif)
            {
                errors.Add("Media: GIF output ignores normal compression presets.");
            }

            if (removeAudio && audioMux is not null)
            {
                errors.Add("Media: Cannot remove audio and mux external audio at the same time.");
            }

            if (output.Format == VideoContainerFormat.Gif && subtitleMux?.Mode == SubtitleMode.SoftMux)
            {
                errors.Add("Media: GIF does not support soft subtitle mux. Use burn-in or disable subtitles.");
            }

            return (options, errors);
        }

        private static string BuildEstimateSummary(VideoProcessingEstimate estimate)
        {
            var sizeText = estimate.EstimatedOutputSizeBytes.HasValue
                ? $"{estimate.EstimatedOutputSizeBytes.Value / (1024.0 * 1024.0):0.00} MB"
                : "Unknown";

            var notes = estimate.Notes.Count > 0
                ? "\nNotes:\n- " + string.Join("\n- ", estimate.Notes)
                : string.Empty;

            return $"Output: {estimate.OutputFormat}\n" +
                   $"Duration: {estimate.EstimatedDuration}\n" +
                   $"Resolution: {estimate.EstimatedWidth}x{estimate.EstimatedHeight}\n" +
                   $"Estimated size: {sizeText}\n" +
                   $"Video re-encode: {estimate.RequiresVideoReencode}\n" +
                   $"Audio re-encode: {estimate.RequiresAudioReencode}\n" +
                   $"Video codec: {estimate.OutputVideoCodec}\n" +
                   $"Audio codec: {estimate.OutputAudioCodec?.ToString() ?? "None"}" + notes;
        }

        private bool ShouldDisableOutputAudioVolume()
        {
            var removesAudio = IsRemoveAudioSelected();
            return removesAudio && !(EnableAudioMuxCheckBox?.IsChecked ?? false);
        }

        private bool IsRemoveAudioSelected()
        {
            return (AudioCodecComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Remove audio";
        }

        private int GetSignedAudioSyncOffsetMilliseconds()
        {
            var magnitude = Math.Clamp((int)Math.Round(AudioSyncOffsetSlider?.Value ?? 0), 0, MaximumAudioSyncOffsetMilliseconds);
            var direction = (AudioSyncDirectionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return string.Equals(direction, "Later", StringComparison.Ordinal)
                ? magnitude
                : -magnitude;
        }

        private void UpdateAudioSyncOffsetText()
        {
            if (AudioSyncOffsetTextBlock is null)
            {
                return;
            }

            var magnitude = Math.Clamp((int)Math.Round(AudioSyncOffsetSlider?.Value ?? 0), 0, MaximumAudioSyncOffsetMilliseconds);
            var direction = (AudioSyncDirectionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var directionText = string.Equals(direction, "Later", StringComparison.Ordinal) ? "later" : "earlier";
            AudioSyncOffsetTextBlock.Text = $"Audio offset: {magnitude} ms {directionText}";
        }

        private async Task ShowSimpleDialogAsync(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "OK",
                XamlRoot = XamlRoot
            };

            _ = await dialog.ShowAsync();
        }

        private async Task<string?> PickOutputPathAsync(StorageFile sourceFile, VideoContainerFormat? format)
        {
            if (App.MainWindow is null)
            {
                return null;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                SuggestedFileName = $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}_processed_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var extension = GetVideoOutputExtension(format, sourceFile.FileType);
            picker.FileTypeChoices.Add(GetVideoSaveChoiceLabel(format), new List<string> { extension });
            picker.DefaultFileExtension = extension;

            var outputFile = await picker.PickSaveFileAsync();
            return outputFile?.Path;
        }

        private static string GetVideoSaveChoiceLabel(VideoContainerFormat? format)
        {
            return format switch
            {
                null => "Original format",
                VideoContainerFormat.Mp4 => "MP4",
                VideoContainerFormat.Mkv => "MKV",
                VideoContainerFormat.Webm => "WebM",
                VideoContainerFormat.Mov => "MOV",
                VideoContainerFormat.Avi => "AVI",
                VideoContainerFormat.Gif => "GIF",
                _ => "Video"
            };
        }

        private static string GetVideoOutputExtension(VideoContainerFormat? format, string sourceExtension)
        {
            return format switch
            {
                null => sourceExtension,
                VideoContainerFormat.Mp4 => ".mp4",
                VideoContainerFormat.Mkv => ".mkv",
                VideoContainerFormat.Webm => ".webm",
                VideoContainerFormat.Mov => ".mov",
                VideoContainerFormat.Avi => ".avi",
                VideoContainerFormat.Gif => ".gif",
                _ => sourceExtension
            };
        }

        private static string NormalizeVideoOutputPath(string selectedOutputPath, VideoContainerFormat? format, string sourceExtension)
        {
            var desiredExtension = GetVideoOutputExtension(format, sourceExtension);
            var normalizedPath = Path.ChangeExtension(selectedOutputPath, desiredExtension);
            return normalizedPath ?? selectedOutputPath;
        }

        private void ShowProcessingState()
        {
            if (ProcessingStatusPanel is null ||
                ProcessingProgressBar is null ||
                ProcessingStatusTextBlock is null ||
                ProcessingEtaTextBlock is null ||
                ProcessingDetailTextBlock is null)
            {
                return;
            }

            ProcessingStatusPanel.Visibility = Visibility.Visible;
            ProcessingProgressBar.IsIndeterminate = false;
            ProcessingProgressBar.Value = 0;
            ProcessingStatusTextBlock.Text = "Processing video...";
            ProcessingEtaTextBlock.Text = "ETA calculating...";
            ProcessingDetailTextBlock.Text = "Preparing FFmpeg...";
        }

        private void UpdateProcessingProgress(VideoProcessingProgress progress)
        {
            if (ProcessingStatusPanel is null ||
                ProcessingProgressBar is null ||
                ProcessingStatusTextBlock is null ||
                ProcessingEtaTextBlock is null ||
                ProcessingDetailTextBlock is null)
            {
                return;
            }

            ProcessingStatusPanel.Visibility = Visibility.Visible;
            ProcessingProgressBar.IsIndeterminate = false;
            ProcessingProgressBar.Value = Math.Clamp(progress.FractionComplete, 0d, 1d);
            ProcessingStatusTextBlock.Text = progress.IsCompleted
                ? "Finalizing output..."
                : $"Processing video... {(progress.FractionComplete * 100d):0}%";
            ProcessingEtaTextBlock.Text = progress.IsCompleted
                ? "ETA 00:00"
                : progress.EstimatedTimeRemaining is TimeSpan eta
                    ? $"ETA {FormatEta(eta)}"
                    : "ETA calculating...";

            var processed = FormatTimelineTime(progress.ProcessedDuration);
            var total = progress.TotalDuration is TimeSpan totalDuration
                ? FormatTimelineTime(totalDuration)
                : "--:--";

            ProcessingDetailTextBlock.Text = progress.IsCompleted
                ? $"Processed {total} of {total}."
                : $"Processed {processed} of {total}.";
        }

        private void ResetProcessingState()
        {
            if (ProcessingStatusPanel is null ||
                ProcessingProgressBar is null ||
                ProcessingStatusTextBlock is null ||
                ProcessingEtaTextBlock is null ||
                ProcessingDetailTextBlock is null)
            {
                return;
            }

            ProcessingStatusPanel.Visibility = Visibility.Collapsed;
            ProcessingProgressBar.IsIndeterminate = false;
            ProcessingProgressBar.Value = 0;
            ProcessingStatusTextBlock.Text = "Processing video...";
            ProcessingEtaTextBlock.Text = "ETA calculating...";
            ProcessingDetailTextBlock.Text = "Preparing FFmpeg...";
        }

        private static string FormatEta(TimeSpan value)
        {
            if (value.TotalHours >= 1)
            {
                return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            }

            return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }

        private async Task<StorageFile?> PickSingleFileAsync(IEnumerable<string> extensions)
        {
            if (App.MainWindow is null)
            {
                return null;
            }

            var picker = new FileOpenPicker();
            foreach (var extension in extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            return await picker.PickSingleFileAsync();
        }

        private static VideoContainerFormat? ParseContainer(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return content switch
            {
                "MP4" => VideoContainerFormat.Mp4,
                "MKV" => VideoContainerFormat.Mkv,
                "WebM" => VideoContainerFormat.Webm,
                "MOV" => VideoContainerFormat.Mov,
                "AVI" => VideoContainerFormat.Avi,
                "GIF" => VideoContainerFormat.Gif,
                _ => null
            };
        }

        private static VideoCodec? ParseVideoCodec(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return content switch
            {
                "H264" => VideoCodec.H264,
                "H265" => VideoCodec.H265,
                "AV1" => VideoCodec.Av1,
                "VP9" => VideoCodec.Vp9,
                "VP8" => VideoCodec.Vp8,
                "MPEG4" => VideoCodec.Mpeg4,
                _ => null
            };
        }

        private static AudioCodec? ParseAudioCodec(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return content switch
            {
                "AAC" => AudioCodec.Aac,
                "Opus" => AudioCodec.Opus,
                "Vorbis" => AudioCodec.Vorbis,
                "MP3" => AudioCodec.Mp3,
                "AC3" => AudioCodec.Ac3,
                "FLAC" => AudioCodec.Flac,
                "PCM_S16LE" => AudioCodec.PcmS16Le,
                "Remove audio" => null,
                _ => null
            };
        }

        private static CompressionPreset ParseCompressionPreset(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return content switch
            {
                "VeryHigh" => CompressionPreset.VeryHigh,
                "High" => CompressionPreset.High,
                "Balanced" => CompressionPreset.Balanced,
                "SmallSize" => CompressionPreset.SmallSize,
                _ => CompressionPreset.Balanced
            };
        }

        private static ResizeMode ParseResizeMode(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return content switch
            {
                "Stretch" => ResizeMode.Stretch,
                "CropToFill" => ResizeMode.CropToFill,
                _ => ResizeMode.PadToFit
            };
        }

        private static SubtitleMode ParseSubtitleMode(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return content switch
            {
                "BurnIn" => SubtitleMode.BurnIn,
                _ => SubtitleMode.SoftMux
            };
        }

        private static RepairMode ParseRepairMode(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return content switch
            {
                "Reencode" => RepairMode.Reencode,
                _ => RepairMode.Remux
            };
        }

        private static int ParseRotation(ComboBox? comboBox)
        {
            var content = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return int.TryParse(content, out var rotation) ? rotation : 0;
        }

        private void TrimTimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTrimTimelineVisuals();
        }

        private void TrimHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement handle || handle.Tag is not string tag)
            {
                return;
            }

            _activeTrimHandle = tag == "Start" ? TrimDragHandle.Start : TrimDragHandle.End;
            TrimTimelineCanvas?.CapturePointer(e.Pointer);
            SeekPreviewToTrimHandle(_activeTrimHandle);
            e.Handled = true;
        }

        private void TrimTimelineCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeTrimHandle == TrimDragHandle.None || _videoDuration is null || TrimTimelineCanvas is null)
            {
                return;
            }

            var position = e.GetCurrentPoint(TrimTimelineCanvas).Position;
            var targetTime = PositionToTimelineTime(position.X);
            var minimumGap = TimeSpan.FromMilliseconds(MinimumTrimGapMilliseconds);

            if (_activeTrimHandle == TrimDragHandle.Start)
            {
                _trimStart = ClampTime(targetTime, TimeSpan.Zero, _trimEnd - minimumGap);
            }
            else
            {
                _trimEnd = ClampTime(targetTime, _trimStart + minimumGap, _videoDuration.Value);
            }

            UpdateTrimTimelineVisuals();
            SeekPreviewToTrimHandle(_activeTrimHandle);
        }

        private void TrimTimelineCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            TrimTimelineCanvas?.ReleasePointerCaptures();
            _activeTrimHandle = TrimDragHandle.None;
        }

        private void ResetTrimState()
        {
            _videoDuration = null;
            _trimStart = TimeSpan.Zero;
            _trimEnd = TimeSpan.Zero;
            _activeTrimHandle = TrimDragHandle.None;
            UpdateTrimUiState();
        }

        private void EnsureTrimRangeInitialized()
        {
            if (_videoDuration is null)
            {
                return;
            }

            if (_trimEnd <= _trimStart || _trimEnd > _videoDuration.Value)
            {
                _trimStart = TimeSpan.Zero;
                _trimEnd = _videoDuration.Value;
            }
        }

        private void TrySyncDurationFromPlayer()
        {
            var playbackSession = VideoPlayer.MediaPlayer?.PlaybackSession;
            if (playbackSession is null)
            {
                return;
            }

            var naturalDuration = playbackSession.NaturalDuration;
            if (naturalDuration > TimeSpan.Zero)
            {
                _videoDuration = naturalDuration;
            }
        }

        private void UpdateTrimUiState()
        {
            if (TrimTimelinePanel is null)
            {
                return;
            }

            var showTrim = _sourceVideoFile is not null && (EnableTrimCheckBox?.IsChecked ?? false) && _videoDuration is not null;
            TrimTimelinePanel.Visibility = showTrim ? Visibility.Visible : Visibility.Collapsed;

            if (showTrim)
            {
                UpdateTrimTimelineVisuals();
            }
            else
            {
                TrimSelectionInfoTextBlock.Text = "00:00:00 - 00:00:00";
                TrimStartTimeTextBlock.Text = "00:00:00";
                TrimEndTimeTextBlock.Text = "00:00:00";
            }
        }

        private void UpdateTrimTimelineVisuals()
        {
            if (TrimTimelineCanvas is null ||
                TrimTimelineRail is null ||
                TrimSelectedRange is null ||
                TrimStartHandle is null ||
                TrimEndHandle is null ||
                TrimSelectionInfoTextBlock is null ||
                TrimStartTimeTextBlock is null ||
                TrimEndTimeTextBlock is null ||
                _videoDuration is null)
            {
                return;
            }

            var width = TrimTimelineCanvas.ActualWidth;
            var trackWidth = Math.Max(0d, width - TrimHandleWidth);
            if (trackWidth <= 0)
            {
                return;
            }

            var startX = TimelineTimeToPosition(_trimStart, trackWidth);
            var endX = TimelineTimeToPosition(_trimEnd, trackWidth);

            Canvas.SetLeft(TrimTimelineRail, TrimHandleWidth / 2);
            Canvas.SetTop(TrimTimelineRail, TrimRailTop);
            TrimTimelineRail.Width = trackWidth;

            Canvas.SetLeft(TrimSelectedRange, startX + TrimHandleWidth / 2);
            Canvas.SetTop(TrimSelectedRange, TrimRailTop);
            TrimSelectedRange.Width = Math.Max(0d, endX - startX);

            Canvas.SetLeft(TrimStartHandle, startX);
            Canvas.SetTop(TrimStartHandle, TrimHandleTop);
            Canvas.SetLeft(TrimEndHandle, endX);
            Canvas.SetTop(TrimEndHandle, TrimHandleTop);

            TrimStartTimeTextBlock.Text = FormatTimelineTime(_trimStart);
            TrimEndTimeTextBlock.Text = FormatTimelineTime(_trimEnd);
            TrimSelectionInfoTextBlock.Text = $"{FormatTimelineTime(_trimStart)} - {FormatTimelineTime(_trimEnd)}   ({FormatTimelineTime(_trimEnd - _trimStart)})";
        }

        private double TimelineTimeToPosition(TimeSpan value, double trackWidth)
        {
            if (_videoDuration is null || _videoDuration.Value <= TimeSpan.Zero)
            {
                return 0;
            }

            var ratio = value.TotalMilliseconds / _videoDuration.Value.TotalMilliseconds;
            return Math.Clamp(ratio, 0d, 1d) * trackWidth;
        }

        private TimeSpan PositionToTimelineTime(double x)
        {
            if (_videoDuration is null || TrimTimelineCanvas is null)
            {
                return TimeSpan.Zero;
            }

            var trackWidth = Math.Max(1d, TrimTimelineCanvas.ActualWidth - TrimHandleWidth);
            var normalized = Math.Clamp(x - (TrimHandleWidth / 2), 0d, trackWidth) / trackWidth;
            return TimeSpan.FromMilliseconds(_videoDuration.Value.TotalMilliseconds * normalized);
        }

        private void SeekPreviewToTrimHandle(TrimDragHandle handle)
        {
            if (VideoPlayer.MediaPlayer?.PlaybackSession is null)
            {
                return;
            }

            VideoPlayer.MediaPlayer.PlaybackSession.Position = handle == TrimDragHandle.Start ? _trimStart : _trimEnd;
        }

        private static TimeSpan ClampTime(TimeSpan value, TimeSpan min, TimeSpan max)
        {
            if (max < min)
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static string FormatTimelineTime(TimeSpan value)
        {
            return value.TotalHours >= 1
                ? value.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture)
                : value.ToString(@"mm\:ss\.ff", CultureInfo.InvariantCulture);
        }
    }
}
