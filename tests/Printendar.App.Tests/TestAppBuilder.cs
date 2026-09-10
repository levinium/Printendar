using Avalonia;
using Avalonia.Headless;
using Printendar.App;
using Printendar.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Printendar.App.Tests;

/// <summary>
/// Starts the real application, without a screen.
/// </summary>
/// <remarks>
/// The real <see cref="App"/> rather than a stand-in, because most of what is worth testing
/// here lives in what App.axaml sets up: the Fluent theme, the colour picker's separate theme
/// assembly, and Printendar's own card and panel brushes. A test application declaring its own
/// styles would pass while the shipped one rendered nothing.
///
/// App only builds a main window when the lifetime is a classic desktop one, which a headless
/// run is not, so nothing here reaches the real settings file on the machine running the tests.
/// </remarks>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
