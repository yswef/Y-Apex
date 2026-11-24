// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace YApex.UI.Settings;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        var useDark = theme switch
        {
            "dark" => true,
            "light" => false,
            _ => IsSystemDark()
        };
        var resources = WpfApplication.Current.Resources;
        resources["AppBackground"] = Brush(useDark ? "#171A20" : "#F3F6FA");
        resources["AppSurface"] = Brush(useDark ? "#222831" : "#FFFFFF");
        resources["TextPrimary"] = Brush(useDark ? "#F0F3F8" : "#17212F");
        resources["TextSecondary"] = Brush(useDark ? "#B3BECE" : "#536174");
        resources["BorderColor"] = Brush(useDark ? "#3A4655" : "#DEE5EF");
        resources["AccentColor"] = Brush("#3478D4");
        resources["DangerColor"] = Brush(useDark ? "#FF737A" : "#C83B42");
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    private static WpfSolidColorBrush Brush(string color) =>
        new((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
