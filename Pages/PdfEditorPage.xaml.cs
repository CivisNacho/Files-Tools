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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Files_Tools.Helpers;
using Files_Tools.Services;

namespace Files_Tools.Pages
{
    public sealed partial class PdfEditorPage : Page
    {
        private PdfService _pdfService;
        private StorageFile _currentPdfFile;
        private PdfDocument _currentPdf;
        private long? _currentPdfFileSizeBytes;
        private int _currentPageIndex;
        private bool _isProcessing;
        private double _currentZoom = 1.0;
        private double _naturalPageWidth;
        private double _naturalPageHeight;
        private readonly List<string> _mergeFilePaths = new();
        private string? _tessDataPath;
        private PdfMetadata? _loadedMetadata;
        private bool _isTessDataDownloading;
        private CancellationTokenSource? _tessDataDownloadCts;
        private static readonly HttpClient _httpClient = new();

        public PdfEditorPage()
        {
            this.InitializeComponent();
            _pdfService = new PdfService();
            LoadSavedTessDataPath();
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

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _tessDataDownloadCts?.Cancel();
        }

        private void InitializeNavigation()
        {
            SelectedOptionHeaderTextBlock.Text = "Select a category from the left";
            HideAllOperationPanels();
        }

        public void HandleNavigationViewSelection(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                HideAllOperationPanels();
                UpdateFileInfoPanel();
                return;
            }

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

        private void UpdateFileInfoPanel()
        {
            if (_currentPdfFile is null || _currentPdf is null)
            {
                FileInfoPanel.Visibility = Visibility.Collapsed;
                return;
            }

            FileInfoNameTextBlock.Text = _currentPdfFile.Name;
            FileInfoPagesTextBlock.Text = _currentPdf.PageCount.ToString();
            FileInfoSizeTextBlock.Text = _currentPdfFileSizeBytes.HasValue ? FormatFileSize(_currentPdfFileSizeBytes.Value) : "—";
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

        private void HideAllOperationPanels()
        {
            FileInfoPanel.Visibility = Visibility.Collapsed;
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
                OptionsPanel.MinWidth = 260;
                UploadSurface.MaxHeight = double.PositiveInfinity;
            }
            else
            {
                EditorGrid.ColumnDefinitions[0].Width = new GridLength(7, GridUnitType.Star);
                EditorGrid.ColumnDefinitions[1].Width = new GridLength(3, GridUnitType.Star);
                Grid.SetColumnSpan(OptionsPanel, 1);
                Grid.SetRow(OptionsPanel, 0);
                Grid.SetColumn(OptionsPanel, 1);
                Grid.SetRowSpan(OptionsPanel, 2);
                OptionsPanel.MinWidth = 280;
                UploadSurface.MaxHeight = double.PositiveInfinity;
            }
        }

        private void UploadSurface_DragOver(object sender, DragEventArgs e)
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
            if (App.MainWindow is null || _currentPdf != null) return;
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".pdf");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
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
                _mergeFilePaths.Clear();
                _currentPdfFile = file;
                _currentPdf = await PdfDocument.LoadFromFileAsync(file);
                _currentPageIndex = 0;

                await DisplayPdfPage(0);

                var properties = await file.GetBasicPropertiesAsync();
                _currentPdfFileSizeBytes = (long)properties.Size;
                var sizeInMb = (properties.Size / (1024.0 * 1024.0)).ToString("F2");
                LoadedPdfInfoTextBlock.Text = $"{file.Name} ({sizeInMb} MB) — {_currentPdf.PageCount} pages";
                UpdateFileInfoPanel();

                PdfPageImage.Visibility = Visibility.Visible;
                PdfNavigationPanel.Visibility = Visibility.Visible;
                DropHintPanel.Visibility = Visibility.Collapsed;
                PageCountTextBlock.Text = $"Page 1 of {_currentPdf.PageCount}";

                if (ZoomControlPanel != null)
                {
                    ZoomControlPanel.Visibility = Visibility.Visible;
                    _currentZoom = 1.0;
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        if (!FitPageToWidth() && ZoomSlider != null)
                            ZoomSlider.Value = 100;
                    });
                }

                UpdateNavigationButtons();
                RefreshMergeFilesList();
                await LoadMetadataIntoForm(file.Path);
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

                    _naturalPageWidth = bitmap.PixelWidth > 0 ? bitmap.PixelWidth : 595;
                    _naturalPageHeight = bitmap.PixelHeight > 0 ? bitmap.PixelHeight : 842;
                    PdfPageImage.Width = _naturalPageWidth * _currentZoom;
                    PdfPageImage.Height = _naturalPageHeight * _currentZoom;

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
            FitPageToWidth();
        }

        private bool FitPageToWidth()
        {
            if (_naturalPageWidth <= 0 || ZoomSlider == null) return false;

            double available = PreviewScrollViewer.ActualWidth - 32;
            if (available <= 0) return false;

            double fitZoom = (available / _naturalPageWidth) * 100.0;
            fitZoom = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, fitZoom));
            ZoomSlider.Value = fitZoom;
            return true;
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
                ZoomLevelTextBlock.Text = $"{zoomPercentage:F0}%";

            if (_naturalPageWidth > 0 && PdfPageImage != null)
            {
                PdfPageImage.Width = _naturalPageWidth * _currentZoom;
                PdfPageImage.Height = _naturalPageHeight * _currentZoom;
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
                if (MergePanel.Visibility == Visibility.Visible && _mergeFilePaths.Count == 0)
                {
                    OrganizationValidationTextBlock.Text = "Add at least one PDF to merge with the loaded file.";
                    isValid = false;
                }

                if (SplitPanel.Visibility == Visibility.Visible)
                {
                    if (SplitModeComboBox.SelectedIndex < 0)
                    {
                        OrganizationValidationTextBlock.Text = "Choose a split mode.";
                        isValid = false;
                    }
                    else if (SplitModeComboBox.SelectedIndex == 1)
                    {
                        var splitError = ValidateSplitRanges(SplitRangesTextBox.Text);
                        if (!string.IsNullOrEmpty(splitError))
                        {
                            OrganizationValidationTextBlock.Text = splitError;
                            isValid = false;
                        }
                    }
                }

                if (ExtractPanel.Visibility == Visibility.Visible &&
                    ExtractImagesCheckBox.IsChecked != true &&
                    ExtractAttachmentsCheckBox.IsChecked != true)
                {
                    OrganizationValidationTextBlock.Text = "Select images, attachments, or both to extract.";
                    isValid = false;
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
            if (ContentPanel.Visibility == Visibility.Visible)
            {
                if (OcrPanel.Visibility == Visibility.Visible)
                {
                    RefreshOcrDownloadPanel();

                    var ocrError = ValidateOcrInputs();
                    if (!string.IsNullOrEmpty(ocrError))
                    {
                        ContentValidationTextBlock.Text = ocrError;
                        isValid = false;
                    }
                }

                if (MetadataPanel.Visibility == Visibility.Visible)
                {
                    var metadataError = ValidateMetadata();
                    if (!string.IsNullOrEmpty(metadataError))
                    {
                        ContentValidationTextBlock.Text = metadataError;
                        isValid = false;
                    }
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

                // Duplicates are allowed — repeating a page number duplicates that page in the output.

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

        private async Task LoadMetadataIntoForm(string pdfPath)
        {
            try
            {
                _loadedMetadata = await _pdfService.ReadMetadataAsync(pdfPath);
            }
            catch
            {
                // Encrypted or unreadable Info dictionary — start from a blank form.
                _loadedMetadata = new PdfMetadata();
            }

            MetadataTitleTextBox.Text    = _loadedMetadata.Title    ?? "";
            MetadataAuthorTextBox.Text   = _loadedMetadata.Author   ?? "";
            MetadataSubjectTextBox.Text  = _loadedMetadata.Subject  ?? "";
            MetadataKeywordsTextBox.Text = _loadedMetadata.Keywords ?? "";
            MetadataCreatorTextBox.Text  = _loadedMetadata.Creator  ?? "";
            MetadataProducerTextBox.Text = _loadedMetadata.Producer ?? "";
        }

        /// <summary>
        /// Builds a metadata patch limited to the editable fields the user actually changed.
        /// Returns <c>null</c> when nothing changed. For each field: unchanged → not written
        /// (null, left as-is), cleared → empty string (drops the key), edited → the new value.
        /// </summary>
        private PdfMetadata? BuildMetadataPatch()
        {
            var loaded = _loadedMetadata ?? new PdfMetadata();

            string? Diff(string current, string? original)
            {
                original ??= "";
                if (current == original) return null;     // unchanged → leave untouched
                return current;                            // edited (empty current → clear)
            }

            var title    = Diff(MetadataTitleTextBox.Text,    loaded.Title);
            var author   = Diff(MetadataAuthorTextBox.Text,   loaded.Author);
            var subject  = Diff(MetadataSubjectTextBox.Text,  loaded.Subject);
            var keywords = Diff(MetadataKeywordsTextBox.Text, loaded.Keywords);

            if (title is null && author is null && subject is null && keywords is null)
                return null;

            return new PdfMetadata
            {
                Title    = title,
                Author   = author,
                Subject  = subject,
                Keywords = keywords,
            };
        }

        /// <summary>
        /// True when the active operation writes multiple files / a folder of results rather than
        /// a single output PDF, so the user must pick a destination folder instead of a file.
        /// </summary>
        private bool IsFolderOutputOperation()
        {
            return OrganizationPanel.Visibility == Visibility.Visible &&
                   (SplitPanel.Visibility == Visibility.Visible ||
                    ExtractPanel.Visibility == Visibility.Visible);
        }

        private void ShowProcessing(bool active)
        {
            ProcessingStatusPanel.Visibility = Visibility.Visible;
            ProcessingProgressBar.IsIndeterminate = active;
            if (!active)
                ProcessingProgressBar.Value = ProcessingProgressBar.Maximum;
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdfFile == null || _isProcessing || App.MainWindow is null)
                return;

            bool folderOutput = IsFolderOutputOperation();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

            string destination;
            string destinationName;

            if (folderOutput)
            {
                var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                folderPicker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder is null) return;
                destination = folder.Path;
                destinationName = folder.Name;
            }
            else
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"{Path.GetFileNameWithoutExtension(_currentPdfFile.Name)}_processed"
                };
                picker.FileTypeChoices.Add("PDF document", new List<string> { ".pdf" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var saveFile = await picker.PickSaveFileAsync();
                if (saveFile is null) return;
                destination = saveFile.Path;
                destinationName = saveFile.Name;
            }

            _isProcessing = true;
            ApplyButton.IsEnabled = false;
            ShowProcessing(true);

            try
            {
                await ProcessPdfOperations(destination, folderOutput);
                ProcessingStatusTextBlock.Text = "PDF processed successfully!";
                ProcessingDetailTextBlock.Text = folderOutput
                    ? $"Saved to folder: {destinationName}"
                    : $"Saved to: {destinationName}";
            }
            catch (Exception ex)
            {
                ProcessingStatusTextBlock.Text = "Error processing PDF";
                ProcessingDetailTextBlock.Text = ex.Message;
            }
            finally
            {
                _isProcessing = false;
                ShowProcessing(false);
                ApplyButton.IsEnabled = true;
            }
        }

        private async Task ProcessPdfOperations(string destination, bool folderOutput)
        {
            ProcessingStatusTextBlock.Text = "Processing PDF...";
            ProcessingDetailTextBlock.Text = "Preparing operations...";

            var inputPath = _currentPdfFile.Path;
            var outputDir = Path.Combine(Path.GetTempPath(), $"ft_pdf_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);

            try
            {
                string outputPath = Path.Combine(outputDir, Path.GetFileName(inputPath));
                string workingPath = inputPath;

                // Organization operations
                if (OrganizationPanel.Visibility == Visibility.Visible)
                {
                    if (MergePanel.Visibility == Visibility.Visible && _mergeFilePaths.Count > 0)
                    {
                        ProcessingStatusTextBlock.Text = "Merging PDFs...";
                        var allPaths = new List<string> { workingPath };
                        allPaths.AddRange(_mergeFilePaths);

                        outputPath = Path.Combine(outputDir, "merged.pdf");
                        await _pdfService.MergeAsync(allPaths, outputPath);
                        workingPath = outputPath;
                        ProcessingDetailTextBlock.Text = $"Merged {allPaths.Count} PDFs";
                    }

                    if (SplitPanel.Visibility == Visibility.Visible && SplitModeComboBox.SelectedIndex >= 0)
                    {
                        // Split produces multiple files; write them straight into the chosen folder.
                        ProcessingStatusTextBlock.Text = "Splitting PDF...";
                        var options = SplitModeComboBox.SelectedIndex == 0
                            ? new PdfSplitOptions { OnePagePerFile = true, OutputPrefix = Path.GetFileNameWithoutExtension(inputPath) }
                            : new PdfSplitOptions
                            {
                                OutputPrefix = Path.GetFileNameWithoutExtension(inputPath),
                                Ranges = SplitRangesTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(r => r.Trim())
                                    .ToList()
                            };

                        var parts = await _pdfService.SplitAsync(workingPath, destination, options);
                        ProcessingDetailTextBlock.Text = $"Split into {parts.Count} files";
                        return;
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
                        // Extract writes images/ and attachments/ folders into the chosen folder.
                        ProcessingStatusTextBlock.Text = "Extracting content...";
                        var (imageCount, attachmentCount) = await _pdfService.ExtractAsync(
                            workingPath,
                            destination,
                            Strings.Get("PdfPage_ImagesFolderName"),
                            Strings.Get("PdfPage_AttachmentsFolderName"));
                        ProcessingDetailTextBlock.Text = $"Extracted {imageCount} images, {attachmentCount} attachments";
                        return;
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
                        bool allowPrint = AllowPrintingCheckBox.IsChecked == true;
                        bool allowEdit = AllowEditingCheckBox.IsChecked == true;
                        var permissions = new PdfPermissions
                        {
                            AllowPrint = allowPrint,
                            AllowHighResPrint = allowPrint,
                            AllowExtract = AllowCopyingCheckBox.IsChecked == true,
                            AllowModify = allowEdit,
                            AllowAssemble = allowEdit,
                            AllowFormFill = allowEdit,
                            AllowAnnotate = AllowAnnotationsCheckBox.IsChecked == true
                        };

                        // Owner password is optional — leave blank to apply restrictions without
                        // any password. The service uses an empty owner password in that case.
                        outputPath = Path.Combine(outputDir, "permissions_updated.pdf");
                        await _pdfService.UpdatePermissionsAsync(
                            workingPath, outputPath, PermissionsOwnerPasswordBox.Password,
                            permissions, PdfEncryptionStrength.Aes_256);
                        workingPath = outputPath;
                        ProcessingDetailTextBlock.Text = "Permissions updated";
                    }
                }

                // Content operations
                if (ContentPanel.Visibility == Visibility.Visible)
                {
                    if (OcrPanel.Visibility == Visibility.Visible && OcrLanguageComboBox.SelectedIndex >= 0)
                    {
                        ProcessingStatusTextBlock.Text = "Running OCR…";
                        ProcessingDetailTextBlock.Text = "Rasterizing pages and recognizing text…";

                        var ocrOptions = new PdfOcrOptions
                        {
                            TessDataPath = _tessDataPath ?? DefaultTessDataPath,
                            Languages    = GetOcrLanguageCode(),
                            Dpi          = 300,
                        };

                        outputPath = Path.Combine(outputDir, "ocr.pdf");
                        await _pdfService.OcrAsync(workingPath, outputPath, ocrOptions);
                        workingPath = outputPath;
                        ProcessingDetailTextBlock.Text = "OCR complete";
                    }

                    if (MetadataPanel.Visibility == Visibility.Visible)
                    {
                        var metadata = BuildMetadataPatch();
                        if (metadata is not null)
                        {
                            ProcessingStatusTextBlock.Text = "Updating metadata...";
                            outputPath = Path.Combine(outputDir, "metadata_updated.pdf");
                            await _pdfService.UpdateMetadataAsync(workingPath, outputPath, metadata);
                            workingPath = outputPath;
                            ProcessingDetailTextBlock.Text = "Metadata updated";
                        }
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

                // Copy final output to the user's chosen location. If no operation ran, fall back
                // to copying the original so the user still gets the file they asked to save.
                var sourcePath = (workingPath != inputPath && File.Exists(workingPath)) ? workingPath : inputPath;
                File.Copy(sourcePath, destination, overwrite: true);

                ProcessingStatusTextBlock.Text = "PDF processed successfully!";
            }
            catch (Exception ex)
            {
                ProcessingStatusTextBlock.Text = "Error processing PDF";
                ProcessingDetailTextBlock.Text = ex.Message;
                throw;
            }
            finally
            {
                try { Directory.Delete(outputDir, recursive: true); } catch { /* best-effort temp cleanup */ }
            }
        }

        // ── OCR helpers ──────────────────────────────────────────────────────────

        private static readonly string DefaultTessDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FilesTools", "tessdata");

        private void LoadSavedTessDataPath()
        {
            // 1. Prefer an explicit user choice stored across sessions.
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            if (localSettings.Values.TryGetValue("OcrTessDataPath", out var saved) &&
                saved is string savedPath &&
                Directory.Exists(savedPath))
            {
                _tessDataPath = savedPath;
                OcrTessDataTextBox.Text = savedPath;
                return;
            }

            // 2. Auto-detect the well-known app-local location.
            if (Directory.Exists(DefaultTessDataPath) &&
                Directory.GetFiles(DefaultTessDataPath, "*.traineddata").Length > 0)
            {
                _tessDataPath = DefaultTessDataPath;
                OcrTessDataTextBox.Text = DefaultTessDataPath;
            }
        }

        private async void BrowseTessData_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is null) return;

            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            _tessDataPath = folder.Path;
            OcrTessDataTextBox.Text = folder.Path;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["OcrTessDataPath"] = folder.Path;
            ValidateInputs();
        }

        private string GetOcrLanguageCode()
        {
            if (OcrLanguageComboBox.SelectedItem is not ComboBoxItem item) return "eng";

            return item.Content?.ToString() switch
            {
                "English"     => "eng",
                "Spanish"     => "spa",
                "French"      => "fra",
                "German"      => "deu",
                "Chinese"     => "chi_sim",
                "Japanese"    => "jpn",
                "Auto-detect" => GetAutoDetectLanguages(),
                _             => "eng"
            };
        }

        private string GetAutoDetectLanguages()
        {
            if (string.IsNullOrEmpty(_tessDataPath) || !Directory.Exists(_tessDataPath))
                return "eng";

            var codes = Directory
                .GetFiles(_tessDataPath, "*.traineddata")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => !n.Equals("osd", StringComparison.OrdinalIgnoreCase) &&
                            !n.Equals("equ", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return codes.Length > 0 ? string.Join("+", codes) : "eng";
        }

        private string ValidateOcrInputs()
        {
            if (OcrLanguageComboBox.SelectedIndex < 0)
                return "Select a language for OCR.";

            var selectedContent = (OcrLanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            if (selectedContent == "Auto-detect")
            {
                if (string.IsNullOrEmpty(_tessDataPath) || !Directory.Exists(_tessDataPath))
                    return "Select a tessdata directory containing .traineddata language files.";

                bool hasAny = Directory
                    .GetFiles(_tessDataPath, "*.traineddata")
                    .Any(f => !Path.GetFileNameWithoutExtension(f).Equals("osd", StringComparison.OrdinalIgnoreCase) &&
                              !Path.GetFileNameWithoutExtension(f).Equals("equ", StringComparison.OrdinalIgnoreCase));
                if (!hasAny)
                    return "No .traineddata language files found in the tessdata directory.";
            }
            else
            {
                // Specific language — check for the pack, show inline download prompt if missing.
                var langCode = GetOcrLanguageCode();
                var tessDir  = !string.IsNullOrEmpty(_tessDataPath) ? _tessDataPath : DefaultTessDataPath;
                var trainedData = Path.Combine(tessDir, langCode + ".traineddata");

                if (!File.Exists(trainedData))
                    return "Language pack not found. Click Download below to get it automatically.";
            }

            return "";
        }

        private void RefreshOcrDownloadPanel()
        {
            if (OcrDownloadPanel is null) return;

            // Show only when a specific (non-auto) language is selected and its pack is absent.
            bool isSpecific = IsSpecificLanguageSelected();
            bool packMissing = isSpecific && !IsSelectedLanguagePackPresent();

            OcrDownloadPanel.Visibility = (packMissing && !_isTessDataDownloading)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (packMissing && isSpecific && DownloadTessDataButton is not null)
            {
                var langName = (OcrLanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "language";
                DownloadTessDataButton.Content = $"Download {langName} language pack";
            }
        }

        private bool IsSpecificLanguageSelected()
        {
            var content = (OcrLanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return !string.IsNullOrEmpty(content) && content != "Auto-detect";
        }

        private bool IsSelectedLanguagePackPresent()
        {
            var dir = !string.IsNullOrEmpty(_tessDataPath) ? _tessDataPath : DefaultTessDataPath;
            if (!Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, GetOcrLanguageCode() + ".traineddata"));
        }

        private const string TessDataFastBaseUrl =
            "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/";

        private async void DownloadTessData_Click(object sender, RoutedEventArgs e)
        {
            if (OcrLanguageComboBox.SelectedItem is not ComboBoxItem item) return;

            var langCode = GetOcrLanguageCode();
            var langName = item.Content?.ToString() ?? langCode;
            var targetDir = !string.IsNullOrEmpty(_tessDataPath) ? _tessDataPath : DefaultTessDataPath;
            var destFile  = Path.Combine(targetDir, langCode + ".traineddata");
            var url       = TessDataFastBaseUrl + langCode + ".traineddata";

            _tessDataDownloadCts?.Cancel();
            _tessDataDownloadCts = new CancellationTokenSource();
            var cts = _tessDataDownloadCts;

            _isTessDataDownloading = true;
            DownloadTessDataButton.IsEnabled = false;
            OcrDownloadProgressBar.Visibility = Visibility.Visible;
            OcrDownloadStatusTextBlock.Text = $"Downloading {langName} language pack…";

            try
            {
                Directory.CreateDirectory(targetDir);

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                await using var src  = await response.Content.ReadAsStreamAsync(cts.Token);
                await using var dest = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buf       = new byte[81920];
                long received = 0;
                int  chunk;

                while ((chunk = await src.ReadAsync(buf, cts.Token)) > 0)
                {
                    await dest.WriteAsync(buf.AsMemory(0, chunk), cts.Token);
                    received += chunk;

                    OcrDownloadStatusTextBlock.Text = totalBytes > 0
                        ? $"Downloading… {received / 1024:N0} KB / {totalBytes / 1024:N0} KB"
                        : $"Downloading… {received / 1024:N0} KB";
                }

                // Set tessdata path to the download dir if not already pointing there.
                if (_tessDataPath != targetDir)
                {
                    _tessDataPath = targetDir;
                    OcrTessDataTextBox.Text = targetDir;
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values["OcrTessDataPath"] = targetDir;
                }

                OcrDownloadStatusTextBlock.Text = $"{langName} language pack downloaded successfully.";
                OcrDownloadProgressBar.Visibility = Visibility.Collapsed;
                OcrDownloadPanel.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(destFile)) File.Delete(destFile); } catch { /* best-effort cleanup */ }
            }
            catch (Exception ex)
            {
                OcrDownloadStatusTextBlock.Text = $"Download failed: {ex.Message}";
                OcrDownloadProgressBar.Visibility = Visibility.Collapsed;
                try { if (File.Exists(destFile)) File.Delete(destFile); } catch { /* best-effort cleanup */ }
            }
            finally
            {
                _isTessDataDownloading = false;
                if (!cts.IsCancellationRequested)
                {
                    DownloadTessDataButton.IsEnabled = true;
                    ValidateInputs();
                }
                cts.Dispose();
            }
        }

        private async void RepairButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPdfFile == null || _isProcessing || App.MainWindow is null) return;

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"{Path.GetFileNameWithoutExtension(_currentPdfFile.Name)}_repaired"
            };
            picker.FileTypeChoices.Add("PDF document", new List<string> { ".pdf" });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var saveFile = await picker.PickSaveFileAsync();
            if (saveFile is null) return;

            _isProcessing = true;
            ApplyButton.IsEnabled = false;
            RepairStatusTextBlock.Text = "Repairing…";

            try
            {
                await _pdfService.RepairAsync(_currentPdfFile.Path, saveFile.Path);
                RepairStatusTextBlock.Text = $"Repaired successfully → {saveFile.Name}";
            }
            catch (Exception ex)
            {
                RepairStatusTextBlock.Text = $"Repair failed: {ex.Message}";
            }
            finally
            {
                _isProcessing = false;
                ValidateInputs();
            }
        }

        private async void AddPdfToMergeButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is null) return;

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".pdf");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                if (!_mergeFilePaths.Any(p => p.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
                    _mergeFilePaths.Add(file.Path);
            }

            RefreshMergeFilesList();
            ValidateInputs();
        }

        private void RemoveMergeFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int index && index >= 0 && index < _mergeFilePaths.Count)
            {
                _mergeFilePaths.RemoveAt(index);
                RefreshMergeFilesList();
                ValidateInputs();
            }
        }

        private void RefreshMergeFilesList()
        {
            MergeFilesList.Children.Clear();

            if (_currentPdfFile != null)
            {
                var primaryRow = BuildMergeRow(_currentPdfFile.Name, _currentPdfFile.Path, isPrimary: true, index: -1);
                MergeFilesList.Children.Add(primaryRow);
            }

            for (int i = 0; i < _mergeFilePaths.Count; i++)
            {
                var row = BuildMergeRow(Path.GetFileName(_mergeFilePaths[i]), _mergeFilePaths[i], isPrimary: false, index: i);
                MergeFilesList.Children.Add(row);
            }
        }

        private Grid BuildMergeRow(string name, string fullPath, bool isPrimary, int index)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = isPrimary ? 1.0 : 0.88
            };
            ToolTipService.SetToolTip(label, fullPath);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            if (!isPrimary)
            {
                var removeBtn = new Button
                {
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    Content = new FontIcon { Glyph = "", FontSize = 12 },
                    Tag = index
                };
                removeBtn.Click += RemoveMergeFile_Click;
                Grid.SetColumn(removeBtn, 1);
                grid.Children.Add(removeBtn);
            }

            return grid;
        }
    }
}
