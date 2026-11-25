// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YApex.UI.Services;

public static partial class MicaBackdrop
{
    private const int SystemBackdropTypeAttribute = 38;
    private const int MainWindowBackdrop = 2;

    public static void TryEnable(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            return;
        }

        var backdrop = MainWindowBackdrop;
        _ = DwmSetWindowAttribute(handle, SystemBackdropTypeAttribute, ref backdrop, sizeof(int));
    }

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static partial int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);
}
