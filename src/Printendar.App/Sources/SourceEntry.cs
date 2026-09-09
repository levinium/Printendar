using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Printendar.Core.Sources;
using SkiaSharp;

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
    private bool _isEditingName;

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

    /// <summary>
    /// Whether the name is currently open for editing.
    /// </summary>
    /// <remarks>
    /// The name used to be a permanently editable box drawn without a border, which read as a
    /// label: nobody could tell it could be changed. Off, the name is plain text; on, it is an
    /// ordinary bordered text box that looks like every other field a person has ever typed in.
    /// </remarks>
    public bool IsEditingName
    {
        get => _isEditingName;
        set
        {
            _isEditingName = value;
            Raise(nameof(IsEditingName));
            Raise(nameof(IsNotEditingName));
            Raise(nameof(RenameButtonLabel));
        }
    }

    public bool IsNotEditingName => !_isEditingName;

    /// <summary>Pencil to start, tick to finish, on the one button that toggles it.</summary>
    public string RenameButtonLabel => _isEditingName ? "✓" : "✎";

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
    public void SetCalendars(IReadOnlyList<CalendarRef> calendars, IReadOnlyCollection<SKColor> colorsInUseElsewhere)
    {
        Calendars.Clear();

        var remembered = Configured.SelectedCalendarIds;

        var colors = CalendarColorAssignment.Assign(
            [.. calendars.Select(c => c.CalendarId)],
            Configured.CalendarColors,
            colorsInUseElsewhere);

        foreach (var calendar in calendars)
        {
            var selectable = new SelectableCalendar(calendar, colors[calendar.CalendarId])
            {
                IsSelected = remembered.Count > 0
                    ? remembered.Contains(calendar.CalendarId)
                    : calendar.IsDefault,
            };

            selectable.SelectionChanged += (_, _) =>
            {
                RecordCalendarState();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };

            selectable.ColorChanged += (_, _) =>
            {
                RecordCalendarState();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };

            Calendars.Add(selectable);
        }

        // Written down straight away, including the colours just handed out. Leaving them only
        // in memory would mean they were worked out afresh on every start, which is the drift
        // this was meant to stop.
        RecordCalendarState();
    }

    /// <summary>Copies what is ticked and what colour each calendar is into the saved shape.</summary>
    private void RecordCalendarState() =>
        Configured = Configured with
        {
            SelectedCalendarIds = [.. SelectedCalendarIds],
            CalendarColors = Calendars.ToDictionary(
                c => c.Reference.CalendarId,
                c => CalendarPalette.ToHex(c.Color),
                StringComparer.Ordinal),
        };

    /// <summary>
    /// Whether this source is actually contributing to the page.
    /// </summary>
    /// <remarks>
    /// Deliberately pessimistic. A source with no live connection, or one carrying an error,
    /// puts nothing on the page, and a page missing events while looking complete is the worst
    /// outcome this program has. Unticking every calendar is not counted: that is a choice the
    /// user made, and nagging about it would teach them to ignore the warning that matters.
    /// </remarks>
    public SourceStatus Status =>
        new(
            Id,
            DisplayName,
            Live is null
                ? SourceAvailability.NotSignedIn
                : HasError ? SourceAvailability.Failed : SourceAvailability.Ready,
            Error);

    public bool IsMissingFromPage => Status.IsMissingFromPage;

    public IEnumerable<string> SelectedCalendarIds =>
        Calendars.Where(c => c.IsSelected).Select(c => c.Reference.CalendarId);

    public IReadOnlyList<SelectableCalendar> Selected =>
        [.. Calendars.Where(c => c.IsSelected)];

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
}
