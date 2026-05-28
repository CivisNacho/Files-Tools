using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Files_Tools.Pages
{
    public sealed partial class HomePage : Page
    {
        private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
        private static readonly string[] SupportedVideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v"];
        private static readonly string[] SupportedAudioExtensions = [".mp3", ".aac", ".m4a", ".wav", ".flac", ".opus", ".ogg", ".ac3"];
        private static readonly string[] SupportedPdfExtensions = [".pdf"];
        private static readonly string[] SupportedDocumentExtensions = [".docx", ".doc", ".odt", ".pptx", ".ppt", ".odp", ".xlsx", ".xls", ".ods", ".csv"];

        public HomePage()
        {
            InitializeComponent();
            UpdateResponsiveLayout(1200);
        }

        private async void DropZoneSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            await PickAndRouteFilesAsync();
        }

        private async void SingleFileButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndRouteFileAsync();
        }

        private async void BatchFilesButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndRouteFilesAsync(forceMultiple: true);
        }

        private void DropZoneSurface_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop to continue";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void DropZoneSurface_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<StorageFile>().Where(IsSupportedLandingFile).ToList();
            if (files.Count == 0)
            {
                return;
            }

            RouteFiles(files);
        }

        private async Task PickAndRouteFileAsync()
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var picker = new FileOpenPicker();
            foreach (var extension in SupportedImageExtensions
                .Concat(SupportedVideoExtensions)
                .Concat(SupportedAudioExtensions)
                .Concat(SupportedPdfExtensions)
                .Concat(SupportedDocumentExtensions)
                .Append(".gif"))
            {
                if (!picker.FileTypeFilter.Contains(extension))
                {
                    picker.FileTypeFilter.Add(extension);
                }
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null)
            {
                return;
            }

            RouteFile(selectedFile);
        }

        private async Task PickAndRouteFilesAsync(bool forceMultiple = false)
        {
            if (App.MainWindow is null)
            {
                return;
            }

            var picker = new FileOpenPicker();
            foreach (var extension in SupportedImageExtensions
                .Concat(SupportedVideoExtensions)
                .Concat(SupportedAudioExtensions)
                .Concat(SupportedPdfExtensions)
                .Concat(SupportedDocumentExtensions)
                .Append(".gif"))
            {
                if (!picker.FileTypeFilter.Contains(extension))
                {
                    picker.FileTypeFilter.Add(extension);
                }
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles is null || selectedFiles.Count == 0)
            {
                return;
            }

            var supported = selectedFiles.Where(IsSupportedLandingFile).ToList();
            if (supported.Count == 0)
            {
                return;
            }

            if (forceMultiple || supported.Count > 1)
            {
                NavigateToPage(typeof(BatchEditorPage), new BatchNavigationRequest { Files = supported });
            }
            else
            {
                RouteFile(supported[0]);
            }
        }

        private void RouteFiles(IReadOnlyList<StorageFile> files)
        {
            if (files.Count == 1)
            {
                RouteFile(files[0]);
                return;
            }

            NavigateToPage(typeof(BatchEditorPage), new BatchNavigationRequest { Files = files });
        }

        private void RouteFile(StorageFile file)
        {
            var extension = file.FileType;
            if (SupportedImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                NavigateToPage(typeof(ImageEditorPage), new FileNavigationRequest { File = file });
                return;
            }

            if (IsSupportedVideoFile(file))
            {
                NavigateToPage(typeof(VideoEditorPage), new FileNavigationRequest { File = file });
                return;
            }

            if (IsSupportedAudioFile(file))
            {
                NavigateToPage(typeof(AudioEditorPage), new FileNavigationRequest { File = file });
                return;
            }

            if (IsSupportedPdfFile(file))
            {
                NavigateToPage(typeof(PdfEditorPage), new FileNavigationRequest { File = file });
                return;
            }

            if (IsSupportedDocumentFile(file))
            {
                NavigateToPage(typeof(DocumentEditorPage), new FileNavigationRequest { File = file });
            }
        }

        private static bool IsSupportedLandingFile(StorageFile file)
        {
            return SupportedImageExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase) ||
                   IsSupportedVideoFile(file) ||
                   IsSupportedAudioFile(file) ||
                   IsSupportedPdfFile(file) ||
                   IsSupportedDocumentFile(file);
        }

        private static bool IsSupportedVideoFile(StorageFile file)
        {
            return SupportedVideoExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase) ||
                   string.Equals(file.FileType, ".gif", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedAudioFile(StorageFile file)
        {
            return SupportedAudioExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsSupportedPdfFile(StorageFile file)
        {
            return SupportedPdfExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsSupportedDocumentFile(StorageFile file)
        {
            return SupportedDocumentExtensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase);
        }

        private static void NavigateToPage(Type pageType, object? parameter = null)
        {
            NavigationService.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
        }

        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }

        private void UpdateResponsiveLayout(double windowWidth)
        {
            if (windowWidth <= 0)
            {
                return;
            }

            var pagePadding = windowWidth < 760
                ? new Thickness(14, 14, 14, 18)
                : windowWidth < 1080
                    ? new Thickness(22, 18, 22, 24)
                    : new Thickness(32, 24, 32, 28);

            LayoutRoot.Padding = pagePadding;
            var compact = windowWidth < 760;
            HeaderText.FontSize = compact ? 34 : 44;
            SubtitleText.FontSize = compact ? 16 : 18;
        }
    }
}
