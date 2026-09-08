using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Printendar.Core.Samples;

namespace Printendar.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = new MainViewModel();

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
