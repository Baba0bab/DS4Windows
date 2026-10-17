using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DS4WinWPF.DS4Forms.Themes
{
    /// <summary>
    /// Makes the native title bar follow the current app theme: dark title bar for the
    /// dark theme and, on Windows 11, a caption colored like the window background.
    /// Older Windows versions ignore the attributes they do not know.
    /// </summary>
    public static class WindowChromeTheme
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static bool registered;

        public static void Register()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(Window_Loaded));
        }

        public static void ApplyToAllWindows()
        {
            if (Application.Current == null)
            {
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                Apply(window);
            }
        }

        public static void Apply(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            bool isDark = Application.Current.TryFindResource("Modern.IsDark") is bool dark && dark;
            int darkValue = isDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkValue, sizeof(int));
            }

            if (Application.Current.TryFindResource("BackgroundColor") is SolidColorBrush background)
            {
                int caption = ToColorRef(background.Color);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            }

            if (Application.Current.TryFindResource("ForegroundColor") is SolidColorBrush foreground)
            {
                int text = ToColorRef(foreground.Color);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
        }

        private static void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                Apply(window);
            }
        }

        // COLORREF layout is 0x00BBGGRR
        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }
    }
}
