using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpoofGUI.Database;
using SpoofGUI.Engine;
using SpoofGUI.GUI.ViewModels;

namespace SpoofGUI.Core;

internal static class Bootstrap
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        services.AddSingleton<DatabaseConnection>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<ProxyPortSettings>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<ProfileRepository>();
        services.AddSingleton<V2RayProfileRepository>();
        services.AddSingleton<DatabaseInitializer>();

        services.AddSingleton<EngineSupervisor>();
        services.AddSingleton<EngineClient>();
        services.AddSingleton<XrayCoreService>();
        services.AddSingleton<SingBoxTunnelService>();
        services.AddSingleton<SniScannerService>();
        services.AddSingleton<SniListService>();

        services.AddSingleton<MainPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<ConfigPageViewModel>();
        services.AddTransient<V2RayPageViewModel>();
        services.AddTransient<SniScannerPageViewModel>();
        services.AddTransient<ShellViewModel>();

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<DatabaseInitializer>().EnsureCreated();
        return sp;
    }
}
