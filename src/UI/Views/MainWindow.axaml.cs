using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Media;

using FluentAvalonia.UI.Windowing;

namespace CrowsNestMqtt.UI.Views;

/// <summary>
/// The main window of the application.
/// Hosts the MainView user control.
/// </summary>
[ExcludeFromCodeCoverage] // UI initialization code is not unit testable
public partial class MainWindow : FAAppWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
        TitleBar.ExtendsContentIntoTitleBar = true;
        Background = Brushes.Transparent;

        InitializeComponent();
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // FAAppWindow's template contains a RootBorder that may have an opaque background.
        // Make it transparent so the Mica backdrop can show through.
        Background = Brushes.Transparent;
        if (e.NameScope.Find<Avalonia.Controls.Border>("RootBorder") is { } rootBorder)
        {
            rootBorder.Background = Brushes.Transparent;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            ApplyMicaBackdrop();
        }
    }

    private void ApplyMicaBackdrop()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null)
            return;

        var hwnd = handle.Handle;

        // Ensure the window background is transparent so the backdrop shows through
        Background = Brushes.Transparent;

        // DWMWA_SYSTEMBACKDROP_TYPE = 38
        // 0 = Auto, 1 = None, 2 = Mica, 3 = Acrylic, 4 = Mica Alt (Tabbed)
        int micaBackdropType = 2;
        _ = DwmSetWindowAttribute(hwnd, 38, ref micaBackdropType, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}