using Avalonia;

namespace Printendar.App;

internal static class Program
{
    // Must not touch Avalonia before AppMain is called: SkiaSharp and the windowing backend
    // are initialized by BuildAvaloniaApp.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Also used by the Avalonia XAML previewer and by headless tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
