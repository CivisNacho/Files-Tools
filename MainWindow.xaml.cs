using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.UI;

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
            NavigationService.Navigate(typeof(Pages.HomePage));

            InitializeTitleBarThemeSync();
        }

        private void InitializeTitleBarThemeSync()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
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

            if (isDark)
            {
                titleBar.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
                titleBar.ForegroundColor = Colors.White;
                titleBar.InactiveBackgroundColor = Color.FromArgb(255, 45, 45, 45);
                titleBar.InactiveForegroundColor = Color.FromArgb(255, 180, 180, 180);
                titleBar.ButtonBackgroundColor = Color.FromArgb(255, 32, 32, 32);
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 55, 55, 55);
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 80, 80, 80);
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 45, 45, 45);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 180, 180, 180);
            }
            else
            {
                titleBar.BackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ForegroundColor = Colors.Black;
                titleBar.InactiveBackgroundColor = Color.FromArgb(255, 249, 249, 249);
                titleBar.InactiveForegroundColor = Color.FromArgb(255, 110, 110, 110);
                titleBar.ButtonBackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 229, 229, 229);
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
                titleBar.ButtonPressedForegroundColor = Colors.Black;
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 249, 249, 249);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 110, 110, 110);
            }
        }
    }
}
