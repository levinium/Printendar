using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Printendar.Core.Model;
using Printendar.Core.Layout;
using Printendar.Core.Settings;
using Printendar.Core.Sources;
using Printendar.Sources.Ics;
using Printendar.Sources.Microsoft365;
using SkiaSharp;

namespace Printendar.App.Sources;

/// <summary>
/// The calendars somebody has added: the list, and reading them.
/// </summary>
/// <remarks>
/// Split out of the main window's state rather than added to it. The window was already
/// carrying the page, the paper, the fit policy and the Microsoft setup, and account
/// management is a separate concern with its own persistence; folding it in would have made
/// one class nobody could hold in their head.
/// </remarks>
public sealed class CalendarSourcesViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _store;
    private AppSettings _settings;

    public CalendarSourcesViewModel(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the set of calendars to print has changed in any way.</summary>
    public event EventHandler? Changed;

    public ObservableCollection<SourceEntry> Sources { get; } = [];

    public bool HasSources => Sources.Count > 0;

    public bool HasNoSources => Sources.Count == 0;

    /// <summary>Rebuilds the list from settings and opens each source.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Sources.Clear();

        foreach (var configured in _settings.Sources)
        {
            Sources.Add(Prepare(new SourceEntry(configured)));
        }

        RaiseCounts();

        foreach (var entry in Sources)
        {
            await OpenAsync(entry, cancellationToken).ConfigureAwait(true);
        }

        // Opening settles a colour for any calendar seen for the first time. Saving here is
        // what makes it stick: left in memory it would be worked out again on the next run,
        // and a calendar added or removed in between would shift the rest.
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates the live source and lists its calendars.
    /// </summary>
    /// <remarks>
    /// A source that cannot be opened stays in the list carrying its reason, rather than
    /// disappearing. Somebody whose calendar file is on a drive that is not plugged in should
    /// see the calendar with a problem, not lose the entry they added.
    /// </remarks>
    public async Task OpenAsync(SourceEntry entry, CancellationToken cancellationToken = default)
    {
        entry.IsBusy = true;
        entry.Error = null;

        try
        {
            if (CalendarSourceFactory.UnavailableReason(entry.Configured.Kind, _settings) is { } reason)
            {
                entry.Error = reason;
                return;
            }

            var source = CalendarSourceFactory.Create(entry.Configured, _settings);
            entry.Attach(source);

            // Never sign in interactively here. This runs when the window opens and after any
            // change to the list, and throwing a browser window at somebody who just started
            // the program is hostile. A cached token means Connected and everything proceeds;
            // an expired one is reported so they can choose when to deal with it.
            var state = await source.GetAuthStateAsync(cancellationToken).ConfigureAwait(true);

            if (state is not AuthState.Connected)
            {
                entry.Error = state is AuthState.NeedsInteraction
                    ? "Signed out. Open Add or manage calendars to sign in again."
                    : "Not signed in yet.";
                return;
            }

            var calendars = await source.ListCalendarsAsync(cancellationToken).ConfigureAwait(true);

            entry.SetCalendars(calendars, ColorsInUseExcept(entry));
        }
        catch (Microsoft365SignInException ex)
        {
            entry.Error = ex.Diagnosis.Message;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message;
        }
        finally
        {
            entry.IsBusy = false;
            RaiseMissingData();
        }
    }

    /// <summary>
    /// The colours every other source is already printing in.
    /// </summary>
    /// <remarks>
    /// Passed in so a calendar being seen for the first time can be given one nobody else is
    /// using. Two calendars in the same colour, under a legend saying they are different, is
    /// the one outcome worth going out of the way to avoid.
    ///
    /// This replaced counting positions across the whole list, which decided a colour by where
    /// a calendar happened to sit. Removing a source renumbered everything below it and the
    /// printed page silently changed colour from one week to the next.
    /// </remarks>
    private IReadOnlyCollection<SKColor> ColorsInUseExcept(SourceEntry entry) =>
    [
        .. Sources
            .Where(other => !ReferenceEquals(other, entry))
            .SelectMany(other => other.Calendars)
            .Select(calendar => calendar.Color)
    ];

    public async Task AddAsync(ConfiguredSource configured, CancellationToken cancellationToken = default)
    {
        var entry = Prepare(new SourceEntry(configured));

        Sources.Add(entry);
        RaiseCounts();

        await OpenAsync(entry, cancellationToken).ConfigureAwait(true);

        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveAsync(SourceEntry entry, CancellationToken cancellationToken = default)
    {
        Sources.Remove(entry);
        RaiseCounts();

        if (entry.Live is { } live)
        {
            try
            {
                // Signing out matters for account-based sources: leaving the token behind
                // would silently sign the next person straight back in.
                await live.SignOutAsync(cancellationToken).ConfigureAwait(true);
                await live.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Removing from the list is what the user asked for, and it has happened.
            }
        }

        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Rename(SourceEntry entry, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        entry.Rename(displayName.Trim());
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the visible window from every source that has something ticked.</summary>
    public async Task<AggregateResult> ReadAsync(
        DateSpan window,
        TimeZoneInfo displayZone,
        CancellationToken cancellationToken = default)
    {
        var selections = Sources
            .Where(s => s.Live is not null)
            .Select(s => new SourceSelection(s.Live!, [.. s.Selected.Select(c => c.Reference)]))
            .ToList();

        var result = await CalendarAggregator
            .GetEventsAsync(selections, window, displayZone, cancellationToken)
            .ConfigureAwait(true);

        // Attach each failure to the source it came from, so the reason sits next to the name
        // rather than in a status bar that cannot say which of four calendars broke.
        foreach (var entry in Sources)
        {
            var failure = result.Failures.FirstOrDefault(f => f.SourceId == entry.Id);

            if (failure is not null)
            {
                entry.Error = failure.Message;
            }
            else if (entry.Live is not null)
            {
                entry.Error = null;
            }
        }

        RaiseMissingData();

        return result;
    }

    /// <summary>The legend for whatever is currently ticked, in the order it is drawn.</summary>
    public IReadOnlyList<CalendarLegendEntry> Legend =>
    [
        .. Sources
            .SelectMany(s => s.Selected)
            .Select(c => new CalendarLegendEntry(c.Reference.CalendarId, c.DisplayName, c.Color))
    ];

    public bool AnythingSelected => Sources.Any(s => s.Selected.Count > 0);

    /// <summary>
    /// One sentence saying the page is incomplete, or null when it is not.
    /// </summary>
    /// <remarks>
    /// Recomputed rather than cached, because it is read at the moment somebody presses Print
    /// and must describe the list as it is then, not as it was when the window opened.
    /// </remarks>
    public string? MissingDataWarning =>
        MissingCalendarWarning.Describe([.. Sources.Select(s => s.Status)]);

    public bool HasMissingData => MissingDataWarning is not null;

    /// <summary>Refreshes the settings this reads Microsoft configuration from.</summary>
    public void UpdateSettings(AppSettings settings) => _settings = settings;

    /// <summary>Wires an entry to this list: selection, renaming, and its buttons.</summary>
    /// <remarks>
    /// One place rather than two, because loading and adding wiring the same entry differently
    /// is the kind of divergence nobody notices until a renamed calendar stops saving.
    /// </remarks>
    private SourceEntry Prepare(SourceEntry entry)
    {
        entry.SelectionChanged += (_, _) =>
        {
            Persist();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        entry.Renamed = () =>
        {
            Persist();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        entry.RemoveCommand = new RelayCommand(async p =>
        {
            if (p is SourceEntry target)
            {
                await RemoveAsync(target).ConfigureAwait(true);
            }
        });

        entry.ReconnectCommand = new RelayCommand(async p =>
        {
            if (p is SourceEntry target)
            {
                await ReconnectAsync(target).ConfigureAwait(true);
            }
        });

        return entry;
    }

    /// <summary>
    /// Signs in again, interactively, for a source that needs it.
    /// </summary>
    /// <remarks>
    /// Only ever from a button the user pressed. Signing in opens a browser window, and doing
    /// that unasked is the difference between a tool and an interruption.
    /// </remarks>
    public async Task ReconnectAsync(SourceEntry entry, CancellationToken cancellationToken = default)
    {
        entry.IsBusy = true;
        entry.Error = null;

        try
        {
            if (entry.Live is null)
            {
                entry.Attach(CalendarSourceFactory.Create(entry.Configured, _settings));
            }

            if (entry.Live is { } live)
            {
                await live.ConnectAsync(cancellationToken).ConfigureAwait(true);

                var calendars = await live.ListCalendarsAsync(cancellationToken).ConfigureAwait(true);
                entry.SetCalendars(calendars, ColorsInUseExcept(entry));
            }
        }
        catch (Microsoft365SignInException ex)
        {
            entry.Error = ex.Diagnosis.Message;
        }
        catch (OperationCanceledException)
        {
            // The browser window was closed. Not an error worth shouting about.
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message;
        }
        finally
        {
            entry.IsBusy = false;
            Persist();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Persist()
    {
        _settings = _settings with { Sources = [.. Sources.Select(s => s.Configured)] };
        _store.Save(_settings);
    }

    private void RaiseCounts()
    {
        Raise(nameof(HasSources));
        Raise(nameof(HasNoSources));
        RaiseMissingData();
    }

    /// <summary>
    /// Repaints the incomplete-page warning.
    /// </summary>
    /// <remarks>
    /// Called from everywhere availability can change: opening, adding, removing, reconnecting
    /// and reading. Missing one of those would leave a stale banner, and a stale banner about
    /// stale data is worse than none because it would be trusted.
    /// </remarks>
    private void RaiseMissingData()
    {
        Raise(nameof(MissingDataWarning));
        Raise(nameof(HasMissingData));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

    /// <summary>Checks a picked file is usable, and suggests what to call it.</summary>
    /// <remarks>
    /// Separate from <see cref="ForFile"/> so the name can be put in front of the user before
    /// the source is created, rather than chosen for them and left to be discovered later.
    /// </remarks>
    public static string SuggestNameForFile(string path) => IcsFileCalendarSource.Validate(path);

    /// <summary>
    /// The best guess at a name for a feed address, or null when it is not a usable address.
    /// </summary>
    /// <remarks>
    /// Only ever a starting point in a box the user can type over. A published feed's address
    /// says almost nothing about the calendar behind it: the path is usually an opaque token,
    /// which leaves the host, and "calendar.google.com" is the name of a company's server rather
    /// than of anybody's calendar. No amount of parsing fixes that, so the fix is to ask.
    /// </remarks>
    public static string? SuggestNameForUrl(string url)
    {
        try
        {
            return IcsUrlCalendarSource.NormalizeUrl(url).Host;
        }
        catch (Exception)
        {
            // Half-typed addresses are the normal case while a suggestion is being shown.
            return null;
        }
    }

    /// <summary>Makes a source from a file the user picked.</summary>
    public static ConfiguredSource ForFile(string path, string? displayName = null)
    {
        // Validated even when the name came from the user, so a missing or absurd file is still
        // refused rather than added as a broken entry.
        var suggested = SuggestNameForFile(path);

        return new ConfiguredSource(
            Id: Guid.NewGuid().ToString("N"),
            Kind: CalendarSourceKind.IcsFile,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? suggested : displayName.Trim(),
            Location: path);
    }

    /// <summary>Makes a source from a feed address the user pasted.</summary>
    public static ConfiguredSource ForUrl(string url, string? displayName)
    {
        var normalized = IcsUrlCalendarSource.NormalizeUrl(url);

        return new ConfiguredSource(
            Id: Guid.NewGuid().ToString("N"),
            Kind: CalendarSourceKind.IcsUrl,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? normalized.Host : displayName.Trim(),
            Location: normalized.ToString());
    }
}
