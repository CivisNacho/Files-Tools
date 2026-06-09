using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;

using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Files_Tools
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        public static Window? MainWindow { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            LoadSubtitlePresets();
        }

        /// <summary>
        /// Loads JSON subtitle presets from <c>Assets/Presets</c> and the user preset folder and
        /// registers them into <see cref="Services.SubtitleStyleCatalog"/> so the editors' style
        /// pickers list them alongside the built-ins. Failures are non-fatal: the app still has its
        /// built-in presets, so a malformed preset file must never block launch.
        /// </summary>
        private static void LoadSubtitlePresets()
        {
            try
            {
                var entries = new Services.Presets.SubtitlePresetLoader().Load();
                Services.SubtitleStyleCatalog.RegisterPresets(entries);
            }
            catch
            {
                // Built-in presets remain available; ignore preset-loading failures at startup.
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();
        }
    }
}
