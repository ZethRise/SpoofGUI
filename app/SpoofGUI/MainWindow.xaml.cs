using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SpoofGUI.Database;
using SpoofGUI.GUI;
using WinRT.Interop;

namespace SpoofGUI;

public sealed partial class MainWindow : Window
{
    private AppWindowTitleBar? _titleBar;

    public MainWindow()
    {
        InitializeComponent();
        Title = "SpoofGUI";

        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1280, Height = 810 });
        appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SpoofGUI.ico"));

        if (appWindow.TitleBar is { } tb)
        {
            _titleBar = tb;
            tb.ExtendsContentIntoTitleBar = true;
            tb.PreferredHeightOption = TitleBarHeightOption.Tall;
        }

        var savedTheme = App.Services.GetRequiredService<SettingsRepository>().Get("theme") ?? "dark";
        ApplyTheme(savedTheme);
    }

    public void ApplyTheme(string theme)
    {
        var requestedTheme = ThemeService.Apply(theme, RootShell);
        ApplyTitleBarTheme(requestedTheme);
    }

    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        if (_titleBar is null)
            return;

        if (theme == ElementTheme.Light)
        {
            _titleBar.ButtonBackgroundColor = ColorHelper.FromArgb(255, 246, 247, 249);
            _titleBar.ButtonInactiveBackgroundColor = ColorHelper.FromArgb(255, 246, 247, 249);
            _titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 235, 238, 243);
            _titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 212, 218, 227);
            _titleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 24, 27, 33);
            _titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 113, 123, 138);
            _titleBar.ButtonHoverForegroundColor = ColorHelper.FromArgb(255, 24, 27, 33);
            _titleBar.ButtonPressedForegroundColor = ColorHelper.FromArgb(255, 24, 27, 33);
            return;
        }

        _titleBar.ButtonBackgroundColor = ColorHelper.FromArgb(255, 27, 30, 37);
        _titleBar.ButtonInactiveBackgroundColor = ColorHelper.FromArgb(255, 27, 30, 37);
        _titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 34, 38, 47);
        _titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 21, 24, 30);
        _titleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 242, 243, 245);
        _titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 168, 173, 183);
        _titleBar.ButtonHoverForegroundColor = ColorHelper.FromArgb(255, 242, 243, 245);
        _titleBar.ButtonPressedForegroundColor = ColorHelper.FromArgb(255, 242, 243, 245);
    }
}
