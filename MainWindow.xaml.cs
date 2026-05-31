using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Files_Tools
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private AppWindow? _appWindow;

        public MainWindow()
        {
            InitializeComponent();
            NavigationService.Initialize(RootFrame);
            RootFrame.Navigated += RootFrame_Navigated;
            SizeChanged += MainWindow_SizeChanged;
            NavigationService.Navigate(typeof(Pages.HomePage));

            InitializeTitleBarThemeSync();
        }

        private void RootFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            AppTitleBar.IsBackButtonVisible = RootFrame.CanGoBack;
            var isImageEditorPage = RootFrame.Content is Pages.ImageEditorPage;
            var isVideoEditorPage = RootFrame.Content is Pages.VideoEditorPage;
            var isAudioEditorPage = RootFrame.Content is Pages.AudioEditorPage;
            var isPdfEditorPage   = RootFrame.Content is Pages.PdfEditorPage;
            var isBatchEditorPage = RootFrame.Content is Pages.BatchEditorPage;
            var showEditorRail    = isImageEditorPage || isVideoEditorPage || isAudioEditorPage || isPdfEditorPage || isBatchEditorPage;
            ImageEditorOptionsNavigationView.Visibility = showEditorRail ? Visibility.Visible : Visibility.Collapsed;
            ImageEditorOptionsNavigationView.SelectedItem = null;

            SetImageEditorNavigationVisibility(isImageEditorPage);
            SetVideoEditorNavigationVisibility(isVideoEditorPage);
            SetAudioEditorNavigationVisibility(isAudioEditorPage);
            SetPdfEditorNavigationVisibility(isPdfEditorPage);
            SetBatchEditorNavigationVisibility(isBatchEditorPage);

            // Measure after per-mode visibility is applied so the pane fits the widest *visible* item.
            UpdateNavigationRailWidth(showEditorRail);
            ImageEditorNavColumn.Width = showEditorRail
                ? new GridLength(ImageEditorOptionsNavigationView.OpenPaneLength)
                : new GridLength(0);
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            var showEditorRail = RootFrame.Content is Pages.ImageEditorPage or Pages.VideoEditorPage or Pages.AudioEditorPage or Pages.PdfEditorPage or Pages.BatchEditorPage;
            UpdateNavigationRailWidth(showEditorRail);
            ImageEditorNavColumn.Width = showEditorRail
                ? new GridLength(ImageEditorOptionsNavigationView.OpenPaneLength)
                : new GridLength(0);
        }

        private void UpdateNavigationRailWidth(bool showEditorRail)
        {
            if (!showEditorRail)
            {
                return;
            }

            ImageEditorOptionsNavigationView.OpenPaneLength =
                MeasureRequiredNavigationPaneWidth(ImageEditorOptionsNavigationView);
        }

        private static double MeasureRequiredNavigationPaneWidth(NavigationView navigationView)
        {
            // iconAndPadding covers: icon box (~40px) + left margin (~16px) + right padding (~16px)
            // + expand chevron on parent items (~32px) = 104px.
            const double iconAndPadding = 104d;
            const double minPane = 160d;

            var maxHeaderWidth = 0d;
            MeasureNavigationItems(navigationView.MenuItems, ref maxHeaderWidth, depth: 0);
            var required = maxHeaderWidth + iconAndPadding;
            return Math.Max(minPane, Math.Ceiling(required));
        }

        /// <param name="depth">
        /// Nesting depth of the current item set (0 = top-level, 1 = direct children, …).
        /// Each depth level adds <c>perLevelIndent</c> pixels to the measured text width so that
        /// indented child items (e.g. "Extract audio" inside "Video") are accounted for correctly.
        /// </param>
        private static void MeasureNavigationItems(
            IEnumerable<object> items,
            ref double maxHeaderWidth,
            int depth)
        {
            // NavigationView indents each nesting level by roughly 40 px in Left-pane mode.
            const double perLevelIndent = 40d;

            foreach (var item in items)
            {
                if (item is not NavigationViewItem nvi)
                    continue;

                // Only items visible in the current editor mode should drive the pane width.
                if (nvi.Visibility != Visibility.Visible)
                    continue;

                var headerText = nvi.Content?.ToString();
                if (!string.IsNullOrWhiteSpace(headerText))
                {
                    var probe = new TextBlock
                    {
                        Text       = headerText,
                        FontSize   = 14,
                        FontWeight = FontWeights.Normal
                    };
                    probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    maxHeaderWidth = Math.Max(maxHeaderWidth,
                        probe.DesiredSize.Width + depth * perLevelIndent);
                }

                if (nvi.MenuItems.Count > 0)
                    MeasureNavigationItems(nvi.MenuItems, ref maxHeaderWidth, depth + 1);
            }
        }

        private void AppTitleBar_BackRequested(TitleBar sender, object args)
        {
            if (RootFrame.Content is Pages.ImageEditorPage imageEditorPage && imageEditorPage.IsCropEnabled)
            {
                imageEditorPage.CancelCrop();
                return;
            }

            if (RootFrame.CanGoBack)
            {
                RootFrame.GoBack();
            }
        }

        private void BatchButton_Click(object sender, RoutedEventArgs e)
        {
            RootFrame.Navigate(typeof(Pages.BatchEditorPage));
        }

        private void LicensesButton_Click(object sender, RoutedEventArgs e)
        {
            RootFrame.Navigate(typeof(Pages.LicensesPage));
        }

        private void InitializeTitleBarThemeSync()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;

            if (Content is FrameworkElement root)
            {
                root.ActualThemeChanged += Root_ActualThemeChanged;
                ApplyTitleBarTheme(root.ActualTheme);
            }
        }

        private void Root_ActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyTitleBarTheme(sender.ActualTheme);
        }

        private void ApplyTitleBarTheme(ElementTheme theme)
        {
            if (_appWindow is null)
            {
                return;
            }

            var titleBar = _appWindow.TitleBar;
            var isDark = theme == ElementTheme.Dark;

            var transparent = Colors.Transparent;
            titleBar.BackgroundColor = transparent;
            titleBar.InactiveBackgroundColor = transparent;
            titleBar.ButtonBackgroundColor = transparent;
            titleBar.ButtonInactiveBackgroundColor = transparent;

            if (isDark)
            {
                titleBar.ForegroundColor = Colors.White;
                titleBar.InactiveForegroundColor = Color.FromArgb(255, 180, 180, 180);
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(80, 255, 255, 255);
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(120, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 180, 180, 180);
            }
            else
            {
                titleBar.ForegroundColor = Colors.Black;
                titleBar.InactiveForegroundColor = Color.FromArgb(255, 110, 110, 110);
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(48, 0, 0, 0);
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(76, 0, 0, 0);
                titleBar.ButtonPressedForegroundColor = Colors.Black;
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 110, 110, 110);
            }
        }

        private void ImageEditorOptionsNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem selectedItem ||
                selectedItem.Tag is not string tag ||
                !tag.Contains(':', StringComparison.Ordinal))
            {
                return;
            }

            if (RootFrame.Content is Pages.ImageEditorPage imageEditorPage)
            {
                imageEditorPage.ApplyOptionSelection(tag);
            }
            else if (RootFrame.Content is Pages.VideoEditorPage videoEditorPage)
            {
                videoEditorPage.ApplyOptionSelection(tag);
            }
            else if (RootFrame.Content is Pages.AudioEditorPage audioEditorPage)
            {
                audioEditorPage.ApplyOptionSelection(tag);
            }
            else if (RootFrame.Content is Pages.PdfEditorPage pdfEditorPage)
            {
                pdfEditorPage.HandleNavigationViewSelection(tag);
            }
            else if (RootFrame.Content is Pages.BatchEditorPage batchEditorPage)
            {
                batchEditorPage.ApplyOptionSelection(tag);
            }
        }

        private void SetImageEditorNavigationVisibility(bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            TitleMediaNavigationItem.Visibility = visibility;
            TitleTransformNavigationItem.Visibility = visibility;
            TitleAdjustNavigationItem.Visibility = visibility;
        }

        private void SetVideoEditorNavigationVisibility(bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            VideoMediaNavigationItem.Visibility = visibility;
            VideoTransformNavigationItem.Visibility = visibility;
            VideoAdvancedNavigationItem.Visibility = visibility;
        }

        private void SetAudioEditorNavigationVisibility(bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            AudioMediaNavigationItem.Visibility = visibility;
            AudioTransformNavigationItem.Visibility = visibility;
            AudioAdjustNavigationItem.Visibility = visibility;
            AudioTranscriptionNavigationItem.Visibility = visibility;
        }

        private void SetPdfEditorNavigationVisibility(bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            PdfOrganizationNavigationItem.Visibility = visibility;
            PdfTransformNavigationItem.Visibility = visibility;
            PdfSecurityNavigationItem.Visibility = visibility;
            PdfContentNavigationItem.Visibility = visibility;
            PdfRepairNavigationItem.Visibility = visibility;
        }

        private void SetBatchEditorNavigationVisibility(bool isVisible)
        {
            // Output item is always shown when the batch page is active.
            BatchOutputNavigationItem.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            // Type-group items are shown dynamically via UpdateBatchNavigationVisibility when
            // files are loaded. When leaving the page, collapse them all.
            if (!isVisible)
            {
                BatchAudioNavigationItem.Visibility    = Visibility.Collapsed;
                BatchVideoNavigationItem.Visibility    = Visibility.Collapsed;
                BatchDocumentNavigationItem.Visibility = Visibility.Collapsed;
                BatchPdfNavigationItem.Visibility      = Visibility.Collapsed;
                BatchImageNavigationItem.Visibility    = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Called by <see cref="Pages.BatchEditorPage"/> whenever its file list changes so the
        /// sidebar reflects which file-type groups are actually loaded.
        /// </summary>
        public void UpdateBatchNavigationVisibility(IReadOnlyList<Services.BatchFileType> activeTypes)
        {
            BatchAudioNavigationItem.Visibility    = activeTypes.Contains(Services.BatchFileType.Audio)    ? Visibility.Visible : Visibility.Collapsed;
            BatchVideoNavigationItem.Visibility    = activeTypes.Contains(Services.BatchFileType.Video)    ? Visibility.Visible : Visibility.Collapsed;
            BatchDocumentNavigationItem.Visibility = activeTypes.Contains(Services.BatchFileType.Document) ? Visibility.Visible : Visibility.Collapsed;
            BatchPdfNavigationItem.Visibility      = activeTypes.Contains(Services.BatchFileType.Pdf)      ? Visibility.Visible : Visibility.Collapsed;
            BatchImageNavigationItem.Visibility    = activeTypes.Contains(Services.BatchFileType.Image)    ? Visibility.Visible : Visibility.Collapsed;
        }

    }
}
