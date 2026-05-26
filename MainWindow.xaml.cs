using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.UI;
using System;
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
            var isPdfEditorPage = RootFrame.Content is Pages.PdfEditorPage;
            var showEditorRail = isImageEditorPage || isVideoEditorPage || isAudioEditorPage || isPdfEditorPage;
            UpdateNavigationRailWidth(showEditorRail);

            ImageEditorOptionsNavigationView.Visibility = showEditorRail ? Visibility.Visible : Visibility.Collapsed;
            ImageEditorNavColumn.Width = showEditorRail
                ? new GridLength(ImageEditorOptionsNavigationView.OpenPaneLength)
                : new GridLength(0);
            ImageEditorOptionsNavigationView.SelectedItem = null;

            SetImageEditorNavigationVisibility(isImageEditorPage);
            SetVideoEditorNavigationVisibility(isVideoEditorPage);
            SetAudioEditorNavigationVisibility(isAudioEditorPage);
            SetPdfEditorNavigationVisibility(isPdfEditorPage);
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            var showEditorRail = RootFrame.Content is Pages.ImageEditorPage or Pages.VideoEditorPage or Pages.AudioEditorPage or Pages.PdfEditorPage;
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

            var responsivePaneLength = AppTitleBar.ActualWidth * 0.18d;
            var requiredContentWidth = MeasureRequiredNavigationPaneWidth(ImageEditorOptionsNavigationView);
            ImageEditorOptionsNavigationView.OpenPaneLength = Math.Max(responsivePaneLength, requiredContentWidth);
        }

        private static double MeasureRequiredNavigationPaneWidth(NavigationView navigationView)
        {
            const double iconAndPadding = 104d;
            const double minPane = 160d;

            var maxHeaderWidth = 0d;
            MeasureNavigationItems(navigationView.MenuItems, ref maxHeaderWidth);
            var required = maxHeaderWidth + iconAndPadding;
            return Math.Max(minPane, Math.Ceiling(required));
        }

        private static void MeasureNavigationItems(System.Collections.Generic.IEnumerable<object> items, ref double maxHeaderWidth)
        {
            foreach (var item in items)
            {
                if (item is not NavigationViewItem nvi)
                {
                    continue;
                }

                var headerText = nvi.Content?.ToString();
                if (!string.IsNullOrWhiteSpace(headerText))
                {
                    var probe = new TextBlock
                    {
                        Text = headerText,
                        FontSize = 14,
                        FontWeight = FontWeights.Normal
                    };
                    probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    maxHeaderWidth = Math.Max(maxHeaderWidth, probe.DesiredSize.Width);
                }

                if (nvi.MenuItems.Count > 0)
                {
                    MeasureNavigationItems(nvi.MenuItems, ref maxHeaderWidth);
                }
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

    }
}
