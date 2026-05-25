using Microsoft.UI.Xaml;
using SpoofGUI.Core;
using System.Diagnostics;
using System.Security.Principal;

namespace SpoofGUI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? CurrentWindow { get; private set; }
    private Window? _window;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("UnobservedTask", e.Exception);
            e.SetObserved();
        };
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("App.UnhandledException", e.Exception);
            e.Handled = true;
        };

        Application.LoadComponent(this, new Uri("ms-appx:///App.xaml"));
        Services = Bootstrap.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!IsRunningAsAdministrator())
        {
            RelaunchAsAdministrator();
            Exit();
            return;
        }

        CurrentWindow = new MainWindow();
        _window = CurrentWindow;
        _window.Activate();
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchAsAdministrator()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch
        {
            // User cancelled UAC or elevation was blocked. App exits because WinDivert needs admin.
        }
    }
}
