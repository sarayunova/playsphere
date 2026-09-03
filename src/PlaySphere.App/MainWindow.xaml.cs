using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

namespace PlaySphere.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
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
}