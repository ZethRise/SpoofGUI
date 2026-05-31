using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace SpoofGUI;

public sealed partial class SplashWindow : Window
{
    private const int Width = 420;
    private const int Height = 300;

    public SplashWindow()
    {
        InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);

        appWindow.Resize(new SizeInt32 { Width = Width, Height = Height });
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary);
        appWindow.Move(new PointInt32
        {
            X = area.WorkArea.X + (area.WorkArea.Width - Width) / 2,
            Y = area.WorkArea.Y + (area.WorkArea.Height - Height) / 2,
        });
    }
}
