using Files_Tools.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Files_Tools.Pages
{
    public sealed partial class AudioEditorPage : Page
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

        private static readonly string[] SupportedAudioExtensions = [".mp3", ".aac", ".m4a", ".wav", ".flac", ".opus", ".ogg", ".ac3"];

        private readonly IAudioProcessingService _audioProcessingService;
        private readonly IVideoAudioDenoiseService _videoAudioDenoiseService;
        private readonly IAudioTranscriptionService _audioTranscriptionService;
        private readonly ISubtitlesService _subtitlesService;

        private StorageFile? _sourceAudioFile;
        private long? _sourceAudioFileSizeBytes;
        private TimeSpan? _audioDuration;
        private TimeSpan _trimStart = TimeSpan.Zero;
        private TimeSpan _trimEnd = TimeSpan.Zero;
        private readonly MediaPlayer _audioPlayer = new();
        private bool _isProcessing;
        private CancellationTokenSource? _processingCancellation;
        private readonly Stopwatch _progressUiThrottleStopwatch = Stopwatch.StartNew();
        private long _lastProgressUiUpdateTick;

        private ComboBox _outputFormatComboBox = null!;
        private ComboBox _outputCodecComboBox = null!;
        private NumberBox _bitrateNumberBox = null!;
        private NumberBox _sampleRateNumberBox = null!;
        private ComboBox _channelComboBox = null!;

        private CheckBox _enableCompressionCheckBox = null!;
        private ComboBox _compressionModeComboBox = null!;
        private ComboBox _compressionCodecComboBox = null!;
        private NumberBox _compressionBitrateNumberBox = null!;
        private NumberBox _compressionSampleRateNumberBox = null!;
        private ComboBox _compressionChannelsComboBox = null!;

        private CheckBox _enableMetadataRemovalCheckBox = null!;

        private CheckBox _enablePodcastModeCheckBox = null!;
        private TextBlock _podcastProfileNameTextBlock = null!;
        private TextBlock _podcastPresetDescriptionTextBlock = null!;

        private CheckBox _enableTrimCheckBox = null!;
        private CheckBox _trimReencodeCheckBox = null!;

        private CheckBox _enableSilenceTrimCheckBox = null!;
        private ComboBox _silenceModeComboBox = null!;
        private NumberBox _silenceThresholdNumberBox = null!;
        private NumberBox _silenceDurationMsNumberBox = null!;

        private CheckBox _enableNormalizeCheckBox = null!;
        private ComboBox _normalizeModeComboBox = null!;
        private NumberBox _normalizePeakNumberBox = null!;
        private NumberBox _normalizeLufsNumberBox = null!;
        private CheckBox _normalizeLimiterCheckBox = null!;
        private CheckBox _normalizeClipCheckBox = null!;

        private CheckBox _enableEqCheckBox = null!;
        private ComboBox _eqPresetComboBox = null!;
        private CheckBox _eqPreventClipCheckBox = null!;
        private StackPanel _customEqBandsPanel = null!;
        private Button _addEqBandButton = null!;

        private CheckBox _enableDenoiseCheckBox = null!;
        private ComboBox _denoiseModeComboBox = null!;
        private Slider _denoiseStrengthSlider = null!;
        private TextBlock _denoiseStrengthTextBlock = null!;

        private TextBlock _mediaValidationTextBlock = null!;
        private TextBlock _transformValidationTextBlock = null!;
        private TextBlock _adjustValidationTextBlock = null!;
        private Border _mediaFormatCardBorder = null!;
        private Border _mediaMetadataCardBorder = null!;
        private Border _mediaPodcastCardBorder = null!;
        private Border _transformTrimCardBorder = null!;
        private Border _transformSilenceCardBorder = null!;
        private Border _adjustNormalizeCardBorder = null!;
        private Border _adjustEqCardBorder = null!;
        private Border _adjustDenoiseCardBorder = null!;
        private Border _transcriptionCardBorder = null!;

        private Button _downloadTranscriptionFeatureButton = null!;
        private ProgressBar _transcriptionDownloadProgressBar = null!;
        private TextBlock _transcriptionDownloadStatusTextBlock = null!;
        private ComboBox _transcriptionOutputTypeComboBox = null!;
        private CheckBox _includeTimestampsCheckBox = null!;
        private Button _generateTranscriptionButton = null!;
        private ProgressBar _transcriptionProgressBar = null!;
        private TextBlock _transcriptionEtaTextBlock = null!;
        private RichEditBox _transcriptionRichEditBox = null!;
        private Button _saveTranscriptionTextButton = null!;
        private TextBlock _transcriptionStatusTextBlock = null!;

        private bool _isInstallingTranscriptionModel;
        private bool _isGeneratingTranscription;
        private bool _isTranscriptionModelInstalled;
        private string? _generatedTranscriptionPath;

        private readonly List<EqBandRow> _eqRows = [];
        private sealed record EqBandRow(NumberBox Frequency, NumberBox Gain, NumberBox Width, Grid Root);
        private enum TrimDragHandle
        {
            None,
            Start,
            End
        }
        private TrimDragHandle _activeTrimHandle = TrimDragHandle.None;

        private enum AudioPipelineStep
        {
            Convert,
            Compress,
            Normalize,
            Trim,
            SilenceTrim,
            Eq,
            Podcast,
            Denoise,
            Metadata
        }

        public AudioEditorPage()
        {
            _videoAudioDenoiseService = new VideoAudioDenoise();
            _audioProcessingService = new AudioProcessingService(_videoAudioDenoiseService);
            _audioTranscriptionService = new AudioTranscriptionService();
            _subtitlesService = new SubtitlesService(_audioTranscriptionService);
            InitializeComponent();
            BuildOptionUi();
            _isTranscriptionModelInstalled = _audioTranscriptionService.IsInstalled();
            RefreshValidationAndState();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is FileNavigationRequest request && IsSupportedAudioFile(request.File))
            {
                await LoadAudioAsync(request.File);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _audioPlayer.Pause();
            _audioPlayer.Source = null;
            base.OnNavigatedFrom(e);
        }

        private static bool IsSupportedAudioFile(StorageFile file) => SupportedAudioExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase);

        private void BuildOptionUi()
        {
            BuildMediaPanel();
            BuildTransformPanel();
            BuildAdjustPanel();
            BuildTranscriptionPanel();
        }

        private void BuildMediaPanel()
        {
            MediaPanel.Children.Clear();
            var formatCard = CreateCard("Format, codec, and compression");
            _outputFormatComboBox = CreateComboBox("Output format", "Keep original", "MP3", "AAC", "M4A", "WAV", "FLAC", "OPUS", "OGG");
            _outputCodecComboBox = CreateComboBox("Codec", "Auto", "libmp3lame", "aac", "flac", "pcm_s16le", "libopus", "libvorbis");
            _bitrateNumberBox = CreateNumberBox("Bitrate (kbps)", 1);
            _sampleRateNumberBox = CreateNumberBox("Sample rate (Hz)", 8000);
            _channelComboBox = CreateComboBox("Channels", "Keep source", "Mono", "Stereo");

            _enableCompressionCheckBox = CreateCheckBox("Enable compression step");
            _compressionModeComboBox = CreateComboBox("Compression mode", "Lossy", "Lossless");
            _compressionCodecComboBox = CreateComboBox("Compression codec", "Auto", "libmp3lame", "aac", "flac", "libopus", "libvorbis");
            _compressionBitrateNumberBox = CreateNumberBox("Target bitrate (kbps)", 1);
            _compressionSampleRateNumberBox = CreateNumberBox("Target sample rate (Hz)", 8000);
            _compressionChannelsComboBox = CreateComboBox("Channels", "Keep source", "Mono", "Stereo");

            formatCard.Children.Add(_outputFormatComboBox);
            formatCard.Children.Add(_outputCodecComboBox);
            formatCard.Children.Add(_bitrateNumberBox);
            formatCard.Children.Add(_sampleRateNumberBox);
            formatCard.Children.Add(_channelComboBox);
            formatCard.Children.Add(_enableCompressionCheckBox);
            formatCard.Children.Add(_compressionModeComboBox);
            formatCard.Children.Add(_compressionCodecComboBox);
            formatCard.Children.Add(_compressionBitrateNumberBox);
            formatCard.Children.Add(_compressionSampleRateNumberBox);
            formatCard.Children.Add(_compressionChannelsComboBox);
            _mediaFormatCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = formatCard };
            MediaPanel.Children.Add(_mediaFormatCardBorder);

            var metadataCard = CreateCard("Metadata");
            _enableMetadataRemovalCheckBox = CreateCheckBox("Remove metadata");
            metadataCard.Children.Add(_enableMetadataRemovalCheckBox);
            metadataCard.Children.Add(new TextBlock { Opacity = 0.72, Text = "Metadata remover is a standalone pipeline step.", TextWrapping = TextWrapping.Wrap });
            _mediaMetadataCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = metadataCard };
            MediaPanel.Children.Add(_mediaMetadataCardBorder);

            var podcastCard = CreateCard("Podcast mode");
            _enablePodcastModeCheckBox = CreateCheckBox("Enable podcast mode");
            _podcastProfileNameTextBlock = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Text = "Default Podcast Profile"
            };
            _podcastPresetDescriptionTextBlock = new TextBlock
            {
                Opacity = 0.76,
                Text = "Balanced spoken-word profile tuned for clarity and natural tone. If denoise is enabled, DTLN runs before podcast shaping.",
                TextWrapping = TextWrapping.Wrap
            };
            podcastCard.Children.Add(_enablePodcastModeCheckBox);
            podcastCard.Children.Add(_podcastProfileNameTextBlock);
            podcastCard.Children.Add(_podcastPresetDescriptionTextBlock);
            _mediaPodcastCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = podcastCard };
            MediaPanel.Children.Add(_mediaPodcastCardBorder);

            _mediaValidationTextBlock = CreateValidationTextBlock();
            MediaPanel.Children.Add(_mediaValidationTextBlock);
        }

        private void BuildTransformPanel()
        {
            TransformPanel.Children.Clear();
            var trimCard = CreateCard("Trim");
            _enableTrimCheckBox = CreateCheckBox("Enable trim");
            _trimReencodeCheckBox = CreateCheckBox("Re-encode output", true);
            trimCard.Children.Add(_enableTrimCheckBox);
            trimCard.Children.Add(new TextBlock
            {
                Opacity = 0.76,
                Text = "Use the trim timeline under the preview player to choose start and end points.",
                TextWrapping = TextWrapping.Wrap
            });
            trimCard.Children.Add(_trimReencodeCheckBox);
            _transformTrimCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = trimCard };
            TransformPanel.Children.Add(_transformTrimCardBorder);

            var silenceCard = CreateCard("Silence trim");
            _enableSilenceTrimCheckBox = CreateCheckBox("Enable silence trim");
            _silenceModeComboBox = CreateComboBox("Mode", "Leading", "Trailing", "Leading and trailing");
            _silenceThresholdNumberBox = CreateNumberBox("Threshold (dB)", null, -40);
            _silenceDurationMsNumberBox = CreateNumberBox("Minimum silence (ms)", 1, 500);
            silenceCard.Children.Add(_enableSilenceTrimCheckBox);
            silenceCard.Children.Add(_silenceModeComboBox);
            silenceCard.Children.Add(_silenceThresholdNumberBox);
            silenceCard.Children.Add(_silenceDurationMsNumberBox);
            _transformSilenceCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = silenceCard };
            TransformPanel.Children.Add(_transformSilenceCardBorder);

            _transformValidationTextBlock = CreateValidationTextBlock();
            TransformPanel.Children.Add(_transformValidationTextBlock);
        }

        private void BuildAdjustPanel()
        {
            AdjustPanel.Children.Clear();
            var normalizeCard = CreateCard("Normalization");
            _enableNormalizeCheckBox = CreateCheckBox("Enable normalization");
            _normalizeModeComboBox = CreateComboBox("Mode", "Peak", "LUFS");
            _normalizePeakNumberBox = CreateNumberBox("Target peak (dB)", null, -1);
            _normalizeLufsNumberBox = CreateNumberBox("Target LUFS", null, -16);
            _normalizeLimiterCheckBox = CreateCheckBox("Use limiter", true);
            _normalizeClipCheckBox = CreateCheckBox("Prevent clipping", true);
            normalizeCard.Children.Add(_enableNormalizeCheckBox);
            normalizeCard.Children.Add(_normalizeModeComboBox);
            normalizeCard.Children.Add(_normalizePeakNumberBox);
            normalizeCard.Children.Add(_normalizeLufsNumberBox);
            normalizeCard.Children.Add(_normalizeLimiterCheckBox);
            normalizeCard.Children.Add(_normalizeClipCheckBox);
            _adjustNormalizeCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = normalizeCard };
            AdjustPanel.Children.Add(_adjustNormalizeCardBorder);

            var eqCard = CreateCard("Simple EQ");
            _enableEqCheckBox = CreateCheckBox("Enable EQ");
            _eqPresetComboBox = CreateComboBox("Preset", "None", "PodcastVoice", "VoiceClarity", "WarmVoice", "BrightVoice", "ReduceBass", "ReduceTreble", "PhoneVoice", "RadioVoice", "RemoveElectricalHum50Hz", "RemoveElectricalHum60Hz", "Custom");
            _eqPreventClipCheckBox = CreateCheckBox("Prevent clipping", true);
            _customEqBandsPanel = new StackPanel { Spacing = 8 };
            _addEqBandButton = new Button { Content = "Add EQ band", HorizontalAlignment = HorizontalAlignment.Left };
            _addEqBandButton.Click += AddEqBandButton_Click;
            _eqPresetComboBox.SelectionChanged += OnControlChanged;
            eqCard.Children.Add(_enableEqCheckBox);
            eqCard.Children.Add(_eqPresetComboBox);
            eqCard.Children.Add(_eqPreventClipCheckBox);
            eqCard.Children.Add(_customEqBandsPanel);
            eqCard.Children.Add(_addEqBandButton);
            _adjustEqCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = eqCard };
            AdjustPanel.Children.Add(_adjustEqCardBorder);
            AddEqBand(120, 0, 1);
            AddEqBand(1000, 0, 1);
            AddEqBand(5000, 0, 1);

            var denoiseCard = CreateCard("Denoise");
            _enableDenoiseCheckBox = CreateCheckBox("Enable denoise");
            _denoiseModeComboBox = CreateComboBox("Mode", "Mono", "Stereo");
            _denoiseModeComboBox.SelectionChanged += OnControlChanged;
            _denoiseStrengthTextBlock = new TextBlock { Text = "Strength: 100%" };
            _denoiseStrengthSlider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, Value = 100 };
            _denoiseStrengthSlider.ValueChanged += DenoiseStrengthSlider_ValueChanged;
            denoiseCard.Children.Add(_enableDenoiseCheckBox);
            denoiseCard.Children.Add(_denoiseModeComboBox);
            denoiseCard.Children.Add(_denoiseStrengthTextBlock);
            denoiseCard.Children.Add(_denoiseStrengthSlider);
            denoiseCard.Children.Add(new TextBlock
            {
                Opacity = 0.72,
                Text = "DTLN denoise. Higher strengths use extra inference passes for stronger cleanup.",
                TextWrapping = TextWrapping.Wrap
            });
            _adjustDenoiseCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = denoiseCard };
            AdjustPanel.Children.Add(_adjustDenoiseCardBorder);

            _adjustValidationTextBlock = CreateValidationTextBlock();
            AdjustPanel.Children.Add(_adjustValidationTextBlock);
        }

        private void BuildTranscriptionPanel()
        {
            TranscriptionPanel.Children.Clear();

            var card = CreateCard("Transcription");
            card.Children.Add(new TextBlock
            {
                Opacity = 0.76,
                Text = "Generate plain text or a plain .srt subtitle file from the loaded audio.",
                TextWrapping = TextWrapping.Wrap
            });

            _downloadTranscriptionFeatureButton = new Button
            {
                Content = "Download transcription feature",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _downloadTranscriptionFeatureButton.Click += DownloadTranscriptionFeatureButton_Click;

            _transcriptionDownloadProgressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Height = 6
            };

            _transcriptionDownloadStatusTextBlock = new TextBlock
            {
                Opacity = 0.76,
                Text = "Transcription feature not downloaded yet.",
                TextWrapping = TextWrapping.Wrap
            };

            _transcriptionOutputTypeComboBox = CreateComboBox("Output type", "Text", "Subtitles");
            _includeTimestampsCheckBox = CreateCheckBox("Include timestamps in text output");

            _generateTranscriptionButton = new Button
            {
                Content = "Generate transcription",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _generateTranscriptionButton.Click += GenerateTranscriptionButton_Click;

            _transcriptionProgressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Height = 6,
                Visibility = Visibility.Collapsed
            };

            _transcriptionEtaTextBlock = new TextBlock
            {
                Opacity = 0.76,
                Text = "ETA calculating...",
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };

            _transcriptionRichEditBox = new RichEditBox
            {
                MinHeight = 180,
                PlaceholderText = "Generated transcription text appears here.",
                IsSpellCheckEnabled = false
            };

            _saveTranscriptionTextButton = new Button
            {
                Content = "Save transcription text",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _saveTranscriptionTextButton.Click += SaveTranscriptionTextButton_Click;

            _transcriptionStatusTextBlock = new TextBlock
            {
                Opacity = 0.76,
                Text = "No transcription generated yet.",
                TextWrapping = TextWrapping.Wrap
            };

            card.Children.Add(_downloadTranscriptionFeatureButton);
            card.Children.Add(_transcriptionDownloadProgressBar);
            card.Children.Add(_transcriptionDownloadStatusTextBlock);
            card.Children.Add(_transcriptionOutputTypeComboBox);
            card.Children.Add(_includeTimestampsCheckBox);
            card.Children.Add(_generateTranscriptionButton);
            card.Children.Add(_transcriptionProgressBar);
            card.Children.Add(_transcriptionEtaTextBlock);
            card.Children.Add(_transcriptionRichEditBox);
            card.Children.Add(_saveTranscriptionTextButton);
            card.Children.Add(_transcriptionStatusTextBlock);

            _transcriptionCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = card };
            TranscriptionPanel.Children.Add(_transcriptionCardBorder);
        }

        private static StackPanel CreateCard(string title)
        {
            var card = new StackPanel { Spacing = 12 };
            card.Children.Add(new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Text = title });
            return card;
        }

        private CheckBox CreateCheckBox(string content, bool isChecked = false)
        {
            var cb = new CheckBox { Content = content, IsChecked = isChecked };
            cb.Checked += OnControlChanged;
            cb.Unchecked += OnControlChanged;
            return cb;
        }

        private ComboBox CreateComboBox(string header, params string[] items)
        {
            var combo = new ComboBox { Header = header };
            foreach (var item in items)
            {
                combo.Items.Add(new ComboBoxItem { Content = item });
            }
            combo.SelectedIndex = 0;
            combo.SelectionChanged += OnControlChanged;
            return combo;
        }

        private NumberBox CreateNumberBox(string header, double? minimum, double initial = double.NaN, double? maximum = null)
        {
            var box = new NumberBox { Header = header, Value = initial, SmallChange = 1 };
            if (minimum.HasValue) box.Minimum = minimum.Value;
            if (maximum.HasValue) box.Maximum = maximum.Value;
            box.ValueChanged += OnNumberChanged;
            return box;
        }

        private static TextBlock CreateValidationTextBlock()
        {
            var brush = TryGetThemeWarningBrush() ?? new SolidColorBrush(Color.FromArgb(255, 255, 99, 71));
            return new TextBlock { Foreground = brush, TextWrapping = TextWrapping.Wrap };
        }

        private static SolidColorBrush? TryGetThemeWarningBrush()
        {
            if (Application.Current?.Resources is null)
            {
                return null;
            }

            if (Application.Current.Resources.TryGetValue("SystemFillColorCautionBrush", out var cautionBrushObj) &&
                cautionBrushObj is SolidColorBrush cautionBrush)
            {
                return cautionBrush;
            }

            if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var criticalBrushObj) &&
                criticalBrushObj is SolidColorBrush criticalBrush)
            {
                return criticalBrush;
            }

            return null;
        }

        private void AddEqBandButton_Click(object sender, RoutedEventArgs e)
        {
            AddEqBand(1000, 0, 1);
            RefreshValidationAndState();
        }

        private void AddEqBand(double frequency, double gain, double width)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var f = CreateNumberBox("Freq (Hz)", 1, frequency);
            var g = CreateNumberBox("Gain (dB)", null, gain);
            var w = CreateNumberBox("Width", 0.01, width);
            var remove = new Button { Content = "Remove", VerticalAlignment = VerticalAlignment.Bottom };
            remove.Click += (_, _) =>
            {
                var match = _eqRows.FirstOrDefault(x => x.Root == row);
                if (match is not null)
                {
                    _eqRows.Remove(match);
                    _customEqBandsPanel.Children.Remove(row);
                    RefreshValidationAndState();
                }
            };
            Grid.SetColumn(f, 0);
            Grid.SetColumn(g, 1);
            Grid.SetColumn(w, 2);
            Grid.SetColumn(remove, 3);
            row.Children.Add(f);
            row.Children.Add(g);
            row.Children.Add(w);
            row.Children.Add(remove);
            _customEqBandsPanel.Children.Add(row);
            _eqRows.Add(new EqBandRow(f, g, w, row));
        }

        public void ApplyOptionSelection(string optionTag)
        {
            if (!TryParseNavigationTag(optionTag, out var section, out var subgroup))
            {
                return;
            }

            SelectedOptionHeaderTextBlock.Text = section;
            FileInfoPanel.Visibility = string.IsNullOrEmpty(section) ? Visibility.Visible : Visibility.Collapsed;
            MediaPanel.Visibility = section == "Media" ? Visibility.Visible : Visibility.Collapsed;
            TransformPanel.Visibility = section == "Transform" ? Visibility.Visible : Visibility.Collapsed;
            AdjustPanel.Visibility = section == "Adjust" ? Visibility.Visible : Visibility.Collapsed;
            TranscriptionPanel.Visibility = section == "Transcription" ? Visibility.Visible : Visibility.Collapsed;

            _mediaFormatCardBorder.Visibility = section == "Media" && subgroup == "Format" ? Visibility.Visible : Visibility.Collapsed;
            _mediaMetadataCardBorder.Visibility = section == "Media" && subgroup == "Metadata" ? Visibility.Visible : Visibility.Collapsed;
            _mediaPodcastCardBorder.Visibility = section == "Media" && subgroup == "Podcast" ? Visibility.Visible : Visibility.Collapsed;

            _transformTrimCardBorder.Visibility = section == "Transform" && subgroup == "Trim" ? Visibility.Visible : Visibility.Collapsed;
            _transformSilenceCardBorder.Visibility = section == "Transform" && subgroup == "Silence" ? Visibility.Visible : Visibility.Collapsed;

            _adjustNormalizeCardBorder.Visibility = section == "Adjust" && subgroup == "Normalize" ? Visibility.Visible : Visibility.Collapsed;
            _adjustEqCardBorder.Visibility = section == "Adjust" && subgroup == "EQ" ? Visibility.Visible : Visibility.Collapsed;
            _adjustDenoiseCardBorder.Visibility = section == "Adjust" && subgroup == "Denoise" ? Visibility.Visible : Visibility.Collapsed;
            _transcriptionCardBorder.Visibility = section == "Transcription" && subgroup == "Generate" ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool TryParseNavigationTag(string tag, out string section, out string subgroup)
        {
            var parts = tag.Split(':', 2, StringSplitOptions.TrimEntries);
            section = parts.Length > 0 ? parts[0] : string.Empty;
            subgroup = parts.Length > 1 ? parts[1] : string.Empty;
            return !string.IsNullOrWhiteSpace(section);
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
            if (_sourceAudioFile is not null) return;
            await PickAudioAsync();
        }

        private void UploadSurface_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop audio to load";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void UploadSurface_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<StorageFile>().FirstOrDefault(IsSupportedAudioFile);
            if (file is not null) await LoadAudioAsync(file);
        }

        private async Task PickAudioAsync()
        {
            if (App.MainWindow is null) return;
            var picker = new FileOpenPicker();
            foreach (var ext in SupportedAudioExtensions) picker.FileTypeFilter.Add(ext);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) await LoadAudioAsync(file);
        }

        private async Task LoadAudioAsync(StorageFile file)
        {
            _sourceAudioFile = file;
            _generatedTranscriptionPath = null;
            DropHintPanel.Visibility = Visibility.Collapsed;
            LoadedAudioInfoTextBlock.Text = $"Loaded: {file.Name}";
            AudioPlayer.SetMediaPlayer(_audioPlayer);
            _audioPlayer.Source = MediaSource.CreateFromStorageFile(file);
            AudioPlayer.Visibility = Visibility.Visible;
            var basicProps = await file.GetBasicPropertiesAsync();
            _sourceAudioFileSizeBytes = (long)basicProps.Size;
            await UpdatePreviewInfoAsync(file.Path);
            UpdateFileInfoPanel();
            EnsureTrimRangeInitialized();
            UpdateTrimUiState();
            RefreshValidationAndState();
        }

        private void UpdateFileInfoPanel()
        {
            if (_sourceAudioFile is null)
            {
                FileInfoPanel.Visibility = Visibility.Collapsed;
                return;
            }

            FileInfoNameTextBlock.Text = _sourceAudioFile.Name;
            FileInfoDurationTextBlock.Text = _audioDuration.HasValue ? FormatDuration(_audioDuration.Value) : "—";
            FileInfoFormatTextBlock.Text = _sourceAudioFile.FileType.TrimStart('.').ToUpperInvariant();
            FileInfoSizeTextBlock.Text = _sourceAudioFileSizeBytes.HasValue ? FormatFileSize(_sourceAudioFileSizeBytes.Value) : "—";
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

        private async Task UpdatePreviewInfoAsync(string path)
        {
            try
            {
                var probe = await _videoAudioDenoiseService.ProbeAudioAsync(path);
                _audioDuration = probe.Duration;
                PreviewCodecTextBlock.Text = $"Codec: {probe.CodecName ?? "Unknown"}";
                PreviewRateTextBlock.Text = $"Sample rate: {probe.SampleRate} Hz";
                PreviewChannelsTextBlock.Text = $"Channels: {probe.Channels}";
                PreviewDurationTextBlock.Text = $"Duration: {probe.Duration?.ToString() ?? "Unknown"}";
            }
            catch
            {
                _audioDuration = null;
                PreviewCodecTextBlock.Text = "Codec: Unknown";
                PreviewRateTextBlock.Text = "Sample rate: Unknown";
                PreviewChannelsTextBlock.Text = "Channels: Unknown";
                PreviewDurationTextBlock.Text = "Duration: Unknown";
            }
        }

        private void OnControlChanged(object sender, object e) => RefreshValidationAndState();
        private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => RefreshValidationAndState();

        private void DenoiseStrengthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var strength = Math.Clamp((int)Math.Round(_denoiseStrengthSlider.Value), 0, 100);
            var passes = GetDenoisePasses(strength);
            _denoiseStrengthTextBlock.Text = passes > 1 ? $"Strength: {strength}% ({passes} DTLN passes)" : $"Strength: {strength}%";
            RefreshValidationAndState();
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing || _sourceAudioFile is null) return;
            var errors = ValidateOptions();
            if (errors.Count > 0)
            {
                RefreshValidationAndState(errors);
                return;
            }

            var outputPath = await PickOutputPathAsync(_sourceAudioFile);
            if (string.IsNullOrWhiteSpace(outputPath)) return;

            _isProcessing = true;
            _processingCancellation = new CancellationTokenSource();
            RefreshValidationAndState();
            SetProcessingUi(true, "Preparing pipeline...", "ETA calculating...", "Preparing...", 0);

            var tempFiles = new List<string>();
            var warnings = new List<string>();
            try
            {
                var steps = BuildPipelineSteps();
                var currentInput = _sourceAudioFile.Path;
                var finalOutput = Path.GetFullPath(outputPath);

                for (var i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    var stepOutput = i == steps.Count - 1 ? finalOutput : CreateTemporaryAudioPath(finalOutput);
                    if (i < steps.Count - 1) tempFiles.Add(stepOutput);
                    await ExecuteStepAsync(step, currentInput, stepOutput, warnings, _processingCancellation.Token);
                    currentInput = stepOutput;
                }

                await ShowSimpleDialogAsync("Audio processing complete", BuildCompletionMessage(finalOutput, warnings));
                LoadedAudioInfoTextBlock.Text = $"Saved: {Path.GetFileName(finalOutput)}";
                await UpdatePreviewInfoAsync(finalOutput);
            }
            catch (OperationCanceledException)
            {
                await ShowSimpleDialogAsync("Cancelled", "Audio processing was cancelled.");
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync("Processing failed", ex.Message);
            }
            finally
            {
                _isProcessing = false;
                _processingCancellation?.Dispose();
                _processingCancellation = null;
                CleanupTemporaryFiles(tempFiles);
                SetProcessingUi(false, "", "", "", 0);
                RefreshValidationAndState();
            }
        }

        private async void DownloadTranscriptionFeatureButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing || _isInstallingTranscriptionModel || _isGeneratingTranscription)
            {
                return;
            }

            try
            {
                _isInstallingTranscriptionModel = true;
                RefreshValidationAndState();

                var progress = new Progress<AudioTranscriptionInstallProgress>(update =>
                {
                    var fraction = Math.Clamp(update.FractionComplete, 0d, 1d);
                    _transcriptionDownloadProgressBar.IsIndeterminate = false;
                    _transcriptionDownloadProgressBar.Value = fraction;
                    _transcriptionDownloadStatusTextBlock.Text = $"{update.Stage} ({fraction * 100d:0}%)";
                });

                await _audioTranscriptionService.InstallAsync(progress);
                _isTranscriptionModelInstalled = _audioTranscriptionService.IsInstalled();
                RefreshValidationAndState();
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync("Transcription download failed", ex.Message);
            }
            finally
            {
                _isInstallingTranscriptionModel = false;
                RefreshValidationAndState();
            }
        }

        private async void GenerateTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceAudioFile is null || _isProcessing || _isInstallingTranscriptionModel || _isGeneratingTranscription)
            {
                return;
            }

            if (!_audioTranscriptionService.IsInstalled())
            {
                await ShowSimpleDialogAsync("Transcription feature required", "Download transcription feature before generating.");
                return;
            }

            try
            {
                _isGeneratingTranscription = true;
                _transcriptionStatusTextBlock.Text = "Generating transcription...";
                _transcriptionProgressBar.Visibility = Visibility.Visible;
                _transcriptionProgressBar.IsIndeterminate = false;
                _transcriptionProgressBar.Value = 0d;
                _transcriptionEtaTextBlock.Visibility = Visibility.Visible;
                _transcriptionEtaTextBlock.Text = "ETA calculating...";
                RefreshValidationAndState();

                var outputType = GetSelectedText(_transcriptionOutputTypeComboBox);
                var progress = new Progress<AudioTranscriptionProgress>(update =>
                {
                    _transcriptionProgressBar.Visibility = Visibility.Visible;
                    _transcriptionProgressBar.IsIndeterminate = false;
                    _transcriptionProgressBar.Value = Math.Clamp(update.OverallPercent, 0d, 1d);
                    _transcriptionEtaTextBlock.Visibility = Visibility.Visible;
                    _transcriptionEtaTextBlock.Text = update.EstimatedRemainingTime is TimeSpan eta
                        ? $"{update.StageDescription} - ETA {FormatDuration(eta)}"
                        : $"{update.StageDescription} - ETA calculating...";
                });

                if (string.Equals(outputType, "Subtitles", StringComparison.Ordinal))
                {
                    var subtitlePath = Path.Combine(
                        Path.GetTempPath(),
                        "files-tools-audio-transcriptions",
                        $"{Path.GetFileNameWithoutExtension(_sourceAudioFile.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.srt");

                    var directory = Path.GetDirectoryName(subtitlePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    _generatedTranscriptionPath = await _subtitlesService.GenerateSrtAsync(_sourceAudioFile.Path, subtitlePath, progress);
                    var srt = await File.ReadAllTextAsync(_generatedTranscriptionPath);
                    _transcriptionRichEditBox.Document.SetText(TextSetOptions.None, srt);
                    _transcriptionStatusTextBlock.Text = $"Subtitle file ready: {_generatedTranscriptionPath}";
                }
                else
                {
                    string text;
                    if (_includeTimestampsCheckBox.IsChecked ?? false)
                    {
                        text = await _audioTranscriptionService.TranscribeToTimestampedTextAsync(_sourceAudioFile.Path, progress);
                    }
                    else
                    {
                        text = await _audioTranscriptionService.TranscribeToTextAsync(_sourceAudioFile.Path, progress);
                    }

                    _generatedTranscriptionPath = null;
                    _transcriptionRichEditBox.Document.SetText(TextSetOptions.None, text);
                    _transcriptionStatusTextBlock.Text = "Text transcription ready.";
                }
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync("Transcription failed", ex.Message);
            }
            finally
            {
                _isGeneratingTranscription = false;
                _transcriptionProgressBar.Visibility = Visibility.Collapsed;
                _transcriptionProgressBar.IsIndeterminate = false;
                _transcriptionProgressBar.Value = 0d;
                _transcriptionEtaTextBlock.Visibility = Visibility.Collapsed;
                _transcriptionEtaTextBlock.Text = "ETA calculating...";
                RefreshValidationAndState();
            }
        }

        private async void SaveTranscriptionTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is null)
            {
                return;
            }

            _transcriptionRichEditBox.Document.GetText(TextGetOptions.None, out var text);
            text = text?.TrimEnd('\r', '\n') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                await ShowSimpleDialogAsync("Nothing to save", "Generate text transcription first.");
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = _sourceAudioFile is null
                    ? $"transcription_{DateTime.Now:yyyyMMdd_HHmmss}"
                    : $"{Path.GetFileNameWithoutExtension(_sourceAudioFile.Name)}_transcription_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });
            picker.DefaultFileExtension = ".txt";
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await File.WriteAllTextAsync(file.Path, text);
            _transcriptionStatusTextBlock.Text = $"Transcription text saved to: {file.Path}";
        }

        private async Task ExecuteStepAsync(AudioPipelineStep step, string inputPath, string outputPath, List<string> warnings, CancellationToken ct)
        {
            switch (step)
            {
                case AudioPipelineStep.Convert:
                    warnings.AddRange((await _audioProcessingService.ConvertAsync(inputPath, outputPath, BuildConversionOptions(), CreateAudioProgress("Converting audio"), ct)).Warnings);
                    break;
                case AudioPipelineStep.Compress:
                    warnings.AddRange((await _audioProcessingService.CompressAsync(inputPath, outputPath, BuildCompressionOptions(), CreateAudioProgress("Compressing audio"), ct)).Warnings);
                    break;
                case AudioPipelineStep.Normalize:
                    warnings.AddRange((await _audioProcessingService.NormalizeAsync(inputPath, outputPath, BuildNormalizationOptions(), CreateAudioProgress("Normalizing audio"), ct)).Warnings);
                    break;
                case AudioPipelineStep.Trim:
                    warnings.AddRange((await _audioProcessingService.TrimAsync(inputPath, outputPath, BuildTrimOptions(), CreateAudioProgress("Trimming audio"), ct)).Warnings);
                    break;
                case AudioPipelineStep.SilenceTrim:
                    warnings.AddRange((await _audioProcessingService.RemoveSilenceAsync(inputPath, outputPath, BuildSilenceOptions(), CreateAudioProgress("Removing silence"), ct)).Warnings);
                    break;
                case AudioPipelineStep.Eq:
                    warnings.AddRange((await _audioProcessingService.ApplyEqualizerAsync(inputPath, outputPath, BuildEqualizerOptions(), CreateAudioProgress("Applying equalizer"), ct)).Warnings);
                    break;
                case AudioPipelineStep.Podcast:
                    warnings.AddRange((await _audioProcessingService.ProcessPodcastAudioAsync(inputPath, outputPath, await BuildPodcastOptionsAsync(inputPath, warnings, ct), CreateAudioProgress("Processing podcast audio"), ct)).Warnings);
                    break;
                case AudioPipelineStep.Denoise:
                {
                    var options = BuildDenoiseOptions();
                    var probe = await _videoAudioDenoiseService.ProbeAudioAsync(inputPath, cancellationToken: ct);
                    if (options.Mode == AudioDenoiseMode.StrongStereo && probe.Channels != 2)
                    {
                        options = new AudioDenoiseOptions
                        {
                            Mode = AudioDenoiseMode.Mono,
                            DenoiseAmount = options.DenoiseAmount,
                            DenoisePasses = options.DenoisePasses,
                            ModelSampleRate = options.ModelSampleRate,
                            OutputSampleRate = options.OutputSampleRate,
                            NormalizePeak = options.NormalizePeak,
                            PreventClipping = options.PreventClipping,
                            KeepTemporaryFiles = options.KeepTemporaryFiles
                        };
                        warnings.Add("Stereo denoise requested on non-stereo input. Falling back to mono denoise.");
                    }
                    var progress = new Progress<DenoiseProgress>(p =>
                    {
                        var now = _progressUiThrottleStopwatch.ElapsedMilliseconds;
                        var shouldForce = p.Stage == DenoiseProcessingStage.Completed || p.OverallPercent >= 1;
                        if (!shouldForce && now - _lastProgressUiUpdateTick < 120)
                        {
                            return;
                        }

                        _lastProgressUiUpdateTick = now;
                        SetProcessingUi(true, "Denoising audio...", FormatEta(p.EstimatedRemainingTime), p.StageDescription, p.OverallPercent);
                    });
                    warnings.AddRange((await _videoAudioDenoiseService.DenoiseAudioAsync(inputPath, outputPath, options, progress, ct)).Warnings);
                    break;
                }
                case AudioPipelineStep.Metadata:
                    warnings.AddRange((await _audioProcessingService.RemoveMetadataAsync(inputPath, outputPath, CreateAudioProgress("Removing metadata"), ct)).Warnings);
                    break;
            }
        }

        private IProgress<AudioProcessProgress> CreateAudioProgress(string status) => new Progress<AudioProcessProgress>(p =>
        {
            SetProcessingUi(true, status, FormatEta(p.EstimatedRemainingTime), p.StageDescription, p.OverallPercent);
        });

        private static string FormatEta(TimeSpan? eta) => eta.HasValue ? $"ETA {FormatDuration(eta.Value)}" : "ETA calculating...";
        private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");

        private AudioConversionOptions BuildConversionOptions() => new()
        {
            OutputFormat = ParseOutputFormat(_outputFormatComboBox),
            OutputCodec = ParseOptionalCodec(_outputCodecComboBox),
            BitrateKbps = ParseOptionalInt(_bitrateNumberBox),
            SampleRate = ParseOptionalInt(_sampleRateNumberBox),
            Channels = ParseChannels(_channelComboBox),
            PreserveMetadata = true
        };

        private AudioCompressionOptions BuildCompressionOptions() => new()
        {
            Mode = GetSelectedText(_compressionModeComboBox) == "Lossless" ? AudioCompressionMode.Lossless : AudioCompressionMode.Lossy,
            OutputCodec = ParseOptionalCodec(_compressionCodecComboBox),
            TargetBitrateKbps = ParseOptionalInt(_compressionBitrateNumberBox),
            SampleRate = ParseOptionalInt(_compressionSampleRateNumberBox),
            Channels = ParseChannels(_compressionChannelsComboBox),
            PreserveMetadata = true
        };

        private AudioNormalizationOptions BuildNormalizationOptions() => new()
        {
            Mode = GetSelectedText(_normalizeModeComboBox) == "LUFS" ? AudioNormalizationMode.Lufs : AudioNormalizationMode.Peak,
            TargetPeakDb = ParseOptionalDouble(_normalizePeakNumberBox),
            TargetLufs = ParseOptionalDouble(_normalizeLufsNumberBox),
            UseLimiter = _normalizeLimiterCheckBox.IsChecked ?? true,
            PreventClipping = _normalizeClipCheckBox.IsChecked ?? true
        };

        private AudioTrimOptions BuildTrimOptions() => new()
        {
            StartTime = _trimStart,
            EndTime = _trimEnd,
            Duration = null,
            ReEncode = _trimReencodeCheckBox.IsChecked ?? true
        };

        private AudioSilenceRemovalOptions BuildSilenceOptions() => new()
        {
            Mode = ParseSilenceMode(_silenceModeComboBox),
            SilenceThresholdDb = ParseNumberValue(_silenceThresholdNumberBox, -40),
            MinimumSilenceDuration = TimeSpan.FromMilliseconds(ParseNumberValue(_silenceDurationMsNumberBox, 500)),
            ReEncode = true
        };

        private AudioEqualizerOptions BuildEqualizerOptions()
        {
            var preset = ParseEqPreset(_eqPresetComboBox);
            var bands = preset == EqualizerPreset.Custom
                ? _eqRows.Select(row => new EqualizerBand
                {
                    FrequencyHz = ParseNumberValue(row.Frequency, 1000),
                    GainDb = ParseNumberValue(row.Gain, 0),
                    Width = ParseNumberValue(row.Width, 1)
                }).ToList()
                : [];
            return new AudioEqualizerOptions { Preset = preset, CustomBands = bands, PreventClipping = _eqPreventClipCheckBox.IsChecked ?? true };
        }

        private async Task<AudioPodcastProcessingOptions> BuildPodcastOptionsAsync(string inputPath, List<string> warnings, CancellationToken cancellationToken)
        {
            var enableDenoise = _enableDenoiseCheckBox?.IsChecked ?? false;
            var denoiseMode = GetSelectedDenoiseMode();
            if (enableDenoise && denoiseMode == AudioDenoiseMode.StrongStereo)
            {
                var probe = await _videoAudioDenoiseService.ProbeAudioAsync(inputPath, cancellationToken: cancellationToken);
                if (probe.Channels != 2)
                {
                    denoiseMode = AudioDenoiseMode.Mono;
                    warnings.Add("Stereo denoise requested on non-stereo input. Podcast processing used mono DTLN denoise.");
                }
            }

            var denoiseAmount = GetDenoiseStrength();
            return new AudioPodcastProcessingOptions
            {
                EnableDtlnDenoise = enableDenoise,
                DtlnDenoiseMode = denoiseMode,
                DtlnDenoiseAmount = denoiseAmount,
                DtlnDenoisePasses = GetDenoisePasses(denoiseAmount),
                HighPassFrequencyHz = 80,
                EnableDeEsser = true,
                EnableCompressor = true,
                TargetLufs = -16,
                LimiterLimit = 0.97,
                PreserveMetadata = true
            };
        }

        private AudioDenoiseOptions BuildDenoiseOptions()
        {
            var strength = GetDenoiseStrength();
            return new AudioDenoiseOptions
            {
                Mode = GetSelectedDenoiseMode(),
                DenoiseAmount = strength,
                DenoisePasses = GetDenoisePasses(strength),
                NormalizePeak = true,
                PreventClipping = true
            };
        }

        private List<AudioPipelineStep> BuildPipelineSteps()
        {
            var steps = new List<AudioPipelineStep>();
            steps.Add(AudioPipelineStep.Convert);
            if (_enableCompressionCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Compress);
            if (_enableNormalizeCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Normalize);
            if (_enableTrimCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Trim);
            if (_enableSilenceTrimCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.SilenceTrim);
            if (_enableEqCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Eq);
            if (_enablePodcastModeCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Podcast);
            if ((_enableDenoiseCheckBox?.IsChecked ?? false) && !(_enablePodcastModeCheckBox?.IsChecked ?? false)) steps.Add(AudioPipelineStep.Denoise);
            if (_enableMetadataRemovalCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Metadata);
            return steps;
        }

        private List<string> ValidateOptions()
        {
            var errors = new List<string>();
            if (_sourceAudioFile is null) errors.Add("Media: Load an audio file first.");
            if (BuildPipelineSteps().Count == 0) errors.Add("Media: Enable at least one operation in the pipeline.");

            if ((_enableCompressionCheckBox?.IsChecked ?? false) &&
                GetSelectedText(_compressionModeComboBox) == "Lossless" &&
                ParseOptionalCodec(_compressionCodecComboBox) is string c &&
                !string.Equals(c, "flac", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Media: Lossless compression supports FLAC in v1.");
            }

            if (_enableTrimCheckBox?.IsChecked ?? false)
            {
                if (_audioDuration is null || _audioDuration <= TimeSpan.Zero)
                {
                    errors.Add("Transform: Trim range is not available until audio metadata is loaded.");
                }
                else if (_trimEnd <= _trimStart)
                {
                    errors.Add("Transform: Trim end must be greater than start.");
                }
            }

            if (_enableSilenceTrimCheckBox?.IsChecked ?? false)
            {
                var threshold = ParseNumberValue(_silenceThresholdNumberBox, -40);
                if (threshold < -100 || threshold > 0) errors.Add("Transform: Silence threshold must be between -100 and 0 dB.");
                var minMs = ParseNumberValue(_silenceDurationMsNumberBox, 500);
                if (minMs <= 0) errors.Add("Transform: Minimum silence duration must be greater than 0.");
            }

            if (_enableNormalizeCheckBox?.IsChecked ?? false)
            {
                var peak = ParseOptionalDouble(_normalizePeakNumberBox);
                if (peak is double p && (p < -60 || p > 0)) errors.Add("Adjust: Peak target must be between -60 and 0 dB.");
                var lufs = ParseOptionalDouble(_normalizeLufsNumberBox);
                if (lufs is double l && (l < -70 || l > 0)) errors.Add("Adjust: LUFS target must be between -70 and 0.");
            }

            if ((_enableEqCheckBox?.IsChecked ?? false) && ParseEqPreset(_eqPresetComboBox) == EqualizerPreset.Custom)
            {
                if (_eqRows.Count == 0) errors.Add("Adjust: Custom EQ requires at least one band.");
                foreach (var row in _eqRows)
                {
                    var frequency = ParseNumberValue(row.Frequency, 0);
                    var gain = ParseNumberValue(row.Gain, 0);
                    var width = ParseNumberValue(row.Width, 0);
                    if (frequency <= 0 || width <= 0) { errors.Add("Adjust: EQ frequency and width must be greater than 0."); break; }
                    if (gain < -24 || gain > 24) { errors.Add("Adjust: EQ gain must be between -24 and 24 dB."); break; }
                }
            }

            return errors;
        }

        private void RefreshValidationAndState(List<string>? forcedErrors = null)
        {
            if (ApplyButton is null ||
                _mediaValidationTextBlock is null ||
                _transformValidationTextBlock is null ||
                _adjustValidationTextBlock is null ||
                _customEqBandsPanel is null ||
                _addEqBandButton is null ||
                _eqPresetComboBox is null ||
                _transcriptionOutputTypeComboBox is null ||
                _includeTimestampsCheckBox is null ||
                _saveTranscriptionTextButton is null ||
                _transcriptionDownloadProgressBar is null ||
                _transcriptionDownloadStatusTextBlock is null ||
                _transcriptionProgressBar is null ||
                _transcriptionEtaTextBlock is null ||
                _transcriptionStatusTextBlock is null ||
                _downloadTranscriptionFeatureButton is null ||
                _generateTranscriptionButton is null)
            {
                return;
            }

            var errors = forcedErrors ?? ValidateOptions();
            _mediaValidationTextBlock.Text = string.Join("\n", errors.Where(e => e.StartsWith("Media:")).Select(e => e[6..].Trim()));
            _transformValidationTextBlock.Text = string.Join("\n", errors.Where(e => e.StartsWith("Transform:")).Select(e => e[10..].Trim()));
            _adjustValidationTextBlock.Text = string.Join("\n", errors.Where(e => e.StartsWith("Adjust:")).Select(e => e[7..].Trim()));
            _isTranscriptionModelInstalled = _audioTranscriptionService.IsInstalled();
            ApplyButton.IsEnabled = !_isProcessing && !_isGeneratingTranscription && !_isInstallingTranscriptionModel && _sourceAudioFile is not null && errors.Count == 0;
            _customEqBandsPanel.Visibility = GetSelectedText(_eqPresetComboBox) == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            _addEqBandButton.Visibility = _customEqBandsPanel.Visibility;
            UpdateOptionUiState();
            UpdateTrimUiState();
        }

        private void UpdateOptionUiState()
        {
            SetDependentOptionsState(_outputFormatComboBox, true);
            SetDependentOptionsState(_outputCodecComboBox, true);
            SetDependentOptionsState(_bitrateNumberBox, true);
            SetDependentOptionsState(_sampleRateNumberBox, true);
            SetDependentOptionsState(_channelComboBox, true);

            SetDependentOptionsState(_compressionModeComboBox, _enableCompressionCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_compressionCodecComboBox, _enableCompressionCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_compressionBitrateNumberBox, _enableCompressionCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_compressionSampleRateNumberBox, _enableCompressionCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_compressionChannelsComboBox, _enableCompressionCheckBox?.IsChecked ?? false);

            SetDependentOptionsState(_podcastProfileNameTextBlock, _enablePodcastModeCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_podcastPresetDescriptionTextBlock, _enablePodcastModeCheckBox?.IsChecked ?? false);

            SetDependentOptionsState(_trimReencodeCheckBox, _enableTrimCheckBox?.IsChecked ?? false);

            SetDependentOptionsState(_silenceModeComboBox, _enableSilenceTrimCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_silenceThresholdNumberBox, _enableSilenceTrimCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_silenceDurationMsNumberBox, _enableSilenceTrimCheckBox?.IsChecked ?? false);

            SetDependentOptionsState(_normalizeModeComboBox, _enableNormalizeCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_normalizePeakNumberBox, _enableNormalizeCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_normalizeLufsNumberBox, _enableNormalizeCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_normalizeLimiterCheckBox, _enableNormalizeCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_normalizeClipCheckBox, _enableNormalizeCheckBox?.IsChecked ?? false);

            SetDependentOptionsState(_eqPresetComboBox, _enableEqCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_eqPreventClipCheckBox, _enableEqCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_customEqBandsPanel, _enableEqCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_addEqBandButton, _enableEqCheckBox?.IsChecked ?? false);

            SetDependentOptionsState(_denoiseModeComboBox, _enableDenoiseCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_denoiseStrengthTextBlock, _enableDenoiseCheckBox?.IsChecked ?? false);
            SetDependentOptionsState(_denoiseStrengthSlider, _enableDenoiseCheckBox?.IsChecked ?? false);

            var isBusy = _isProcessing || _isInstallingTranscriptionModel || _isGeneratingTranscription;
            _downloadTranscriptionFeatureButton.IsEnabled = !isBusy;
            _generateTranscriptionButton.IsEnabled = !isBusy && _sourceAudioFile is not null && _isTranscriptionModelInstalled;

            var showDownloadUi = !_isTranscriptionModelInstalled;
            _downloadTranscriptionFeatureButton.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;
            _transcriptionDownloadProgressBar.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;
            _transcriptionDownloadStatusTextBlock.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;

            var outputType = GetSelectedText(_transcriptionOutputTypeComboBox);
            var isSubtitleOutput = string.Equals(outputType, "Subtitles", StringComparison.Ordinal);
            _includeTimestampsCheckBox.IsEnabled = !isBusy && !isSubtitleOutput;
            _saveTranscriptionTextButton.IsEnabled = !isBusy && !isSubtitleOutput;

            if (_isInstallingTranscriptionModel)
            {
                _transcriptionDownloadProgressBar.IsIndeterminate = false;
            }
            else if (_isTranscriptionModelInstalled)
            {
                _transcriptionDownloadProgressBar.IsIndeterminate = false;
                _transcriptionDownloadProgressBar.Value = 1d;
                _transcriptionDownloadStatusTextBlock.Text = "Transcription feature downloaded.";
            }
            else
            {
                _transcriptionDownloadProgressBar.IsIndeterminate = false;
                _transcriptionDownloadProgressBar.Value = 0d;
                _transcriptionDownloadStatusTextBlock.Text = "Transcription feature not downloaded yet.";
            }

            if (_isGeneratingTranscription)
            {
                _transcriptionStatusTextBlock.Text = "Generating transcription...";
            }
        }

        private static void SetDependentOptionsState(UIElement? element, bool isEnabled)
        {
            if (element is null)
            {
                return;
            }

            if (element is Control control)
            {
                control.IsEnabled = isEnabled;
            }

            element.IsHitTestVisible = isEnabled;
            element.Opacity = isEnabled ? 1d : 0.5d;
        }

        private void SetProcessingUi(bool visible, string status, string eta, string detail, double progress)
        {
            ProcessingStatusPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ProcessingStatusTextBlock.Text = status;
            ProcessingEtaTextBlock.Text = eta;
            ProcessingDetailTextBlock.Text = detail;
            ProcessingProgressBar.Value = Math.Clamp(progress, 0, 1);
        }

        private void TrimTimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTrimTimelineVisuals();
        }

        private void TrimHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tag)
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
            if (_activeTrimHandle == TrimDragHandle.None || _audioDuration is null || TrimTimelineCanvas is null)
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
                _trimEnd = ClampTime(targetTime, _trimStart + minimumGap, _audioDuration.Value);
            }

            UpdateTrimTimelineVisuals();
            SeekPreviewToTrimHandle(_activeTrimHandle);
        }

        private void TrimTimelineCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            TrimTimelineCanvas?.ReleasePointerCaptures();
            _activeTrimHandle = TrimDragHandle.None;
            RefreshValidationAndState();
        }

        private void EnsureTrimRangeInitialized()
        {
            if (_audioDuration is null || _audioDuration <= TimeSpan.Zero)
            {
                _trimStart = TimeSpan.Zero;
                _trimEnd = TimeSpan.Zero;
                return;
            }

            if (_trimEnd <= _trimStart || _trimEnd > _audioDuration.Value)
            {
                _trimStart = TimeSpan.Zero;
                _trimEnd = _audioDuration.Value;
            }
        }

        private void UpdateTrimUiState()
        {
            if (TrimTimelinePanel is null)
            {
                return;
            }

            var showTrim = _sourceAudioFile is not null && (_enableTrimCheckBox?.IsChecked ?? false) && _audioDuration is not null;
            TrimTimelinePanel.Visibility = showTrim ? Visibility.Visible : Visibility.Collapsed;
            if (showTrim)
            {
                EnsureTrimRangeInitialized();
                UpdateTrimTimelineVisuals();
            }
        }

        private void UpdateTrimTimelineVisuals()
        {
            if (TrimTimelineCanvas is null ||
                TrimTimelineRail is null ||
                TrimSelectedRange is null ||
                TrimStartHandle is null ||
                TrimEndHandle is null ||
                TrimStartTimeTextBlock is null ||
                TrimEndTimeTextBlock is null ||
                TrimSelectionInfoTextBlock is null ||
                _audioDuration is null ||
                _audioDuration <= TimeSpan.Zero)
            {
                return;
            }

            var width = TrimTimelineCanvas.ActualWidth;
            var trackWidth = Math.Max(0d, width - TrimHandleWidth);

            var startX = TimelineTimeToPosition(_trimStart, trackWidth);
            var endX = TimelineTimeToPosition(_trimEnd, trackWidth);

            Canvas.SetLeft(TrimTimelineRail, TrimHandleWidth / 2);
            Canvas.SetTop(TrimTimelineRail, TrimRailTop);
            TrimTimelineRail.Width = trackWidth;

            Canvas.SetLeft(TrimSelectedRange, startX + TrimHandleWidth / 2);
            Canvas.SetTop(TrimSelectedRange, TrimRailTop);
            TrimSelectedRange.Width = Math.Max(0, endX - startX);

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
            if (_audioDuration is null || _audioDuration <= TimeSpan.Zero)
            {
                return 0;
            }

            var normalized = Math.Clamp(value.TotalMilliseconds / _audioDuration.Value.TotalMilliseconds, 0d, 1d);
            return normalized * trackWidth;
        }

        private TimeSpan PositionToTimelineTime(double x)
        {
            if (_audioDuration is null || TrimTimelineCanvas is null)
            {
                return TimeSpan.Zero;
            }

            var trackWidth = Math.Max(1d, TrimTimelineCanvas.ActualWidth - TrimHandleWidth);
            var normalized = Math.Clamp(x - (TrimHandleWidth / 2), 0d, trackWidth) / trackWidth;
            return TimeSpan.FromMilliseconds(_audioDuration.Value.TotalMilliseconds * normalized);
        }

        private static TimeSpan ClampTime(TimeSpan value, TimeSpan min, TimeSpan max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private void SeekPreviewToTrimHandle(TrimDragHandle handle)
        {
            if (_audioPlayer.PlaybackSession is null)
            {
                return;
            }

            _audioPlayer.PlaybackSession.Position = handle == TrimDragHandle.Start ? _trimStart : _trimEnd;
        }

        private static string FormatTimelineTime(TimeSpan value)
        {
            if (value.TotalHours >= 1)
            {
                return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            }

            return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }

        private static string? ParseOptionalCodec(ComboBox combo)
        {
            var selected = GetSelectedText(combo);
            return string.Equals(selected, "Auto", StringComparison.OrdinalIgnoreCase) ? null : selected;
        }

        private static string? ParseOutputFormat(ComboBox combo)
        {
            return GetSelectedText(combo) switch
            {
                "MP3" => "mp3",
                "AAC" => "adts",
                "M4A" => "ipod",
                "WAV" => "wav",
                "FLAC" => "flac",
                "OPUS" => "opus",
                "OGG" => "ogg",
                _ => null
            };
        }

        private static int? ParseChannels(ComboBox combo) => GetSelectedText(combo) switch
        {
            "Mono" => 1,
            "Stereo" => 2,
            _ => null
        };

        private static SilenceRemovalMode ParseSilenceMode(ComboBox combo) => GetSelectedText(combo) switch
        {
            "Leading" => SilenceRemovalMode.Leading,
            "Trailing" => SilenceRemovalMode.Trailing,
            _ => SilenceRemovalMode.LeadingAndTrailing
        };

        private static EqualizerPreset ParseEqPreset(ComboBox combo) => GetSelectedText(combo) switch
        {
            "PodcastVoice" => EqualizerPreset.PodcastVoice,
            "VoiceClarity" => EqualizerPreset.VoiceClarity,
            "WarmVoice" => EqualizerPreset.WarmVoice,
            "BrightVoice" => EqualizerPreset.BrightVoice,
            "ReduceBass" => EqualizerPreset.ReduceBass,
            "ReduceTreble" => EqualizerPreset.ReduceTreble,
            "PhoneVoice" => EqualizerPreset.PhoneVoice,
            "RadioVoice" => EqualizerPreset.RadioVoice,
            "RemoveElectricalHum50Hz" => EqualizerPreset.RemoveElectricalHum50Hz,
            "RemoveElectricalHum60Hz" => EqualizerPreset.RemoveElectricalHum60Hz,
            "Custom" => EqualizerPreset.Custom,
            _ => EqualizerPreset.None
        };

        private AudioDenoiseMode GetSelectedDenoiseMode()
        {
            return GetSelectedText(_denoiseModeComboBox) == "Stereo" ? AudioDenoiseMode.StrongStereo : AudioDenoiseMode.Mono;
        }

        private int GetDenoiseStrength()
        {
            return Math.Clamp((int)Math.Round(_denoiseStrengthSlider.Value), 0, 100);
        }

        private static int GetDenoisePasses(int strength)
        {
            return strength >= 95 ? 3 : strength >= 75 ? 2 : 1;
        }

        private static string GetSelectedText(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        private static int? ParseOptionalInt(NumberBox box) => double.IsNaN(box.Value) ? null : (int)Math.Round(box.Value);
        private static double? ParseOptionalDouble(NumberBox box) => double.IsNaN(box.Value) ? null : box.Value;
        private static double ParseNumberValue(NumberBox box, double fallback) => double.IsNaN(box.Value) ? fallback : box.Value;

        private async Task<string?> PickOutputPathAsync(StorageFile sourceFile)
        {
            if (App.MainWindow is null) return null;
            var extension = Path.GetExtension(sourceFile.Name);
            var selectedFormat = GetSelectedText(_outputFormatComboBox);
            if (!string.Equals(selectedFormat, "Keep original", StringComparison.OrdinalIgnoreCase))
            {
                extension = selectedFormat.ToLowerInvariant() switch
                {
                    "opus" => ".opus",
                    "ogg" => ".ogg",
                    _ => "." + selectedFormat.ToLowerInvariant()
                };
            }
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                SuggestedFileName = $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}_processed_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            picker.FileTypeChoices.Add("Audio", new List<string> { extension });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }

        private static string CreateTemporaryAudioPath(string finalOutputPath)
        {
            var extension = Path.GetExtension(finalOutputPath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".wav";
            var tempDirectory = Path.Combine(Path.GetTempPath(), "files-tools-audio-stage");
            Directory.CreateDirectory(tempDirectory);
            return Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + extension);
        }

        private static void CleanupTemporaryFiles(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch
                {
                }
            }
        }

        private static string BuildCompletionMessage(string outputPath, IReadOnlyCollection<string> warnings)
        {
            return warnings.Count == 0
                ? $"Audio saved to:\n{outputPath}"
                : $"Audio saved to:\n{outputPath}\n\nWarnings:\n- {string.Join("\n- ", warnings)}";
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
    }
}
