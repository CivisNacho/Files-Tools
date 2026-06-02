using Files_Tools.Services;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Microsoft.UI.Xaml.Navigation;

namespace Files_Tools.Pages
{
    public sealed partial class VideoEditorPage : Page
    {
        private const double MinimumTwoColumnBreakpoint = 980;
        private const double OuterHorizontalPadding = 64;
        private const double WideColumnSpacing = 18;
        private const double OptionsColumnRatio = 0.3;
        private const double OptionsPanelMinimumWidth = 280;
        private const double TrimHandleWidth = 16;
        private const double TrimRailTop = 15;
        private const double TrimHandleTop = 6;
        private const double MinimumTrimGapMilliseconds = 100;
        private const int MaximumAudioSyncOffsetMilliseconds = 5000;
        private const int MaximumDenoiseStrength = 100;
        private const double DefaultSubtitlePlacementX = 0.5d;
        private const double DefaultSubtitlePlacementY = 0.88d;
        private const double SubtitlePlacementMarkerWidth = 180d;
        private const double SubtitlePlacementMarkerMinHeight = 58d;

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
        private readonly IVideoAudioDenoiseService _videoAudioDenoiseService = new VideoAudioDenoise();
        private readonly IAudioTranscriptionService _audioTranscriptionService = new AudioTranscriptionService();
        private readonly ISubtitlesService _subtitlesService;
        private StorageFile? _sourceVideoFile;
        private string? _sourceVideoCodecName;
        private string? _sourceAudioCodecName;
        private TimeSpan? _videoDuration;
        private TimeSpan _trimStart = TimeSpan.Zero;
        private TimeSpan _trimEnd = TimeSpan.Zero;
        private TrimDragHandle _activeTrimHandle = TrimDragHandle.None;
        private bool _isProcessing;
        private bool _isInstallingTranscriptionModel;
        private bool _isGeneratingSubtitles;
        private bool _isTranscriptionModelInstalled;
        private string? _generatedSubtitlePath;
        private string? _pendingAdvancedSubtitleOutputPath;
        private PendingAdvancedSubtitleKind _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;
        private bool _isSubtitleSectionActive;
        private bool _isSynchronizingSubtitleSectionDurationControls;
        private bool _isSynchronizingSubtitlePlacementControls;
        private double _subtitlePlacementX = DefaultSubtitlePlacementX;
        private double _subtitlePlacementY = DefaultSubtitlePlacementY;
        private Size _previewVideoSize = new(1920d, 1080d);
        private CancellationTokenSource? _transcriptionOperationCancellation;
        private readonly List<string> _temporaryPreviewFiles = [];
        private readonly ObservableCollection<SubtitleEditableRow> _subtitleEditableRows = [];
        private readonly List<SubtitleEditorBinding> _subtitleEditorBindings = [];
        private SubtitleEditorKind _subtitleEditorKind = SubtitleEditorKind.None;
        private SubtitleDraft? _advancedSubtitleDraft;
        private SubtitlePresetConfiguration _advancedSubtitlePresetConfiguration = SubtitlePresetConfiguration.CreateDefault();
        private readonly IReadOnlyList<string> _installedFontFamilies = LoadInstalledFontFamilies();

        private enum TrimDragHandle
        {
            None,
            Start,
            End
        }

        private sealed class VideoDenoiseRequest
        {
            public AudioDenoiseMode Mode { get; init; }
            public int Strength { get; init; }
        }

        private enum SubtitleEditorKind
        {
            None,
            Srt,
            AdvancedDraft
        }

        private enum PendingAdvancedSubtitleKind
        {
            None,
            Styled,
            Karaoke
        }

        private enum StyledSubtitleBasePreset
        {
            SocialImpact,
            CleanSans,
            CaptionBox,
            BroadcastLowerThird
        }

        private enum KaraokeSubtitleBasePreset
        {
            NeonKaraoke,
            Punch,
            Bubbly
        }

        private sealed class SubtitleEditableRow : INotifyPropertyChanged
        {
            public int CueNumber { get; init; }
            private string _startText = string.Empty;
            private string _endText = string.Empty;
            private string _text = string.Empty;
            private string _status = "No changes";
            public string StartText { get => _startText; set => SetField(ref _startText, value); }
            public string EndText { get => _endText; set => SetField(ref _endText, value); }
            public string Text { get => _text; set => SetField(ref _text, value); }
            public string Status { get => _status; set => SetField(ref _status, value); }
            public string OriginalStartText { get; init; } = string.Empty;
            public string OriginalEndText { get; init; } = string.Empty;
            public string OriginalText { get; init; } = string.Empty;

            public event PropertyChangedEventHandler? PropertyChanged;

            private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
            {
                if (EqualityComparer<T>.Default.Equals(field, value))
                {
                    return;
                }

                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private sealed class SubtitleEditorBinding
        {
            public required SubtitleEditableRow Row { get; init; }

            public required RichEditBox Editor { get; init; }
        }

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

            public static SubtitlePresetConfiguration CreateDefault()
            {
                return new SubtitlePresetConfiguration
                {
                    StyledPreset = StyledSubtitleBasePreset.SocialImpact,
                    KaraokePreset = KaraokeSubtitleBasePreset.NeonKaraoke,
                    FontFamily = "Impact",
                    KaraokeHighlightColor = new SubtitleColor(0, 255, 110, 0)
                };
            }
        }

        public VideoEditorPage()
        {
            _subtitlesService = new SubtitlesService(_audioTranscriptionService);
            InitializeComponent();

            InitializeDefaults();

            RefreshValidationAndState();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isTranscriptionModelInstalled = _audioTranscriptionService.IsInstalled();
            RefreshValidationAndState();

            if (e.Parameter is FileNavigationRequest navigationRequest &&
                IsSupportedVideoFile(navigationRequest.File))
            {
                await LoadVideoPreviewAsync(navigationRequest.File);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _transcriptionOperationCancellation?.Cancel();
            _transcriptionOperationCancellation?.Dispose();
            _transcriptionOperationCancellation = null;
            CleanupPreviewFiles();
            base.OnNavigatedFrom(e);
        }

        private void InitializeDefaults()
        {
            ApplyButton.IsEnabled = false;

            OutputContainerComboBox.SelectedIndex = 0;
            VideoCodecComboBox.SelectedIndex = 0;
            AudioCodecComboBox.SelectedIndex = 0;
            CompressionPresetComboBox.SelectedIndex = 2;
            SubtitleModeComboBox.SelectedIndex = 0;
            SubtitleGenerationModeComboBox.SelectedIndex = 0;
            AdvancedSubtitleTypeComboBox.SelectedIndex = 0;
            ResizeModeComboBox.SelectedIndex = 0;
            RotationComboBox.SelectedIndex = 0;
            RepairModeComboBox.SelectedIndex = 0;
            AudioSyncDirectionComboBox.SelectedIndex = 0;
            AudioDenoiseModeComboBox.SelectedIndex = 0;
            ApplySubtitleSectionDurationValue(SubtitleSectionDurationSlider?.Value ?? 6.5d, synchronizeSlider: true, synchronizeNumberBox: true, refreshState: false);
            ApplySubtitlePlacementValue(DefaultSubtitlePlacementX * 100d, DefaultSubtitlePlacementY * 100d, synchronizeX: true, synchronizeY: true, refreshState: false);
            UpdateAudioSyncOffsetText();
            UpdateAudioDenoiseStrengthText();
            UpdateCodecOptionsForSelectedContainer();
            UpdateAdvancedSubtitlePresetSummary();
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

            var contentWidth = Math.Max(0, width - OuterHorizontalPadding);
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
            _sourceVideoCodecName = null;
            _sourceAudioCodecName = null;
            ResetTrimState();
            _generatedSubtitlePath = null;
            _pendingAdvancedSubtitleOutputPath = null;
            _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;
            _previewVideoSize = new Size(1920d, 1080d);
            CleanupPreviewFiles();
            ClearSubtitleEditor();

            VideoPlayer.Source = MediaSource.CreateFromStorageFile(file);
            VideoPlayer.Visibility = Visibility.Visible;
            DropHintPanel.Visibility = Visibility.Collapsed;
            AttachVideoOpenedHandler();

            var basicProperties = await file.GetBasicPropertiesAsync();
            LoadedVideoInfoTextBlock.Text = $"Loaded video: {file.Name} ({basicProperties.Size / (1024.0 * 1024.0):0.00} MB)";

            UpdateSubtitlePlacementPreview();
            RefreshValidationAndState();

            _ = ProbeAndStoreSourceCodecsAsync(file.Path);
        }

        private async Task ProbeAndStoreSourceCodecsAsync(string path)
        {
            try
            {
                var info = await _videoProcessingService.ProbeSourceAsync(path);
                _sourceVideoCodecName = info.VideoCodecName;
                _sourceAudioCodecName = info.AudioCodecName;
            }
            catch
            {
                _sourceVideoCodecName = null;
                _sourceAudioCodecName = null;
            }
        }

        private static string CreateGeneratedSubtitlePath(StorageFile sourceVideoFile, string extension)
        {
            var directory = Path.Combine(Path.GetTempPath(), "files-tools-whisper-subtitles");
            Directory.CreateDirectory(directory);
            var safeExtension = extension.StartsWith('.') ? extension : "." + extension;
            var fileName = $"{Path.GetFileNameWithoutExtension(sourceVideoFile.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{safeExtension}";
            return Path.Combine(directory, fileName);
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
            var naturalWidth = sender.PlaybackSession.NaturalVideoWidth;
            var naturalHeight = sender.PlaybackSession.NaturalVideoHeight;
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                _videoDuration = duration;
                if (naturalWidth > 0 && naturalHeight > 0)
                {
                    _previewVideoSize = new Size(naturalWidth, naturalHeight);
                }

                if (EnableTrimCheckBox?.IsChecked ?? false)
                {
                    EnsureTrimRangeInitialized();
                }

                UpdateTrimUiState();
                UpdateSubtitlePlacementPreview();
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

            if (!await TryAutoApplySubtitleEditsAsync())
            {
                return;
            }

            var (options, denoise, errors) = BuildOptionsFromUi();
            if (errors.Count > 0)
            {
                RefreshValidationAndState(errors);
                return;
            }

            string? temporaryOutputPath = null;
            try
            {
                var selectedOutputPath = await PickOutputPathAsync(_sourceVideoFile, options.Output.Format);
                if (selectedOutputPath is null)
                {
                    return;
                }

                var outputPath = NormalizeVideoOutputPath(selectedOutputPath, options.Output.Format, _sourceVideoFile.FileType);
                var processingOutputPath = denoise is null ? outputPath : CreateTemporaryVideoPath(outputPath);
                temporaryOutputPath = denoise is null ? null : processingOutputPath;
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
                await _videoProcessingService.ProcessVideoAsync(_sourceVideoFile.Path, processingOutputPath, options, progress);

                var warnings = new List<string>();
                if (denoise is not null)
                {
                    var probe = await _videoAudioDenoiseService.ProbeAudioAsync(processingOutputPath);
                    var denoiseMode = denoise.Mode;
                    if (denoiseMode == AudioDenoiseMode.StrongStereo && probe.Channels != 2)
                    {
                        denoiseMode = AudioDenoiseMode.Mono;
                        warnings.Add("Stereo denoise was selected, but staged output audio is not stereo. Denoise was applied in mono mode.");
                    }

                    var denoiseOptions = new VideoAudioDenoiseOptions
                    {
                        Mode = denoiseMode,
                        DenoiseAmount = denoise.Strength,
                        DenoisePasses = denoise.Strength >= 95 ? 3 : denoise.Strength >= 75 ? 2 : 1,
                        OutputAudioCodec = MapAudioCodecToEncoder(options.Output.AudioCodec, options.Output.Format),
                        OutputAudioBitrateKbps = probe.BitrateKbps
                    };

                    var denoiseProgress = new Progress<DenoiseProgress>(UpdateDenoiseProgress);
                    await _videoAudioDenoiseService.DenoiseVideoAudioAsync(processingOutputPath, outputPath, denoiseOptions, denoiseProgress);
                }

                var doneDialog = new ContentDialog
                {
                    Title = "Done",
                    Content = BuildCompletionMessage(outputPath, warnings),
                    PrimaryButtonText = "OK",
                    XamlRoot = XamlRoot
                };

                _ = await doneDialog.ShowAsync();
                ResetProcessingState();
            }
            catch (DenoiseValidationException ex)
            {
                ResetProcessingState();
                await ShowSimpleDialogAsync("Audio denoise error", ex.Message);
            }
            catch (DenoiseUnsupportedMediaException ex)
            {
                ResetProcessingState();
                await ShowSimpleDialogAsync("Audio denoise error", ex.Message);
            }
            catch (DenoiseModelException ex)
            {
                ResetProcessingState();
                await ShowSimpleDialogAsync("Audio denoise model error", ex.Message);
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
                if (temporaryOutputPath is not null)
                {
                    try
                    {
                        if (File.Exists(temporaryOutputPath))
                        {
                            File.Delete(temporaryOutputPath);
                        }
                    }
                    catch
                    {
                        // Best effort cleanup.
                    }
                }

                _isProcessing = false;
                RefreshValidationAndState();
            }
        }

        private static string BuildCompletionMessage(string outputPath, IReadOnlyList<string> warnings)
        {
            if (warnings.Count == 0)
            {
                return $"Video saved to:\n{outputPath}";
            }

            return $"Video saved to:\n{outputPath}\n\nWarnings:\n- {string.Join("\n- ", warnings)}";
        }

        private static string CreateTemporaryVideoPath(string finalOutputPath)
        {
            var extension = Path.GetExtension(finalOutputPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".mp4";
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "files-tools-video-stage");
            Directory.CreateDirectory(tempDirectory);
            return Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + extension);
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
            _isSubtitleSectionActive = string.Equals(section, "Media", StringComparison.Ordinal) &&
                string.Equals(subgroup, "Subtitles", StringComparison.Ordinal);
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

            if (SubtitleEditorBorder is not null)
            {
                SubtitleEditorBorder.Visibility = _isSubtitleSectionActive && ShouldShowSubtitleEditor()
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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

        private void AnyOptionChanged_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, EnableSubtitleMuxCheckBox))
            {
                if (IsAdvancedTranscriptionReviewActive())
                {
                    if (EnableSubtitleMuxCheckBox?.IsChecked == true)
                    {
                        LoadAdvancedSubtitleEditor(_advancedSubtitleDraft!);
                    }
                    else
                    {
                        ClearSubtitleEditor();
                    }

                    RefreshValidationAndState();
                    return;
                }

                if (EnableSubtitleMuxCheckBox?.IsChecked == true)
                {
                    TryLoadSubtitleEditorFromPath(SubtitlePathTextBox?.Text);
                }
                else if (!HasPendingTranscriptionReview())
                {
                    ClearSubtitleEditor();
                }
            }

            RefreshValidationAndState();
        }

        private void AnyOptionChanged_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshValidationAndState();
        }

        private void AnyOptionChanged_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ReferenceEquals(sender, SubtitlePathTextBox))
            {
                if (IsAdvancedTranscriptionReviewActive())
                {
                    RefreshValidationAndState();
                    return;
                }

                TryLoadSubtitleEditorFromPath(SubtitlePathTextBox?.Text);
            }

            RefreshValidationAndState();
        }
        private void AnyOptionChanged_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => RefreshValidationAndState();

        private void SubtitleGenerationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshValidationAndState();
        }

        private void AdvancedSubtitleTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshValidationAndState();
        }

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

            UpdateCodecComboItems(VideoCodecComboBox, videoSupported, allowRemoveAudio: false, _sourceVideoCodecName);
            UpdateCodecComboItems(AudioCodecComboBox, audioSupported, allowRemoveAudio: true, _sourceAudioCodecName);
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

        private static void UpdateCodecComboItems(ComboBox? comboBox, HashSet<string> supportedCodecs, bool allowRemoveAudio, string? originalCodecName = null)
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
                return;
            }

            // When "Keep original" is selected but the source codec is incompatible with the chosen
            // container, switch to the first supported codec so the output isn't silently broken.
            if (supportedCodecs.Count > 0 &&
                originalCodecName is not null &&
                !supportedCodecs.Contains(originalCodecName) &&
                comboBox.SelectedItem is ComboBoxItem currentItem &&
                string.Equals(currentItem.Content?.ToString(), "Keep original", StringComparison.Ordinal))
            {
                var firstSupported = comboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => supportedCodecs.Contains(i.Content?.ToString() ?? string.Empty));
                if (firstSupported is not null)
                    comboBox.SelectedItem = firstSupported;
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

        private void AudioDenoiseModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAudioDenoiseStrengthText();
            RefreshValidationAndState();
        }

        private void AudioDenoiseStrengthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateAudioDenoiseStrengthText();
            RefreshValidationAndState();
        }

        private void SubtitleSectionDurationSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ApplySubtitleSectionDurationValue(e.NewValue, synchronizeSlider: false, synchronizeNumberBox: true);
        }

        private void SubtitleSectionDurationNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(sender.Value))
            {
                return;
            }

            ApplySubtitleSectionDurationValue(sender.Value, synchronizeSlider: true, synchronizeNumberBox: false);
        }

        private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSubtitlePlacementPreview();
        }

        private void SubtitlePlacementXNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(sender.Value))
            {
                return;
            }

            ApplySubtitlePlacementValue(sender.Value, _subtitlePlacementY * 100d, synchronizeX: false, synchronizeY: true);
        }

        private void SubtitlePlacementYNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(sender.Value))
            {
                return;
            }

            ApplySubtitlePlacementValue(_subtitlePlacementX * 100d, sender.Value, synchronizeX: true, synchronizeY: false);
        }

        private void SubtitlePlacementMarker_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!ShouldShowSubtitlePlacementControls() || SubtitlePlacementCanvas is null)
            {
                return;
            }

            var videoBounds = GetPreviewVideoBounds();
            if (videoBounds.Width <= 0 || videoBounds.Height <= 0)
            {
                return;
            }

            var normalizedDeltaX = e.HorizontalChange / videoBounds.Width;
            var normalizedDeltaY = e.VerticalChange / videoBounds.Height;

            var newNormalizedX = Math.Clamp(_subtitlePlacementX + normalizedDeltaX, 0d, 1d);
            var newNormalizedY = Math.Clamp(_subtitlePlacementY + normalizedDeltaY, 0d, 1d);

            ApplySubtitlePlacementValue(newNormalizedX * 100d, newNormalizedY * 100d, synchronizeX: true, synchronizeY: true);
        }

        private void ResetSubtitlePlacementButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySubtitlePlacementValue(DefaultSubtitlePlacementX * 100d, DefaultSubtitlePlacementY * 100d, synchronizeX: true, synchronizeY: true);
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
            _pendingAdvancedSubtitleOutputPath = null;
            _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;
            if (!string.Equals(_generatedSubtitlePath, file.Path, StringComparison.OrdinalIgnoreCase))
            {
                _generatedSubtitlePath = null;
            }
            TryLoadSubtitleEditorFromPath(file.Path);
            RefreshValidationAndState();
        }

        private async void DownloadTranscriptionFeatureButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstallingTranscriptionModel || _isGeneratingSubtitles || _isProcessing)
            {
                return;
            }

            _transcriptionOperationCancellation?.Cancel();
            _transcriptionOperationCancellation?.Dispose();
            _transcriptionOperationCancellation = new CancellationTokenSource();

            try
            {
                _isInstallingTranscriptionModel = true;
                RefreshValidationAndState();

                var lastProgressUpdate = DateTimeOffset.UtcNow;
                var progress = new Progress<AudioTranscriptionInstallProgress>(update =>
                {
                    var now = DateTimeOffset.UtcNow;
                    if ((now - lastProgressUpdate).TotalMilliseconds < 200d)
                    {
                        return;
                    }

                    lastProgressUpdate = now;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (TranscriptionDownloadProgressBar is not null)
                        {
                            TranscriptionDownloadProgressBar.IsIndeterminate = false;
                            TranscriptionDownloadProgressBar.Value = Math.Clamp(update.FractionComplete, 0d, 1d);
                        }

                        if (TranscriptionDownloadStatusTextBlock is not null)
                        {
                            TranscriptionDownloadStatusTextBlock.Text = $"{update.Stage} ({(Math.Clamp(update.FractionComplete, 0d, 1d) * 100d):0}%)";
                        }
                    });
                });

                await _audioTranscriptionService.InstallAsync(progress, _transcriptionOperationCancellation.Token);
                _isTranscriptionModelInstalled = _audioTranscriptionService.IsInstalled();
                RefreshValidationAndState();
            }
            catch (OperationCanceledException)
            {
                _ = ShowSimpleDialogAsync("Transcription download canceled", "Transcription feature download was canceled.");
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync("Transcription download error", ex.Message);
            }
            finally
            {
                _isInstallingTranscriptionModel = false;
                RefreshValidationAndState();
            }
        }

        private async void GenerateSubtitlesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceVideoFile is null || _isGeneratingSubtitles || _isInstallingTranscriptionModel || _isProcessing)
            {
                return;
            }

            if (!_audioTranscriptionService.IsInstalled())
            {
                await ShowSimpleDialogAsync("Transcription feature required", "Download transcription feature before generating subtitles.");
                return;
            }

            _transcriptionOperationCancellation?.Cancel();
            _transcriptionOperationCancellation?.Dispose();
            _transcriptionOperationCancellation = new CancellationTokenSource();

            try
            {
                _isGeneratingSubtitles = true;
                RefreshValidationAndState();

                var isAdvanced = IsAdvancedSubtitleModeSelected();
                var outputPath = CreateGeneratedSubtitlePath(_sourceVideoFile, isAdvanced ? ".ass" : ".srt");
                var progress = new Progress<AudioTranscriptionProgress>(update =>
                {
                    if (TranscriptionProgressBar is not null)
                    {
                        TranscriptionProgressBar.Visibility = Visibility.Visible;
                        TranscriptionProgressBar.IsIndeterminate = false;
                        TranscriptionProgressBar.Value = Math.Clamp(update.OverallPercent, 0d, 1d);
                    }

                    if (TranscriptionEtaTextBlock is not null)
                    {
                        TranscriptionEtaTextBlock.Visibility = Visibility.Visible;
                        TranscriptionEtaTextBlock.Text = update.EstimatedRemainingTime is TimeSpan eta
                            ? $"{update.StageDescription} - ETA {FormatEta(eta)}"
                            : $"{update.StageDescription} - ETA calculating...";
                    }
                });

                var subtitleOptions = CreateSubtitlePostprocessingOptionsFromUi();
                string generatedPath;
                if (!isAdvanced)
                {
                    generatedPath = await _subtitlesService.GenerateSrtAsync(
                        _sourceVideoFile.Path,
                        outputPath,
                        progress,
                        _transcriptionOperationCancellation.Token);
                    _advancedSubtitleDraft = null;
                    _pendingAdvancedSubtitleOutputPath = null;
                    _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;
                }
                else
                {
                    var draft = await _subtitlesService.GenerateAdvancedDraftAsync(
                        _sourceVideoFile.Path,
                        subtitleOptions,
                        progress,
                        _transcriptionOperationCancellation.Token);
                    _advancedSubtitleDraft = draft;
                    _pendingAdvancedSubtitleOutputPath = outputPath;
                    _pendingAdvancedSubtitleKind = IsKaraokeAdvancedSubtitleTypeSelected()
                        ? PendingAdvancedSubtitleKind.Karaoke
                        : PendingAdvancedSubtitleKind.Styled;
                    generatedPath = outputPath;
                    LoadAdvancedSubtitleEditor(draft);
                    if (TranscriptionReadyStatusTextBlock is not null)
                    {
                        TranscriptionReadyStatusTextBlock.Text = draft.Cues.Count == 0
                            ? "Subtitle sections generated, but no editable review sections were produced."
                            : IsKaraokeAdvancedSubtitleTypeSelected()
                                ? $"Subtitle sections ready with {draft.Cues.Count} editable review sections for karaoke subtitles."
                                : $"Subtitle sections ready with {draft.Cues.Count} editable review sections.";
                    }
                }

                var isAdvancedReview = isAdvanced;
                _generatedSubtitlePath = isAdvancedReview
                    ? null
                    : generatedPath;
                SubtitlePathTextBox.Text = isAdvancedReview
                    ? string.Empty
                    : generatedPath;
                EnableSubtitleMuxCheckBox.IsChecked = true;
                if (!isAdvancedReview)
                {
                    TryLoadSubtitleEditorFromPath(generatedPath);
                }
                RefreshValidationAndState();
            }
            catch (OperationCanceledException)
            {
                _ = ShowSimpleDialogAsync("Transcription canceled", "Subtitle generation was canceled.");
            }
            catch (AudioTranscriptionNotInstalledException ex)
            {
                await ShowSimpleDialogAsync("Transcription feature required", ex.Message);
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync("Subtitle generation error", ex.Message);
            }
            finally
            {
                _isGeneratingSubtitles = false;
                if (TranscriptionProgressBar is not null)
                {
                    TranscriptionProgressBar.Visibility = Visibility.Collapsed;
                    TranscriptionProgressBar.IsIndeterminate = false;
                    TranscriptionProgressBar.Value = 0;
                }

                if (TranscriptionEtaTextBlock is not null)
                {
                    TranscriptionEtaTextBlock.Visibility = Visibility.Collapsed;
                    TranscriptionEtaTextBlock.Text = "ETA calculating...";
                }

                RefreshValidationAndState();
            }
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

            _isTranscriptionModelInstalled = _audioTranscriptionService.IsInstalled();
            UpdateTranscriptionUiState();
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
            SetDependentOptionsState(
                AudioDenoisePanel,
                (EnableAudioDenoiseCheckBox?.IsChecked ?? false) && !ShouldDisableOutputAudioVolume());
            SetDependentOptionsState(
                AudioDenoiseStrengthPanel,
                (EnableAudioDenoiseCheckBox?.IsChecked ?? false) &&
                IsStereoBlendDenoiseModeSelected() &&
                !ShouldDisableOutputAudioVolume());

            if (AudioDenoiseStrengthPanel is not null)
            {
                AudioDenoiseStrengthPanel.Visibility = IsStereoBlendDenoiseModeSelected() ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SubtitleSectionWordCapNumberBox is not null)
            {
                SubtitleSectionWordCapNumberBox.IsEnabled = EnableSubtitleSectionWordCapCheckBox?.IsChecked == true;
            }

            if (ExtractAudioButton is not null)
            {
                ExtractAudioButton.IsEnabled = _sourceVideoFile is not null && !_isProcessing;
            }

            if (DownloadTranscriptionFeatureButton is not null)
            {
                DownloadTranscriptionFeatureButton.IsEnabled = !_isProcessing && !_isGeneratingSubtitles && !_isInstallingTranscriptionModel;
            }

            if (GenerateSubtitlesButton is not null)
            {
                GenerateSubtitlesButton.IsEnabled =
                    _sourceVideoFile is not null &&
                    _isTranscriptionModelInstalled &&
                    !_isProcessing &&
                    !_isGeneratingSubtitles &&
                    !_isInstallingTranscriptionModel;
            }

            if (RepairOptionsPanel is not null)
            {
                var repairEnabled = EnableRepairCheckBox?.IsChecked ?? false;
                RepairOptionsPanel.IsEnabled = repairEnabled;
                RepairOptionsPanel.Opacity = repairEnabled ? 1d : 0.5d;
            }

            var subtitlePlacementEnabled = EnableSubtitleMuxCheckBox?.IsChecked ?? false;
            if (SubtitlePlacementXNumberBox is not null)
            {
                SubtitlePlacementXNumberBox.IsEnabled = subtitlePlacementEnabled;
            }

            if (SubtitlePlacementYNumberBox is not null)
            {
                SubtitlePlacementYNumberBox.IsEnabled = subtitlePlacementEnabled;
            }

            if (ResetSubtitlePlacementButton is not null)
            {
                ResetSubtitlePlacementButton.IsEnabled = subtitlePlacementEnabled;
            }

            UpdateSubtitlePlacementPreview();
        }

        private void UpdateTranscriptionUiState()
        {
            var showDownloadUi = !_isTranscriptionModelInstalled;
            var advancedMode = IsAdvancedSubtitleModeSelected();

            if (DownloadTranscriptionFeatureButton is not null)
            {
                DownloadTranscriptionFeatureButton.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TranscriptionDownloadProgressBar is not null)
            {
                TranscriptionDownloadProgressBar.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TranscriptionDownloadStatusTextBlock is not null)
            {
                TranscriptionDownloadStatusTextBlock.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TranscriptionDownloadProgressBar is not null)
            {
                if (_isInstallingTranscriptionModel)
                {
                    TranscriptionDownloadProgressBar.IsIndeterminate = false;
                }
                else if (_isTranscriptionModelInstalled)
                {
                    TranscriptionDownloadProgressBar.IsIndeterminate = false;
                    TranscriptionDownloadProgressBar.Value = 1d;
                }
                else
                {
                    TranscriptionDownloadProgressBar.IsIndeterminate = false;
                    TranscriptionDownloadProgressBar.Value = 0d;
                }
            }

            if (TranscriptionDownloadStatusTextBlock is not null && !_isInstallingTranscriptionModel)
            {
                TranscriptionDownloadStatusTextBlock.Text = _isTranscriptionModelInstalled
                    ? "Transcription feature downloaded."
                    : "Transcription feature not downloaded yet.";
            }

            if (TranscriptionReadyStatusTextBlock is not null)
            {
                if (_isGeneratingSubtitles)
                {
                    TranscriptionReadyStatusTextBlock.Text = "Generating subtitles...";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_generatedSubtitlePath) && File.Exists(_generatedSubtitlePath))
                {
                    TranscriptionReadyStatusTextBlock.Text = $"Subtitles ready to insert: {_generatedSubtitlePath}";
                }
                else if (IsAdvancedTranscriptionReviewActive() && _advancedSubtitleDraft is not null && _subtitleEditorKind == SubtitleEditorKind.AdvancedDraft)
                {
                    TranscriptionReadyStatusTextBlock.Text = _pendingAdvancedSubtitleKind == PendingAdvancedSubtitleKind.Karaoke
                        ? "Subtitle sections are ready for review. Final karaoke ASS will be rendered from these reviewed sections when you apply/export."
                        : "Subtitle sections are ready for review. Final styled ASS will be rendered from these reviewed sections when you apply/export.";
                }
                else
                {
                    TranscriptionReadyStatusTextBlock.Text = "Subtitles not generated yet.";
                }
            }

            if (AdvancedSubtitleOptionsPanel is not null)
            {
                AdvancedSubtitleOptionsPanel.Visibility = advancedMode ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SubtitleEditorBorder is not null)
            {
                SubtitleEditorBorder.Visibility = ShouldShowSubtitleEditor() ? Visibility.Visible : Visibility.Collapsed;
            }

        }

        private bool IsAdvancedSubtitleModeSelected()
        {
            return (SubtitleGenerationModeComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Advanced (ASS)";
        }

        private bool IsKaraokeAdvancedSubtitleTypeSelected()
        {
            return (AdvancedSubtitleTypeComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Karaoke ASS";
        }

        private async void ConfigureAdvancedSubtitlesButton_Click(object sender, RoutedEventArgs e)
        {
            var isKaraokeMode = IsKaraokeAdvancedSubtitleTypeSelected();

            var basePresetComboBox = new ComboBox
            {
                Header = "Base preset"
            };

            if (isKaraokeMode)
            {
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "NeonKaraoke" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "Punch" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "Bubbly" });
                basePresetComboBox.SelectedIndex = _advancedSubtitlePresetConfiguration.KaraokePreset switch
                {
                    KaraokeSubtitleBasePreset.Punch => 1,
                    KaraokeSubtitleBasePreset.Bubbly => 2,
                    _ => 0
                };
            }
            else
            {
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "SocialImpact" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "CleanSans" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "CaptionBox" });
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = "BroadcastLowerThird" });
                basePresetComboBox.SelectedIndex = _advancedSubtitlePresetConfiguration.StyledPreset switch
                {
                    StyledSubtitleBasePreset.CleanSans => 1,
                    StyledSubtitleBasePreset.CaptionBox => 2,
                    StyledSubtitleBasePreset.BroadcastLowerThird => 3,
                    _ => 0
                };
            }

            var fontSizeNumberBox = new NumberBox
            {
                Header = "Font size",
                Minimum = 24,
                Maximum = 160,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _advancedSubtitlePresetConfiguration.FontSize
            };

            var fontFamilyComboBox = new ComboBox
            {
                Header = "System font",
                ItemsSource = _installedFontFamilies.ToList(),
                PlaceholderText = "Select an installed font"
            };
            if (_installedFontFamilies.Contains(_advancedSubtitlePresetConfiguration.FontFamily, StringComparer.OrdinalIgnoreCase))
            {
                var selected = _installedFontFamilies.First(name => string.Equals(name, _advancedSubtitlePresetConfiguration.FontFamily, StringComparison.OrdinalIgnoreCase));
                fontFamilyComboBox.SelectedItem = selected;
            }

            var outlineNumberBox = new NumberBox
            {
                Header = "Outline width",
                Minimum = 0,
                Maximum = 20,
                SmallChange = 0.5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _advancedSubtitlePresetConfiguration.OutlineWidth
            };

            var marginVerticalNumberBox = new NumberBox
            {
                Header = "Vertical margin",
                Minimum = 0,
                Maximum = 400,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _advancedSubtitlePresetConfiguration.MarginVertical
            };

            var boldCheckBox = new CheckBox
            {
                Content = "Bold text",
                IsChecked = _advancedSubtitlePresetConfiguration.Bold
            };

            var textTransformComboBox = new ComboBox
            {
                Header = "Text transform"
            };
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = "Original case" });
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = "UPPERCASE" });
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = "lowercase" });
            textTransformComboBox.SelectedIndex = _advancedSubtitlePresetConfiguration.TextTransform switch
            {
                SubtitleTextTransform.Uppercase => 1,
                SubtitleTextTransform.Lowercase => 2,
                _ => 0
            };

            var karaokeAccentColorPicker = new ColorPicker
            {
                Color = ToUiColor(_advancedSubtitlePresetConfiguration.KaraokeHighlightColor),
                Visibility = isKaraokeMode ? Visibility.Visible : Visibility.Collapsed,
                IsEnabled = isKaraokeMode
            };
            var karaokeAccentLabel = new TextBlock
            {
                Text = "Karaoke highlight color",
                Visibility = isKaraokeMode ? Visibility.Visible : Visibility.Collapsed
            };

            basePresetComboBox.SelectionChanged += (_, _) =>
            {
                var defaultFont = GetDefaultFontFamilyForPreset(basePresetComboBox.SelectedIndex, isKaraokeMode);
                if (_installedFontFamilies.Contains(defaultFont, StringComparer.OrdinalIgnoreCase))
                {
                    fontFamilyComboBox.SelectedItem = _installedFontFamilies.First(name => string.Equals(name, defaultFont, StringComparison.OrdinalIgnoreCase));
                }

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
                outlineNumberBox.Value = defaults.OutlineWidth;
                marginVerticalNumberBox.Value = defaults.MarginVertical;
                boldCheckBox.IsChecked = defaults.Bold;
                textTransformComboBox.SelectedIndex = defaults.TextTransform switch
                {
                    SubtitleTextTransform.Uppercase => 1,
                    SubtitleTextTransform.Lowercase => 2,
                    _ => 0
                };
            };

            var content = new StackPanel
            {
                Spacing = 10
            };
            content.Children.Add(new TextBlock
            {
                Text = "Choose a preset and tune the typography, presentation, and karaoke accent used for advanced subtitle rendering.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.76
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

            var scrollableContent = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 560,
                MinWidth = 420,
                Content = content
            };

            var dialog = new ContentDialog
            {
                Title = "Configure advanced subtitles",
                Content = scrollableContent,
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Reset defaults",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                _advancedSubtitlePresetConfiguration = SubtitlePresetConfiguration.CreateDefault();
                UpdateAdvancedSubtitlePresetSummary();
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var newConfig = SubtitlePresetConfiguration.CreateDefault();
            newConfig.FontFamily = (fontFamilyComboBox.SelectedItem as string) ?? GetDefaultFontFamilyForPreset(basePresetComboBox.SelectedIndex, isKaraokeMode);
            newConfig.FontSize = Math.Clamp(double.IsNaN(fontSizeNumberBox.Value) ? 72d : fontSizeNumberBox.Value, 24d, 160d);
            newConfig.OutlineWidth = Math.Clamp(double.IsNaN(outlineNumberBox.Value) ? 5d : outlineNumberBox.Value, 0d, 20d);
            newConfig.MarginVertical = Math.Clamp(double.IsNaN(marginVerticalNumberBox.Value) ? 90 : (int)Math.Round(marginVerticalNumberBox.Value), 0, 400);
            newConfig.Bold = boldCheckBox.IsChecked == true;
            newConfig.TextTransform = textTransformComboBox.SelectedIndex switch
            {
                1 => SubtitleTextTransform.Uppercase,
                2 => SubtitleTextTransform.Lowercase,
                _ => SubtitleTextTransform.None
            };
            newConfig.KaraokeHighlightColor = FromUiColor(karaokeAccentColorPicker.Color);

            if (isKaraokeMode)
            {
                newConfig.KaraokePreset = basePresetComboBox.SelectedIndex switch
                {
                    1 => KaraokeSubtitleBasePreset.Punch,
                    2 => KaraokeSubtitleBasePreset.Bubbly,
                    _ => KaraokeSubtitleBasePreset.NeonKaraoke
                };
            }
            else
            {
                newConfig.StyledPreset = basePresetComboBox.SelectedIndex switch
                {
                    1 => StyledSubtitleBasePreset.CleanSans,
                    2 => StyledSubtitleBasePreset.CaptionBox,
                    3 => StyledSubtitleBasePreset.BroadcastLowerThird,
                    _ => StyledSubtitleBasePreset.SocialImpact
                };
            }

            _advancedSubtitlePresetConfiguration = newConfig;

            UpdateAdvancedSubtitlePresetSummary();
        }

        private void UpdateAdvancedSubtitlePresetSummary()
        {
            if (AdvancedSubtitlePresetSummaryTextBlock is null)
            {
                return;
            }

            var isKaraokeMode = IsKaraokeAdvancedSubtitleTypeSelected();
            var (presetName, presentation) = isKaraokeMode
                ? (_advancedSubtitlePresetConfiguration.KaraokePreset.ToString(), _advancedSubtitlePresetConfiguration.KaraokePreset switch
                {
                    KaraokeSubtitleBasePreset.Punch => "Punch karaoke",
                    KaraokeSubtitleBasePreset.Bubbly => "Bubbly karaoke",
                    _ => "Neon karaoke"
                })
                : (_advancedSubtitlePresetConfiguration.StyledPreset.ToString(), _advancedSubtitlePresetConfiguration.StyledPreset switch
                {
                    StyledSubtitleBasePreset.CaptionBox => "Boxed caption",
                    StyledSubtitleBasePreset.BroadcastLowerThird => "Lower third",
                    StyledSubtitleBasePreset.CleanSans => "Clean fade",
                    _ => "Impact pop"
                });
            var textTransform = _advancedSubtitlePresetConfiguration.TextTransform switch
            {
                SubtitleTextTransform.Uppercase => "Uppercase",
                SubtitleTextTransform.Lowercase => "Lowercase",
                _ => "Original case"
            };
            var fontWeight = _advancedSubtitlePresetConfiguration.Bold ? "Bold" : "Regular";
            AdvancedSubtitlePresetSummaryTextBlock.Text =
                $"Preset: {presetName} ({presentation}) | {_advancedSubtitlePresetConfiguration.FontFamily} {_advancedSubtitlePresetConfiguration.FontSize:0.#} | {fontWeight} | {textTransform} | Outline {_advancedSubtitlePresetConfiguration.OutlineWidth:0.#}";
        }

        private SubtitleStylePreset CreateAdvancedSubtitleStylePresetFromConfiguration()
        {
            var isKaraokeMode = IsKaraokeAdvancedSubtitleTypeSelected();
            var basePreset = isKaraokeMode
                ? _advancedSubtitlePresetConfiguration.KaraokePreset switch
                {
                    KaraokeSubtitleBasePreset.Punch => KaraokeSubtitlePresets.CreatePunch(),
                    KaraokeSubtitleBasePreset.Bubbly => KaraokeSubtitlePresets.CreateBubbly(),
                    _ => KaraokeSubtitlePresets.CreateNeonKaraoke()
                }
                : _advancedSubtitlePresetConfiguration.StyledPreset switch
                {
                    StyledSubtitleBasePreset.CleanSans => StyledSubtitlePresets.CreateCleanSans(),
                    StyledSubtitleBasePreset.CaptionBox => StyledSubtitlePresets.CreateCaptionBox(),
                    StyledSubtitleBasePreset.BroadcastLowerThird => StyledSubtitlePresets.CreateBroadcastLowerThird(),
                    _ => StyledSubtitlePresets.CreateSocialImpact()
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
                PrimaryFontFamily = _advancedSubtitlePresetConfiguration.FontFamily,
                FontFamilyFallbacks = BuildFontFallbacks(_advancedSubtitlePresetConfiguration.FontFamily, basePreset.FontFamilyFallbacks),
                FontSize = _advancedSubtitlePresetConfiguration.FontSize,
                Bold = _advancedSubtitlePresetConfiguration.Bold,
                Italic = basePreset.Italic,
                TextTransform = _advancedSubtitlePresetConfiguration.TextTransform,
                FillColor = basePreset.FillColor,
                OutlineColor = basePreset.OutlineColor,
                ShadowColor = basePreset.ShadowColor,
                KaraokeHighlightColor = _advancedSubtitlePresetConfiguration.KaraokeHighlightColor,
                UseBackgroundBox = basePreset.UseBackgroundBox,
                PresentationAnimation = basePreset.PresentationAnimation,
                EntryFadeMilliseconds = basePreset.EntryFadeMilliseconds,
                ExitFadeMilliseconds = basePreset.ExitFadeMilliseconds,
                IntroScale = basePreset.IntroScale,
                OutlineWidth = _advancedSubtitlePresetConfiguration.OutlineWidth,
                ShadowDepth = basePreset.ShadowDepth,
                Alignment = basePreset.Alignment,
                MarginLeft = basePreset.MarginLeft,
                MarginRight = basePreset.MarginRight,
                MarginVertical = _advancedSubtitlePresetConfiguration.MarginVertical,
                PositionX = basePreset.PositionX,
                PositionY = basePreset.PositionY,
                MaxLines = basePreset.MaxLines,
                MaxCharsPerLine = basePreset.MaxCharsPerLine
            };
        }

        private static string GetDefaultFontFamilyForPreset(int basePresetIndex, bool isKaraokeMode)
        {
            if (isKaraokeMode)
            {
                return basePresetIndex switch
                {
                    1 => "Arial Black",
                    2 => "Bahnschrift",
                    _ => "Segoe UI Semibold"
                };
            }

            return basePresetIndex switch
            {
                1 => "Segoe UI",
                2 => "Arial",
                3 => "Segoe UI Semibold",
                _ => "Impact"
            };
        }

        private static IReadOnlyList<string> BuildFontFallbacks(string primary, IReadOnlyList<string> existingFallbacks)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(primary))
            {
                result.Add(primary.Trim());
            }

            foreach (var fallback in existingFallbacks)
            {
                if (string.IsNullOrWhiteSpace(fallback))
                {
                    continue;
                }

                if (result.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(fallback);
            }

            if (result.Count == 0)
            {
                result.Add("Arial");
            }

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

                if (names.Length > 0)
                {
                    return names;
                }
            }
            catch
            {
            }

            return new[] { "Impact", "Segoe UI", "Arial" };
        }

        private static Color ToUiColor(SubtitleColor color)
        {
            return Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        }

        private static SubtitleColor FromUiColor(Color color)
        {
            return new SubtitleColor(color.A, color.R, color.G, color.B);
        }

        private SubtitlePostprocessingOptions CreateSubtitlePostprocessingOptionsFromUi()
        {
            var maximumDurationSeconds = SubtitleSectionDurationSlider?.Value ?? 6.5d;
            var maxWordsPerSection = EnableSubtitleSectionWordCapCheckBox?.IsChecked == true &&
                SubtitleSectionWordCapNumberBox is not null &&
                !double.IsNaN(SubtitleSectionWordCapNumberBox.Value)
                ? Math.Max(1, (int)Math.Round(SubtitleSectionWordCapNumberBox.Value, MidpointRounding.AwayFromZero))
                : (int?)null;
            return new SubtitlePostprocessingOptions
            {
                MaximumDuration = TimeSpan.FromSeconds(maximumDurationSeconds),
                MaxWordsPerSection = maxWordsPerSection
            };
        }

        private SubtitlePlacementOptions CreateSubtitlePlacementOptionsFromUi()
        {
            return new SubtitlePlacementOptions
            {
                NormalizedX = _subtitlePlacementX,
                NormalizedY = _subtitlePlacementY
            };
        }

        private void ApplySubtitlePlacementValue(double xPercent, double yPercent, bool synchronizeX, bool synchronizeY, bool refreshState = true)
        {
            if (_isSynchronizingSubtitlePlacementControls)
            {
                return;
            }

            var normalizedX = Math.Clamp(Math.Round(xPercent, 1, MidpointRounding.AwayFromZero), 0d, 100d);
            var normalizedY = Math.Clamp(Math.Round(yPercent, 1, MidpointRounding.AwayFromZero), 0d, 100d);

            _isSynchronizingSubtitlePlacementControls = true;
            try
            {
                if (synchronizeX &&
                    SubtitlePlacementXNumberBox is not null &&
                    (double.IsNaN(SubtitlePlacementXNumberBox.Value) || Math.Abs(SubtitlePlacementXNumberBox.Value - normalizedX) > 0.01d))
                {
                    SubtitlePlacementXNumberBox.Value = normalizedX;
                }

                if (synchronizeY &&
                    SubtitlePlacementYNumberBox is not null &&
                    (double.IsNaN(SubtitlePlacementYNumberBox.Value) || Math.Abs(SubtitlePlacementYNumberBox.Value - normalizedY) > 0.01d))
                {
                    SubtitlePlacementYNumberBox.Value = normalizedY;
                }
            }
            finally
            {
                _isSynchronizingSubtitlePlacementControls = false;
            }

            _subtitlePlacementX = normalizedX / 100d;
            _subtitlePlacementY = normalizedY / 100d;
            UpdateSubtitlePlacementPreview();

            if (refreshState)
            {
                RefreshValidationAndState();
            }
        }

        private void UpdateSubtitlePlacementPreview()
        {
            if (SubtitlePlacementMarker is null ||
                SubtitlePlacementCanvas is null ||
                SubtitlePlacementStatusTextBlock is null)
            {
                return;
            }

            var shouldShow = ShouldShowSubtitlePlacementControls();
            SubtitlePlacementMarker.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            SubtitlePlacementCanvas.IsHitTestVisible = shouldShow;

            var previewTextBlock = FindSubtitlePlacementPreviewTextBlock();
            if (previewTextBlock is not null)
            {
                previewTextBlock.Text = $"X {(int)Math.Round(_subtitlePlacementX * 100d):0}%  Y {(int)Math.Round(_subtitlePlacementY * 100d):0}%";
            }

            SubtitlePlacementStatusTextBlock.Text = BuildSubtitlePlacementStatusText();

            if (!shouldShow)
            {
                return;
            }

            SubtitlePlacementCanvas.Width = PreviewHost?.ActualWidth ?? 0d;
            SubtitlePlacementCanvas.Height = PreviewHost?.ActualHeight ?? 0d;

            var videoBounds = GetPreviewVideoBounds();
            if (videoBounds.Width <= 0 || videoBounds.Height <= 0)
            {
                Canvas.SetLeft(SubtitlePlacementMarker, 0d);
                Canvas.SetTop(SubtitlePlacementMarker, 0d);
                return;
            }

            var markerHeight = Math.Max(SubtitlePlacementMarkerMinHeight, SubtitlePlacementMarker.ActualHeight);
            var centerX = videoBounds.X + (_subtitlePlacementX * videoBounds.Width);
            var centerY = videoBounds.Y + (_subtitlePlacementY * videoBounds.Height);
            var left = Math.Clamp(centerX - (SubtitlePlacementMarkerWidth / 2d), videoBounds.X, videoBounds.X + videoBounds.Width - SubtitlePlacementMarkerWidth);
            var top = Math.Clamp(centerY - (markerHeight / 2d), videoBounds.Y, videoBounds.Y + videoBounds.Height - markerHeight);

            Canvas.SetLeft(SubtitlePlacementMarker, left);
            Canvas.SetTop(SubtitlePlacementMarker, top);
        }

        private TextBlock? FindSubtitlePlacementPreviewTextBlock()
        {
            if (SubtitlePlacementMarker?.Template is null)
            {
                return null;
            }

            return FindChildByName<TextBlock>(SubtitlePlacementMarker, "SubtitlePlacementPreviewTextBlock");
        }

        private T? FindChildByName<T>(DependencyObject parent, string name) where T : DependencyObject
        {
            var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && child.GetValue(FrameworkElement.NameProperty) as string == name)
                {
                    return typedChild;
                }

                var result = FindChildByName<T>(child, name);
                if (result is not null)
                {
                    return result;
                }
            }

            return null;
        }

        private Rect GetPreviewVideoBounds()
        {
            if (PreviewHost is null)
            {
                return Rect.Empty;
            }

            var hostWidth = PreviewHost.ActualWidth;
            var hostHeight = PreviewHost.ActualHeight;
            if (hostWidth <= 0 || hostHeight <= 0)
            {
                return Rect.Empty;
            }

            var videoWidth = _previewVideoSize.Width > 0 ? _previewVideoSize.Width : 1920d;
            var videoHeight = _previewVideoSize.Height > 0 ? _previewVideoSize.Height : 1080d;
            var hostRatio = hostWidth / hostHeight;
            var videoRatio = videoWidth / videoHeight;

            if (hostRatio > videoRatio)
            {
                var displayWidth = hostHeight * videoRatio;
                return new Rect((hostWidth - displayWidth) / 2d, 0d, displayWidth, hostHeight);
            }

            var displayHeight = hostWidth / videoRatio;
            return new Rect(0d, (hostHeight - displayHeight) / 2d, hostWidth, displayHeight);
        }

        private bool ShouldShowSubtitlePlacementControls()
        {
            return _sourceVideoFile is not null &&
                (EnableSubtitleMuxCheckBox?.IsChecked ?? false) &&
                SubtitlePlacementMarker is not null;
        }

        private string BuildSubtitlePlacementStatusText()
        {
            if (!(EnableSubtitleMuxCheckBox?.IsChecked ?? false))
            {
                return "Turn on Add subtitles to place the subtitle overlay on the preview.";
            }

            var mode = ParseSubtitleMode(SubtitleModeComboBox);
            var subtitlePath = SubtitlePathTextBox?.Text?.Trim();
            var extension = string.IsNullOrWhiteSpace(subtitlePath) ? string.Empty : Path.GetExtension(subtitlePath);
            var isAss = string.Equals(extension, ".ass", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".ssa", StringComparison.OrdinalIgnoreCase);
            var isGeneratedAdvanced = _subtitleEditorKind == SubtitleEditorKind.AdvancedDraft || isAss;

            if (mode == SubtitleMode.BurnIn)
            {
                return isAss
                    ? "Placement is embedded exactly in generated ASS subtitles and approximated for external text subtitles during burn-in."
                    : "Placement is rendered into the video during burn-in. Text subtitle files use the nearest supported FFmpeg alignment.";
            }

            return isGeneratedAdvanced
                ? "Soft-mux keeps placement for ASS subtitles in compatible players and containers, but some players may ignore it."
                : "Soft-mux players usually decide where text subtitles appear. Use BurnIn or generated ASS for more reliable placement.";
        }

        private bool IsEditableSubtitleLoaded()
        {
            return _subtitleEditorBindings.Count > 0;
        }

        private bool HasPendingTranscriptionReview()
        {
            return _subtitleEditorKind == SubtitleEditorKind.AdvancedDraft &&
                _advancedSubtitleDraft is not null &&
                _subtitleEditableRows.Count > 0;
        }

        private bool IsAdvancedTranscriptionReviewActive()
        {
            return !string.IsNullOrWhiteSpace(_pendingAdvancedSubtitleOutputPath) &&
                _pendingAdvancedSubtitleKind != PendingAdvancedSubtitleKind.None &&
                _advancedSubtitleDraft is not null;
        }

        private bool ShouldShowSubtitleEditor()
        {
            if (!_isSubtitleSectionActive)
            {
                return false;
            }

            if (HasPendingTranscriptionReview() || IsAdvancedTranscriptionReviewActive())
            {
                return true;
            }

            if (!(EnableSubtitleMuxCheckBox?.IsChecked ?? false))
            {
                return false;
            }

            var subtitlePath = SubtitlePathTextBox?.Text;
            return !string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath.Trim());
        }

        private void ClearSubtitleEditor()
        {
            _subtitleEditableRows.Clear();
            _subtitleEditorBindings.Clear();
            _subtitleEditorKind = SubtitleEditorKind.None;
            _advancedSubtitleDraft = null;
            _pendingAdvancedSubtitleOutputPath = null;
            _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;
            if (SubtitleEditorItemsPanel is not null)
            {
                SubtitleEditorItemsPanel.Children.Clear();
            }

            if (SubtitleEditorStatusTextBlock is not null)
            {
                SubtitleEditorStatusTextBlock.Text = "Generate or choose a subtitle file to edit.";
            }
        }

        private void TryLoadSubtitleEditorFromPath(string? subtitlePath)
        {
            if (IsAdvancedTranscriptionReviewActive())
            {
                LoadAdvancedSubtitleEditor(_advancedSubtitleDraft!);
                return;
            }

            if (string.IsNullOrWhiteSpace(subtitlePath) || !File.Exists(subtitlePath))
            {
                if (_subtitleEditorKind == SubtitleEditorKind.AdvancedDraft && _advancedSubtitleDraft is not null)
                {
                    return;
                }

                ClearSubtitleEditor();
                return;
            }

            var extension = Path.GetExtension(subtitlePath);
            if (string.Equals(extension, ".srt", StringComparison.OrdinalIgnoreCase))
            {
                LoadSrtEditor(subtitlePath);
                return;
            }

            if (string.Equals(extension, ".ass", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".ssa", StringComparison.OrdinalIgnoreCase))
            {
                if (_advancedSubtitleDraft is not null)
                {
                    LoadAdvancedSubtitleEditor(_advancedSubtitleDraft);
                }
                else
                {
                    ClearSubtitleEditor();
                    if (SubtitleEditorStatusTextBlock is not null)
                    {
                        SubtitleEditorStatusTextBlock.Text = "ASS text editing is disabled in advanced flow. Generate subtitle sections first, then review and apply them here.";
                    }
                }
                return;
            }

            ClearSubtitleEditor();
        }

        private void LoadSrtEditor(string path)
        {
            var content = File.ReadAllText(path);
            var normalized = content.Replace("\r\n", "\n");
            var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

            _subtitleEditableRows.Clear();
            _subtitleEditorKind = SubtitleEditorKind.Srt;
            _advancedSubtitleDraft = null;
            _pendingAdvancedSubtitleOutputPath = null;
            _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;

            var cueNumber = 1;
            foreach (var block in blocks)
            {
                var lines = block.Split('\n');
                if (lines.Length < 2)
                {
                    continue;
                }

                var timelineLine = lines[1].Trim();
                var separatorIndex = timelineLine.IndexOf("-->", StringComparison.Ordinal);
                if (separatorIndex < 0)
                {
                    continue;
                }

                var start = timelineLine[..separatorIndex].Trim();
                var end = timelineLine[(separatorIndex + 3)..].Trim();
                var text = string.Join('\n', lines.Skip(2)).Trim();

                _subtitleEditableRows.Add(new SubtitleEditableRow
                {
                    CueNumber = cueNumber++,
                    StartText = start,
                    EndText = end,
                    Text = text,
                    OriginalStartText = start,
                    OriginalEndText = end,
                    OriginalText = text
                });
            }
            RebuildSubtitleEditorUi();

            if (SubtitleEditorStatusTextBlock is not null)
            {
                SubtitleEditorStatusTextBlock.Text = _subtitleEditableRows.Count == 0
                    ? "No editable cues found in .srt file."
                    : $"{_subtitleEditableRows.Count} subtitle cues loaded.";
            }
        }

        private void LoadAdvancedSubtitleEditor(SubtitleDraft draft)
        {
            _subtitleEditableRows.Clear();
            _subtitleEditorKind = SubtitleEditorKind.AdvancedDraft;
            _advancedSubtitleDraft = draft;

            var cueNumber = 1;
            foreach (var cue in draft.Cues)
            {
                _subtitleEditableRows.Add(new SubtitleEditableRow
                {
                    CueNumber = cueNumber++,
                    StartText = FormatDraftTime(cue.Start),
                    EndText = FormatDraftTime(cue.End),
                    Text = cue.Text,
                    OriginalStartText = FormatDraftTime(cue.Start),
                    OriginalEndText = FormatDraftTime(cue.End),
                    OriginalText = cue.Text
                });
            }
            RebuildSubtitleEditorUi();

            if (SubtitleEditorStatusTextBlock is not null)
            {
                SubtitleEditorStatusTextBlock.Text = _subtitleEditableRows.Count == 0
                    ? "No editable subtitle sections were produced."
                    : $"{_subtitleEditableRows.Count} subtitle sections loaded for review.";
            }
        }

        private static string FormatDraftTime(TimeSpan value)
        {
            return value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        private void UpdateSubtitleEditorRowStatuses()
        {
            foreach (var row in _subtitleEditableRows)
            {
                row.Status = IsSubtitleRowChanged(row) ? "Pending changes" : "No changes";
            }
        }

        private static bool IsSubtitleRowChanged(SubtitleEditableRow row)
        {
            return !string.Equals(row.Text.Trim(), row.OriginalText, StringComparison.Ordinal);
        }

        private void RebuildSubtitleEditorUi()
        {
            _subtitleEditorBindings.Clear();
            if (SubtitleEditorItemsPanel is null)
            {
                return;
            }

            SubtitleEditorItemsPanel.Children.Clear();

            foreach (var row in _subtitleEditableRows)
            {
                var cueLabel = new TextBlock
                {
                    Text = $"Cue {row.CueNumber}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };

                var startLabel = new TextBlock { Text = "Start", Opacity = 0.76 };
                var startValue = new TextBox
                {
                    Text = row.StartText,
                    IsReadOnly = true
                };

                var endLabel = new TextBlock { Text = "End", Opacity = 0.76 };
                var endValue = new TextBox
                {
                    Text = row.EndText,
                    IsReadOnly = true
                };

                var timingGrid = new Grid { ColumnSpacing = 8 };
                timingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                timingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var startPanel = new StackPanel { Spacing = 4 };
                startPanel.Children.Add(startLabel);
                startPanel.Children.Add(startValue);
                Grid.SetColumn(startPanel, 0);

                var endPanel = new StackPanel { Spacing = 4 };
                endPanel.Children.Add(endLabel);
                endPanel.Children.Add(endValue);
                Grid.SetColumn(endPanel, 1);

                timingGrid.Children.Add(startPanel);
                timingGrid.Children.Add(endPanel);

                var editor = new RichEditBox
                {
                    MinHeight = 96,
                    AcceptsReturn = true,
                    IsSpellCheckEnabled = true
                };
                editor.Document.SetText(TextSetOptions.None, row.Text);
                editor.TextChanged += SubtitleFragmentEditor_TextChanged;

                var statusText = new TextBlock
                {
                    Text = row.Status,
                    Opacity = 0.76
                };
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(SubtitleEditableRow.Status))
                    {
                        statusText.Text = row.Status;
                    }
                };

                var card = new Border
                {
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = startValue.BorderBrush
                };

                var cardStack = new StackPanel { Spacing = 8 };
                cardStack.Children.Add(cueLabel);
                cardStack.Children.Add(timingGrid);
                cardStack.Children.Add(editor);
                cardStack.Children.Add(statusText);
                card.Child = cardStack;

                SubtitleEditorItemsPanel.Children.Add(card);
                _subtitleEditorBindings.Add(new SubtitleEditorBinding
                {
                    Row = row,
                    Editor = editor
                });
            }
        }

        private void SubtitleFragmentEditor_TextChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not RichEditBox editor)
            {
                UpdateSubtitleEditorRowStatuses();
                return;
            }

            var binding = _subtitleEditorBindings.FirstOrDefault(item => ReferenceEquals(item.Editor, editor));
            if (binding is null)
            {
                UpdateSubtitleEditorRowStatuses();
                return;
            }

            editor.Document.GetText(TextGetOptions.None, out var text);
            binding.Row.Text = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
            binding.Row.Status = IsSubtitleRowChanged(binding.Row) ? "Pending changes" : "No changes";
        }

        private void SyncSubtitleRowsFromEditors()
        {
            foreach (var binding in _subtitleEditorBindings)
            {
                binding.Editor.Document.GetText(TextGetOptions.None, out var text);
                binding.Row.Text = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
            }
        }

        private async Task<bool> TryAutoApplySubtitleEditsAsync()
        {
            if (!(EnableSubtitleMuxCheckBox?.IsChecked ?? false) || !IsEditableSubtitleLoaded())
            {
                return true;
            }

            if (_subtitleEditorKind == SubtitleEditorKind.Srt)
            {
                if (string.IsNullOrWhiteSpace(SubtitlePathTextBox?.Text) || !File.Exists(SubtitlePathTextBox.Text))
                {
                    await ShowSimpleDialogAsync("Subtitle editor", "Select a valid subtitle file first.");
                    return false;
                }

                var path = SubtitlePathTextBox.Text.Trim();
                return await ApplySrtEditsAsync(path);
            }

            if (_subtitleEditorKind == SubtitleEditorKind.AdvancedDraft)
            {
                if (_sourceVideoFile is null)
                {
                    await ShowSimpleDialogAsync("Subtitle review", "No source video is loaded.");
                    return false;
                }

                var candidatePath = string.IsNullOrWhiteSpace(SubtitlePathTextBox?.Text)
                    ? _pendingAdvancedSubtitleOutputPath ?? CreateGeneratedSubtitlePath(_sourceVideoFile, ".ass")
                    : SubtitlePathTextBox.Text.Trim();
                return await ApplyAdvancedDraftEditsAsync(SubtitlesService.EnsureAssExtension(candidatePath));
            }

            await ShowSimpleDialogAsync("Subtitle editor", "This subtitle format is not editable in the current editor.");
            return false;
        }

        private async Task<bool> ApplySrtEditsAsync(string path)
        {
            SyncSubtitleRowsFromEditors();
            if (_subtitleEditableRows.Count == 0)
            {
                await ShowSimpleDialogAsync("Subtitle editor", "There is no subtitle text to apply.");
                return false;
            }

            var outputLines = new List<string>();
            for (var index = 0; index < _subtitleEditableRows.Count; index++)
            {
                var row = _subtitleEditableRows[index];
                outputLines.Add((index + 1).ToString(CultureInfo.InvariantCulture));
                outputLines.Add($"{row.StartText.Trim()} --> {row.EndText.Trim()}");
                outputLines.AddRange((row.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n'));
                outputLines.Add(string.Empty);
            }

            await File.WriteAllLinesAsync(path, outputLines);
            TryLoadSubtitleEditorFromPath(path);
            RefreshValidationAndState();
            return true;
        }

        private async Task<bool> ApplyAdvancedDraftEditsAsync(string path)
        {
            if (_advancedSubtitleDraft is null)
            {
                await ShowSimpleDialogAsync("Subtitle review", "No subtitle sections are loaded.");
                return false;
            }

            SyncSubtitleRowsFromEditors();
            if (_subtitleEditableRows.Count == 0)
            {
                await ShowSimpleDialogAsync("Subtitle review", "No editable subtitle sections were found.");
                return false;
            }
            var corrections = new List<SubtitleSegmentCorrection>();
            var cues = _advancedSubtitleDraft.Cues;
            if (_subtitleEditableRows.Count != cues.Count)
            {
                await ShowSimpleDialogAsync("Subtitle review", $"Expected {cues.Count} subtitle sections, but found {_subtitleEditableRows.Count}.");
                return false;
            }

            var count = Math.Min(cues.Count, _subtitleEditableRows.Count);
            for (var index = 0; index < count; index++)
            {
                var cue = cues[index];
                var row = _subtitleEditableRows[index];

                var normalizedText = (row.Text ?? string.Empty).Replace("\r\n", "\n").Trim();
                if (!string.Equals(normalizedText, cue.Text, StringComparison.Ordinal))
                {
                    corrections.Add(new SubtitleSegmentCorrection(cue.Id, normalizedText, Start: null, End: null));
                }
            }

            var reviewedDraft = corrections.Count == 0
                ? _advancedSubtitleDraft
                : _subtitlesService.ApplyCorrections(_advancedSubtitleDraft, corrections, _advancedSubtitleDraft.Options);
            var placement = CreateSubtitlePlacementOptionsFromUi();
            var preset = CreateAdvancedSubtitleStylePresetFromConfiguration();
            string ass;
            if (_pendingAdvancedSubtitleKind == PendingAdvancedSubtitleKind.Karaoke)
            {
                ass = _subtitlesService.RenderKaraokeAss(reviewedDraft, preset, placement);
            }
            else
            {
                ass = _subtitlesService.RenderStyledAss(reviewedDraft, preset, placement);
            }
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllTextAsync(path, ass);
            _advancedSubtitleDraft = reviewedDraft;
            _generatedSubtitlePath = path;
            _pendingAdvancedSubtitleOutputPath = null;
            _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;
            SubtitlePathTextBox.Text = path;
            LoadAdvancedSubtitleEditor(reviewedDraft);
            RefreshValidationAndState();
            return true;
        }

        private void CleanupPreviewFiles()
        {
            foreach (var file in _temporaryPreviewFiles.ToArray())
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }

            _temporaryPreviewFiles.Clear();
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

        private (ProcessVideoOptions Options, VideoDenoiseRequest? Denoise, List<string> Errors) BuildOptionsFromUi()
        {
            var errors = new List<string>();

            var output = new VideoOutputOptions
            {
                Format = ParseContainer(OutputContainerComboBox),
                VideoCodec = ParseVideoCodec(VideoCodecComboBox),
                AudioCodec = ParseAudioCodec(AudioCodecComboBox)
            };
            var removeAudio = IsRemoveAudioSelected() && !(EnableAudioMuxCheckBox?.IsChecked ?? false);
            VideoDenoiseRequest? denoise = null;

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

            if ((EnableAudioDenoiseCheckBox?.IsChecked ?? false) && !ShouldDisableOutputAudioVolume())
            {
                denoise = new VideoDenoiseRequest
                {
                    Mode = ParseDenoiseMode(),
                    Strength = Math.Clamp((int)Math.Round(AudioDenoiseStrengthSlider?.Value ?? 50), 0, MaximumDenoiseStrength)
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
                var hasPendingTranscriptionReview = HasPendingTranscriptionReview();
                var resolvedSubtitlePath = string.IsNullOrWhiteSpace(SubtitlePathTextBox?.Text)
                    ? _pendingAdvancedSubtitleOutputPath
                    : SubtitlePathTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(resolvedSubtitlePath) ||
                    (!File.Exists(resolvedSubtitlePath) && !hasPendingTranscriptionReview))
                {
                    errors.Add("Media: Subtitle file is required when subtitle mux is enabled.");
                }
                else
                {
                    subtitleMux = new MuxSubtitleOptions
                    {
                        SubtitlePath = resolvedSubtitlePath,
                        Mode = ParseSubtitleMode(SubtitleModeComboBox),
                        Language = string.IsNullOrWhiteSpace(SubtitleLanguageTextBox?.Text) ? null : SubtitleLanguageTextBox.Text.Trim(),
                        Title = string.IsNullOrWhiteSpace(SubtitleTitleTextBox?.Text) ? null : SubtitleTitleTextBox.Text.Trim(),
                        SetAsDefault = SetDefaultSubtitleCheckBox?.IsChecked ?? false,
                        Placement = CreateSubtitlePlacementOptionsFromUi()
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
                AudioDenoise = denoise is null ? null : new AudioDenoiseRequestOptions
                {
                    Enabled = true,
                    Mode = denoise.Mode,
                    Strength = denoise.Strength
                },
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

            if (removeAudio && denoise is not null)
            {
                errors.Add("Media: Audio denoise cannot be enabled when output audio is removed.");
            }

            if (output.Format == VideoContainerFormat.Gif && subtitleMux?.Mode == SubtitleMode.SoftMux)
            {
                errors.Add("Media: GIF does not support soft subtitle mux. Use burn-in or disable subtitles.");
            }

            return (options, denoise, errors);
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

        private bool IsStereoBlendDenoiseModeSelected()
        {
            return ParseDenoiseMode() == AudioDenoiseMode.StrongStereo;
        }

        private AudioDenoiseMode ParseDenoiseMode()
        {
            var mode = (AudioDenoiseModeComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return mode switch
            {
                "Preserve stereo" => AudioDenoiseMode.StrongStereo,
                "Stereo" => AudioDenoiseMode.StrongStereo,
                _ => AudioDenoiseMode.Mono
            };
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

        private void UpdateAudioDenoiseStrengthText()
        {
            if (AudioDenoiseStrengthTextBlock is null)
            {
                return;
            }

            var strength = Math.Clamp((int)Math.Round(AudioDenoiseStrengthSlider?.Value ?? 50), 0, MaximumDenoiseStrength);
            AudioDenoiseStrengthTextBlock.Text = $"Strength: {strength}%";
        }

        private void UpdateSubtitleSectionDurationText(double value)
        {
            if (SubtitleSectionDurationTextBlock is null)
            {
                return;
            }

            SubtitleSectionDurationTextBlock.Text = $"Advanced subtitle section length: {value:0.#} s max";
        }

        private void ApplySubtitleSectionDurationValue(double value, bool synchronizeSlider, bool synchronizeNumberBox, bool refreshState = true)
        {
            if (_isSynchronizingSubtitleSectionDurationControls)
            {
                return;
            }

            var minimum = SubtitleSectionDurationSlider?.Minimum ?? 2d;
            var maximum = SubtitleSectionDurationSlider?.Maximum ?? 12d;
            var normalizedValue = Math.Clamp(Math.Round(value * 2d, MidpointRounding.AwayFromZero) / 2d, minimum, maximum);

            _isSynchronizingSubtitleSectionDurationControls = true;
            try
            {
                if (synchronizeSlider &&
                    SubtitleSectionDurationSlider is not null &&
                    Math.Abs(SubtitleSectionDurationSlider.Value - normalizedValue) > 0.001d)
                {
                    SubtitleSectionDurationSlider.Value = normalizedValue;
                }

                if (synchronizeNumberBox &&
                    SubtitleSectionDurationNumberBox is not null &&
                    (double.IsNaN(SubtitleSectionDurationNumberBox.Value) || Math.Abs(SubtitleSectionDurationNumberBox.Value - normalizedValue) > 0.001d))
                {
                    SubtitleSectionDurationNumberBox.Value = normalizedValue;
                }
            }
            finally
            {
                _isSynchronizingSubtitleSectionDurationControls = false;
            }

            UpdateSubtitleSectionDurationText(normalizedValue);

            if (refreshState)
            {
                RefreshValidationAndState();
            }
        }

        private static string? MapAudioCodecToEncoder(AudioCodec? codec, VideoContainerFormat? container)
        {
            if (!codec.HasValue)
            {
                return container == VideoContainerFormat.Webm ? "libopus" : "aac";
            }

            return codec.Value switch
            {
                AudioCodec.Aac => "aac",
                AudioCodec.Opus => "libopus",
                AudioCodec.Vorbis => "libvorbis",
                AudioCodec.Mp3 => "libmp3lame",
                AudioCodec.Ac3 => "ac3",
                AudioCodec.Flac => "flac",
                AudioCodec.PcmS16Le => "pcm_s16le",
                _ => null
            };
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

        private void UpdateDenoiseProgress(DenoiseProgress progress)
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
            ProcessingProgressBar.Value = Math.Clamp(progress.OverallPercent, 0d, 1d);
            ProcessingStatusTextBlock.Text = progress.Stage == DenoiseProcessingStage.Completed
                ? "Finalizing denoised audio..."
                : $"Denoising audio... {(progress.OverallPercent * 100d):0}%";
            ProcessingEtaTextBlock.Text = progress.EstimatedRemainingTime is TimeSpan eta
                ? $"ETA {FormatEta(eta)}"
                : "ETA calculating...";

            var processed = progress.ProcessedDuration is TimeSpan processedDuration
                ? FormatTimelineTime(processedDuration)
                : "--:--";
            var total = progress.TotalDuration is TimeSpan totalDuration
                ? FormatTimelineTime(totalDuration)
                : "--:--";
            var activity = progress.IsInferenceActive
                ? "AI inference"
                : progress.IsFfmpegActive
                    ? "FFmpeg"
                    : "Audio processing";

            ProcessingDetailTextBlock.Text = $"{progress.StageDescription} ({activity}) - {processed} of {total}.";
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
