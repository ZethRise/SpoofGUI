using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SpoofGUI.GUI;

public static class ThemeService
{
    public static ElementTheme Apply(string theme, FrameworkElement? root = null)
    {
        var requestedTheme = theme == "light" ? ElementTheme.Light : ElementTheme.Dark;
        if (root is not null)
            root.RequestedTheme = requestedTheme;

        var palette = requestedTheme == ElementTheme.Light ? LightPalette : DarkPalette;
        foreach (var (key, color) in palette)
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
                brush.Color = color;
        }

        return requestedTheme;
    }

    private static readonly IReadOnlyDictionary<string, Windows.UI.Color> DarkPalette =
        new Dictionary<string, Windows.UI.Color>
        {
            ["SurfaceBase"] = ColorHelper.FromArgb(255, 27, 30, 37),
            ["SurfaceRaised"] = ColorHelper.FromArgb(255, 34, 38, 47),
            ["SurfaceSunken"] = ColorHelper.FromArgb(255, 21, 24, 30),
            ["BorderSubtle"] = ColorHelper.FromArgb(255, 46, 51, 61),
            ["BorderStrong"] = ColorHelper.FromArgb(255, 74, 80, 92),
            ["TextPrimary"] = ColorHelper.FromArgb(255, 242, 243, 245),
            ["TextSecondary"] = ColorHelper.FromArgb(255, 168, 173, 183),
            ["TextTertiary"] = ColorHelper.FromArgb(255, 110, 115, 126),
            ["AccentBase"] = ColorHelper.FromArgb(255, 244, 182, 86),
            ["AccentDim"] = ColorHelper.FromArgb(255, 122, 90, 44),
            ["StatusDanger"] = ColorHelper.FromArgb(255, 212, 75, 58),
            ["StatusWarn"] = ColorHelper.FromArgb(255, 216, 162, 59),
        };

    private static readonly IReadOnlyDictionary<string, Windows.UI.Color> LightPalette =
        new Dictionary<string, Windows.UI.Color>
        {
            ["SurfaceBase"] = ColorHelper.FromArgb(255, 246, 247, 249),
            ["SurfaceRaised"] = ColorHelper.FromArgb(255, 255, 255, 255),
            ["SurfaceSunken"] = ColorHelper.FromArgb(255, 235, 238, 243),
            ["BorderSubtle"] = ColorHelper.FromArgb(255, 212, 218, 227),
            ["BorderStrong"] = ColorHelper.FromArgb(255, 164, 173, 188),
            ["TextPrimary"] = ColorHelper.FromArgb(255, 24, 27, 33),
            ["TextSecondary"] = ColorHelper.FromArgb(255, 74, 82, 95),
            ["TextTertiary"] = ColorHelper.FromArgb(255, 113, 123, 138),
            ["AccentBase"] = ColorHelper.FromArgb(255, 190, 126, 28),
            ["AccentDim"] = ColorHelper.FromArgb(255, 247, 222, 184),
            ["StatusDanger"] = ColorHelper.FromArgb(255, 178, 55, 42),
            ["StatusWarn"] = ColorHelper.FromArgb(255, 151, 103, 18),
        };
}
