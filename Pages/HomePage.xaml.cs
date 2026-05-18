using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Numerics;

namespace Files_Tools.Pages
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
            UpdateResponsiveLayout(1200);
        }

        private void ImagesCard_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(typeof(ImageEditorPage), null, new DrillInNavigationTransitionInfo());
        }

        private void VideoCard_Click(object sender, RoutedEventArgs e)
        {
            // Video page will be connected when implemented.
        }

        private void DocumentsCard_Click(object sender, RoutedEventArgs e)
        {
            // Documents page will be connected when implemented.
        }

        private void AudioCard_Click(object sender, RoutedEventArgs e)
        {
            // Audio page will be connected when implemented.
        }

        private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement card)
            {
                return;
            }

            AnimateCard(card, scale: 1.03f, durationMs: 220);
        }

        private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement card)
            {
                return;
            }

            AnimateCard(card, scale: 1.0f, durationMs: 220);
        }

        private static void AnimateCard(FrameworkElement card, float scale, int durationMs)
        {
            var visual = ElementCompositionPreview.GetElementVisual(card);
            var compositor = visual.Compositor;

            visual.CenterPoint = new Vector3((float)card.ActualWidth / 2, (float)card.ActualHeight / 2, 0f);

            var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.InsertKeyFrame(1.0f, new Vector3(scale, scale, 1.0f));
            scaleAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);

            visual.StartAnimation(nameof(visual.Scale), scaleAnimation);
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

            var availableContentWidth = Math.Max(320, windowWidth - pagePadding.Left - pagePadding.Right);
            ContentContainer.Width = Math.Min(1240, availableContentWidth);

            var useSingleColumn = ContentContainer.Width < 860;

            HeaderText.FontSize = useSingleColumn ? 34 : 44;
            SubtitleText.FontSize = useSingleColumn ? 16 : 19;
            CardsGrid.ColumnSpacing = useSingleColumn ? 0 : 28;
            CardsGrid.RowSpacing = useSingleColumn ? 16 : 24;

            ApplyCardLayout(useSingleColumn);
        }

        private void ApplyCardLayout(bool singleColumn)
        {
            CardsColumnLeft.Width = new GridLength(1, GridUnitType.Star);
            CardsColumnRight.Width = singleColumn
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);

            if (singleColumn)
            {
                PositionCard(ImagesCardButton, row: 0, column: 0, centered: true);
                PositionCard(VideoCardButton, row: 1, column: 0, centered: true);
                PositionCard(DocumentsCardButton, row: 2, column: 0, centered: true);
                PositionCard(AudioCardButton, row: 3, column: 0, centered: true);
                return;
            }

            PositionCard(ImagesCardButton, row: 0, column: 0, centered: false);
            PositionCard(VideoCardButton, row: 0, column: 1, centered: false);
            PositionCard(DocumentsCardButton, row: 1, column: 0, centered: false);
            PositionCard(AudioCardButton, row: 1, column: 1, centered: false);
        }

        private static void PositionCard(Button card, int row, int column, bool centered)
        {
            Grid.SetRow(card, row);
            Grid.SetColumn(card, column);
            card.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            card.MaxWidth = centered ? 640 : double.PositiveInfinity;
        }
    }
}
