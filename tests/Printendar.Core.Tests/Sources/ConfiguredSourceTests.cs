using Printendar.Core.Settings;
using Printendar.Core.Sources;

namespace Printendar.Core.Tests.Sources;

/// <summary>
/// The calendars somebody has added, remembered between runs.
/// </summary>
/// <remarks>
/// Until this existed, reopening the app lost whatever you had connected and you started from
/// a sample month again. That is the difference between a demo and a tool.
///
/// What is stored is deliberately thin: where to find a calendar, what to call it, and what
/// colour to print it. No credential reaches this file, because there is none to reach it:
/// Printendar signs in to nothing.
///
/// A published feed address is the exception worth naming. It is not a password, but anyone
/// holding one can read that calendar, so it is treated as worth keeping quiet about.
/// </remarks>
public sealed class ConfiguredSourceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "printendar-src-" + Guid.NewGuid().ToString("N"));

    private SettingsStore NewStore() => new(new FixedLocation(_directory));

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

    private sealed class FixedLocation(string directory) : ISettingsLocation
    {
        public string Directory { get; } = directory;
    }

    [Fact]
    public void A_fresh_install_has_no_sources()
    {
        Assert.Empty(NewStore().Load().Sources);
    }

    [Fact]
    public void A_local_file_survives_a_restart()
    {
        var store = NewStore();

        store.Save(store.Load() with
        {
            Sources =
            [
                new ConfiguredSource(
                    Id: "s1",
                    Kind: CalendarSourceKind.IcsFile,
                    DisplayName: "Family",
                    Location: @"C:\calendars\family.ics"),
            ],
        });

        var reloaded = NewStore().Load().Sources.Single();

        Assert.Equal("s1", reloaded.Id);
        Assert.Equal(CalendarSourceKind.IcsFile, reloaded.Kind);
        Assert.Equal("Family", reloaded.DisplayName);
        Assert.Equal(@"C:\calendars\family.ics", reloaded.Location);
    }

    [Fact]
    public void Several_calendars_of_the_same_kind_are_kept_apart()
    {
        // Merging a work and a personal calendar onto one sheet is a real reason people want
        // this, so two feeds have to coexist rather than overwrite each other.
        var store = NewStore();

        store.Save(store.Load() with
        {
            Sources =
            [
                new ConfiguredSource("a", CalendarSourceKind.IcsUrl, "Work", "https://example.com/work.ics"),
                new ConfiguredSource("b", CalendarSourceKind.IcsUrl, "Personal", "https://example.com/home.ics"),
            ],
        });

        var reloaded = NewStore().Load().Sources;

        Assert.Equal(2, reloaded.Count);
        Assert.Equal(["Work", "Personal"], reloaded.Select(s => s.DisplayName));
    }

    [Fact]
    public void Which_calendars_were_ticked_is_remembered()
    {
        // Otherwise every restart reselects the default calendar and quietly drops the other
        // three the user had chosen, which looks like data loss.
        var store = NewStore();

        store.Save(store.Load() with
        {
            Sources =
            [
                new ConfiguredSource("a", CalendarSourceKind.IcsUrl, "Work", "https://example.com/work.ics")
                {
                    SelectedCalendarIds = ["cal-1", "cal-3"],
                },
            ],
        });

        Assert.Equal(["cal-1", "cal-3"], NewStore().Load().Sources.Single().SelectedCalendarIds);
    }

    [Fact]
    public void No_token_or_secret_field_exists_to_be_written()
    {
        // Structural, not a spot check: if somebody adds a Token or Secret property to the
        // record, this fails and they have to justify persisting a credential in plain JSON.
        var names = typeof(ConfiguredSource)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_source_whose_kind_is_unknown_is_dropped_rather_than_breaking_the_file()
    {
        // A settings file written by a newer version must not stop an older one starting, and
        // must cost the user only that one entry.
        //
        // Asserting the bad source is gone is not enough on its own: quarantining the whole
        // file would also satisfy that, while silently throwing away every calendar the user
        // had. Insisting the good one is still there is what makes this a test about
        // tolerance rather than about failing quietly.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, SettingsStore.FileName),
            """
            {
              "Sources": [
                { "Id": "future", "Kind": "SomethingFromTheFuture", "DisplayName": "?" },
                { "Id": "good", "Kind": "IcsFile", "DisplayName": "Family", "Location": "family.ics" }
              ]
            }
            """);

        Assert.Equal("good", NewStore().Load().Sources.Single().Id);
    }

    [Fact]
    public void A_source_with_no_id_is_dropped()
    {
        // The id is how a source is addressed everywhere else, so a blank one is not a source.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, SettingsStore.FileName),
            """
            { "Sources": [ { "Id": "", "Kind": "IcsFile", "DisplayName": "Broken", "Location": "x.ics" } ] }
            """);

        Assert.Empty(NewStore().Load().Sources);
    }
}
