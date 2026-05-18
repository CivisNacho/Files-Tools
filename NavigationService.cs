using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace Files_Tools
{
    public static class NavigationService
    {
        private static Frame? _frame;

        public static void Initialize(Frame frame)
        {
            _frame = frame;
        }

        public static bool Navigate(Type pageType, object? parameter = null, NavigationTransitionInfo? transition = null)
        {
            if (_frame is null)
            {
                return false;
            }

            return _frame.Navigate(pageType, parameter, transition);
        }
    }
}
