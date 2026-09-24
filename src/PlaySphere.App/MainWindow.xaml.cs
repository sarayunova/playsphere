using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace PlaySphere.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        ApplyDarkTitleBar();
    }

    private void SetWindowIcon()
    {
        try
        {
            string icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (!File.Exists(icoPath))
                return;

            AppWindow appWindow = AppWindow;
            appWindow.SetIcon(icoPath);
        }
        catch
        {
            // Best-effort; a missing/failed icon must never block app launch.
        }
    }

    // The default Windows caption bar follows the OS light/dark setting, not this app's
    // RequestedTheme="Dark" (App.xaml) — so on a light-themed OS the titlebar renders white over
    // PlaySphere's dark content (PICKLE.md §7 Vibrant Dark palette). Two things are needed: the
    // DWM immersive-dark-mode attribute (darkens the native frame/border) and the AppWindowTitleBar
    // colors (darkens the caption background/buttons to match the palette).
    private void ApplyDarkTitleBar()
    {
        try
        {
            TrySetDwmImmersiveDarkMode();

            AppWindow appWindow = AppWindow;
            if (appWindow?.TitleBar is null || !AppWindowTitleBar.IsCustomizationSupported())
                return;

            var titleBar = appWindow.TitleBar;
            var background = Color.FromArgb(0xFF, 0x0E, 0x11, 0x17); // ColorBackgroundPrimary
            var foreground = Color.FromArgb(0xFF, 0xF8, 0xFA, 0xFC); // ColorTextPrimary
            var hover = Color.FromArgb(0xFF, 0x26, 0x31, 0x42);      // ColorSurfaceHover

            titleBar.BackgroundColor = background;
            titleBar.InactiveBackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveForegroundColor = foreground;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonInactiveBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = hover;
            titleBar.ButtonPressedForegroundColor = foreground;
        }
        catch
        {
            // Best-effort; a failed titlebar theme must never block app launch.
        }
    }

    private void TrySetDwmImmersiveDarkMode()
    {
        try
        {
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == 0)
                return;

            int useImmersiveDarkMode = 1;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useImmersiveDarkMode, sizeof(int));
        }
        catch
        {
            // Best-effort; not all Windows builds support this attribute.
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}