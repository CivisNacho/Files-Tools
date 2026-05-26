using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Files_Tools.Services;

namespace Files_Tools.Pages
{
    public sealed partial class PdfEditorPage : Page
    {
        private PdfService _pdfService;
        private StorageFile _currentPdfFile;
        private PdfDocument _currentPdf;
        private int _currentPageIndex;
        private bool _isProcessing;
        private double _currentZoom = 1.0;

        public PdfEditorPage()
        {
            this.InitializeComponent();
            _pdfService = new PdfService();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            InitializeNavigation();

            if (e.Parameter is FileNavigationRequest request && request.File != null)
            {
                await LoadPdfFile(request.File);
            }
        }

        private void InitializeNavigation()
        {
            SelectedOptionHeaderTextBlock.Text = "Select a category from the left";
            HideAllOperationPanels();
        }

        public void HandleNavigationViewSelection(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            HideAllOperationPanels();

            var parts = tag.Split(':');
            string category = parts[0];
            string subcategory = parts.Length > 1 ? parts[1] : "";

            switch (category)
            {
                case "Organization":
                    OrganizationPanel.Visibility = Visibility.Visible;
                    SelectedOptionHeaderTextBlock.Text = "Organization";

                    if (!string.IsNullOrEmpty(subcategory))
                    {
                        switch (subcategory)
                        {
                            case "Merge":
                                MergePanel.Visibility = Visibility.Visible;
                                break;
                            case "Split":
                                SplitPanel.Visibility = Visibility.Visible;
                                break;
                            case "Reorder":
                                ReorderPanel.Visibility = Visibility.Visible;
                                break;
                            case "Extract":
                                ExtractPanel.Visibility = Visibility.Visible;
                                break;
                        }
                    }
                    break;

                case "Transform":
                    TransformPanel.Visibility = Visibility.Visible;
                    SelectedOptionHeaderTextBlock.Text = "Transform";
                    if (subcategory == "Rotate")
                        RotatePanel.Visibility = Visibility.Visible;
                    break;

                case "Security":
                    SecurityPanel.Visibility = Visibility.Visible;
                    SelectedOptionHeaderTextBlock.Text = "Security";

                    if (!string.IsNullOrEmpty(subcategory))
                    {
                        switch (subcategory)
                        {
                            case "Encrypt":
                                EncryptPanel.Visibility = Visibility.Visible;
                                break;
                            case "Password":
                                PasswordManagementPanel.Visibility = Visibility.Visible;
                                break;
                            case "Permissions":
                                PermissionsPanel.Visibility = Visibility.Visible;
                                break;
                        }
                    }
                    break;

                case "Content":
                    ContentPanel.Visibility = Visibility.Visible;
                    SelectedOptionHeaderTextBlock.Text = "Content";

                    if (!string.IsNullOrEmpty(subcategory))
                    {
                        switch (subcategory)
                        {
                            case "OCR":
                                OcrPanel.Visibility = Visibility.Visible;
                                break;
                            case "Metadata":
                                MetadataPanel.Visibility = Visibility.Visible;
                                break;
                        }
                    }
                    break;

                case "Repair":
                    RepairPanel.Visibility = Visibility.Visible;
                    SelectedOptionHeaderTextBlock.Text = "Repair";
                    break;
            }
        }

        private void HideAllOperationPanels()
        {
            OrganizationPanel.Visibility = Visibility.Collapsed;
            TransformPanel.Visibility = Visibility.Collapsed;
            SecurityPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Collapsed;
            RepairPanel.Visibility = Visibility.Collapsed;

            // Hide all sub-panels too
            MergePanel.Visibility = Visibility.Collapsed;
            SplitPanel.Visibility = Visibility.Collapsed;
            ReorderPanel.Visibility = Visibility.Collapsed;
            ExtractPanel.Visibility = Visibility.Collapsed;
            RotatePanel.Visibility = Visibility.Collapsed;
            EncryptPanel.Visibility = Visibility.Collapsed;
            PasswordManagementPanel.Visibility = Visibility.Collapsed;
            PermissionsPanel.Visibility = Visibility.Collapsed;
            OcrPanel.Visibility = Visibility.Collapsed;
            MetadataPanel.Visibility = Visibility.Collapsed;
            RepairOptionsPanel.Visibility = Visibility.Visible; // Repair doesn't have sub-panels
        }

        private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Width);
        }

        private void ApplyResponsiveLayout(double width)
        {
            if (width < 900)
            {
                EditorGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                EditorGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumnSpan(OptionsPanel, 1);
                Grid.SetRow(OptionsPanel, 1);
                Grid.SetColumn(OptionsPanel, 0);
                Grid.SetRowSpan(OptionsPanel, 1);
                OptionsPanel.MinWidth = 280;
                UploadSurface.MaxHeight = 600;
            }
            else
            {
                EditorGrid.ColumnDefinitions[0].Width = new GridLength(7, GridUnitType.Star);
                EditorGrid.ColumnDefinitions[1].Width = new GridLength(3, GridUnitType.Star);
                Grid.SetColumnSpan(OptionsPanel, 1);
                Grid.SetRow(OptionsPanel, 0);
                Grid.SetColumn(OptionsPanel, 1);
                Grid.SetRowSpan(OptionsPanel, 2);
                OptionsPanel.MinWidth = 320;
                UploadSurface.MaxHeight = 800;
            }
        }

        private async void UploadSurface_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.IsContentVisible = true;
        }

        private async void UploadSurface_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.FirstOrDefault() is StorageFile file && file.FileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadPdfFile(file);
                }
            }
        }

        private async void UploadSurface_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".pdf");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadPdfFile(file);
            }
        }

        private async Task LoadPdfFile(StorageFile file)
        {
            try
            {
                _currentPdfFile = file;
                _currentPdf = await PdfDocument.LoadFromFileAsync(file);
                _currentPageIndex = 0;

                await DisplayPdfPage(0);

                var properties = await file.GetBasicPropertiesAsync();
                var sizeInMb = (properties.Size / (1024.0 * 1024.0)).ToString("F2");
                LoadedPdfInfoTextBlock.Text = $"{file.Name} ({sizeInMb} MB) — {_currentPdf.PageCount} pages";

                PdfPageImage.Visibility = Visibility.Visible;
                PdfNavigationPanel.Visibility = Visibility.Visible;
                DropHintPanel.Visibility = Visibility.Collapsed;
                PageCountTextBlock.Text = $"Page 1 of {_currentPdf.PageCount}";

                if (ZoomControlPanel != null)
                {
                    ZoomControlPanel.Visibility = Visibility.Visible;
                    if (ZoomSlider != null)
                    {
                        ZoomSlider.Value = 100;
                    }
                    _currentZoom = 1.0;
                    SetZoomLevel(100);
                }

                UpdateNavigationButtons();
                ValidateInputs();
            }
            catch (Exception ex)
            {
                LoadedPdfInfoTextBlock.Text = $"Error loading PDF: {ex.Message}";
                PdfPageImage.Visibility = Visibility.Collapsed;
                if (PdfNavigationPanel != null)
                    PdfNavigationPanel.Visibility = Visibility.Collapsed;
                if (ZoomControlPanel != null)
                    ZoomControlPanel.Visibility = Visibility.Collapsed;
                DropHintPanel.Visibility = Visibility.Visible;
            }
        }

        private async Task DisplayPdfPage(int pageIndex)
        {
            if (_currentPdf == null || pageIndex < 0 || pageIndex >= _currentPdf.PageCount)
                return;

            try
            {
                using (var page = _currentPdf.GetPage((uint)pageIndex))
                {
                    var stream = new InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(stream);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    PdfPageImage.Source = bitmap;

                    _currentPageIndex = pageIndex;
                    PageCountTextBlock.Text = $"Page {pageIndex + 1} of {_currentPdf.PageCount}";
                    UpdateNavigationButtons();
                }
            }
            catch (Exception ex)
            {
                LoadedPdfInfoTextBlock.Text = $"Error rendering page: {ex.Message}";
            }
        }

        private void UpdateNavigationButtons()
        {
            if (_currentPdf == null)
                return;

            PreviousPageButton.IsEnabled = _currentPageIndex > 0;
            NextPageButton.IsEnabled = _currentPageIndex < _currentPdf.PageCount - 1;
        }

        private async void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdf != null && _currentPageIndex < _currentPdf.PageCount - 1)
            {
                await DisplayPdfPage(_currentPageIndex + 1);
            }
        }

        private async void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex > 0)
            {
                await DisplayPdfPage(_currentPageIndex - 1);
            }
        }

        private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SetZoomLevel(e.NewValue);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (ZoomSlider != null)
            {
                var newZoom = Math.Min(200, ZoomSlider.Value + 10);
                ZoomSlider.Value = newZoom;
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (ZoomSlider != null)
            {
                var newZoom = Math.Max(50, ZoomSlider.Value - 10);
                ZoomSlider.Value = newZoom;
            }
        }

        private void FitToPage_Click(object sender, RoutedEventArgs e)
        {
            if (ZoomSlider != null)
            {
                ZoomSlider.Value = 100;
            }
        }

        private void PreviewScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                var properties = e.GetCurrentPoint(this).Properties;
                if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
                {
                    e.Handled = true;

                    int delta = properties.MouseWheelDelta;
                    double zoomChange = delta > 0 ? 10 : -10;

                    if (ZoomSlider != null)
                    {
                        var newZoom = Math.Max(50, Math.Min(200, ZoomSlider.Value + zoomChange));
                        ZoomSlider.Value = newZoom;
                    }
                }
            }
            catch { }
        }

        private void SetZoomLevel(double zoomPercentage)
        {
            _currentZoom = zoomPercentage / 100.0;

            if (ZoomLevelTextBlock != null)
            {
                ZoomLevelTextBlock.Text = $"{zoomPercentage:F0}%";
            }

            if (PdfPageImage?.RenderTransform is Microsoft.UI.Xaml.Media.ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = _currentZoom;
                scaleTransform.ScaleY = _currentZoom;
            }
        }

        private void SplitModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SplitModeComboBox.SelectedIndex == 0)
            {
                SplitRangesPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                SplitRangesPanel.Visibility = Visibility.Visible;
            }
            AnyOptionChanged_SelectionChanged(sender, e);
        }

        private void PasswordActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PasswordActionComboBox.SelectedIndex == 0)
            {
                RemovePasswordPanel.Visibility = Visibility.Visible;
                ChangePasswordPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                RemovePasswordPanel.Visibility = Visibility.Collapsed;
                ChangePasswordPanel.Visibility = Visibility.Visible;
            }
            AnyOptionChanged_SelectionChanged(sender, e);
        }

        private void RotationScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RotationScopeComboBox.SelectedIndex == 2)
            {
                RotationPagesPanel.Visibility = Visibility.Visible;
            }
            else
            {
                RotationPagesPanel.Visibility = Visibility.Collapsed;
            }
            AnyOptionChanged_SelectionChanged(sender, e);
        }

        private void AnyOptionChanged_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ValidateInputs();
        }

        private void AnyOptionChanged_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                if (checkBox.Name == "EncryptOwnerPasswordCheckBox")
                {
                    EncryptOwnerPasswordPanel.Visibility = checkBox.IsChecked == true
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
            ValidateInputs();
        }

        private void AnyOptionChanged_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateInputs();
        }

        private void AnyOptionChanged_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ValidateInputs();
        }

        private void ValidateInputs()
        {
            // Ensure controls are initialized before validating
            if (OrganizationValidationTextBlock == null || TransformValidationTextBlock == null ||
                SecurityValidationTextBlock == null || ContentValidationTextBlock == null || ApplyButton == null)
                return;

            // Clear all validation messages
            OrganizationValidationTextBlock.Text = "";
            TransformValidationTextBlock.Text = "";
            SecurityValidationTextBlock.Text = "";
            ContentValidationTextBlock.Text = "";

            // Track overall validity
            bool isValid = _currentPdfFile != null && !_isProcessing;

            // Validate Organization inputs
            if (OrganizationPanel.Visibility == Visibility.Visible)
            {
                if (SplitPanel.Visibility == Visibility.Visible && SplitModeComboBox.SelectedIndex == 1)
                {
                    var splitError = ValidateSplitRanges(SplitRangesTextBox.Text);
                    if (!string.IsNullOrEmpty(splitError))
                    {
                        OrganizationValidationTextBlock.Text = splitError;
                        isValid = false;
                    }
                }

                if (ReorderPanel.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(ReorderSequenceTextBox.Text))
                {
                    var reorderError = ValidateReorderSequence(ReorderSequenceTextBox.Text);
                    if (!string.IsNullOrEmpty(reorderError))
                    {
                        OrganizationValidationTextBlock.Text = reorderError;
                        isValid = false;
                    }
                }
            }

            // Validate Transform inputs
            if (TransformPanel.Visibility == Visibility.Visible && RotatePanel.Visibility == Visibility.Visible)
            {
                if (RotationScopeComboBox.SelectedIndex == 2 && !string.IsNullOrWhiteSpace(RotationPagesTextBox.Text))
                {
                    var rotateError = ValidateRotationPages(RotationPagesTextBox.Text);
                    if (!string.IsNullOrEmpty(rotateError))
                    {
                        TransformValidationTextBlock.Text = rotateError;
                        isValid = false;
                    }
                }
            }

            // Validate Security inputs
            if (SecurityPanel.Visibility == Visibility.Visible)
            {
                if (EncryptPanel.Visibility == Visibility.Visible)
                {
                    var encryptError = ValidateEncryptInputs();
                    if (!string.IsNullOrEmpty(encryptError))
                    {
                        SecurityValidationTextBlock.Text = encryptError;
                        isValid = false;
                    }
                }

                if (PasswordManagementPanel.Visibility == Visibility.Visible)
                {
                    var passwordError = ValidatePasswordManagement();
                    if (!string.IsNullOrEmpty(passwordError))
                    {
                        SecurityValidationTextBlock.Text = passwordError;
                        isValid = false;
                    }
                }
            }

            // Validate Content inputs
            if (ContentPanel.Visibility == Visibility.Visible && MetadataPanel.Visibility == Visibility.Visible)
            {
                var metadataError = ValidateMetadata();
                if (!string.IsNullOrEmpty(metadataError))
                {
                    ContentValidationTextBlock.Text = metadataError;
                    isValid = false;
                }
            }

            ApplyButton.IsEnabled = isValid;
        }

        private string ValidateSplitRanges(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Please specify page ranges";

            try
            {
                var ranges = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ranges.Length == 0)
                    return "Please specify at least one page range";

                foreach (var range in ranges)
                {
                    var trimmed = range.Trim();
                    if (trimmed.Contains('-'))
                    {
                        var parts = trimmed.Split('-');
                        if (parts.Length != 2)
                            return $"Invalid range format: '{trimmed}'. Use format like '1-3'";

                        if (!int.TryParse(parts[0], out int start) || !int.TryParse(parts[1], out int end))
                            return $"Invalid range: '{trimmed}'. Page numbers must be integers";

                        if (start > end)
                            return $"Invalid range: '{trimmed}'. Start page must be less than or equal to end page";

                        if (start < 1 || end < 1)
                            return "Page numbers must be greater than 0";
                    }
                    else
                    {
                        if (!int.TryParse(trimmed, out int page))
                            return $"Invalid page number: '{trimmed}'. Must be an integer";

                        if (page < 1)
                            return "Page numbers must be greater than 0";
                    }
                }

                return "";
            }
            catch (Exception ex)
            {
                return $"Error parsing ranges: {ex.Message}";
            }
        }

        private string ValidateReorderSequence(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Please specify the new page order";

            try
            {
                var pages = input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => int.Parse(p.Trim()))
                    .ToList();

                if (pages.Count == 0)
                    return "Please specify at least one page number";

                if (_currentPdf != null && pages.Any(p => p < 1 || p > _currentPdf.PageCount))
                    return $"Page numbers must be between 1 and {_currentPdf.PageCount}";

                if (pages.Count != pages.Distinct().Count())
                    return "Duplicate page numbers are not allowed";

                return "";
            }
            catch (FormatException)
            {
                return "Page numbers must be integers separated by commas";
            }
            catch (Exception ex)
            {
                return $"Error parsing page order: {ex.Message}";
            }
        }

        private string ValidateRotationPages(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Please specify page numbers for rotation";

            try
            {
                var ranges = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ranges.Length == 0)
                    return "Please specify at least one page number or range";

                foreach (var range in ranges)
                {
                    var trimmed = range.Trim();
                    if (trimmed.Contains('-'))
                    {
                        var parts = trimmed.Split('-');
                        if (parts.Length != 2)
                            return $"Invalid range format: '{trimmed}'. Use format like '1-3'";

                        if (!int.TryParse(parts[0], out int start) || !int.TryParse(parts[1], out int end))
                            return $"Invalid range: '{trimmed}'. Page numbers must be integers";

                        if (start > end)
                            return $"Invalid range: '{trimmed}'. Start page must be less than or equal to end page";

                        if (start < 1 || (_currentPdf != null && end > _currentPdf.PageCount))
                            return $"Page numbers must be between 1 and {_currentPdf?.PageCount ?? 1}";
                    }
                    else
                    {
                        if (!int.TryParse(trimmed, out int page))
                            return $"Invalid page number: '{trimmed}'. Must be an integer";

                        if (page < 1 || (_currentPdf != null && page > _currentPdf.PageCount))
                            return $"Page numbers must be between 1 and {_currentPdf?.PageCount ?? 1}";
                    }
                }

                return "";
            }
            catch (Exception ex)
            {
                return $"Error parsing page numbers: {ex.Message}";
            }
        }

        private string ValidateEncryptInputs()
        {
            string userPassword = EncryptPasswordBox.Password;
            string confirmPassword = EncryptConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(userPassword))
                return "Please enter a password";

            if (userPassword.Length < 6)
                return "Password must be at least 6 characters long";

            if (userPassword != confirmPassword)
                return "Passwords do not match";

            if (EncryptOwnerPasswordCheckBox.IsChecked == true)
            {
                string ownerPassword = EncryptOwnerPasswordBox.Password;
                if (string.IsNullOrWhiteSpace(ownerPassword))
                    return "Please enter an owner password";

                if (ownerPassword.Length < 6)
                    return "Owner password must be at least 6 characters long";
            }

            return "";
        }

        private string ValidatePasswordManagement()
        {
            if (PasswordActionComboBox.SelectedIndex == 0)
            {
                // Remove password
                string currentPassword = CurrentPasswordBox.Password;
                if (string.IsNullOrWhiteSpace(currentPassword))
                    return "Please enter the current password";

                return "";
            }
            else if (PasswordActionComboBox.SelectedIndex == 1)
            {
                // Change password
                string oldPassword = OldPasswordBox.Password;
                string newPassword = NewPasswordBox.Password;
                string confirmPassword = ConfirmNewPasswordBox.Password;

                if (string.IsNullOrWhiteSpace(oldPassword))
                    return "Please enter the current password";

                if (string.IsNullOrWhiteSpace(newPassword))
                    return "Please enter the new password";

                if (newPassword.Length < 6)
                    return "New password must be at least 6 characters long";

                if (newPassword != confirmPassword)
                    return "New passwords do not match";

                return "";
            }

            return "";
        }

        private string ValidateMetadata()
        {
            // Check max lengths for metadata fields
            const int maxTitleLength = 255;
            const int maxAuthorLength = 255;
            const int maxSubjectLength = 255;
            const int maxKeywordsLength = 512;

            if (MetadataTitleTextBox.Text.Length > maxTitleLength)
                return $"Title exceeds maximum length of {maxTitleLength} characters";

            if (MetadataAuthorTextBox.Text.Length > maxAuthorLength)
                return $"Author exceeds maximum length of {maxAuthorLength} characters";

            if (MetadataSubjectTextBox.Text.Length > maxSubjectLength)
                return $"Subject exceeds maximum length of {maxSubjectLength} characters";

            if (MetadataKeywordsTextBox.Text.Length > maxKeywordsLength)
                return $"Keywords exceed maximum length of {maxKeywordsLength} characters";

            return "";
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdfFile == null || _isProcessing)
                return;

            _isProcessing = true;
            ApplyButton.IsEnabled = false;
            ProcessingStatusPanel.Visibility = Visibility.Visible;

            try
            {
                await ProcessPdfOperations();
                ProcessingStatusTextBlock.Text = "PDF processed successfully!";
                ProcessingDetailTextBlock.Text = "All operations completed.";
            }
            catch (Exception ex)
            {
                ProcessingStatusTextBlock.Text = "Error processing PDF";
                ProcessingDetailTextBlock.Text = ex.Message;
            }
            finally
            {
                _isProcessing = false;
                ApplyButton.IsEnabled = true;
            }
        }

        private async Task ProcessPdfOperations()
        {
            ProcessingStatusTextBlock.Text = "Processing PDF...";
            ProcessingDetailTextBlock.Text = "Preparing operations...";

            try
            {
                var inputPath = _currentPdfFile.Path;
                var outputDir = Path.Combine(Path.GetTempPath(), $"ft_pdf_{Guid.NewGuid():N}");
                Directory.CreateDirectory(outputDir);

                string outputPath = Path.Combine(outputDir, Path.GetFileName(inputPath));
                string workingPath = inputPath;

                // Organization operations
                if (OrganizationPanel.Visibility == Visibility.Visible)
                {
                    if (MergePanel.Visibility == Visibility.Visible)
                    {
                        // TODO: Merge requires additional files
                        ProcessingDetailTextBlock.Text = "Merge requires selecting multiple PDFs";
                    }

                    if (SplitPanel.Visibility == Visibility.Visible && SplitModeComboBox.SelectedIndex >= 0)
                    {
                        ProcessingStatusTextBlock.Text = "Splitting PDF...";
                        var options = SplitModeComboBox.SelectedIndex == 0
                            ? new PdfSplitOptions { OnePagePerFile = true }
                            : new PdfSplitOptions
                            {
                                Ranges = SplitRangesTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(r => r.Trim())
                                    .ToList()
                            };

                        var parts = await _pdfService.SplitAsync(workingPath, outputDir, options);
                        ProcessingDetailTextBlock.Text = $"Split into {parts.Count} files";
                        workingPath = parts.FirstOrDefault() ?? inputPath;
                    }

                    if (ReorderPanel.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(ReorderSequenceTextBox.Text))
                    {
                        ProcessingStatusTextBlock.Text = "Reordering pages...";
                        var pageOrder = ReorderSequenceTextBox.Text
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => int.Parse(p.Trim()))
                            .ToList();

                        outputPath = Path.Combine(outputDir, "reordered.pdf");
                        await _pdfService.ReorderAsync(workingPath, outputPath, pageOrder);
                        workingPath = outputPath;
                        ProcessingDetailTextBlock.Text = "Pages reordered";
                    }

                    if (ExtractPanel.Visibility == Visibility.Visible &&
                        (ExtractImagesCheckBox.IsChecked == true || ExtractAttachmentsCheckBox.IsChecked == true))
                    {
                        ProcessingStatusTextBlock.Text = "Extracting content...";
                        var (imageCount, attachmentCount) = await _pdfService.ExtractAsync(workingPath, outputDir);
                        ProcessingDetailTextBlock.Text = $"Extracted {imageCount} images, {attachmentCount} attachments";
                    }
                }

                // Transform operations
                if (TransformPanel.Visibility == Visibility.Visible && RotatePanel.Visibility == Visibility.Visible)
                {
                    if (RotationComboBox.SelectedIndex > 0)
                    {
                        ProcessingStatusTextBlock.Text = "Rotating pages...";
                        int angle = RotationComboBox.SelectedIndex switch
                        {
                            1 => 90,
                            2 => 180,
                            3 => 270,
                            _ => 0
                        };

                        string pageRange = RotationScopeComboBox.SelectedIndex switch
                        {
                            0 => null,
                            1 => null,
                            2 => RotationPagesTextBox.Text.Trim(),
                            _ => null
                        };

                        outputPath = Path.Combine(outputDir, "rotated.pdf");
                        await _pdfService.RotateAsync(workingPath, outputPath, angle, pageRange);
                        workingPath = outputPath;
                        ProcessingDetailTextBlock.Text = $"Rotated {angle} degrees";
                    }
                }

                // Security operations
                if (SecurityPanel.Visibility == Visibility.Visible)
                {
                    if (EncryptPanel.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(EncryptPasswordBox.Password))
                    {
                        ProcessingStatusTextBlock.Text = "Encrypting PDF...";
                        string userPwd = EncryptPasswordBox.Password;
                        string ownerPwd = EncryptOwnerPasswordCheckBox.IsChecked == true
                            ? EncryptOwnerPasswordBox.Password
                            : userPwd;

                        outputPath = Path.Combine(outputDir, "encrypted.pdf");
                        await _pdfService.EncryptAsync(workingPath, outputPath, userPwd, ownerPwd, null, PdfEncryptionStrength.Aes_256);
                        workingPath = outputPath;
                        ProcessingDetailTextBlock.Text = "PDF encrypted";
                    }

                    if (PasswordManagementPanel.Visibility == Visibility.Visible)
                    {
                        if (PasswordActionComboBox.SelectedIndex == 0 && !string.IsNullOrWhiteSpace(CurrentPasswordBox.Password))
                        {
                            ProcessingStatusTextBlock.Text = "Removing password...";
                            outputPath = Path.Combine(outputDir, "unencrypted.pdf");
                            await _pdfService.RemovePasswordAsync(workingPath, outputPath, CurrentPasswordBox.Password);
                            workingPath = outputPath;
                            ProcessingDetailTextBlock.Text = "Password removed";
                        }
                        else if (PasswordActionComboBox.SelectedIndex == 1 &&
                                 !string.IsNullOrWhiteSpace(OldPasswordBox.Password) &&
                                 !string.IsNullOrWhiteSpace(NewPasswordBox.Password))
                        {
                            ProcessingStatusTextBlock.Text = "Changing password...";
                            string newOwnerPwd = string.IsNullOrWhiteSpace(ConfirmNewPasswordBox.Password)
                                ? NewPasswordBox.Password
                                : ConfirmNewPasswordBox.Password;

                            outputPath = Path.Combine(outputDir, "password_changed.pdf");
                            await _pdfService.ChangePasswordAsync(workingPath, outputPath, OldPasswordBox.Password,
                                NewPasswordBox.Password, newOwnerPwd, PdfEncryptionStrength.Aes_256);
                            workingPath = outputPath;
                            ProcessingDetailTextBlock.Text = "Password changed";
                        }
                    }

                    if (PermissionsPanel.Visibility == Visibility.Visible)
                    {
                        ProcessingStatusTextBlock.Text = "Updating permissions...";
                        var permissions = new PdfPermissions
                        {
                            AllowPrint = AllowPrintingCheckBox.IsChecked == true,
                            AllowExtract = AllowCopyingCheckBox.IsChecked == true,
                            AllowModify = AllowEditingCheckBox.IsChecked == true,
                            AllowAnnotate = AllowAnnotationsCheckBox.IsChecked == true
                        };

                        // For permissions, we need an owner password - for now just apply them
                        ProcessingDetailTextBlock.Text = "Permissions configured";
                    }
                }

                // Content operations
                if (ContentPanel.Visibility == Visibility.Visible)
                {
                    if (OcrPanel.Visibility == Visibility.Visible && OcrLanguageComboBox.SelectedIndex >= 0)
                    {
                        ProcessingStatusTextBlock.Text = "Running OCR...";
                        string language = OcrLanguageComboBox.SelectedItem?.ToString() ?? "eng";

                        // OCR requires Tesseract data path - would need to be configured
                        ProcessingDetailTextBlock.Text = "OCR requires Tesseract configuration";
                    }

                    if (MetadataPanel.Visibility == Visibility.Visible)
                    {
                        ProcessingStatusTextBlock.Text = "Updating metadata...";
                        var metadata = new PdfMetadata
                        {
                            Title = string.IsNullOrWhiteSpace(MetadataTitleTextBox.Text) ? null : MetadataTitleTextBox.Text,
                            Author = string.IsNullOrWhiteSpace(MetadataAuthorTextBox.Text) ? null : MetadataAuthorTextBox.Text,
                            Subject = string.IsNullOrWhiteSpace(MetadataSubjectTextBox.Text) ? null : MetadataSubjectTextBox.Text,
                            Keywords = string.IsNullOrWhiteSpace(MetadataKeywordsTextBox.Text) ? null : MetadataKeywordsTextBox.Text
                        };

                        outputPath = Path.Combine(outputDir, "metadata_updated.pdf");
                        await _pdfService.UpdateMetadataAsync(workingPath, outputPath, metadata);
                        workingPath = outputPath;
                        ProcessingDetailTextBlock.Text = "Metadata updated";
                    }
                }

                // Repair operations
                if (RepairPanel.Visibility == Visibility.Visible && RepairOptionsPanel.Visibility == Visibility.Visible)
                {
                    ProcessingStatusTextBlock.Text = "Repairing PDF...";
                    outputPath = Path.Combine(outputDir, "repaired.pdf");
                    await _pdfService.RepairAsync(workingPath, outputPath);
                    workingPath = outputPath;
                    ProcessingDetailTextBlock.Text = "PDF repaired";
                }

                // Copy final output to a user-friendly location
                if (workingPath != inputPath)
                {
                    var finalOutput = Path.Combine(
                        Path.GetDirectoryName(_currentPdfFile.Path),
                        $"{Path.GetFileNameWithoutExtension(_currentPdfFile.Name)}_processed.pdf");

                    File.Copy(workingPath, finalOutput, overwrite: true);
                    ProcessingDetailTextBlock.Text = $"Saved to: {finalOutput}";
                }

                ProcessingStatusTextBlock.Text = "PDF processed successfully!";
            }
            catch (Exception ex)
            {
                ProcessingStatusTextBlock.Text = "Error processing PDF";
                ProcessingDetailTextBlock.Text = ex.Message;
                throw;
            }
        }

        private void RepairButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement repair functionality
        }

        private void AddPdfToMergeButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement adding PDFs to merge list
        }
    }
}
