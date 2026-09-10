using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Printendar.Core.Samples;
using Printendar.Core.Settings;

namespace Printendar.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Switches the whole application between the light and dark schemes.
    /// </summary>
    /// <remarks>
    /// Every window reads from this, so a change here reaches the settings dialog it was made
    /// in as well as the main window behind it. Applying it per window would leave whichever
    /// one was not open at the time in the old scheme.
    /// </remarks>
    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is null)
        {
            return;
        }

        Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = new MainViewModel();

            // Read and applied before the window exists. Doing it after the view model has
            // loaded would be simpler and would open every dark-mode session with a white
            // window for as long as the first paint takes.
            ApplyTheme(new SettingsStore(new DesktopSettingsLocation()).Load().Theme);

            // Until a calendar account is connected, the window opens on a demo month rather
            // than an empty grid. An empty grid on first run looks broken, and gives no idea
            // what the program is for.
            model.SetEvents(
                SampleCalendar.ForMonth(model.Month.Year, model.Month.Month),
                SampleCalendar.Calendars);

            desktop.MainWindow = new MainWindow(model);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
