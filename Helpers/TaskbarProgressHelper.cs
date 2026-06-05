using System;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files_Tools.Helpers;

/// <summary>
/// Drives ITaskbarList3 to reflect operation progress in the app's taskbar button.
/// All methods are safe to call from the UI thread; COM and shell errors are swallowed
/// so a missing taskbar (server SKUs, RDP sessions without shell) never surfaces.
/// </summary>
internal static class TaskbarProgressHelper
{
    private static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    private static ITaskbarList3? _instance;
    private static bool _initialized;

    private static IntPtr AppWindowHwnd => App.MainWindow is null
        ? IntPtr.Zero
        : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

    private static ITaskbarList3? Instance()
    {
        if (_initialized)
        {
            return _instance;
        }

        _initialized = true;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidTaskbarList);
            if (type is null)
            {
                return _instance;
            }

            var obj = Activator.CreateInstance(type);
            if (obj is ITaskbarList3 list)
            {
                list.HrInit();
                _instance = list;
            }
        }
        catch
        {
            _instance = null;
        }

        return _instance;
    }

    /// <summary>Shows a green fill at <paramref name="fraction"/> (0–1) in the taskbar button.</summary>
    internal static void SetProgress(double fraction)
    {
        var hwnd = AppWindowHwnd;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var list = Instance();
            if (list is null)
            {
                return;
            }

            var handle = new HWND(hwnd);
            list.SetProgressState(handle, TBPFLAG.TBPF_NORMAL);
            list.SetProgressValue(handle, (ulong)Math.Clamp(fraction * 1000d, 0d, 1000d), 1000ul);
        }
        catch { }
    }

    /// <summary>Shows an animated indeterminate stripe in the taskbar button.</summary>
    internal static void SetIndeterminate()
    {
        var hwnd = AppWindowHwnd;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Instance()?.SetProgressState(new HWND(hwnd), TBPFLAG.TBPF_INDETERMINATE);
        }
        catch { }
    }

    /// <summary>Removes any progress overlay from the taskbar button.</summary>
    internal static void Clear()
    {
        var hwnd = AppWindowHwnd;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Instance()?.SetProgressState(new HWND(hwnd), TBPFLAG.TBPF_NOPROGRESS);
        }
        catch { }
    }
}
