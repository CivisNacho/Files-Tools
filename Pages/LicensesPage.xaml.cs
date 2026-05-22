using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files_Tools.Pages;

public sealed partial class LicensesPage : Page
{
    private const string MitLicenseText =
        "MIT License\n\n" +
        "Permission is hereby granted, free of charge, to any person obtaining a copy " +
        "of this software and associated documentation files (the \"Software\"), to deal " +
        "in the Software without restriction, including without limitation the rights " +
        "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell " +
        "copies of the Software, and to permit persons to whom the Software is " +
        "furnished to do so, subject to the following conditions:\n\n" +
        "The above copyright notice and this permission notice shall be included in all " +
        "copies or substantial portions of the Software.\n\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR " +
        "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
        "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE " +
        "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
        "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, " +
        "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.";

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

    private const string DtlnLicenseText =
        "Component: DTLN pretrained ONNX models\n" +
        "Copyright: Copyright (c) 2020 Nils L. Westhausen\n" +
        "License: MIT\n\n" +
        "Project URL:\n" +
        "https://github.com/breizhn/DTLN\n\n" +
        "Bundled model files:\n" +
        "Assets/Models/Dtln/model_1.onnx\n" +
        "Assets/Models/Dtln/model_2.onnx\n\n" +
        MitLicenseText;

    private const string OnnxRuntimeLicenseText =
        "Component: ONNX Runtime / Microsoft.ML.OnnxRuntime\n" +
        "Copyright: Copyright (c) Microsoft Corporation\n" +
        "License: MIT\n\n" +
        "Project URL:\n" +
        "https://github.com/microsoft/onnxruntime\n\n" +
        "Usage: Executes the bundled DTLN ONNX models for audio denoise.\n\n" +
        MitLicenseText;

    private const string WhisperLicenseText =
        "Component: Whisper.net / whisper.cpp\n" +
        "License: MIT\n\n" +
        "Whisper.net project URL:\n" +
        "https://github.com/sandrohanea/whisper.net\n\n" +
        "whisper.cpp project URL:\n" +
        "https://github.com/ggml-org/whisper.cpp\n\n" +
        "Usage: local speech-to-text transcription and subtitle generation.\n\n" +
        MitLicenseText;

    public LicensesPage()
    {
        InitializeComponent();
    }

    private void ShowLibvipsLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("libvips License", LibvipsLicenseText);
    }

    private void ShowFfmpegLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("FFmpeg License", FfmpegLicenseText);
    }

    private void ShowDtlnLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("DTLN Model License", DtlnLicenseText);
    }

    private void ShowOnnxRuntimeLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("ONNX Runtime License", OnnxRuntimeLicenseText);
    }

    private void ShowWhisperLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("Whisper License", WhisperLicenseText);
    }

    private void ShowLicenseDialog(string title, string licenseText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new ScrollViewer
            {
                Width = 560,
                Height = 360,
                Content = new TextBlock
                {
                    Text = licenseText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };

        _ = dialog.ShowAsync();
    }
}
