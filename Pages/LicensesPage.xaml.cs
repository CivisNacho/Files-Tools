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

    private const string LibreOfficeLicenseText =
        "Component: LibreOffice\n" +
        "Copyright: Copyright (C) The Document Foundation and contributors\n" +
        "License: MPL-2.0\n\n" +
        "Project URL:\n" +
        "https://www.libreoffice.org\n\n" +
        "Usage: document format conversion — DOCX, DOC, ODT, PPTX, PPT, ODP to PDF " +
        "via headless CLI invocation (soffice --headless --convert-to pdf).\n\n" +
        "LibreOffice is distributed under the Mozilla Public License 2.0 (MPL-2.0).\n" +
        "Source code is available at https://cgit.freedesktop.org/libreoffice/core/\n\n" +
        "Mozilla Public License Version 2.0\n" +
        "-----------------------------------\n\n" +
        "1. Definitions\n\n" +
        "1.1. \"Contributor\" means each individual or legal entity that creates, contributes to the " +
        "creation of, or owns Covered Software.\n\n" +
        "1.2. \"Contributor Version\" means the combination of the Contributions of others (if any) " +
        "used by a Contributor and that particular Contributor's Contribution.\n\n" +
        "1.3. \"Contribution\" means Covered Software of a particular Contributor.\n\n" +
        "1.4. \"Covered Software\" means Source Code Form to which the initial Contributor has " +
        "attached the notice in Exhibit A, the Executable Form of such Source Code Form, and " +
        "Modifications of such Source Code Form, in each case including portions thereof.\n\n" +
        "2. License Grants and Conditions\n\n" +
        "2.1. Grants — Each Contributor hereby grants you a world-wide, royalty-free, " +
        "non-exclusive license to reproduce, prepare Derivative Works of, publicly display, " +
        "publicly perform, distribute, and sublicense the Covered Software, in each case " +
        "under the terms of this License.\n\n" +
        "2.2. Effective Date — The licenses granted in Section 2.1 with respect to any " +
        "Contribution become effective for each Contribution on the date the Contributor " +
        "first distributes such Contribution.\n\n" +
        "3. Responsibilities — If You distribute Covered Software, You must make the Source " +
        "Code Form of the Covered Software available under the terms of this License.\n\n" +
        "Full license text: https://mozilla.org/MPL/2.0/\n\n" +
        "LibreOffice is downloaded on-demand by this application when the user first " +
        "uses the Document Editor. The download (~420 MB) is stored in the user's local " +
        "app data folder and is not bundled or redistributed with this application.";

    private const string Apache2LicenseSummary =
        "Apache License, Version 2.0\n\n" +
        "Licensed under the Apache License, Version 2.0 (the \"License\"); you may not " +
        "use this file except in compliance with the License. You may obtain a copy of " +
        "the License at:\n\n" +
        "    http://www.apache.org/licenses/LICENSE-2.0\n\n" +
        "Unless required by applicable law or agreed to in writing, software distributed " +
        "under the License is distributed on an \"AS IS\" BASIS, WITHOUT WARRANTIES OR " +
        "CONDITIONS OF ANY KIND, either express or implied. See the License for the " +
        "specific language governing permissions and limitations under the License.\n\n" +
        "Full license text: https://www.apache.org/licenses/LICENSE-2.0";

    private const string QpdfLicenseText =
        "Component: qpdf (via QPdfNet)\n" +
        "Copyright: Copyright (c) 2005-2024 Jay Berkenbilt; 2022-2024 Manfred Nerurkar\n" +
        "License: Apache-2.0\n\n" +
        "qpdf project URL:\n" +
        "https://github.com/qpdf/qpdf\n\n" +
        "QPdfNet (.NET wrapper) project URL:\n" +
        "https://github.com/Sicos1977/QPdfNet\n\n" +
        "Package source:\n" +
        "https://www.nuget.org/packages/QPdfNet\n\n" +
        "Usage: PDF manipulation backbone — merge, split, reorder, rotate, encrypt, " +
        "decrypt, change/remove password, update permissions, repair, update Info " +
        "metadata, extract images and attachments. The native qpdf library and its " +
        "Microsoft Visual C++ runtime dependencies are bundled in the QPdfNet NuGet " +
        "package and redistributed with this application.\n\n" +
        "Note: starting with qpdf 11, the project is licensed under Apache-2.0. Earlier " +
        "versions were dual-licensed under Artistic 2.0 / Apache-2.0.\n\n" +
        Apache2LicenseSummary;

    private const string TesseractLicenseText =
        "Component: Tesseract OCR (via TesseractOCR .NET wrapper)\n" +
        "Copyright: Copyright (c) Google Inc. and contributors; (c) Sicos1977 for the .NET wrapper\n" +
        "License: Apache-2.0\n\n" +
        "Tesseract project URL:\n" +
        "https://github.com/tesseract-ocr/tesseract\n\n" +
        "TesseractOCR (.NET wrapper) project URL:\n" +
        "https://github.com/Sicos1977/TesseractOCR\n\n" +
        "Package source:\n" +
        "https://www.nuget.org/packages/TesseractOCR\n\n" +
        "Usage: optical character recognition on PDF pages to produce searchable PDFs. " +
        "The native Tesseract and Leptonica libraries shipped with the TesseractOCR " +
        "NuGet package are redistributed with this application.\n\n" +
        "Trained language data files (tessdata) are not bundled — users supply their own " +
        "*.traineddata files at the path configured via PdfOcrOptions.TessDataPath. " +
        "Language data files distributed by the Tesseract project are themselves " +
        "Apache-2.0 licensed.\n\n" +
        Apache2LicenseSummary;

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

    private void ShowLibreOfficeLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("LibreOffice License", LibreOfficeLicenseText);
    }

    private void ShowQpdfLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("qpdf / QPdfNet License", QpdfLicenseText);
    }

    private void ShowTesseractLicense_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseDialog("Tesseract OCR License", TesseractLicenseText);
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
