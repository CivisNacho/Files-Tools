using Files_Tools.Helpers;
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
        private long? _sourceVideoFileSizeBytes;
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
        private bool _isSyncingAdvancedStylePicker;
        private bool _isRestylingAdvancedSubtitles;
        private DispatcherTimer? _subtitlePreviewTimer;
        private int _previewCueKey = int.MinValue;
        private bool _previewIsKaraoke;
        private readonly List<(Microsoft.UI.Xaml.Documents.Run Run, TimeSpan Start)> _previewKaraokeRuns = [];
        private Color _previewBaseColor = Microsoft.UI.Colors.White;
        private Color _previewHighlightColor = Microsoft.UI.Colors.Yellow;
        private FrameworkElement? _previewContent;
        private double _previewMaxWidth = 0d;
        private bool _isDraggingPreview;
        private Point _previewDragLastPoint;
        private SubtitleRenderTarget? _previewRenderTarget;
        private Microsoft.UI.Xaml.Media.Animation.Storyboard? _runningPreviewStoryboard;
        private Microsoft.UI.Xaml.Media.Animation.Storyboard? _runningWordPopStoryboard;
        private KaraokeFill _previewKaraokeFill = KaraokeFill.Instant;
        private double _previewActiveWordPopScale = 1d;
        private int _previewActiveWordIndex = -1;
        private TimeSpan _previewCueEnd = TimeSpan.Zero;
        // Guards against re-entrant UpdateSubtitlePreview calls that fire from XAML layout events
        // (e.g. PreviewHost_SizeChanged) that are triggered synchronously while
        // BuildSubtitlePreviewContent is modifying the canvas. Set true for the duration of every
        // build; RefreshSubtitlePreview skips its immediate UpdateSubtitlePreview call while this
        // is true, so the next timer tick picks up the refresh instead (at most ~16 ms delay).
        private bool _buildingSubtitlePreview;
        // Wall-clock millisecond tick captured at the end of each active-cue build so the first-
        // tick guard in UpdateKaraokeHighlight can detect "this is the natural cue start" vs "this
        // is a mid-cue rebuild" regardless of how far into the cue the playback position already is.
        private long _previewCueBuildTick;
        private Microsoft.UI.Xaml.Media.SolidColorBrush? _cachedPreviewBaseBrush;
        private Microsoft.UI.Xaml.Media.SolidColorBrush? _cachedPreviewHighlightBrush;
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush TransparentBrush = new(Microsoft.UI.Colors.Transparent);
        // Tracks whether the last SRT-path build used the ASS preset; forces a rebuild on mode switch
        // even if _previewCueKey was not reset (e.g. when RefreshValidationAndState throws before
        // RefreshSubtitlePreview can run).
        private bool _previewSrtIsAssMode = false;

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
            // Ids reference SubtitleStyleCatalog entries, so adding a catalog style needs no UI changes.
            public string StyledPresetId { get; set; } = "SocialImpact";

            public string KaraokePresetId { get; set; } = "GlowKaraoke";

            public string FontFamily { get; set; } = "Impact";

            public double FontSize { get; set; } = 72d;

            public bool Bold { get; set; } = true;

            public SubtitleTextTransform TextTransform { get; set; } = SubtitleTextTransform.Uppercase;

            public double OutlineWidth { get; set; } = 5d;

            public int MarginVertical { get; set; } = 90;

            public SubtitleColor KaraokeHighlightColor { get; set; } = new(0, 255, 110, 0);

            /// <summary>Text fill colour used for styled (non-karaoke) subtitles.</summary>
            public SubtitleColor FillColor { get; set; } = new(0, 255, 255, 255);

            /// <summary>Outline colour used for styled (non-karaoke) subtitles.</summary>
            public SubtitleColor OutlineColor { get; set; } = new(0, 0, 0, 0);

            /// <summary>Text fill colour used for karaoke subtitles (the "unsung" word colour).</summary>
            public SubtitleColor KaraokeFillColor { get; set; } = new(0, 255, 255, 255);

            /// <summary>Outline colour used for karaoke subtitles.</summary>
            public SubtitleColor KaraokeOutlineColor { get; set; } = new(0, 20, 40, 90);

            public static SubtitlePresetConfiguration CreateDefault()
            {
                return new SubtitlePresetConfiguration
                {
                    StyledPresetId = "SocialImpact",
                    KaraokePresetId = "GlowKaraoke",
                    FontFamily = "Impact",
                    KaraokeHighlightColor = new SubtitleColor(0, 255, 110, 0),
                    FillColor = new SubtitleColor(0, 255, 255, 255),
                    OutlineColor = new SubtitleColor(0, 0, 0, 0),
                    KaraokeFillColor = new SubtitleColor(0, 255, 255, 255),
                    KaraokeOutlineColor = new SubtitleColor(0, 20, 40, 90)
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
            StopSubtitlePreviewTimer();
            ClearSubtitlePreview();
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
            PopulateAdvancedStylePicker();
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
            _sourceVideoFileSizeBytes = (long)basicProperties.Size;
            LoadedVideoInfoTextBlock.Text = $"Loaded video: {file.Name} ({basicProperties.Size / (1024.0 * 1024.0):0.00} MB)";
            UpdateFileInfoPanel();

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
                UpdateFileInfoPanel();
                EnsureSubtitlePreviewTimer();
                UpdatePreviewRenderTargetAsync();
                RefreshSubtitlePreview();
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

                // For SoftMux of a styled .ass that the container can't carry (e.g. MP4), drop the
                // subtitle next to the FINAL output as a sidecar the player loads. Done here (not only
                // in the service) so it lands beside the real output even when denoise re-routes the
                // service output through a temporary file.
                EnsureSoftMuxSubtitleSidecar(options.SubtitleMux, outputPath);

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
            FileInfoPanel.Visibility = string.IsNullOrEmpty(selected) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateFileInfoPanel()
        {
            if (_sourceVideoFile is null)
            {
                FileInfoPanel.Visibility = Visibility.Collapsed;
                return;
            }

            FileInfoNameTextBlock.Text = _sourceVideoFile.Name;
            FileInfoDimensionsTextBlock.Text = _previewVideoSize.Width > 0 && _previewVideoSize.Height > 0
                ? $"{(int)_previewVideoSize.Width} × {(int)_previewVideoSize.Height} px"
                : "—";
            FileInfoDurationTextBlock.Text = _videoDuration.HasValue ? FormatDuration(_videoDuration.Value) : "—";
            FileInfoFormatTextBlock.Text = _sourceVideoFile.FileType.TrimStart('.').ToUpperInvariant();
            FileInfoSizeTextBlock.Text = _sourceVideoFileSizeBytes.HasValue ? FormatFileSize(_sourceVideoFileSizeBytes.Value) : "—";
            FileInfoPanel.Visibility = Visibility.Visible;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1_073_741_824)
            {
                return $"{bytes / 1_073_741_824.0:F1} GB";
            }

            if (bytes >= 1_048_576)
            {
                return $"{bytes / 1_048_576.0:F1} MB";
            }

            if (bytes >= 1024)
            {
                return $"{bytes / 1024.0:F1} KB";
            }

            return $"{bytes} B";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
                : $"{duration.Minutes}:{duration.Seconds:D2}";
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
            RefreshSubtitlePreview();
        }

        private async void AdvancedSubtitleTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Switching Styled <-> Karaoke changes which catalog presets apply; refresh the picker and
            // restyle the existing draft (if any) to the newly selected kind without regenerating.
            PopulateAdvancedStylePicker();
            UpdateAdvancedSubtitlePresetSummary();
            RefreshValidationAndState();
            await ReRenderAdvancedSubtitlesIfReadyAsync();
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
            RefreshSubtitlePreview();
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

                        TaskbarProgressHelper.SetProgress(update.FractionComplete);

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
                TaskbarProgressHelper.Clear();
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

                    TaskbarProgressHelper.SetProgress(update.OverallPercent);

                    if (TranscriptionEtaTextBlock is not null)
                    {
                        TranscriptionEtaTextBlock.Visibility = Visibility.Visible;
                        var pct = (int)Math.Round(update.OverallPercent * 100d);
                        TranscriptionEtaTextBlock.Text = update.EstimatedRemainingTime is TimeSpan eta
                            ? $"{update.StageDescription} · {pct}% · ETA {FormatEta(eta)}"
                            : $"{update.StageDescription} · {pct}%";
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
                    EnsureSubtitlePreviewTimer();
                    RefreshSubtitlePreview();
                }

                var isAdvancedReview = isAdvanced;
                _generatedSubtitlePath = isAdvancedReview
                    ? null
                    : generatedPath;
                SubtitlePathTextBox.Text = generatedPath;
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

                TaskbarProgressHelper.Clear();
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

            // The on-screen "ready" status label was removed from the page; skip the rest of the UI
            // refresh while a generation is in progress, as before.
            if (_isGeneratingSubtitles)
            {
                return;
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
            // Compare by index, not by Content text: the item Content is localized via x:Uid, so a
            // text comparison against the English literal silently fails in other languages (e.g.
            // Spanish "Avanzado (ASS)"). Item order is fixed in XAML: 0 = Basic (SRT), 1 = Advanced (ASS).
            return SubtitleGenerationModeComboBox?.SelectedIndex == 1;
        }

        private bool IsKaraokeAdvancedSubtitleTypeSelected()
        {
            // Item order is fixed in XAML: 0 = Styled ASS, 1 = Karaoke ASS. Compared by index so it is
            // locale-independent (the Content is localized via x:Uid).
            return AdvancedSubtitleTypeComboBox?.SelectedIndex == 1;
        }

        private async void ConfigureAdvancedSubtitlesButton_Click(object sender, RoutedEventArgs e)
        {
            var isKaraokeMode = IsKaraokeAdvancedSubtitleTypeSelected();

            // The picker is driven entirely by the style catalog: registering a new style there makes
            // it appear here automatically, with no further UI changes required.
            var presetEntries = SubtitleStyleCatalog
                .ByKind(isKaraokeMode ? SubtitleStyleKind.Karaoke : SubtitleStyleKind.Styled)
                .ToList();
            var configuredPresetId = isKaraokeMode
                ? _advancedSubtitlePresetConfiguration.KaraokePresetId
                : _advancedSubtitlePresetConfiguration.StyledPresetId;

            var basePresetComboBox = new ComboBox
            {
                Header = Strings.Get("VideoPage_BasePreset_Header")
            };

            foreach (var entry in presetEntries)
            {
                basePresetComboBox.Items.Add(new ComboBoxItem { Content = entry.DisplayName });
            }

            var configuredIndex = presetEntries.FindIndex(entry => string.Equals(entry.Id, configuredPresetId, StringComparison.OrdinalIgnoreCase));
            basePresetComboBox.SelectedIndex = configuredIndex >= 0 ? configuredIndex : 0;

            var fontSizeNumberBox = new NumberBox
            {
                Header = Strings.Get("VideoPage_FontSize_Header"),
                Minimum = 24,
                Maximum = 160,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _advancedSubtitlePresetConfiguration.FontSize
            };

            var fontFamilyComboBox = new ComboBox
            {
                Header = Strings.Get("VideoPage_SystemFont_Header"),
                ItemsSource = _installedFontFamilies.ToList(),
                PlaceholderText = Strings.Get("VideoPage_SelectInstalledFont")
            };
            if (_installedFontFamilies.Contains(_advancedSubtitlePresetConfiguration.FontFamily, StringComparer.OrdinalIgnoreCase))
            {
                var selected = _installedFontFamilies.First(name => string.Equals(name, _advancedSubtitlePresetConfiguration.FontFamily, StringComparison.OrdinalIgnoreCase));
                fontFamilyComboBox.SelectedItem = selected;
            }

            var outlineNumberBox = new NumberBox
            {
                Header = Strings.Get("VideoPage_OutlineWidth_Header"),
                Minimum = 0,
                Maximum = 20,
                SmallChange = 0.5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _advancedSubtitlePresetConfiguration.OutlineWidth
            };

            var marginVerticalNumberBox = new NumberBox
            {
                Header = Strings.Get("VideoPage_VerticalMargin_Header"),
                Minimum = 0,
                Maximum = 400,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = _advancedSubtitlePresetConfiguration.MarginVertical
            };

            var boldCheckBox = new CheckBox
            {
                Content = Strings.Get("VideoPage_BoldText_Content"),
                IsChecked = _advancedSubtitlePresetConfiguration.Bold
            };

            var textTransformComboBox = new ComboBox
            {
                Header = Strings.Get("VideoPage_TextTransform_Header")
            };
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = Strings.Get("VideoPage_OriginalCase") });
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = Strings.Get("VideoPage_Uppercase") });
            textTransformComboBox.Items.Add(new ComboBoxItem { Content = Strings.Get("VideoPage_Lowercase") });
            textTransformComboBox.SelectedIndex = _advancedSubtitlePresetConfiguration.TextTransform switch
            {
                SubtitleTextTransform.Uppercase => 1,
                SubtitleTextTransform.Lowercase => 2,
                _ => 0
            };

            // Karaoke-only: word highlight colour.
            var karaokeAccentColorPicker = new ColorPicker
            {
                Color = ToUiColor(_advancedSubtitlePresetConfiguration.KaraokeHighlightColor),
                Visibility = isKaraokeMode ? Visibility.Visible : Visibility.Collapsed,
                IsEnabled = isKaraokeMode
            };
            var karaokeAccentLabel = new TextBlock
            {
                Text = Strings.Get("VideoPage_FillingColor"),
                Visibility = isKaraokeMode ? Visibility.Visible : Visibility.Collapsed
            };

            // Word fill and outline colours — available in both styled and karaoke modes.
            var fillColorPicker = new ColorPicker
            {
                Color = ToUiColor(isKaraokeMode
                    ? _advancedSubtitlePresetConfiguration.KaraokeFillColor
                    : _advancedSubtitlePresetConfiguration.FillColor)
            };
            var fillColorLabel = new TextBlock { Text = isKaraokeMode ? Strings.Get("VideoPage_PreFillingColor") : Strings.Get("VideoPage_WordFillColor") };
            var outlineColorPicker = new ColorPicker
            {
                Color = ToUiColor(isKaraokeMode
                    ? _advancedSubtitlePresetConfiguration.KaraokeOutlineColor
                    : _advancedSubtitlePresetConfiguration.OutlineColor)
            };
            var outlineColorLabel = new TextBlock { Text = Strings.Get("VideoPage_OutlineColor") };

            // Karaoke mode: selecting a preset auto-fills font/size/outline/margin defaults.
            // Styled mode has a single customisable base so the preset picker is omitted.
            if (isKaraokeMode)
            {
                basePresetComboBox.SelectionChanged += (_, _) =>
                {
                    var selectedIndex = basePresetComboBox.SelectedIndex;
                    if (selectedIndex < 0 || selectedIndex >= presetEntries.Count)
                    {
                        return;
                    }

                    var defaults = presetEntries[selectedIndex].Factory();

                    var defaultFont = defaults.PrimaryFontFamily;
                    if (_installedFontFamilies.Contains(defaultFont, StringComparer.OrdinalIgnoreCase))
                    {
                        fontFamilyComboBox.SelectedItem = _installedFontFamilies.First(name => string.Equals(name, defaultFont, StringComparison.OrdinalIgnoreCase));
                    }

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
                    fillColorPicker.Color = ToUiColor(defaults.FillColor);
                    outlineColorPicker.Color = ToUiColor(defaults.OutlineColor);
                };
            }

            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = isKaraokeMode
                    ? Strings.Get("VideoPage_ConfigureKaraokeSummary")
                    : Strings.Get("VideoPage_ConfigureStyledSummary"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.76
            });
            if (isKaraokeMode)
            {
                content.Children.Add(basePresetComboBox);
            }
            content.Children.Add(fontFamilyComboBox);
            content.Children.Add(fontSizeNumberBox);
            content.Children.Add(outlineNumberBox);
            content.Children.Add(marginVerticalNumberBox);
            content.Children.Add(boldCheckBox);
            content.Children.Add(textTransformComboBox);
            content.Children.Add(karaokeAccentLabel);
            content.Children.Add(karaokeAccentColorPicker);
            content.Children.Add(fillColorLabel);
            content.Children.Add(fillColorPicker);
            content.Children.Add(outlineColorLabel);
            content.Children.Add(outlineColorPicker);

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
                Title = Strings.Get("VideoPage_ConfigureAdvancedTitle"),
                Content = scrollableContent,
                PrimaryButtonText = Strings.Get("VideoPage_ConfigureAdvancedSave"),
                SecondaryButtonText = Strings.Get("VideoPage_ConfigureAdvancedResetDefaults"),
                CloseButtonText = Strings.Get("Shared_Cancel"),
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                _advancedSubtitlePresetConfiguration = SubtitlePresetConfiguration.CreateDefault();
                PopulateAdvancedStylePicker();
                UpdateAdvancedSubtitlePresetSummary();
                await ReRenderAdvancedSubtitlesIfReadyAsync();
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var savedIndex = basePresetComboBox.SelectedIndex;
            var selectedEntry = savedIndex >= 0 && savedIndex < presetEntries.Count
                ? presetEntries[savedIndex]
                : presetEntries.FirstOrDefault();

            var newConfig = SubtitlePresetConfiguration.CreateDefault();
            // Preserve the unselected mode's choice; only the mode being edited is overwritten below.
            newConfig.StyledPresetId = _advancedSubtitlePresetConfiguration.StyledPresetId;
            newConfig.KaraokePresetId = _advancedSubtitlePresetConfiguration.KaraokePresetId;
            newConfig.FontFamily = (fontFamilyComboBox.SelectedItem as string) ?? selectedEntry?.Factory().PrimaryFontFamily ?? "Arial";
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
                newConfig.KaraokeFillColor = FromUiColor(fillColorPicker.Color);
                newConfig.KaraokeOutlineColor = FromUiColor(outlineColorPicker.Color);
                // Preserve the styled-mode colours untouched.
                newConfig.FillColor = _advancedSubtitlePresetConfiguration.FillColor;
                newConfig.OutlineColor = _advancedSubtitlePresetConfiguration.OutlineColor;
            }
            else
            {
                newConfig.FillColor = FromUiColor(fillColorPicker.Color);
                newConfig.OutlineColor = FromUiColor(outlineColorPicker.Color);
                // Preserve the karaoke-mode colours untouched.
                newConfig.KaraokeFillColor = _advancedSubtitlePresetConfiguration.KaraokeFillColor;
                newConfig.KaraokeOutlineColor = _advancedSubtitlePresetConfiguration.KaraokeOutlineColor;
            }

            if (selectedEntry is not null)
            {
                if (isKaraokeMode)
                {
                    newConfig.KaraokePresetId = selectedEntry.Id;
                }
                else
                {
                    newConfig.StyledPresetId = selectedEntry.Id;
                }
            }

            _advancedSubtitlePresetConfiguration = newConfig;

            PopulateAdvancedStylePicker();
            UpdateAdvancedSubtitlePresetSummary();
            await ReRenderAdvancedSubtitlesIfReadyAsync();
        }

        private void UpdateAdvancedSubtitlePresetSummary()
        {
            if (AdvancedSubtitlePresetSummaryTextBlock is null)
            {
                return;
            }

            var isKaraokeMode = IsKaraokeAdvancedSubtitleTypeSelected();
            var presetId = isKaraokeMode
                ? _advancedSubtitlePresetConfiguration.KaraokePresetId
                : _advancedSubtitlePresetConfiguration.StyledPresetId;
            var catalogEntry = SubtitleStyleCatalog.Find(presetId);
            var presetName = catalogEntry?.DisplayName ?? presetId;
            var presentation = isKaraokeMode ? "karaoke" : "styled";
            var textTransform = _advancedSubtitlePresetConfiguration.TextTransform switch
            {
                SubtitleTextTransform.Uppercase => "Uppercase",
                SubtitleTextTransform.Lowercase => "Lowercase",
                _ => "Original case"
            };
            var fontWeight = _advancedSubtitlePresetConfiguration.Bold ? "Bold" : "Regular";
            AdvancedSubtitlePresetSummaryTextBlock.Text = string.Format(
                Strings.Get("VideoPage_AdvancedPresetSummaryFormat"),
                presetName,
                presentation,
                _advancedSubtitlePresetConfiguration.FontFamily,
                _advancedSubtitlePresetConfiguration.FontSize.ToString("0.#"),
                fontWeight,
                textTransform,
                _advancedSubtitlePresetConfiguration.OutlineWidth.ToString("0.#"));
        }

        /// <summary>
        /// Resolves the true display resolution of the source video for subtitle sizing. Probes the
        /// file (rotation-aware, deterministic) and falls back to the preview size only if probing
        /// yields nothing. Returns null when no reliable dimensions are available.
        /// </summary>
        private async Task<SubtitleRenderTarget?> ResolveSubtitleRenderTargetAsync()
        {
            if (_sourceVideoFile is not null)
            {
                try
                {
                    var info = await _videoProcessingService.ProbeSourceAsync(_sourceVideoFile.Path);
                    if (info.Width > 0 && info.Height > 0)
                    {
                        return new SubtitleRenderTarget(info.Width, info.Height);
                    }
                }
                catch
                {
                    // Fall back to the preview size below if probing fails.
                }
            }

            return _previewVideoSize.Width > 0 && _previewVideoSize.Height > 0
                ? new SubtitleRenderTarget((int)_previewVideoSize.Width, (int)_previewVideoSize.Height)
                : null;
        }

        /// <summary>
        /// Ensures a SoftMux'd styled subtitle ends up as a sidecar file next to the final output video
        /// for containers that can't carry ASS natively (anything but MKV — MKV embeds it as a soft
        /// track). The video is left untouched (never burned); players auto-load the matching-name
        /// sidecar and render it with libass. Best-effort.
        /// </summary>
        private static void EnsureSoftMuxSubtitleSidecar(MuxSubtitleOptions? subtitleMux, string finalOutputPath)
        {
            if (subtitleMux is not { Mode: SubtitleMode.SoftMux })
            {
                return;
            }

            var extension = Path.GetExtension(subtitleMux.SubtitlePath);
            var isAss = extension.Equals(".ass", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);
            // MKV embeds the .ass as a soft track (self-contained), so no sidecar is written for it.
            if (!isAss || Path.GetExtension(finalOutputPath).Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var source = Path.GetFullPath(subtitleMux.SubtitlePath);
            if (!File.Exists(source))
            {
                return;
            }

            var sidecar = Path.ChangeExtension(Path.GetFullPath(finalOutputPath), ".ass");
            if (string.Equals(source, sidecar, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                File.Copy(source, sidecar, overwrite: true);
            }
            catch (IOException)
            {
                // Best effort: the video still rendered; a sidecar copy failure should not abort it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private SubtitleStylePreset CreateAdvancedSubtitleStylePresetFromConfiguration()
        {
            var isKaraokeMode = IsKaraokeAdvancedSubtitleTypeSelected();

            // Karaoke: resolve from catalog with GlowKaraoke as fallback.
            // Styled: always use SocialImpact as the single internal base (no catalog entry).
            SubtitleStylePreset basePreset;
            if (isKaraokeMode)
            {
                var presetId = _advancedSubtitlePresetConfiguration.KaraokePresetId;
                basePreset = (SubtitleStyleCatalog.Find(presetId)
                    ?? SubtitleStyleCatalog.Find("GlowKaraoke")!).Factory();
            }
            else
            {
                basePreset = StyledSubtitlePresets.SocialImpact;
            }

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
                FillColor = isKaraokeMode ? _advancedSubtitlePresetConfiguration.KaraokeFillColor : _advancedSubtitlePresetConfiguration.FillColor,
                OutlineColor = isKaraokeMode ? _advancedSubtitlePresetConfiguration.KaraokeOutlineColor : _advancedSubtitlePresetConfiguration.OutlineColor,
                ShadowColor = basePreset.ShadowColor,
                KaraokeHighlightColor = _advancedSubtitlePresetConfiguration.KaraokeHighlightColor,
                UseBackgroundBox = basePreset.UseBackgroundBox,
                PresentationAnimation = basePreset.PresentationAnimation,
                EntryFadeMilliseconds = basePreset.EntryFadeMilliseconds,
                ExitFadeMilliseconds = basePreset.ExitFadeMilliseconds,
                IntroScale = basePreset.IntroScale,
                Effects = basePreset.Effects,
                OutlineWidth = _advancedSubtitlePresetConfiguration.OutlineWidth,
                ShadowDepth = basePreset.ShadowDepth,
                Alignment = basePreset.Alignment,
                MarginLeft = basePreset.MarginLeft,
                MarginRight = basePreset.MarginRight,
                MarginVertical = _advancedSubtitlePresetConfiguration.MarginVertical,
                PositionX = basePreset.PositionX,
                PositionY = basePreset.PositionY,
                MaxLines = basePreset.MaxLines,
                MaxCharsPerLine = basePreset.MaxCharsPerLine,
                MaxWordsPerChunk = basePreset.MaxWordsPerChunk
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
            // ASS alpha is inverted relative to Windows ARGB:
            //   ASS  0x00 = fully opaque  →  Windows 0xFF = fully opaque
            //   ASS  0xFF = fully transparent  →  Windows 0x00 = fully transparent
            return Color.FromArgb((byte)(255 - color.Alpha), color.Red, color.Green, color.Blue);
        }

        private static SubtitleColor FromUiColor(Color color)
        {
            return new SubtitleColor((byte)(255 - color.A), color.R, color.G, color.B);
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
            RefreshSubtitlePreview();

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

        // ----- Live subtitle preview (Part A) -----------------------------------------------------
        // Draws the active subtitle cue as a plain-XAML overlay over the player, synced to playback.
        // It is an approximation of the final libass burn-in (outline via layered offset copies), but
        // tracks the chosen style/preset live, including per-word karaoke highlighting.

        private bool ShouldShowSubtitlePreview()
        {
            if (_sourceVideoFile is null || SubtitlePreviewCanvas is null)
            {
                return false;
            }

            var hasSrtRows = _subtitleEditorKind == SubtitleEditorKind.Srt && _subtitleEditableRows.Count > 0;

            if (IsAdvancedSubtitleModeSelected())
            {
                // ASS mode: prefer the real draft; fall back to SRT cues so the preset styling is
                // visible immediately after switching without having to re-generate.
                return _advancedSubtitleDraft is not null || hasSrtRows;
            }

            return hasSrtRows;
        }

        // Caches the same render target the burn uses (probed encoded size) so the preview can size
        // text identically. Probing is async, so we cache it and refresh the preview when it arrives.
        private async void UpdatePreviewRenderTargetAsync()
        {
            try
            {
                if (_sourceVideoFile is not null)
                {
                    var info = await _videoProcessingService.ProbeSourceAsync(_sourceVideoFile.Path);
                    if (info.Width > 0 && info.Height > 0)
                    {
                        _previewRenderTarget = new SubtitleRenderTarget(info.Width, info.Height);
                        RefreshSubtitlePreview();
                        return;
                    }
                }

                _previewRenderTarget = _previewVideoSize.Width > 0 && _previewVideoSize.Height > 0
                    ? new SubtitleRenderTarget((int)_previewVideoSize.Width, (int)_previewVideoSize.Height)
                    : null;
            }
            catch
            {
                _previewRenderTarget = null;
            }

            RefreshSubtitlePreview();
        }

        private void EnsureSubtitlePreviewTimer()
        {
            if (_subtitlePreviewTimer is not null)
            {
                return;
            }

            _subtitlePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _subtitlePreviewTimer.Tick += SubtitlePreviewTimer_Tick;
            _subtitlePreviewTimer.Start();
        }

        private void StopSubtitlePreviewTimer()
        {
            if (_subtitlePreviewTimer is null)
            {
                return;
            }

            _subtitlePreviewTimer.Stop();
            _subtitlePreviewTimer.Tick -= SubtitlePreviewTimer_Tick;
            _subtitlePreviewTimer = null;
        }

        private void SubtitlePreviewTimer_Tick(object? sender, object e)
        {
            try
            {
                var position = VideoPlayer?.MediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
                UpdateSubtitlePreview(position);
            }
            catch (Exception ex)
            {
                // Never let a render glitch kill the preview loop; log and keep ticking.
                System.Diagnostics.Debug.WriteLine($"[Preview] tick failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Refreshes the preview at the current playback position (after style, placement, size or draft
        // changes). While dragging the subtitle, only reposition the existing element so pointer
        // capture is preserved; otherwise rebuild.
        private void RefreshSubtitlePreview()
        {
            if (ShouldShowSubtitlePreview())
            {
                EnsureSubtitlePreviewTimer();
            }

            // Sync the separate placement box's visibility: it must hide once the draggable live
            // preview takes over, otherwise it sits on top and covers the subtitle text.
            if (!_isDraggingPreview)
            {
                UpdateSubtitlePlacementPreview();
            }

            if (_isDraggingPreview)
            {
                PositionPreviewContent();
                return;
            }

            _previewCueKey = int.MinValue;
            // If a build is already running (re-entrant XAML layout event), skip the immediate
            // update — the key reset above is enough to make the next timer tick rebuild cleanly.
            if (_buildingSubtitlePreview)
            {
                return;
            }

            var position = VideoPlayer?.MediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
            UpdateSubtitlePreview(position);
        }

        private void ClearSubtitlePreview()
        {
            _runningPreviewStoryboard?.Stop();
            _runningPreviewStoryboard = null;
            _runningWordPopStoryboard?.Stop();
            _runningWordPopStoryboard = null;
            SubtitlePreviewCanvas?.Children.Clear();
            _previewKaraokeRuns.Clear();
            _previewContent = null;
            _previewCueKey = int.MinValue;
            _previewActiveWordIndex = -1;
            _previewSrtIsAssMode = false;
        }

        private void UpdateSubtitlePreview(TimeSpan position)
        {
            if (SubtitlePreviewCanvas is null)
            {
                return;
            }

            if (!ShouldShowSubtitlePreview())
            {
                if (SubtitlePreviewCanvas.Children.Count > 0)
                {
                    ClearSubtitlePreview();
                }

                return;
            }

            var target = _previewRenderTarget
                ?? (_previewVideoSize.Width > 0 && _previewVideoSize.Height > 0
                    ? new SubtitleRenderTarget((int)Math.Round(_previewVideoSize.Width), (int)Math.Round(_previewVideoSize.Height))
                    : null);

            // SRT path: no word timestamps, no karaoke.
            // In SRT mode use the plain default preset; in ASS mode use the configured preset so
            // switching modes immediately shows the styling without needing to re-generate.
            if (_subtitleEditorKind == SubtitleEditorKind.Srt)
            {
                var row = PickPreviewSrtRow(position, out var srtActive);
                if (row is null)
                {
                    if (SubtitlePreviewCanvas.Children.Count > 0)
                    {
                        ClearSubtitlePreview();
                    }

                    return;
                }

                var isAssMode = IsAdvancedSubtitleModeSelected();
                var srtPreset = _subtitlesService.ApplyRenderTarget(
                    isAssMode ? CreateAdvancedSubtitleStylePresetFromConfiguration() : CreateSrtPreviewPreset(),
                    target);

                var rawText = StripSrtTags(row.Text).Replace('\n', ' ').Trim();
                // Apply the preset's text transform (e.g. UPPERCASE) when showing with ASS styling.
                var srtWords = isAssMode
                    ? TransformPreviewText(rawText, srtPreset.TextTransform)
                    : new[] { rawText };

                // In ASS mode the purpose of the SRT fallback is to show the full styling, not to
                // position a subtitle — always present cues at full opacity with entry animations.
                // Ghost-dimming (isActive=false) is only useful in SRT positioning mode.
                var effectiveActive = srtActive || isAssMode;
                var srtKey = effectiveActive ? row.CueNumber : -(row.CueNumber + 1);

                // Also rebuild when the mode changes even if the cue key is the same (guards against
                // _previewCueKey not being reset before this path runs).
                if (srtKey != _previewCueKey || _previewSrtIsAssMode != isAssMode)
                {
                    _previewSrtIsAssMode = isAssMode;
                    _previewIsKaraoke = false;
                    BuildSubtitlePreviewContent(srtPreset, srtWords, [TimeSpan.Zero], effectiveActive);
                    _previewCueKey = srtKey;
                }

                return;
            }

            var cue = PickPreviewCue(position, out var isActive);
            if (cue is null)
            {
                if (SubtitlePreviewCanvas.Children.Count > 0)
                {
                    ClearSubtitlePreview();
                }

                return;
            }

            _previewIsKaraoke = IsKaraokeAdvancedSubtitleTypeSelected();

            // Build the preset exactly as the renderer would for this frame: scale font/margins to the
            // SAME render target the burn uses (probed encoded size), and apply the chunked karaoke
            // fit-to-frame clamp, so the preview matches the burned output rather than design-space size.
            var preset = CreateAdvancedSubtitleStylePresetFromConfiguration();
            preset = _subtitlesService.ApplyRenderTarget(preset, target);

            var (words, starts, unitKey) = ResolvePreviewWords(cue, isActive, preset, position);

            // Distinguish an on-screen cue/chunk from a dimmed positioning ghost so the overlay rebuilds
            // when crossing that boundary (and when the active karaoke chunk advances).
            var key = isActive ? unitKey : -(unitKey + 1);
            if (key != _previewCueKey)
            {
                BuildSubtitlePreviewContent(preset, words, starts, isActive, cue.End);
                _previewCueKey = key;
            }

            if (_previewIsKaraoke && isActive && !_isDraggingPreview)
            {
                UpdateKaraokeHighlight(position);
            }
        }

        // Resolves the words (and their start times) to show for a cue at a position. For chunked
        // karaoke (MaxWordsPerChunk) this returns only the active chunk's words, mirroring the burned
        // "viral" rendering; otherwise the whole cue. The key changes when the visible unit changes.
        private (string[] Words, TimeSpan[] Starts, int Key) ResolvePreviewWords(
            SubtitleCue cue, bool isActive, SubtitleStylePreset preset, TimeSpan position)
        {
            var allWords = TransformPreviewText(cue.Text, preset.TextTransform);
            var allStarts = ComputeWordStarts(cue, allWords.Length);

            var chunkSize = _previewIsKaraoke && preset.MaxWordsPerChunk is int n && n > 0 ? n : 0;
            if (chunkSize <= 0 || allWords.Length <= chunkSize)
            {
                return (allWords, allStarts, cue.Id * 1000);
            }

            var chunkCount = (allWords.Length + chunkSize - 1) / chunkSize;
            var activeChunk = 0;
            if (isActive)
            {
                for (var c = 0; c < chunkCount; c++)
                {
                    var chunkStart = allStarts[c * chunkSize];
                    var nextStart = (c + 1) < chunkCount ? allStarts[(c + 1) * chunkSize] : cue.End;
                    if (position >= chunkStart)
                    {
                        activeChunk = c;
                    }

                    if (position >= chunkStart && position < nextStart)
                    {
                        break;
                    }
                }
            }

            var from = activeChunk * chunkSize;
            var to = Math.Min(from + chunkSize, allWords.Length);
            return (allWords[from..to], allStarts[from..to], (cue.Id * 1000) + activeChunk);
        }

        // The cue to preview at a position: the active cue if any, otherwise the nearest cue shown as a
        // dimmed placeholder so the subtitle is always present and draggable for positioning.
        private SubtitleCue? PickPreviewCue(TimeSpan position, out bool isActive)
        {
            isActive = false;
            var draft = _advancedSubtitleDraft;
            if (draft is null || draft.Cues.Count == 0)
            {
                return null;
            }

            var active = draft.Cues.FirstOrDefault(cue => position >= cue.Start && position < cue.End);
            if (active is not null)
            {
                isActive = true;
                return active;
            }

            return draft.Cues
                .OrderBy(cue => Math.Min(Math.Abs((cue.Start - position).Ticks), Math.Abs((cue.End - position).Ticks)))
                .First();
        }

        // Returns the active SRT row at position, or the nearest as a dimmed ghost for positioning.
        private SubtitleEditableRow? PickPreviewSrtRow(TimeSpan position, out bool isActive)
        {
            isActive = false;

            SubtitleEditableRow? nearest = null;
            var nearestDistance = long.MaxValue;

            foreach (var row in _subtitleEditableRows)
            {
                if (!TryParseSrtTime(row.StartText, out var start) || !TryParseSrtTime(row.EndText, out var end))
                {
                    continue;
                }

                if (position >= start && position < end)
                {
                    isActive = true;
                    return row;
                }

                var distance = Math.Min(Math.Abs((start - position).Ticks), Math.Abs((end - position).Ticks));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = row;
                }
            }

            return nearest;
        }

        private static bool TryParseSrtTime(string text, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            // SRT uses HH:MM:SS,mmm; normalize comma to period for TimeSpan parsing.
            var normalized = text.Trim().Replace(',', '.');
            return TimeSpan.TryParseExact(normalized, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out result)
                || TimeSpan.TryParseExact(normalized, @"h\:mm\:ss\.fff", CultureInfo.InvariantCulture, out result);
        }

        private static string StripSrtTags(string text)
        {
            // Strip basic SRT/HTML formatting tags (<i>, <b>, <u>, <font ...>, etc.).
            return System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, @"<[^>]+>", string.Empty);
        }

        private static SubtitleStylePreset CreateSrtPreviewPreset() => new SubtitleStylePreset
        {
            Name = "SRT Preview",
            AssStyleName = "Default",
            ScriptTitle = "SRT preview",
            PlayResX = 1920,
            PlayResY = 1080,
            PrimaryFontFamily = "Segoe UI",
            FontFamilyFallbacks = ["Arial", "Helvetica"],
            FontSize = 56,
            Bold = false,
            FillColor = SubtitleColor.White,
            OutlineColor = SubtitleColor.Black,
            OutlineWidth = 4,
            ShadowDepth = 0,
            Alignment = SubtitleVisualAlignment.BottomCenter,
            MarginLeft = 96,
            MarginRight = 96,
            MarginVertical = 60,
        };

        private void BuildSubtitlePreviewContent(SubtitleStylePreset preset, string[] words, TimeSpan[] wordStarts, bool isActive, TimeSpan cueEnd = default)
        {
            _buildingSubtitlePreview = true;
            try
            {
                BuildSubtitlePreviewContentCore(preset, words, wordStarts, isActive, cueEnd);
            }
            finally
            {
                _buildingSubtitlePreview = false;
            }
        }

        private void BuildSubtitlePreviewContentCore(SubtitleStylePreset preset, string[] words, TimeSpan[] wordStarts, bool isActive, TimeSpan cueEnd)
        {
            _runningPreviewStoryboard?.Stop();
            _runningPreviewStoryboard = null;
            _runningWordPopStoryboard?.Stop();
            _runningWordPopStoryboard = null;
            SubtitlePreviewCanvas!.Children.Clear();
            _previewKaraokeRuns.Clear();
            _previewContent = null;
            _previewActiveWordIndex = -1;
            _previewCueEnd = cueEnd;

            var bounds = GetPreviewVideoBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            SubtitlePreviewCanvas.Width = PreviewHost?.ActualWidth ?? bounds.Width;
            SubtitlePreviewCanvas.Height = PreviewHost?.ActualHeight ?? bounds.Height;

            _previewBaseColor = ToPreviewColor(preset.FillColor);
            _previewHighlightColor = ToPreviewColor(preset.KaraokeHighlightColor);
            _cachedPreviewBaseBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(_previewBaseColor);
            _cachedPreviewHighlightBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(_previewHighlightColor);

            if (_previewIsKaraoke)
            {
                _previewKaraokeFill = ResolvePreviewKaraokeFill(preset);
                _previewActiveWordPopScale = ResolvePreviewActiveWordPopScale(preset);
            }
            else
            {
                _previewKaraokeFill = KaraokeFill.Instant;
                _previewActiveWordPopScale = 1d;
            }

            // The preset is already in the target frame's coordinate space (PlayResY == video height,
            // font/margins scaled to the same target the renderer probes), so the on-screen size is
            // exactly the generated .ass Style Fontsize scaled to the displayed video rectangle. This
            // matches the burn 1:1 (verified against the actual generated .ass files).
            var scale = bounds.Height / Math.Max(1, preset.PlayResY);
            var fontPx = Math.Max(8d, preset.FontSize * scale);
            var outlineOffset = Math.Max(1d, preset.OutlineWidth * scale * 0.6d);
            _previewMaxWidth = bounds.Width * 0.92d;

            if (words.Length == 0)
            {
                return;
            }

            var fontFamily = ResolvePreviewFontFamily(preset);
            var weight = preset.Bold ? FontWeights.Bold : FontWeights.Normal;
            var fontStyle = preset.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;

            var lines = new Grid();

            // Shadow: a single offset copy behind everything (approximates the ASS shadow depth).
            if (preset.ShadowDepth > 0)
            {
                var shadowOffset = Math.Max(1d, preset.ShadowDepth * scale);
                var shadowBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToPreviewColor(preset.ShadowColor));
                lines.Children.Add(CreatePreviewLine(
                    words, fontFamily, fontPx, weight, fontStyle, _previewMaxWidth, shadowBrush, perWordRuns: false,
                    margin: new Thickness(shadowOffset, shadowOffset, 0, 0)));
            }

            // Outline: layered offset copies of the full line in the outline colour. A boxed preset
            // (ASS BorderStyle 3) draws a filled box instead of an outline, so skip the copies there.
            if (!preset.UseBackgroundBox)
            {
                var outlineBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToPreviewColor(preset.OutlineColor));
                foreach (var (dx, dy) in OutlineOffsets)
                {
                    lines.Children.Add(CreatePreviewLine(
                        words, fontFamily, fontPx, weight, fontStyle, _previewMaxWidth, outlineBrush, perWordRuns: false,
                        margin: new Thickness(dx * outlineOffset, dy * outlineOffset, 0, 0)));
                }
            }

            // Fill: one run per word so karaoke can recolour individual words.
            var fillBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(_previewBaseColor);
            lines.Children.Add(CreatePreviewLine(
                words, fontFamily, fontPx, weight, fontStyle, _previewMaxWidth, fillBrush, perWordRuns: true, margin: default));

            if (_previewIsKaraoke && isActive)
            {
                for (var i = 0; i < _previewKaraokeRuns.Count && i < wordStarts.Length; i++)
                {
                    _previewKaraokeRuns[i] = (_previewKaraokeRuns[i].Run, wordStarts[i]);
                }
            }

            // A border wraps the text so its whole bounding box is a drag target. Dragging it sets the
            // subtitle placement. It also renders the background box for boxed presets. Dimmed when it
            // is only a positioning ghost.
            var content = new Border
            {
                Opacity = isActive ? 1d : 0.5d,
                Child = lines
            };
            if (preset.UseBackgroundBox)
            {
                content.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToPreviewColor(preset.OutlineColor));
                content.Padding = new Thickness(fontPx * 0.28d, fontPx * 0.10d, fontPx * 0.28d, fontPx * 0.10d);
                content.CornerRadius = new CornerRadius(Math.Max(2d, fontPx * 0.06d));
            }
            else
            {
                // Transparent (not null) so the empty area is still hit-testable for dragging.
                content.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            content.PointerPressed += SubtitlePreview_PointerPressed;
            content.PointerMoved += SubtitlePreview_PointerMoved;
            content.PointerReleased += SubtitlePreview_PointerReleased;
            content.PointerCanceled += SubtitlePreview_PointerReleased;
            content.PointerCaptureLost += SubtitlePreview_PointerReleased;

            SubtitlePreviewCanvas.Children.Add(content);
            _previewContent = content;
            PositionPreviewContent();

            if (isActive)
            {
                // Record the wall-clock build time so UpdateKaraokeHighlight can tell whether a
                // first tick is at a natural cue start (small elapsed) or a mid-cue rebuild caused
                // by an automatic refresh (large elapsed).  Captured here — after wordStarts have
                // been applied — so the tick is as close as possible to when the runs are ready.
                _previewCueBuildTick = Environment.TickCount64;

                // Defer Begin() to the next dispatcher iteration so the element has completed its
                // layout pass in the visual tree before the storyboard targets it. Calling Begin()
                // before layout is done causes WinUI 3 to silently drop the animation.
                var capturedContent = content;
                var capturedPreset = preset;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ReferenceEquals(_previewContent, capturedContent))
                    {
                        PlayPreviewEntryAnimation(capturedContent, capturedPreset);
                    }
                });
            }

        }

        // Approximates the preset's entry presentation on the live preview: a fade-in and/or a
        // scale "pop". Karaoke colour sweep is handled per-frame in UpdateKaraokeHighlight.
        private void PlayPreviewEntryAnimation(Border content, SubtitleStylePreset preset)
        {
            // Stop any in-flight word-pop storyboard: both target the same ScaleTransform on
            // `content` and animating ScaleX/ScaleY from two concurrent storyboards causes
            // broken/jittery visuals. The entry animation takes priority at cue start.
            _runningWordPopStoryboard?.Stop();
            _runningWordPopStoryboard = null;

            var entryFadeMs = preset.EntryFadeMilliseconds;
            var introScale = preset.IntroScale;
            if (preset.Effects is not null)
            {
                foreach (var effect in preset.Effects)
                {
                    if (effect.Kind == SubtitleEffectKind.EntryFade)
                    {
                        entryFadeMs = Math.Max(entryFadeMs, effect.DurationMs);
                    }
                    else if (effect.Kind == SubtitleEffectKind.EntryPop)
                    {
                        introScale = Math.Max(introScale, effect.Scale);
                    }
                }
            }

            var hasFade = entryFadeMs > 0;
            var hasPop = introScale > 1.001d;
            if (!hasFade && !hasPop)
            {
                return;
            }

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var targetOpacity = content.Opacity;
            var duration = TimeSpan.FromMilliseconds(Math.Max(hasFade ? entryFadeMs : 0, hasPop ? 160 : 0));

            if (hasFade)
            {
                var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0d,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(entryFadeMs)
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, content);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
                storyboard.Children.Add(fade);
            }

            if (hasPop)
            {
                // Reuse an existing ScaleTransform if one is already on the element (set by a
                // prior word-pop animation). Creating a new one would orphan any storyboard still
                // targeting the old transform, causing the word pop to have no visual effect.
                if (content.RenderTransform is not Microsoft.UI.Xaml.Media.ScaleTransform scale)
                {
                    scale = new Microsoft.UI.Xaml.Media.ScaleTransform();
                    content.RenderTransform = scale;
                    content.RenderTransformOrigin = new Point(0.5d, 0.5d);
                }

                var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                };
                foreach (var axis in new[] { "ScaleX", "ScaleY" })
                {
                    var pop = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                    {
                        From = introScale,
                        To = 1d,
                        Duration = duration,
                        EasingFunction = ease
                    };
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(pop, scale);
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(pop, axis);
                    storyboard.Children.Add(pop);
                }
            }

            _runningPreviewStoryboard = storyboard;
            storyboard.Begin();
        }

        // Centres the current preview element on the placement anchor within the displayed video rect.
        private void PositionPreviewContent()
        {
            if (_previewContent is null || SubtitlePreviewCanvas is null)
            {
                return;
            }

            var bounds = GetPreviewVideoBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            SubtitlePreviewCanvas.Width = PreviewHost?.ActualWidth ?? bounds.Width;
            SubtitlePreviewCanvas.Height = PreviewHost?.ActualHeight ?? bounds.Height;

            _previewContent.Measure(new Size(_previewMaxWidth > 0 ? _previewMaxWidth : bounds.Width, double.PositiveInfinity));
            var desired = _previewContent.DesiredSize;
            var centerX = bounds.X + (_subtitlePlacementX * bounds.Width);
            var centerY = bounds.Y + (_subtitlePlacementY * bounds.Height);
            var left = Math.Clamp(centerX - (desired.Width / 2d), bounds.X, Math.Max(bounds.X, bounds.X + bounds.Width - desired.Width));
            var top = Math.Clamp(centerY - (desired.Height / 2d), bounds.Y, Math.Max(bounds.Y, bounds.Y + bounds.Height - desired.Height));
            Canvas.SetLeft(_previewContent, left);
            Canvas.SetTop(_previewContent, top);
        }

        private void SubtitlePreview_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement element || !ShouldShowSubtitlePreview())
            {
                return;
            }

            _isDraggingPreview = true;
            _previewDragLastPoint = e.GetCurrentPoint(SubtitlePreviewCanvas).Position;
            element.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void SubtitlePreview_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingPreview)
            {
                return;
            }

            var bounds = GetPreviewVideoBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var point = e.GetCurrentPoint(SubtitlePreviewCanvas).Position;
            var deltaXPercent = (point.X - _previewDragLastPoint.X) / bounds.Width * 100d;
            var deltaYPercent = (point.Y - _previewDragLastPoint.Y) / bounds.Height * 100d;
            _previewDragLastPoint = point;

            // Hot path: update the position and reposition without touching NumberBoxes or rebuilding
            // anything. NumberBox.Value writes on every frame are expensive (WinUI control layout work);
            // UpdateSubtitlePlacementPreview builds a template tree on a hidden element every frame.
            // Both happen in ApplySubtitlePlacementValue, so we bypass it here and sync once on release.
            var newX = Math.Clamp(Math.Round((_subtitlePlacementX * 100d) + deltaXPercent, 1, MidpointRounding.AwayFromZero), 0d, 100d);
            var newY = Math.Clamp(Math.Round((_subtitlePlacementY * 100d) + deltaYPercent, 1, MidpointRounding.AwayFromZero), 0d, 100d);
            _subtitlePlacementX = newX / 100d;
            _subtitlePlacementY = newY / 100d;
            PositionPreviewContent();
            e.Handled = true;
        }

        private void SubtitlePreview_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingPreview)
            {
                return;
            }

            _isDraggingPreview = false;
            if (sender is UIElement element)
            {
                element.ReleasePointerCapture(e.Pointer);
            }

            e.Handled = true;

            // Sync the NumberBoxes and placement marker once now that the drag has ended.
            ApplySubtitlePlacementValue(_subtitlePlacementX * 100d, _subtitlePlacementY * 100d,
                synchronizeX: true, synchronizeY: true, refreshState: true);

            // Persist placement into the .ass file. Use the lightweight path: no editor reload, no
            // extra ffprobe — LoadAdvancedSubtitleEditor rebuilds every cue row (visible "refresh")
            // and ResolveSubtitleRenderTargetAsync launches ffprobe; neither is needed for a position
            // change only.
            _ = PersistSubtitlePlacementAsync();
        }

        /// <summary>
        /// Lightweight placement persist: re-renders the .ass with the current position and writes it
        /// to disk. Does NOT reload the subtitle editor (no cue-row rebuild) and reuses the cached
        /// render target (no ffprobe). Called after a drag-release so only the \pos/\an values change.
        /// </summary>
        private async Task PersistSubtitlePlacementAsync()
        {
            if (_advancedSubtitleDraft is null || _sourceVideoFile is null
                || _isRestylingAdvancedSubtitles || _isGeneratingSubtitles || _isProcessing)
            {
                return;
            }

            _isRestylingAdvancedSubtitles = true;
            try
            {
                var path = SubtitlesService.EnsureAssExtension(
                    _generatedSubtitlePath
                    ?? _pendingAdvancedSubtitleOutputPath
                    ?? CreateGeneratedSubtitlePath(_sourceVideoFile, ".ass"));

                var placement = CreateSubtitlePlacementOptionsFromUi();
                var preset = CreateAdvancedSubtitleStylePresetFromConfiguration();

                // Reuse the cached render target — no need to re-probe the source file.
                var target = _previewRenderTarget
                    ?? (_previewVideoSize.Width > 0 && _previewVideoSize.Height > 0
                        ? new SubtitleRenderTarget((int)Math.Round(_previewVideoSize.Width), (int)Math.Round(_previewVideoSize.Height))
                        : null);

                var ass = IsKaraokeAdvancedSubtitleTypeSelected()
                    ? _subtitlesService.RenderKaraokeAss(_advancedSubtitleDraft, preset, placement, target)
                    : _subtitlesService.RenderStyledAss(_advancedSubtitleDraft, preset, placement, target);

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(path, ass);
                _generatedSubtitlePath = path;
                _pendingAdvancedSubtitleOutputPath = null;
                _pendingAdvancedSubtitleKind = PendingAdvancedSubtitleKind.None;
                if (SubtitlePathTextBox is not null)
                {
                    SubtitlePathTextBox.Text = path;
                }
            }
            finally
            {
                _isRestylingAdvancedSubtitles = false;
            }
        }

        private TextBlock CreatePreviewLine(
            string[] words,
            Microsoft.UI.Xaml.Media.FontFamily fontFamily,
            double fontPx,
            Windows.UI.Text.FontWeight weight,
            Windows.UI.Text.FontStyle fontStyle,
            double maxWidth,
            Microsoft.UI.Xaml.Media.Brush brush,
            bool perWordRuns,
            Thickness margin)
        {
            var block = new TextBlock
            {
                FontFamily = fontFamily,
                FontSize = fontPx,
                FontWeight = weight,
                FontStyle = fontStyle,
                Foreground = brush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = maxWidth,
                Margin = margin
            };

            if (!perWordRuns)
            {
                block.Text = string.Join(' ', words);
                return block;
            }

            for (var i = 0; i < words.Length; i++)
            {
                var run = new Microsoft.UI.Xaml.Documents.Run { Text = words[i] };
                block.Inlines.Add(run);
                if (i < words.Length - 1)
                {
                    block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " });
                }

                // Tracked for karaoke recolouring; start times are filled in by the caller.
                _previewKaraokeRuns.Add((run, TimeSpan.Zero));
            }

            return block;
        }

        private void UpdateKaraokeHighlight(TimeSpan position)
        {
            var runs = _previewKaraokeRuns;
            if (runs.Count == 0)
            {
                return;
            }

            // Find which word is currently being spoken.
            // Primary pass: strict half-open window [wordStart, nextWordStart).
            var activeIndex = -1;
            for (var i = 0; i < runs.Count; i++)
            {
                var wordStart = runs[i].Start;
                var wordEnd = i + 1 < runs.Count ? runs[i + 1].Start : _previewCueEnd;
                if (position >= wordStart && position < wordEnd)
                {
                    activeIndex = i;
                    break;
                }
            }

            // Fallback for inter-word gaps: if no exact window matched but position has passed
            // word 0's start, use the last word whose start is ≤ position (handles silence gaps
            // at the end of a cue where _previewCueEnd may not fully cover all words).
            if (activeIndex < 0 && runs.Count > 0 && position >= runs[0].Start)
            {
                for (var i = runs.Count - 1; i >= 0; i--)
                {
                    if (position >= runs[i].Start)
                    {
                        activeIndex = i;
                        break;
                    }
                }
            }

            // First-tick guard: on the very first call after a new active-cue build
            // (_previewActiveWordIndex == -1) the scan may land past word 0 because:
            //   (a) The 16 ms timer fired slightly after a short window closed (jitter).
            //   (b) A mid-cue automatic refresh (style change, size change, …) rebuilt the
            //       cue while playback was already deep inside it.
            // In case (a) we want to clamp back to word 0 so the sweep always starts from
            // the first word.  In case (b) we must NOT clamp — the user or the player is
            // genuinely mid-cue and should see the correct "already spoken" state.
            //
            // We distinguish the two cases with a wall-clock delta: _previewCueBuildTick is
            // captured at the END of BuildSubtitlePreviewContentCore (after wordStarts are
            // applied) so it is very close in time to this first UpdateKaraokeHighlight call.
            // A small delta (≤ 250 ms) means we are at the natural cue start; a large delta
            // means a mid-cue rebuild happened and we should leave the scan result as-is.
            if (_previewActiveWordIndex < 0 && activeIndex > 0 && runs.Count > 0)
            {
                var wallClockElapsedMs = Environment.TickCount64 - _previewCueBuildTick;
                if (wallClockElapsedMs <= 250L)
                    activeIndex = 0;
            }

            // Trigger a scale pop on the whole block when the active word advances.
            if (activeIndex >= 0 && activeIndex != _previewActiveWordIndex && _previewActiveWordPopScale > 1.001d)
            {
                TriggerActiveWordPopAnimation();
            }
            _previewActiveWordIndex = activeIndex;

            var isDropIn = _previewKaraokeFill == KaraokeFill.DropIn;
            var isSweep = _previewKaraokeFill == KaraokeFill.Sweep || isDropIn;

            var baseBrush = _cachedPreviewBaseBrush ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(_previewBaseColor);
            var highlightBrush = _cachedPreviewHighlightBrush ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(_previewHighlightColor);

            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i].Run;
                if (i < activeIndex)
                {
                    // Words already spoken — always show as highlighted.
                    run.Foreground = highlightBrush;
                }
                else if (i == activeIndex)
                {
                    if (isSweep)
                    {
                        var wordStart = runs[i].Start;
                        var wordEnd = i + 1 < runs.Count ? runs[i + 1].Start : _previewCueEnd;
                        var wordDuration = (wordEnd - wordStart).TotalMilliseconds;
                        var elapsed = (position - wordStart).TotalMilliseconds;
                        var fraction = wordDuration > 0 ? Math.Clamp(elapsed / wordDuration, 0d, 1d) : 1d;
                        run.Foreground = CreateKaraokeSweepBrush(fraction, isDropIn);
                    }
                    else
                    {
                        run.Foreground = highlightBrush;
                    }
                }
                else
                {
                    // Words not yet spoken — transparent for DropIn, base colour otherwise.
                    run.Foreground = isDropIn ? TransparentBrush : baseBrush;
                }
            }
        }

        // Horizontal sweep brush: filled (highlight or transparent-to-highlight for DropIn)
        // on the left up to `fraction`, unfilled on the right.
        private Microsoft.UI.Xaml.Media.Brush CreateKaraokeSweepBrush(double fraction, bool isDropIn)
        {
            var filledColor = _previewHighlightColor;
            var unfilledColor = isDropIn ? Microsoft.UI.Colors.Transparent : _previewBaseColor;

            if (fraction <= 0d)
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(unfilledColor);
            }

            if (fraction >= 1d)
            {
                return _cachedPreviewHighlightBrush ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(filledColor);
            }

            // Two adjacent stops at `fraction` create a sharp horizontal fill edge.
            var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = filledColor, Offset = 0 });
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = filledColor, Offset = fraction });
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = unfilledColor, Offset = fraction });
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = unfilledColor, Offset = 1 });
            return brush;
        }

        // Plays a brief scale-down animation on the whole preview block when the active karaoke
        // word changes. Approximates the per-word pop of `ActiveWordPop` in the ASS output.
        private void TriggerActiveWordPopAnimation()
        {
            if (_previewContent is null)
            {
                return;
            }

            // Reuse existing transform if available, so this doesn't interfere with the entry pop.
            if (_previewContent.RenderTransform is not Microsoft.UI.Xaml.Media.ScaleTransform scaleTransform)
            {
                scaleTransform = new Microsoft.UI.Xaml.Media.ScaleTransform();
                _previewContent.RenderTransform = scaleTransform;
                _previewContent.RenderTransformOrigin = new Point(0.5d, 0.5d);
            }

            _runningWordPopStoryboard?.Stop();

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            };
            foreach (var axis in new[] { "ScaleX", "ScaleY" })
            {
                var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = _previewActiveWordPopScale,
                    To = 1d,
                    Duration = TimeSpan.FromMilliseconds(120),
                    EasingFunction = ease
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, scaleTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, axis);
                storyboard.Children.Add(anim);
            }

            _runningWordPopStoryboard = storyboard;
            storyboard.Begin();
        }

        // Mirrors SubtitlesService.ResolveKaraokeFill for use in the preview without reaching
        // into the service's private implementation.
        private static KaraokeFill ResolvePreviewKaraokeFill(SubtitleStylePreset preset)
        {
            if (preset.Effects is { Count: > 0 } effects)
            {
                foreach (var effect in effects)
                {
                    switch (effect.Kind)
                    {
                        case SubtitleEffectKind.DropIn: return KaraokeFill.DropIn;
                        case SubtitleEffectKind.KaraokeColorInstant: return KaraokeFill.Instant;
                        case SubtitleEffectKind.KaraokeColorSweep: return KaraokeFill.Sweep;
                    }
                }
            }

            return preset.PresentationAnimation switch
            {
                SubtitlePresentationAnimation.DropIn => KaraokeFill.DropIn,
                SubtitlePresentationAnimation.None => KaraokeFill.Instant,
                _ => KaraokeFill.Sweep
            };
        }

        // Mirrors SubtitlesService.ResolveActiveWordScale.
        private static double ResolvePreviewActiveWordPopScale(SubtitleStylePreset preset)
        {
            if (preset.Effects is { Count: > 0 } effects)
            {
                foreach (var effect in effects)
                {
                    if (effect.Kind == SubtitleEffectKind.ActiveWordPop)
                    {
                        return effect.Scale > 0 ? Math.Min(effect.Scale, 1.4d) : 1d;
                    }
                }
            }

            return 1d;
        }

        private TimeSpan[] ComputeWordStarts(SubtitleCue cue, int wordCount)
        {
            if (wordCount <= 0)
            {
                return [];
            }

            // Prefer real word timing from the source transcription when it lines up with this cue's
            // words; otherwise distribute evenly across the cue window.
            var sourceWords = _advancedSubtitleDraft?.SourceWords;
            if (sourceWords is { Count: > 0 })
            {
                var inCue = sourceWords
                    .Where(word => word.Start < cue.End && word.End > cue.Start
                        // Mirror the boundary-spanning filter in BuildCueWordsFromSubtitleCue:
                        // exclude words that started before this cue and extend only trivially
                        // past cue.Start (< 100 ms). They belong to the previous segment and
                        // would produce a near-zero first-word window.
                        && !(word.Start < cue.Start
                             && (word.End - cue.Start) < TimeSpan.FromMilliseconds(100)))
                    .OrderBy(word => word.Start)
                    .ToList();
                if (inCue.Count == wordCount)
                {
                    var wordStarts = inCue.Select(word => word.Start).ToArray();

                    // Apply the same cursor-based clamping that BuildCueWordsFromSubtitleCue
                    // uses when generating the .ass file.  If the raw alignment gives
                    // word[i+1].Start < word[i].End (overlap), that word's start is clamped
                    // to the previous word's end — exactly mirroring "if (start < cursor)
                    // start = cursor; … cursor = end;" in the .ass generator.
                    // Without this, the preview sees zero-duration windows [T,T) that the
                    // half-open scan can never match, making the first word appear instantly
                    // filled instead of sweeping progressively (most visible on even cues
                    // whose raw alignment timestamps overlap).
                    var clampCursor = cue.Start;
                    for (var i = 0; i < wordStarts.Length; i++)
                    {
                        if (wordStarts[i] < clampCursor)
                            wordStarts[i] = clampCursor;

                        // Advance cursor to this word's end time, with a 1 ms floor to
                        // guarantee forward progress (mirrors "if (end <= start) end = start
                        // + MinimumPositiveDuration;" in BuildCueWordsFromSubtitleCue).
                        var wordEnd = inCue[i].End;
                        if (wordEnd <= wordStarts[i])
                            wordEnd = wordStarts[i] + TimeSpan.FromMilliseconds(1);
                        clampCursor = wordEnd;
                    }

                    return wordStarts;
                }
            }

            var starts = new TimeSpan[wordCount];
            var duration = cue.End > cue.Start ? cue.End - cue.Start : TimeSpan.FromMilliseconds(1);
            for (var i = 0; i < wordCount; i++)
            {
                starts[i] = cue.Start + TimeSpan.FromTicks(duration.Ticks * i / wordCount);
            }

            return starts;
        }

        private static string[] TransformPreviewText(string text, SubtitleTextTransform transform)
        {
            var normalized = (text ?? string.Empty)
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
            normalized = transform switch
            {
                SubtitleTextTransform.Uppercase => normalized.ToUpperInvariant(),
                SubtitleTextTransform.Lowercase => normalized.ToLowerInvariant(),
                _ => normalized
            };

            return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        // SubtitleColor uses the ASS alpha convention (0 = opaque, 255 = transparent); WinUI's Color
        // uses the opposite (255 = opaque). Invert the alpha so preview colours render correctly.
        private static Color ToPreviewColor(SubtitleColor color)
        {
            return Color.FromArgb((byte)(255 - color.Alpha), color.Red, color.Green, color.Blue);
        }

        // Picks the first of the preset's preferred fonts that is actually installed so distinct styles
        // look distinct (an uninstalled font would silently collapse to the same system fallback).
        private Microsoft.UI.Xaml.Media.FontFamily ResolvePreviewFontFamily(SubtitleStylePreset preset)
        {
            var candidates = new List<string> { preset.PrimaryFontFamily };
            candidates.AddRange(preset.FontFamilyFallbacks);
            foreach (var name in candidates)
            {
                if (!string.IsNullOrWhiteSpace(name) &&
                    _installedFontFamilies.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    return new Microsoft.UI.Xaml.Media.FontFamily(name);
                }
            }

            return new Microsoft.UI.Xaml.Media.FontFamily(preset.PrimaryFontFamily);
        }

        // 8-direction offsets used to fake a text outline by layering copies behind the fill text.
        private static readonly (double X, double Y)[] OutlineOffsets =
        [
            (-1, -1), (0, -1), (1, -1),
            (-1, 0), (1, 0),
            (-1, 1), (0, 1), (1, 1)
        ];

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
            // When the live preview is up (advanced or SRT), the subtitle text is the drag handle.
            // Hide the separate placement box to avoid overlapping controls.
            return _sourceVideoFile is not null &&
                (EnableSubtitleMuxCheckBox?.IsChecked ?? false) &&
                SubtitlePlacementMarker is not null &&
                !ShouldShowSubtitlePreview();
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

            await RenderAndStoreAdvancedAssAsync(reviewedDraft, path);
            return true;
        }

        /// <summary>
        /// Renders <paramref name="reviewedDraft"/> to the styled or karaoke ASS chosen in the UI and
        /// writes it to <paramref name="path"/>, then refreshes editor/state. Shared by the "Apply
        /// changes" review flow and the live restyle flow, so both stay in sync.
        /// </summary>
        private async Task RenderAndStoreAdvancedAssAsync(SubtitleDraft reviewedDraft, string path)
        {
            var placement = CreateSubtitlePlacementOptionsFromUi();
            var preset = CreateAdvancedSubtitleStylePresetFromConfiguration();

            // Size to the real frame so styles stay undistorted on any aspect ratio (e.g. vertical
            // video). Probe the source rather than trusting the 1920x1080 preview default.
            var target = await ResolveSubtitleRenderTargetAsync();
            var ass = IsKaraokeAdvancedSubtitleTypeSelected()
                ? _subtitlesService.RenderKaraokeAss(reviewedDraft, preset, placement, target)
                : _subtitlesService.RenderStyledAss(reviewedDraft, preset, placement, target);

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
            RefreshSubtitlePreview();
        }

        /// <summary>
        /// Re-renders the already-generated advanced subtitles with the current style/typography,
        /// preserving any in-progress text edits. Does nothing when no draft exists yet. This is what
        /// lets the user switch styles without regenerating (re-transcribing) the subtitles.
        /// </summary>
        private async Task ReRenderAdvancedSubtitlesIfReadyAsync()
        {
            if (_advancedSubtitleDraft is null || _sourceVideoFile is null || _isRestylingAdvancedSubtitles
                || _isGeneratingSubtitles || _isProcessing)
            {
                return;
            }

            _isRestylingAdvancedSubtitles = true;
            try
            {
                // Fold any unsaved text edits from the review editor into the draft before restyling
                // so a style change never discards them.
                var reviewedDraft = _advancedSubtitleDraft;
                SyncSubtitleRowsFromEditors();
                var cues = _advancedSubtitleDraft.Cues;
                if (_subtitleEditableRows.Count == cues.Count)
                {
                    var corrections = new List<SubtitleSegmentCorrection>();
                    for (var index = 0; index < cues.Count; index++)
                    {
                        var normalizedText = (_subtitleEditableRows[index].Text ?? string.Empty).Replace("\r\n", "\n").Trim();
                        if (!string.Equals(normalizedText, cues[index].Text, StringComparison.Ordinal))
                        {
                            corrections.Add(new SubtitleSegmentCorrection(cues[index].Id, normalizedText, Start: null, End: null));
                        }
                    }

                    if (corrections.Count > 0)
                    {
                        reviewedDraft = _subtitlesService.ApplyCorrections(_advancedSubtitleDraft, corrections, _advancedSubtitleDraft.Options);
                    }
                }

                var path = SubtitlesService.EnsureAssExtension(
                    _generatedSubtitlePath
                    ?? _pendingAdvancedSubtitleOutputPath
                    ?? CreateGeneratedSubtitlePath(_sourceVideoFile, ".ass"));

                await RenderAndStoreAdvancedAssAsync(reviewedDraft, path);
                EnableSubtitleMuxCheckBox.IsChecked = true;
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync("Subtitle style", $"Could not update the subtitle style: {ex.Message}");
            }
            finally
            {
                _isRestylingAdvancedSubtitles = false;
            }
        }

        /// <summary>
        /// Fills the inline style picker from the style catalog for the currently selected output
        /// kind (Styled/Karaoke) and selects the configured preset. Catalog-driven, so registering a
        /// new style makes it appear here with no further UI changes.
        /// </summary>
        private void PopulateAdvancedStylePicker()
        {
            if (AdvancedSubtitleStyleComboBox is null)
            {
                return;
            }

            var isKaraoke = IsKaraokeAdvancedSubtitleTypeSelected();
            var entries = SubtitleStyleCatalog
                .ByKind(isKaraoke ? SubtitleStyleKind.Karaoke : SubtitleStyleKind.Styled)
                .ToList();
            var configuredId = isKaraoke
                ? _advancedSubtitlePresetConfiguration.KaraokePresetId
                : _advancedSubtitlePresetConfiguration.StyledPresetId;

            // The style picker is only meaningful when there are multiple options. For styled mode
            // the catalog has no entries (the page exposes a single customisable base), so hide it.
            var pickerVisible = entries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            AdvancedSubtitleStyleComboBox.Visibility = pickerVisible;
            if (AdvancedSubtitleStyleHintTextBlock is not null)
            {
                AdvancedSubtitleStyleHintTextBlock.Visibility = pickerVisible;
            }

            _isSyncingAdvancedStylePicker = true;
            try
            {
                AdvancedSubtitleStyleComboBox.Items.Clear();
                foreach (var entry in entries)
                {
                    AdvancedSubtitleStyleComboBox.Items.Add(new ComboBoxItem { Content = entry.DisplayName });
                }

                var index = entries.FindIndex(entry => string.Equals(entry.Id, configuredId, StringComparison.OrdinalIgnoreCase));
                AdvancedSubtitleStyleComboBox.SelectedIndex = index >= 0 ? index : (entries.Count > 0 ? 0 : -1);
            }
            finally
            {
                _isSyncingAdvancedStylePicker = false;
            }
        }

        private async void AdvancedSubtitleStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingAdvancedStylePicker)
            {
                return;
            }

            var isKaraoke = IsKaraokeAdvancedSubtitleTypeSelected();
            var entries = SubtitleStyleCatalog
                .ByKind(isKaraoke ? SubtitleStyleKind.Karaoke : SubtitleStyleKind.Styled)
                .ToList();
            var index = AdvancedSubtitleStyleComboBox.SelectedIndex;
            if (index < 0 || index >= entries.Count)
            {
                return;
            }

            ApplyStylePresetToConfiguration(entries[index]);
            UpdateAdvancedSubtitlePresetSummary();
            await ReRenderAdvancedSubtitlesIfReadyAsync();
        }

        /// <summary>
        /// Adopts a catalog preset's full look (font, size, outline, margin, weight, transform, and
        /// karaoke accent) into the active configuration, keyed to the preset's kind.
        /// </summary>
        private void ApplyStylePresetToConfiguration(SubtitleStyleCatalogEntry entry)
        {
            var preset = entry.Factory();
            var configuration = _advancedSubtitlePresetConfiguration;
            if (entry.Kind == SubtitleStyleKind.Karaoke)
            {
                configuration.KaraokePresetId = entry.Id;
                configuration.KaraokeHighlightColor = preset.KaraokeHighlightColor;
            }
            else
            {
                configuration.StyledPresetId = entry.Id;
            }

            configuration.FontFamily = preset.PrimaryFontFamily;
            configuration.FontSize = preset.FontSize;
            configuration.OutlineWidth = preset.OutlineWidth;
            configuration.MarginVertical = preset.MarginVertical;
            configuration.Bold = preset.Bold;
            configuration.TextTransform = preset.TextTransform;
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
            // Items carry x:Uid, so their Content is localized — match by index.
            // XAML order: 0 = Mono, 1 = Stereo.
            return AudioDenoiseModeComboBox?.SelectedIndex == 1
                ? AudioDenoiseMode.StrongStereo
                : AudioDenoiseMode.Mono;
        }

        private bool IsRemoveAudioSelected()
        {
            // The "Remove audio" item carries x:Uid (localized Content) and is the last item in the
            // combo box, so detect it by index rather than by comparing the English literal.
            return AudioCodecComboBox is not null
                && AudioCodecComboBox.SelectedIndex == AudioCodecComboBox.Items.Count - 1;
        }

        private int GetSignedAudioSyncOffsetMilliseconds()
        {
            var magnitude = Math.Clamp((int)Math.Round(AudioSyncOffsetSlider?.Value ?? 0), 0, MaximumAudioSyncOffsetMilliseconds);
            // Items carry x:Uid, so their Content is localized — match by index.
            // XAML order: 0 = Earlier, 1 = Later.
            return AudioSyncDirectionComboBox?.SelectedIndex == 1
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

            SubtitleSectionDurationTextBlock.Text = string.Format(Strings.Get("VideoPage_SubtitleSectionDurationFormat"), value.ToString("0.#"));
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
            TaskbarProgressHelper.SetIndeterminate();
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
            TaskbarProgressHelper.SetProgress(progress.FractionComplete);
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
            TaskbarProgressHelper.SetProgress(progress.OverallPercent);
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
            TaskbarProgressHelper.Clear();
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
            // The "Remove audio" item carries x:Uid, so its Content is localized — detect it by index
            // (it is the last item; see IsRemoveAudioSelected). The codec items below have plain,
            // non-localized Content, so matching them by text is safe.
            if (comboBox is not null && comboBox.SelectedIndex == comboBox.Items.Count - 1)
            {
                return null;
            }

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
            // Items carry x:Uid, so their Content is localized — match by index. XAML order:
            // 0 = PadToFit, 1 = CropToFill, 2 = Stretch.
            return comboBox?.SelectedIndex switch
            {
                1 => ResizeMode.CropToFill,
                2 => ResizeMode.Stretch,
                _ => ResizeMode.PadToFit
            };
        }

        private static SubtitleMode ParseSubtitleMode(ComboBox? comboBox)
        {
            // Items carry x:Uid, so their Content is localized — match by index instead of text or
            // BurnIn is never detected in non-English UIs. XAML order: 0 = SoftMux, 1 = BurnIn.
            return comboBox?.SelectedIndex == 1 ? SubtitleMode.BurnIn : SubtitleMode.SoftMux;
        }

        private static RepairMode ParseRepairMode(ComboBox? comboBox)
        {
            // Items carry x:Uid, so their Content is localized — match by index.
            // XAML order: 0 = Remux, 1 = Reencode.
            return comboBox?.SelectedIndex == 1 ? RepairMode.Reencode : RepairMode.Remux;
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
