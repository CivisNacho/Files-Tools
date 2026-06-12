using Files_Tools.Helpers;
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

        private static readonly string[] SupportedAudioExtensions = [".mp3", ".aac", ".m4a", ".wav", ".flac", ".opus", ".ogg"];

        private readonly IAudioService _AudioService;
        private readonly IAudioDenoiseService _audioDenoiseService;
        private readonly ITranscriptionService _TranscriptionService;
        private readonly ISubtitleService _SubtitleService;
        private readonly VoiceStudioService _voiceStudioService = new();

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

        // Position within the multi-step processing pipeline, so progress readouts show the
        // percentage completed of the WHOLE pipeline rather than the current step only.
        private int _pipelineStepIndex;
        private int _pipelineStepCount;

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
        private CheckBox _podcastDenoiseCheckBox = null!;
        private CheckBox _podcastFullnessCheckBox = null!;
        private CheckBox _podcastMasterCheckBox = null!;

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
            _audioDenoiseService = new AudioDenoiseService();
            _AudioService = new AudioService(_audioDenoiseService);
            _TranscriptionService = new TranscriptionService();
            _SubtitleService = new SubtitleService(_TranscriptionService);
            InitializeComponent();
            BuildOptionUi();
            _isTranscriptionModelInstalled = _TranscriptionService.IsInstalled();
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
            var formatCard = CreateCard(Strings.Get("AudioPage_FormatCard"));
            _outputFormatComboBox = CreateComboBox(Strings.Get("AudioPage_OutputFormat_Header"), Strings.Get("AudioPage_KeepOriginal"), "MP3", "AAC", "M4A", "WAV", "FLAC", "OPUS", "OGG");
            _outputCodecComboBox = CreateComboBox(Strings.Get("AudioPage_Codec_Header"), Strings.Get("AudioPage_Auto"), "libmp3lame", "aac", "flac", "pcm_s16le", "libopus", "libvorbis");
            _bitrateNumberBox = CreateNumberBox(Strings.Get("AudioPage_Bitrate_Header"), 1);
            _sampleRateNumberBox = CreateNumberBox(Strings.Get("AudioPage_SampleRateHz_Header"), 8000);
            _channelComboBox = CreateComboBox(Strings.Get("AudioPage_Channels_Header"), Strings.Get("AudioPage_KeepSource"), Strings.Get("AudioPage_Mono"), Strings.Get("AudioPage_Stereo"));

            _enableCompressionCheckBox = CreateCheckBox(Strings.Get("AudioPage_EnableCompression"));
            _compressionModeComboBox = CreateComboBox(Strings.Get("AudioPage_CompressionMode_Header"), Strings.Get("AudioPage_Lossy"), Strings.Get("AudioPage_Lossless"));
            _compressionCodecComboBox = CreateComboBox(Strings.Get("AudioPage_CompressionCodec_Header"), Strings.Get("AudioPage_Auto"), "libmp3lame", "aac", "flac", "libopus", "libvorbis");
            _compressionBitrateNumberBox = CreateNumberBox(Strings.Get("AudioPage_TargetBitrate_Header"), 1);
            _compressionSampleRateNumberBox = CreateNumberBox(Strings.Get("AudioPage_TargetSampleRate_Header"), 8000);
            _compressionChannelsComboBox = CreateComboBox(Strings.Get("AudioPage_Channels_Header"), Strings.Get("AudioPage_KeepSource"), Strings.Get("AudioPage_Mono"), Strings.Get("AudioPage_Stereo"));

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

            var metadataCard = CreateCard(Strings.Get("AudioPage_MetadataCard"));
            _enableMetadataRemovalCheckBox = CreateCheckBox(Strings.Get("AudioPage_RemoveMetadata"));
            metadataCard.Children.Add(_enableMetadataRemovalCheckBox);
            metadataCard.Children.Add(new TextBlock { Opacity = 0.72, Text = Strings.Get("AudioPage_MetadataHint"), TextWrapping = TextWrapping.Wrap });
            _mediaMetadataCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = metadataCard };
            MediaPanel.Children.Add(_mediaMetadataCardBorder);

            var podcastCard = CreateCard(Strings.Get("AudioPage_PodcastCard"));
            _enablePodcastModeCheckBox = CreateCheckBox(Strings.Get("AudioPage_EnablePodcast"));
            _podcastDenoiseCheckBox = CreateCheckBox(Strings.Get("AudioPage_PodcastDenoise"), true);
            _podcastFullnessCheckBox = CreateCheckBox(Strings.Get("AudioPage_PodcastFullness"), true);
            _podcastMasterCheckBox = CreateCheckBox(Strings.Get("AudioPage_PodcastMaster"), true);
            podcastCard.Children.Add(_enablePodcastModeCheckBox);
            podcastCard.Children.Add(_podcastDenoiseCheckBox);
            podcastCard.Children.Add(_podcastFullnessCheckBox);
            podcastCard.Children.Add(_podcastMasterCheckBox);
            podcastCard.Children.Add(new TextBlock
            {
                Opacity = 0.72,
                Text = Strings.Get("AudioPage_PodcastHint"),
                TextWrapping = TextWrapping.Wrap
            });
            _mediaPodcastCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = podcastCard };
            MediaPanel.Children.Add(_mediaPodcastCardBorder);

            _mediaValidationTextBlock = CreateValidationTextBlock();
            MediaPanel.Children.Add(_mediaValidationTextBlock);
        }

        private void BuildTransformPanel()
        {
            TransformPanel.Children.Clear();
            var trimCard = CreateCard(Strings.Get("AudioPage_TrimCard"));
            _enableTrimCheckBox = CreateCheckBox(Strings.Get("AudioPage_EnableTrim"));
            _trimReencodeCheckBox = CreateCheckBox(Strings.Get("AudioPage_ReEncodeOutput"), true);
            trimCard.Children.Add(_enableTrimCheckBox);
            trimCard.Children.Add(new TextBlock
            {
                Opacity = 0.76,
                Text = Strings.Get("AudioPage_TrimHint"),
                TextWrapping = TextWrapping.Wrap
            });
            trimCard.Children.Add(_trimReencodeCheckBox);
            _transformTrimCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = trimCard };
            TransformPanel.Children.Add(_transformTrimCardBorder);

            var silenceCard = CreateCard(Strings.Get("AudioPage_SilenceTrimCard"));
            _enableSilenceTrimCheckBox = CreateCheckBox(Strings.Get("AudioPage_EnableSilenceTrim"));
            _silenceModeComboBox = CreateComboBox(Strings.Get("AudioPage_Mode_Header"), Strings.Get("AudioPage_Leading"), Strings.Get("AudioPage_Trailing"), Strings.Get("AudioPage_LeadingAndTrailing"));
            _silenceThresholdNumberBox = CreateNumberBox(Strings.Get("AudioPage_Threshold_Header"), null, -40);
            _silenceDurationMsNumberBox = CreateNumberBox(Strings.Get("AudioPage_MinSilence_Header"), 1, 500);
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
            var normalizeCard = CreateCard(Strings.Get("AudioPage_NormalizationCard"));
            _enableNormalizeCheckBox = CreateCheckBox(Strings.Get("AudioPage_EnableNormalization"));
            _normalizeModeComboBox = CreateComboBox(Strings.Get("AudioPage_Mode_Header"), Strings.Get("AudioPage_Peak"), Strings.Get("AudioPage_LUFS"));
            _normalizePeakNumberBox = CreateNumberBox(Strings.Get("AudioPage_TargetPeak_Header"), null, -1);
            _normalizeLufsNumberBox = CreateNumberBox(Strings.Get("AudioPage_TargetLUFS_Header"), null, -16);
            _normalizeLimiterCheckBox = CreateCheckBox(Strings.Get("AudioPage_UseLimiter"), true);
            _normalizeClipCheckBox = CreateCheckBox(Strings.Get("AudioPage_PreventClipping"), true);
            normalizeCard.Children.Add(_enableNormalizeCheckBox);
            normalizeCard.Children.Add(_normalizeModeComboBox);
            normalizeCard.Children.Add(_normalizePeakNumberBox);
            normalizeCard.Children.Add(_normalizeLufsNumberBox);
            normalizeCard.Children.Add(_normalizeLimiterCheckBox);
            normalizeCard.Children.Add(_normalizeClipCheckBox);
            _adjustNormalizeCardBorder = new Border { Style = (Style)Resources["SettingsCardStyle"], Child = normalizeCard };
            AdjustPanel.Children.Add(_adjustNormalizeCardBorder);

            var eqCard = CreateCard(Strings.Get("AudioPage_EqCard"));
            _enableEqCheckBox = CreateCheckBox(Strings.Get("AudioPage_EnableEq"));
            _eqPresetComboBox = CreateComboBox(Strings.Get("AudioPage_Preset_Header"), "None", "PodcastVoice", "VoiceClarity", "WarmVoice", "BrightVoice", "ReduceBass", "ReduceTreble", "PhoneVoice", "RadioVoice", "RemoveElectricalHum50Hz", "RemoveElectricalHum60Hz", "Custom");
            _eqPreventClipCheckBox = CreateCheckBox(Strings.Get("AudioPage_PreventClipping"), true);
            _customEqBandsPanel = new StackPanel { Spacing = 8 };
            _addEqBandButton = new Button { Content = Strings.Get("AudioPage_AddEqBand"), HorizontalAlignment = HorizontalAlignment.Left };
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

            var denoiseCard = CreateCard(Strings.Get("AudioPage_DenoiseCard"));
            _enableDenoiseCheckBox = CreateCheckBox(Strings.Get("AudioPage_EnableDenoise"));
            denoiseCard.Children.Add(_enableDenoiseCheckBox);
            denoiseCard.Children.Add(new TextBlock
            {
                Opacity = 0.72,
                Text = Strings.Get("AudioPage_DenoiseHint"),
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

            var card = CreateCard(Strings.Get("AudioPage_TranscriptionCard"));
            card.Children.Add(new TextBlock
            {
                Opacity = 0.76,
                Text = Strings.Get("AudioPage_TranscriptionHint"),
                TextWrapping = TextWrapping.Wrap
            });

            _downloadTranscriptionFeatureButton = new Button
            {
                Content = Strings.Get("AudioPage_DownloadTranscription"),
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
                Text = Strings.Get("AudioPage_TranscriptionNotDownloaded"),
                TextWrapping = TextWrapping.Wrap
            };

            _transcriptionOutputTypeComboBox = CreateComboBox(Strings.Get("AudioPage_OutputType_Header"), Strings.Get("AudioPage_Text"), Strings.Get("AudioPage_Subtitles"));
            _includeTimestampsCheckBox = CreateCheckBox(Strings.Get("AudioPage_IncludeTimestamps"));

            _generateTranscriptionButton = new Button
            {
                Content = Strings.Get("AudioPage_GenerateTranscription"),
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
                Text = FormatPercent(0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };

            _transcriptionRichEditBox = new RichEditBox
            {
                MinHeight = 180,
                PlaceholderText = Strings.Get("AudioPage_TranscriptionPlaceholder"),
                IsSpellCheckEnabled = false
            };

            _saveTranscriptionTextButton = new Button
            {
                Content = Strings.Get("AudioPage_SaveTranscription"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _saveTranscriptionTextButton.Click += SaveTranscriptionTextButton_Click;

            _transcriptionStatusTextBlock = new TextBlock
            {
                Opacity = 0.76,
                Text = Strings.Get("AudioPage_NoTranscriptionYet"),
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
            var f = CreateNumberBox(Strings.Get("AudioPage_EqFreq_Header"), 1, frequency);
            var g = CreateNumberBox(Strings.Get("AudioPage_EqGain_Header"), null, gain);
            var w = CreateNumberBox(Strings.Get("AudioPage_EqWidth_Header"), 0.01, width);
            var remove = new Button { Content = Strings.Get("AudioPage_Remove"), VerticalAlignment = VerticalAlignment.Bottom };
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
            InitializePickerWithMainWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) await LoadAudioAsync(file);
        }

        private async Task LoadAudioAsync(StorageFile file)
        {
            _sourceAudioFile = file;
            _generatedTranscriptionPath = null;
            DropHintPanel.Visibility = Visibility.Collapsed;
            LoadedAudioInfoTextBlock.Text = string.Format(Strings.Get("AudioPage_LoadedFmt"), file.Name);
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
            var unknown = Strings.Get("AudioPage_Unknown");
            try
            {
                var probe = await _audioDenoiseService.ProbeAudioAsync(path);
                _audioDuration = probe.Duration;
                SetPreviewInfo(probe.CodecName ?? unknown, $"{probe.SampleRate} Hz", probe.Channels.ToString(), probe.Duration?.ToString() ?? unknown);
            }
            catch
            {
                _audioDuration = null;
                SetPreviewInfo(unknown, unknown, unknown, unknown);
            }
        }

        private void SetPreviewInfo(string codec, string rate, string channels, string duration)
        {
            PreviewCodecTextBlock.Text = string.Format(Strings.Get("AudioPage_PreviewCodecFmt"), codec);
            PreviewRateTextBlock.Text = string.Format(Strings.Get("AudioPage_PreviewRateFmt"), rate);
            PreviewChannelsTextBlock.Text = string.Format(Strings.Get("AudioPage_PreviewChannelsFmt"), channels);
            PreviewDurationTextBlock.Text = string.Format(Strings.Get("AudioPage_PreviewDurationFmt"), duration);
        }

        private void OnControlChanged(object sender, object e) => RefreshValidationAndState();
        private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => RefreshValidationAndState();



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
            SetProcessingUi(true, Strings.Get("AudioPage_PreparingPipeline.Text"), FormatPercent(0), Strings.Get("AudioPage_Preparing"), 0);

            var tempFiles = new List<string>();
            var warnings = new List<string>();
            try
            {
                var finalOutput = Path.GetFullPath(outputPath);
                await RunPipelineAsync(finalOutput, tempFiles, warnings, _processingCancellation.Token);
                await ShowSimpleDialogAsync(Strings.Get("AudioPage_ProcessingComplete"), BuildCompletionMessage(finalOutput, warnings));
                LoadedAudioInfoTextBlock.Text = string.Format(Strings.Get("AudioPage_SavedFmt"), Path.GetFileName(finalOutput));
                await UpdatePreviewInfoAsync(finalOutput);
            }
            catch (OperationCanceledException)
            {
                await ShowSimpleDialogAsync(Strings.Get("AudioPage_Cancelled"), Strings.Get("AudioPage_ProcessingCancelled"));
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync(Strings.Get("AudioPage_ProcessingFailed"), ex.Message);
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
                    TaskbarProgressHelper.SetProgress(fraction);
                });

                await _TranscriptionService.InstallAsync(progress);
                _isTranscriptionModelInstalled = _TranscriptionService.IsInstalled();
                RefreshValidationAndState();
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync(Strings.Get("AudioPage_TranscriptionDownloadFailed"), ex.Message);
            }
            finally
            {
                _isInstallingTranscriptionModel = false;
                TaskbarProgressHelper.Clear();
                RefreshValidationAndState();
            }
        }

        private async void GenerateTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sourceAudioFile is null || _isProcessing || _isInstallingTranscriptionModel || _isGeneratingTranscription)
            {
                return;
            }

            if (!_TranscriptionService.IsInstalled())
            {
                await ShowSimpleDialogAsync(Strings.Get("AudioPage_TranscriptionRequired"), Strings.Get("AudioPage_TranscriptionRequiredMessage"));
                return;
            }

            try
            {
                _isGeneratingTranscription = true;
                _transcriptionStatusTextBlock.Text = Strings.Get("AudioPage_GeneratingTranscription");
                _transcriptionProgressBar.Visibility = Visibility.Visible;
                _transcriptionProgressBar.IsIndeterminate = false;
                _transcriptionProgressBar.Value = 0d;
                _transcriptionEtaTextBlock.Visibility = Visibility.Visible;
                _transcriptionEtaTextBlock.Text = FormatPercent(0);
                RefreshValidationAndState();

                var isSubtitles = (_transcriptionOutputTypeComboBox?.SelectedIndex ?? 0) == 1;
                var progress = new Progress<AudioTranscriptionProgress>(update =>
                {
                    _transcriptionProgressBar.Visibility = Visibility.Visible;
                    _transcriptionProgressBar.IsIndeterminate = false;
                    _transcriptionProgressBar.Value = Math.Clamp(update.OverallPercent, 0d, 1d);
                    _transcriptionEtaTextBlock.Visibility = Visibility.Visible;
                    _transcriptionEtaTextBlock.Text = $"{update.StageDescription} - {FormatPercent(update.OverallPercent)}";
                    TaskbarProgressHelper.SetProgress(update.OverallPercent);
                });

                if (isSubtitles)
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

                    _generatedTranscriptionPath = await _SubtitleService.GenerateSrtAsync(_sourceAudioFile.Path, subtitlePath, progress);
                    var srt = await File.ReadAllTextAsync(_generatedTranscriptionPath);
                    _transcriptionRichEditBox.Document.SetText(TextSetOptions.None, srt);
                    _transcriptionStatusTextBlock.Text = string.Format(Strings.Get("AudioPage_SubtitleReadyFmt"), _generatedTranscriptionPath);
                }
                else
                {
                    string text;
                    if (_includeTimestampsCheckBox.IsChecked ?? false)
                    {
                        text = await _TranscriptionService.TranscribeToTimestampedTextAsync(_sourceAudioFile.Path, progress);
                    }
                    else
                    {
                        text = await _TranscriptionService.TranscribeToTextAsync(_sourceAudioFile.Path, progress);
                    }

                    _generatedTranscriptionPath = null;
                    _transcriptionRichEditBox.Document.SetText(TextSetOptions.None, text);
                    _transcriptionStatusTextBlock.Text = Strings.Get("AudioPage_TextTranscriptionReady");
                }
            }
            catch (Exception ex)
            {
                await ShowSimpleDialogAsync(Strings.Get("AudioPage_TranscriptionFailed"), ex.Message);
            }
            finally
            {
                _isGeneratingTranscription = false;
                _transcriptionProgressBar.Visibility = Visibility.Collapsed;
                _transcriptionProgressBar.IsIndeterminate = false;
                _transcriptionProgressBar.Value = 0d;
                _transcriptionEtaTextBlock.Visibility = Visibility.Collapsed;
                _transcriptionEtaTextBlock.Text = FormatPercent(0);
                TaskbarProgressHelper.Clear();
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
                await ShowSimpleDialogAsync(Strings.Get("AudioPage_NothingToSave"), Strings.Get("AudioPage_GenerateFirst"));
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
            InitializePickerWithMainWindow(picker);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await File.WriteAllTextAsync(file.Path, text);
            _transcriptionStatusTextBlock.Text = string.Format(Strings.Get("AudioPage_TranscriptionSavedFmt"), file.Path);
        }

        /// <summary>Runs every enabled pipeline step, chaining each step's output into the next step's input.</summary>
        private async Task RunPipelineAsync(string finalOutput, List<string> tempFiles, List<string> warnings, CancellationToken ct)
        {
            var steps = BuildPipelineSteps();
            var currentInput = _sourceAudioFile!.Path;
            _pipelineStepCount = steps.Count;

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                _pipelineStepIndex = i;
                var isLastStep = i == steps.Count - 1;
                var stepOutput = isLastStep ? finalOutput : CreateTemporaryStepOutputPath(step, finalOutput);
                if (!isLastStep) tempFiles.Add(stepOutput);
                await ExecuteStepAsync(step, currentInput, stepOutput, warnings, ct);
                currentInput = stepOutput;
            }
        }

        private static string CreateTemporaryStepOutputPath(AudioPipelineStep step, string finalOutputPath)
        {
            // VoiceStudio-backed steps always produce WAV; other steps keep the final extension.
            var extension = step is AudioPipelineStep.Denoise or AudioPipelineStep.Podcast
                ? ".wav"
                : Path.GetExtension(finalOutputPath) is { Length: > 0 } ext ? ext : ".wav";
            var tempDirectory = Path.Combine(Path.GetTempPath(), "files-tools-audio-stage");
            Directory.CreateDirectory(tempDirectory);
            return Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + extension);
        }

        private async Task ExecuteStepAsync(AudioPipelineStep step, string inputPath, string outputPath, List<string> warnings, CancellationToken ct)
        {
            switch (step)
            {
                case AudioPipelineStep.Convert:
                    warnings.AddRange((await _AudioService.ConvertAsync(inputPath, outputPath, BuildConversionOptions(), CreateAudioProgress(Strings.Get("AudioPage_ConvertingAudio")), ct)).Warnings);
                    break;
                case AudioPipelineStep.Compress:
                    warnings.AddRange((await _AudioService.CompressAsync(inputPath, outputPath, BuildCompressionOptions(), CreateAudioProgress(Strings.Get("AudioPage_CompressingAudio")), ct)).Warnings);
                    break;
                case AudioPipelineStep.Normalize:
                    warnings.AddRange((await _AudioService.NormalizeAsync(inputPath, outputPath, BuildNormalizationOptions(), CreateAudioProgress(Strings.Get("AudioPage_NormalizingAudio")), ct)).Warnings);
                    break;
                case AudioPipelineStep.Trim:
                    warnings.AddRange((await _AudioService.TrimAsync(inputPath, outputPath, BuildTrimOptions(), CreateAudioProgress(Strings.Get("AudioPage_TrimmingAudio")), ct)).Warnings);
                    break;
                case AudioPipelineStep.SilenceTrim:
                    warnings.AddRange((await _AudioService.RemoveSilenceAsync(inputPath, outputPath, BuildSilenceOptions(), CreateAudioProgress(Strings.Get("AudioPage_RemovingSilence")), ct)).Warnings);
                    break;
                case AudioPipelineStep.Eq:
                    warnings.AddRange((await _AudioService.ApplyEqualizerAsync(inputPath, outputPath, BuildEqualizerOptions(), CreateAudioProgress(Strings.Get("AudioPage_ApplyingEq")), ct)).Warnings);
                    break;
                case AudioPipelineStep.Podcast:
                    await _voiceStudioService.ProcessAudioAsync(
                        inputPath, outputPath, BuildPodcastOptions(),
                        CreateVoiceStudioProgress(Strings.Get("AudioPage_ProcessingPodcast")), ct);
                    break;
                case AudioPipelineStep.Denoise:
                    await _voiceStudioService.ProcessAudioAsync(
                        inputPath, outputPath,
                        new VoiceStudioOptions { Denoise = true, SuperResolution = false, Master = false },
                        CreateVoiceStudioProgress(Strings.Get("AudioPage_DenoisingAudio")), ct);
                    break;
                case AudioPipelineStep.Metadata:
                    warnings.AddRange((await _AudioService.RemoveMetadataAsync(inputPath, outputPath, CreateAudioProgress(Strings.Get("AudioPage_RemovingMetadata")), ct)).Warnings);
                    break;
            }
        }

        private IProgress<AudioProcessProgress> CreateAudioProgress(string status) => new Progress<AudioProcessProgress>(p =>
        {
            var overall = PipelineOverallFraction(p.OverallPercent);
            SetProcessingUi(true, status, FormatPercent(overall), p.StageDescription, overall);
        });

        private IProgress<VoiceStudioProgress> CreateVoiceStudioProgress(string status) => new Progress<VoiceStudioProgress>(p =>
        {
            var now = _progressUiThrottleStopwatch.ElapsedMilliseconds;
            if (p.Stage != VoiceStudioStage.Completed && p.Fraction < 1 && now - _lastProgressUiUpdateTick < 120) return;
            _lastProgressUiUpdateTick = now;
            var detail = p.Stage switch
            {
                VoiceStudioStage.Extracting => Strings.Get("AudioPage_ExtractingAudio"),
                VoiceStudioStage.Denoising => Strings.Get("AudioPage_DenoisingDFN3"),
                VoiceStudioStage.RestoringFullness => Strings.Get("AudioPage_RestoringFullness"),
                VoiceStudioStage.Mastering => Strings.Get("AudioPage_Mastering"),
                _ => Strings.Get("AudioPage_Finalizing")
            };
            var overall = PipelineOverallFraction(p.Fraction);
            SetProcessingUi(true, status, FormatPercent(overall), detail, overall);
        });

        /// <summary>Maps the current step's local fraction onto the whole pipeline's 0..1 span.</summary>
        private double PipelineOverallFraction(double stepFraction) => _pipelineStepCount <= 0
            ? Math.Clamp(stepFraction, 0d, 1d)
            : Math.Clamp((_pipelineStepIndex + Math.Clamp(stepFraction, 0d, 1d)) / _pipelineStepCount, 0d, 1d);

        private static string FormatPercent(double fraction) => $"{Math.Round(Math.Clamp(fraction, 0d, 1d) * 100)}%";
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
            Mode = (_compressionModeComboBox?.SelectedIndex ?? 0) == 1 ? AudioCompressionMode.Lossless : AudioCompressionMode.Lossy,
            OutputCodec = ParseOptionalCodec(_compressionCodecComboBox),
            TargetBitrateKbps = ParseOptionalInt(_compressionBitrateNumberBox),
            SampleRate = ParseOptionalInt(_compressionSampleRateNumberBox),
            Channels = ParseChannels(_compressionChannelsComboBox),
            PreserveMetadata = true
        };

        private AudioNormalizationOptions BuildNormalizationOptions() => new()
        {
            Mode = (_normalizeModeComboBox?.SelectedIndex ?? 0) == 1 ? AudioNormalizationMode.Lufs : AudioNormalizationMode.Peak,
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

        private VoiceStudioOptions BuildPodcastOptions() => new()
        {
            Denoise = _podcastDenoiseCheckBox?.IsChecked ?? true,
            SuperResolution = _podcastFullnessCheckBox?.IsChecked ?? true,
            Master = _podcastMasterCheckBox?.IsChecked ?? true,
        };

        private List<AudioPipelineStep> BuildPipelineSteps()
        {
            var steps = new List<AudioPipelineStep>();
            var podcastEnabled = _enablePodcastModeCheckBox?.IsChecked ?? false;
            if (podcastEnabled) steps.Add(AudioPipelineStep.Podcast);
            else if (_enableDenoiseCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Denoise);
            steps.Add(AudioPipelineStep.Convert);
            if (_enableCompressionCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Compress);
            if (_enableNormalizeCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Normalize);
            if (_enableTrimCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Trim);
            if (_enableSilenceTrimCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.SilenceTrim);
            if (_enableEqCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Eq);
            if (_enableMetadataRemovalCheckBox?.IsChecked ?? false) steps.Add(AudioPipelineStep.Metadata);
            return steps;
        }

        private List<string> ValidateOptions()
        {
            var errors = new List<string>();
            if (_sourceAudioFile is null) errors.Add("Media: Load an audio file first.");
            if (BuildPipelineSteps().Count == 0) errors.Add("Media: Enable at least one operation in the pipeline.");

            if ((_enableCompressionCheckBox?.IsChecked ?? false) &&
                (_compressionModeComboBox?.SelectedIndex ?? 0) == 1 &&
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
                _podcastDenoiseCheckBox is null ||
                _podcastFullnessCheckBox is null ||
                _podcastMasterCheckBox is null ||
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
            _isTranscriptionModelInstalled = _TranscriptionService.IsInstalled();
            ApplyButton.IsEnabled = !_isProcessing && !_isGeneratingTranscription && !_isInstallingTranscriptionModel && _sourceAudioFile is not null && errors.Count == 0;
            _customEqBandsPanel.Visibility = ParseEqPreset(_eqPresetComboBox) == EqualizerPreset.Custom ? Visibility.Visible : Visibility.Collapsed;
            _addEqBandButton.Visibility = _customEqBandsPanel.Visibility;
            UpdateOptionUiState();
            UpdateTrimUiState();
        }

        private void UpdateOptionUiState()
        {
            SetDependentOptionsState(true, _outputFormatComboBox, _outputCodecComboBox, _bitrateNumberBox, _sampleRateNumberBox, _channelComboBox);

            SetDependentOptionsState(_enableCompressionCheckBox?.IsChecked ?? false,
                _compressionModeComboBox, _compressionCodecComboBox, _compressionBitrateNumberBox, _compressionSampleRateNumberBox, _compressionChannelsComboBox);

            SetDependentOptionsState(_enableTrimCheckBox?.IsChecked ?? false, _trimReencodeCheckBox);

            SetDependentOptionsState(_enableSilenceTrimCheckBox?.IsChecked ?? false,
                _silenceModeComboBox, _silenceThresholdNumberBox, _silenceDurationMsNumberBox);

            SetDependentOptionsState(_enableNormalizeCheckBox?.IsChecked ?? false,
                _normalizeModeComboBox, _normalizePeakNumberBox, _normalizeLufsNumberBox, _normalizeLimiterCheckBox, _normalizeClipCheckBox);

            SetDependentOptionsState(_enableEqCheckBox?.IsChecked ?? false,
                _eqPresetComboBox, _eqPreventClipCheckBox, _customEqBandsPanel, _addEqBandButton);

            SetDependentOptionsState(_enablePodcastModeCheckBox?.IsChecked ?? false,
                _podcastDenoiseCheckBox, _podcastFullnessCheckBox, _podcastMasterCheckBox);

            var isBusy = _isProcessing || _isInstallingTranscriptionModel || _isGeneratingTranscription;
            _downloadTranscriptionFeatureButton.IsEnabled = !isBusy;
            _generateTranscriptionButton.IsEnabled = !isBusy && _sourceAudioFile is not null && _isTranscriptionModelInstalled;

            var showDownloadUi = !_isTranscriptionModelInstalled;
            _downloadTranscriptionFeatureButton.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;
            _transcriptionDownloadProgressBar.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;
            _transcriptionDownloadStatusTextBlock.Visibility = showDownloadUi ? Visibility.Visible : Visibility.Collapsed;

            var isSubtitleOutput = (_transcriptionOutputTypeComboBox?.SelectedIndex ?? 0) == 1;
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
                _transcriptionDownloadStatusTextBlock.Text = Strings.Get("AudioPage_TranscriptionDownloaded");
            }
            else
            {
                _transcriptionDownloadProgressBar.IsIndeterminate = false;
                _transcriptionDownloadProgressBar.Value = 0d;
                _transcriptionDownloadStatusTextBlock.Text = Strings.Get("AudioPage_TranscriptionNotDownloaded");
            }

            if (_isGeneratingTranscription)
            {
                _transcriptionStatusTextBlock.Text = Strings.Get("AudioPage_GeneratingTranscription");
            }
        }

        private static void SetDependentOptionsState(bool isEnabled, params UIElement?[] elements)
        {
            foreach (var element in elements)
            {
                if (element is null)
                {
                    continue;
                }

                if (element is Control control)
                {
                    control.IsEnabled = isEnabled;
                }

                element.IsHitTestVisible = isEnabled;
                element.Opacity = isEnabled ? 1d : 0.5d;
            }
        }

        private void SetProcessingUi(bool visible, string status, string eta, string detail, double progress)
        {
            ProcessingStatusPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ProcessingStatusTextBlock.Text = status;
            ProcessingEtaTextBlock.Text = eta;
            ProcessingDetailTextBlock.Text = detail;
            ProcessingProgressBar.Value = Math.Clamp(progress, 0, 1);

            if (!visible)
                TaskbarProgressHelper.Clear();
            else if (progress > 0)
                TaskbarProgressHelper.SetProgress(progress);
            else
                TaskbarProgressHelper.SetIndeterminate();
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
            return combo.SelectedIndex == 0 ? null : GetSelectedText(combo);
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

        private static int? ParseChannels(ComboBox combo) => combo.SelectedIndex switch
        {
            1 => 1,
            2 => 2,
            _ => null
        };

        private static SilenceRemovalMode ParseSilenceMode(ComboBox combo) => combo.SelectedIndex switch
        {
            0 => SilenceRemovalMode.Leading,
            1 => SilenceRemovalMode.Trailing,
            _ => SilenceRemovalMode.LeadingAndTrailing
        };

        private static EqualizerPreset ParseEqPreset(ComboBox combo) => combo.SelectedIndex switch
        {
            1 => EqualizerPreset.PodcastVoice,
            2 => EqualizerPreset.VoiceClarity,
            3 => EqualizerPreset.WarmVoice,
            4 => EqualizerPreset.BrightVoice,
            5 => EqualizerPreset.ReduceBass,
            6 => EqualizerPreset.ReduceTreble,
            7 => EqualizerPreset.PhoneVoice,
            8 => EqualizerPreset.RadioVoice,
            9 => EqualizerPreset.RemoveElectricalHum50Hz,
            10 => EqualizerPreset.RemoveElectricalHum60Hz,
            11 => EqualizerPreset.Custom,
            _ => EqualizerPreset.None
        };

        private static string GetSelectedText(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        private static int? ParseOptionalInt(NumberBox box) => double.IsNaN(box.Value) ? null : (int)Math.Round(box.Value);
        private static double? ParseOptionalDouble(NumberBox box) => double.IsNaN(box.Value) ? null : box.Value;
        private static double ParseNumberValue(NumberBox box, double fallback) => double.IsNaN(box.Value) ? fallback : box.Value;

        private async Task<string?> PickOutputPathAsync(StorageFile sourceFile)
        {
            if (App.MainWindow is null) return null;
            var extension = Path.GetExtension(sourceFile.Name);
            if (_outputFormatComboBox?.SelectedIndex != 0)
            {
                var selectedFormat = GetSelectedText(_outputFormatComboBox!);
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
            InitializePickerWithMainWindow(picker);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }

        private static void InitializePickerWithMainWindow(object picker)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
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
            var msg = Strings.Get("AudioPage_AudioSaved") + "\n" + outputPath;
            if (warnings.Count > 0)
                msg += "\n\n" + Strings.Get("AudioPage_Warnings") + "\n- " + string.Join("\n- ", warnings);
            return msg;
        }

        private async Task ShowSimpleDialogAsync(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = Strings.Get("Shared_OK"),
                XamlRoot = XamlRoot
            };
            _ = await dialog.ShowAsync();
        }
    }
}
