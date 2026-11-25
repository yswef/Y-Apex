// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Windows;
using Application = System.Windows.Application;
using YApex.UI.Settings;

namespace YApex.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = UiSettingsStore.Load();
        LocalizationService.Apply(settings.Language);
        ThemeService.Apply(settings.Theme);
        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();
    }
}
