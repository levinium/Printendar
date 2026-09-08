using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Printendar.Core.Sources;

namespace Printendar.App.Sources;

/// <summary>
/// One calendar source as the window shows it: what it is, and the calendars inside it.
/// </summary>
/// <remarks>
/// The sidebar lists these rather than a flat set of calendars, because a person thinks in
/// accounts first. Two calendars both called "Calendar" mean nothing side by side; under
/// "Work" and "Personal" they are obvious.
/// </remarks>
public sealed class SourceEntry : INotifyPropertyChanged
{
    private string? _error;
    private bool _isBusy;

    public SourceEntry(ConfiguredSource configured)
    {
        Configured = configured;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when a calendar inside this source is ticked or unticked.</summary>
    public event EventHandler? SelectionChanged;

    public ConfiguredSource Configured { get; private set; }

    public string Id => Configured.Id;

    public string DisplayName => Configured.DisplayName;

    /// <summary>The live source, once it has been created. Null until then.</summary>
    public ICalendarSource? Live { get; private set; }

    public ObservableCollection<SelectableCalendar> Calendars { get; } = [];

    /// <summary>What kind of thing this is, in words, under the name.</summary>
    public string KindLabel => Configured.Kind switch
    {
        CalendarSourceKind.Microsoft365 => "Microsoft 365",
        CalendarSourceKind.Google => "Google Calendar",
        CalendarSourceKind.IcsFile => "Calendar file",
        CalendarSourceKind.IcsUrl => "Calendar feed",
        _ => "Unknown",
    };

    /// <summary>Why this source could not be read, or null when it is fine.</summary>
    /// <remarks>
    /// Shown against the source rather than in the status bar, because with four calendars
    /// connected "could not read the calendar" does not say which one to go and fix.
    /// </remarks>
    public string? Error
    {
        get => _error;
        set
        {
            _error = value;
            Raise(nameof(Error));
            Raise(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            Raise(nameof(IsBusy));
        }
    }

    /// <summary>Renaming and removing, as the manage window's buttons call them.</summary>
    /// <remarks>
    /// Held on the entry rather than reached for through the visual tree. Binding a button
    /// inside an item template up to its window's view model needs a parent lookup and a cast
    /// that compiled bindings cannot check, so a typo there fails silently at runtime; a
    /// command on the item is checked at build time.
    /// </remarks>
    public System.Windows.Input.ICommand? RemoveCommand { get; set; }

    public System.Windows.Input.ICommand? ReconnectCommand { get; set; }

    /// <summary>Called when the user edits the name, so the change is saved.</summary>
    internal Action? Renamed { get; set; }

    /// <summary>
    /// The name as the manage window edits it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="DisplayName"/> so that a rename persists at the moment it
    /// happens. A blank name is ignored rather than accepted, because a source with no name is
    /// unidentifiable in the sidebar and there is nothing to undo it with.
    /// </remarks>
    public string EditableName
    {
        get => DisplayName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == DisplayName)
            {
                return;
            }

            Rename(value.Trim());
            Renamed?.Invoke();
        }
    }

    public void Rename(string displayName)
    {
        Configured = Configured with { DisplayName = displayName };
        Raise(nameof(DisplayName));
        Raise(nameof(EditableName));
    }

    public void Attach(ICalendarSource source) => Live = source;

    /// <summary>
    /// Replaces the calendars, restoring which ones were ticked last time.
    /// </summary>
    /// <remarks>
    /// A source that has never been opened has no remembered selection, so its default
    /// calendar is ticked. Falling back to nothing would show a connected account whose month
    /// is blank, which reads as a failure.
    /// </remarks>
    public void SetCalendars(IReadOnlyList<CalendarRef> calendars, int paletteOffset)
    {
        Calendars.Clear();

        var remembered = Configured.SelectedCalendarIds;

        for (var i = 0; i < calendars.Count; i++)
        {
            var selectable = new SelectableCalendar(calendars[i], CalendarPalette.At(paletteOffset + i))
            {
                IsSelected = remembered.Count > 0
                    ? remembered.Contains(calendars[i].CalendarId)
                    : calendars[i].IsDefault,
            };

            selectable.SelectionChanged += (_, _) =>
            {
                Configured = Configured with { SelectedCalendarIds = [.. SelectedCalendarIds] };
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };

            Calendars.Add(selectable);
        }

        Configured = Configured with { SelectedCalendarIds = [.. SelectedCalendarIds] };
    }

    public IEnumerable<string> SelectedCalendarIds =>
        Calendars.Where(c => c.IsSelected).Select(c => c.Reference.CalendarId);

    public IReadOnlyList<SelectableCalendar> Selected =>
        [.. Calendars.Where(c => c.IsSelected)];

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
}
