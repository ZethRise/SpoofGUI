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

        // Database
        services.AddSingleton<DatabaseConnection>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<ProfileRepository>();
        services.AddSingleton<V2RayProfileRepository>();
        services.AddSingleton<DatabaseInitializer>();

        // Engine supervisor + IPC
        services.AddSingleton<EngineSupervisor>();
        services.AddSingleton<EngineClient>();
        services.AddSingleton<XrayCoreService>();
        services.AddSingleton<TunnelService>();

        // View models
        services.AddSingleton<MainPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<ConfigPageViewModel>();
        services.AddTransient<V2RayPageViewModel>();
        services.AddTransient<ShellViewModel>();

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<DatabaseInitializer>().EnsureCreated();
        return sp;
    }
}
