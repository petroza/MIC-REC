using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MicRec;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not Window window) return;
                nint handle = new WindowInteropHelper(window).Handle;
                if (handle == 0) return;
                int enabled = 1;
                DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
                int caption = 0x000F0D0C;
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            }));
        base.OnStartup(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
