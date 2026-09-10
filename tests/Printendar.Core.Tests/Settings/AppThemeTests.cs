using Printendar.Core.Settings;

namespace Printendar.Core.Tests.Settings;

/// <summary>
/// Remembering whether somebody wants the light interface, the dark one, or whichever the
/// computer is using.
/// </summary>
/// <remarks>
/// Following the system is the default and stays the default, because a program that decides
/// on its own to be the only bright window at night has made a choice that was not its to
/// make. The setting exists for the people the system answer is wrong for.
/// </remarks>
public sealed class AppThemeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "printendar-theme-" + Guid.NewGuid().ToString("N"));

    private SettingsStore NewStore() => new(new FixedSettingsLocation(_directory));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FixedSettingsLocation(string directory) : ISettingsLocation
    {
        public string Directory { get; } = directory;
    }

    [Fact]
    public void Following_the_computer_is_what_happens_without_a_choice()
    {
        Assert.Equal(AppTheme.System, NewStore().Load().Theme);
    }

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void A_choice_survives_a_restart(AppTheme chosen)
    {
        var store = NewStore();

        store.Save(store.Load() with { Theme = chosen });

        Assert.Equal(chosen, NewStore().Load().Theme);
    }

    [Fact]
    public void It_is_written_by_name_so_the_file_can_be_read_by_a_person()
    {
        // The settings file is meant to be opened by somebody trying to fix something. "Theme":
        // 2 tells them nothing they can act on.
        var store = NewStore();

        store.Save(store.Load() with { Theme = AppTheme.Dark });

        Assert.Contains("\"Dark\"", File.ReadAllText(store.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_theme_this_build_does_not_have_falls_back_rather_than_breaking_the_file()
    {
        // A newer version might add one, or somebody edits the file and mistypes. Refusing the
        // whole file over a colour scheme would cost them every calendar they had added.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, SettingsStore.FileName), """
            {
              "Theme": "Solarized",
              "Sources": [
                { "Id": "a", "Kind": "IcsUrl", "DisplayName": "Team", "Location": "https://example.com/t.ics" }
              ]
            }
            """);

        var settings = NewStore().Load();

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Single(settings.Sources);
    }
}
