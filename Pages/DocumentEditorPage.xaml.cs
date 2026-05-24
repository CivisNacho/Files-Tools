using Files_Tools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Files_Tools.Pages;

public sealed partial class DocumentEditorPage : Page
{
    private DocumentService _documentService = new();
    private string? _loadedDocumentPath;
    private string? _selectedOperation;
    private CancellationTokenSource? _currentOperation;
    private CancellationTokenSource? _libreofficeCts;
    private bool _isProcessing;

    public DocumentEditorPage()
    {
        InitializeComponent();
        SetupDefaultSelection();
        RefreshLibreOfficeSetupBanner();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is FileNavigationRequest request && request.File != null)
        {
            LoadDocument(request.File.Path);
        }
    }

    private void SetupDefaultSelection()
    {
        SelectOperation("convert-to-pdf");
    }

    private void SelectOperation(string operation)
    {
        _selectedOperation = operation;
        SelectedOptionHeaderTextBlock.Text = operation switch
        {
            "convert-to-pdf" => "Convert to PDF",
            "repair" => "Repair Document",
            "extract-images" => "Extract Images",
            _ => "Select an operation"
        };

        ConvertToPdfPanel.Visibility = operation == "convert-to-pdf" ? Visibility.Visible : Visibility.Collapsed;
        RepairPanel.Visibility = operation == "repair" ? Visibility.Visible : Visibility.Collapsed;
        ExtractImagesPanel.Visibility = operation == "extract-images" ? Visibility.Visible : Visibility.Collapsed;

        UpdateApplyButtonState();
    }

    private void UploadSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = BrowseForDocument();
    }

    private void UploadSurface_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void UploadSurface_Drop(object sender, DragEventArgs e)
    {
        _ = HandleDroppedFiles(e);
    }

    private async Task BrowseForDocument()
    {
        var picker = new FileOpenPicker();
        if (App.MainWindow != null)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        }

        picker.FileTypeFilter.Add(".docx");
        picker.FileTypeFilter.Add(".doc");
        picker.FileTypeFilter.Add(".odt");
        picker.FileTypeFilter.Add(".pptx");
        picker.FileTypeFilter.Add(".ppt");
        picker.FileTypeFilter.Add(".odp");
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xls");
        picker.FileTypeFilter.Add(".ods");
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".pdf");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            LoadDocument(file.Path);
        }
    }

    private async Task HandleDroppedFiles(DragEventArgs e)
    {
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0 && items[0] is StorageFile file)
        {
            LoadDocument(file.Path);
        }
    }

    private void LoadDocument(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var supportedExtensions = new[] { ".docx", ".doc", ".odt", ".pptx", ".ppt", ".odp", ".xlsx", ".xls", ".ods", ".csv", ".pdf" };

        if (!Array.Exists(supportedExtensions, e => e.Equals(ext)))
        {
            ShowError($"File format '{ext}' is not supported.");
            return;
        }

        _loadedDocumentPath = path;

        var fileName = Path.GetFileName(path);
        var fileInfo = new FileInfo(path);
        var sizeKb = fileInfo.Length / 1024;

        LoadedDocumentInfoTextBlock.Text = $"{fileName} • {sizeKb} KB";
        LoadedDocumentPreview.Text = fileName;
        LoadedDocumentPreview.Visibility = Visibility.Visible;
        DropHintPanel.Visibility = Visibility.Collapsed;

        UpdateApplyButtonState();
    }

    private void UpdateApplyButtonState()
    {
        var isPageRangeValid = PageRangeValidationTextBlock?.Visibility != Visibility.Visible;
        ApplyButton.IsEnabled = !_isProcessing
            && _loadedDocumentPath != null
            && isPageRangeValid
            && LibreOfficeSetupService.IsAvailable;
    }

    // ── LibreOffice setup banner ──────────────────────────────────────────────

    /// <summary>
    /// Syncs the banner visibility with <see cref="LibreOfficeSetupService.IsAvailable"/>.
    /// Call whenever the availability state may have changed.
    /// </summary>
    private void RefreshLibreOfficeSetupBanner()
    {
        var available = LibreOfficeSetupService.IsAvailable;
        LibreOfficeSetupBanner.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        UpdateApplyButtonState();
    }

    private async void DownloadLibreOfficeButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadLibreOfficeButton.IsEnabled = false;
        CancelLibreOfficeButton.Visibility  = Visibility.Visible;
        LibreOfficeSetupProgressBar.Visibility       = Visibility.Visible;
        LibreOfficeSetupProgressBar.IsIndeterminate  = false;
        LibreOfficeSetupProgressBar.Value            = 0;

        _libreofficeCts = new CancellationTokenSource();

        var progress = new Progress<LibreOfficeSetupProgress>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LibreOfficeSetupStatusText.Text = p.StatusText;

                if (p.Percentage >= 0)
                {
                    LibreOfficeSetupProgressBar.IsIndeterminate = false;
                    LibreOfficeSetupProgressBar.Value           = p.Percentage;
                }
                else
                {
                    LibreOfficeSetupProgressBar.IsIndeterminate = true;
                }

                if (p.Stage == LibreOfficeSetupStage.Complete)
                {
                    RefreshLibreOfficeSetupBanner();
                }
            });
        });

        try
        {
            await LibreOfficeSetupService.DownloadAndInstallAsync(progress, _libreofficeCts.Token);
        }
        catch (OperationCanceledException)
        {
            LibreOfficeSetupStatusText.Text            = "Download cancelled.";
            DownloadLibreOfficeButton.Content          = "Download LibreOffice";
            DownloadLibreOfficeButton.IsEnabled        = true;
            CancelLibreOfficeButton.Visibility         = Visibility.Collapsed;
            LibreOfficeSetupProgressBar.Visibility     = Visibility.Collapsed;
            LibreOfficeSetupProgressBar.Value          = 0;
        }
        catch (Exception ex)
        {
            LibreOfficeSetupStatusText.Text     = $"Download failed: {ex.Message} — tap to retry.";
            DownloadLibreOfficeButton.Content   = "Retry Download";
            DownloadLibreOfficeButton.IsEnabled = true;
            CancelLibreOfficeButton.Visibility  = Visibility.Collapsed;
            LibreOfficeSetupProgressBar.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _libreofficeCts?.Dispose();
            _libreofficeCts = null;
        }
    }

    private void CancelLibreOfficeButton_Click(object sender, RoutedEventArgs e)
    {
        _libreofficeCts?.Cancel();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedDocumentPath == null || _selectedOperation == null)
        {
            return;
        }

        _currentOperation = new CancellationTokenSource();
        _isProcessing = true;
        UpdateApplyButtonState();
        ApplyButton.Content = "Processing...";

        try
        {
            switch (_selectedOperation)
            {
                case "convert-to-pdf":
                    await ProcessConvertToPdf(_loadedDocumentPath, _currentOperation.Token);
                    break;
                case "repair":
                    await ProcessRepair(_loadedDocumentPath, _currentOperation.Token);
                    break;
                case "extract-images":
                    await ProcessExtractImages(_loadedDocumentPath, _currentOperation.Token);
                    break;
            }

            ShowSuccess("Document processed successfully!");
        }
        catch (OperationCanceledException)
        {
            ShowError("Operation was cancelled.");
        }
        catch (DocumentConversionException ex)
        {
            ShowError($"Processing failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            ApplyButton.Content = "Process document";
            UpdateApplyButtonState();
            _currentOperation?.Dispose();
            _currentOperation = null;
        }
    }

    private async Task ProcessConvertToPdf(string inputPath, CancellationToken cancellationToken)
    {
        var outputPath = await AskForOutputPath("PDF Files (*.pdf)|*.pdf", ".pdf");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        var options = new DocumentConversionOptions
        {
            Variant = (PdfOutputVariant)PdfVariantComboBox.SelectedIndex,
            ImageCompression = (PdfImageCompression)ImageCompressionComboBox.SelectedIndex,
            JpegQuality = ImageCompressionComboBox.SelectedIndex == 2 ? (int?)Convert.ToInt32(JpegQualitySlider.Value) : null,
            PageRange = string.IsNullOrWhiteSpace(PageRangeTextBox.Text) ? null : PageRangeTextBox.Text
        };

        await _documentService.ConvertToPdfAsync(inputPath, outputPath, options, cancellationToken);
    }

    private async Task ProcessRepair(string inputPath, CancellationToken cancellationToken)
    {
        var outputPath = await AskForOutputPath("All Documents|*.*");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        var originalExt = Path.GetExtension(inputPath);
        var outputExt = RepairOutputFormatComboBox.SelectedIndex switch
        {
            0 => originalExt,
            1 => ".docx",
            2 => ".pptx",
            3 => ".xlsx",
            4 => ".odt",
            5 => ".odp",
            6 => ".ods",
            _ => originalExt
        };

        if (!outputPath.EndsWith(outputExt, StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.ChangeExtension(outputPath, outputExt);
        }

        await _documentService.RepairAsync(inputPath, outputPath, cancellationToken);
    }

    private async Task ProcessExtractImages(string inputPath, CancellationToken cancellationToken)
    {
        var outputPath = await AskForOutputPath("ZIP Archives (*.zip)|*.zip", ".zip");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        await _documentService.ExtractImagesAsync(inputPath, outputPath, cancellationToken);
    }

    private async Task<string?> AskForOutputPath(string filterString, string? defaultExt = null)
    {
        var picker = new FileSavePicker();
        if (App.MainWindow != null)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        }

        if (defaultExt != null)
        {
            picker.FileTypeChoices.Add(_selectedOperation switch
            {
                "convert-to-pdf" => "PDF",
                "extract-images" => "ZIP",
                _ => "Document"
            }, new[] { defaultExt });
        }

        var suggestedName = Path.GetFileNameWithoutExtension(_loadedDocumentPath);
        if (_selectedOperation == "convert-to-pdf")
        {
            picker.SuggestedFileName = suggestedName + ".pdf";
        }
        else if (_selectedOperation == "extract-images")
        {
            picker.SuggestedFileName = suggestedName + "_images.zip";
        }
        else
        {
            picker.SuggestedFileName = suggestedName + "_repaired" + Path.GetExtension(_loadedDocumentPath);
        }

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private void ShowError(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        _ = dialog.ShowAsync();
    }

    private void ShowSuccess(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Success",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        _ = dialog.ShowAsync();
    }

    private void OptionsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender == ImageCompressionComboBox && JpegQualityPanel != null)
        {
            JpegQualityPanel.Visibility = ImageCompressionComboBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void JpegQualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        JpegQualityTextBlock.Text = $"JPEG quality: {Convert.ToInt32(e.NewValue)}%";
    }

    private void PageRangeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidatePageRange();
    }

    private void ValidatePageRange()
    {
        var input = PageRangeTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            PageRangeValidationTextBlock.Visibility = Visibility.Collapsed;
            UpdateApplyButtonState();
            return;
        }

        var (isValid, errorMessage) = CheckPageRangeFormat(input);

        if (isValid)
        {
            PageRangeValidationTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            PageRangeValidationTextBlock.Text = errorMessage;
            PageRangeValidationTextBlock.Visibility = Visibility.Visible;
        }

        UpdateApplyButtonState();
    }

    private (bool isValid, string errorMessage) CheckPageRangeFormat(string input)
    {
        var parts = input.Split(',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return (false, "Empty values in comma-separated list. Example: 1-3,5,8");
            }

            if (trimmed.Contains('-'))
            {
                var rangeParts = trimmed.Split('-');
                if (rangeParts.Length != 2)
                {
                    return (false, $"Invalid range format '{trimmed}'. Use format like '1-3' for ranges.");
                }

                if (!int.TryParse(rangeParts[0].Trim(), out var start) || !int.TryParse(rangeParts[1].Trim(), out var end))
                {
                    return (false, $"Range '{trimmed}' contains non-numeric values. Use only numbers and hyphens.");
                }

                if (start < 1 || end < 1)
                {
                    return (false, "Page numbers must be 1 or greater.");
                }

                if (start > end)
                {
                    return (false, $"Invalid range '{trimmed}': start page is greater than end page.");
                }
            }
            else
            {
                if (!int.TryParse(trimmed, out var pageNum))
                {
                    return (false, $"'{trimmed}' is not a valid page number. Use only numbers, or ranges like '1-3'.");
                }

                if (pageNum < 1)
                {
                    return (false, "Page numbers must be 1 or greater.");
                }
            }
        }

        return (true, "");
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete && _loadedDocumentPath != null)
        {
            _loadedDocumentPath = null;
            LoadedDocumentInfoTextBlock.Text = "No document loaded yet.";
            LoadedDocumentPreview.Visibility = Visibility.Collapsed;
            DropHintPanel.Visibility = Visibility.Visible;
            UpdateApplyButtonState();
            e.Handled = true;
        }
    }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width < 1000)
        {
            EditorGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            EditorGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(OptionsPanel, 0);
            Grid.SetColumnSpan(OptionsPanel, 2);
            Grid.SetRow(OptionsPanel, 1);
            Grid.SetRowSpan(OptionsPanel, 2);
            PreviewSection.Visibility = Visibility.Visible;
        }
        else
        {
            EditorGrid.ColumnDefinitions[0].Width = new GridLength(7, GridUnitType.Star);
            EditorGrid.ColumnDefinitions[1].Width = new GridLength(3, GridUnitType.Star);
            Grid.SetColumn(OptionsPanel, 1);
            Grid.SetColumnSpan(OptionsPanel, 1);
            Grid.SetRow(OptionsPanel, 0);
            Grid.SetRowSpan(OptionsPanel, 3);
        }
    }
}
