using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files_Tools.Pages;

public sealed partial class LicensesPage : Page
{
    private const string LibvipsLicenseText =
        "Component: libvips (via NetVips.Native.win-x64 / win-x86 / win-arm64)\n" +
        "License: LGPL-3.0-or-later\n\n" +
        "License URL:\n" +
        "https://licenses.nuget.org/LGPL-3.0-or-later\n\n" +
        "Project URL:\n" +
        "https://kleisauke.github.io/net-vips\n\n" +
        "NuGet package metadata in this machine confirms LGPL-3.0-or-later.\n" +
        "For redistribution, keep this attribution and comply with LGPL terms.";
    private const string FfmpegLicenseText =
        "Component: FFmpeg (via DevEnvy.FFmpeg.Binaries.LGPL)\n" +
        "License: LGPL-2.1-or-later\n\n" +
        "License URL:\n" +
        "https://licenses.nuget.org/LGPL-2.1-or-later\n\n" +
        "Project URL:\n" +
        "https://ffmpeg.org/\n\n" +
        "Package source:\n" +
        "https://www.nuget.org/packages/DevEnvy.FFmpeg.Binaries.LGPL\n\n" +
        "NuGet package metadata in this machine confirms LGPL-2.1-or-later.\n" +
        "For redistribution, keep this attribution and comply with LGPL terms.";

    public LicensesPage()
    {
        InitializeComponent();
    }

    private void ShowLibvipsLicense_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "libvips License",
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new ScrollViewer
            {
                Width = 560,
                Height = 360,
                Content = new TextBlock
                {
                    Text = LibvipsLicenseText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };

        _ = dialog.ShowAsync();
    }

    private void ShowFfmpegLicense_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "FFmpeg License",
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new ScrollViewer
            {
                Width = 560,
                Height = 360,
                Content = new TextBlock
                {
                    Text = FfmpegLicenseText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };

        _ = dialog.ShowAsync();
    }
}
