using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Printendar.App.Sources;
using Printendar.Core.Layout;
using Printendar.Core.Layout.Fit;
using Printendar.Core.Layout.Month;
using Printendar.Core.Model;
using Printendar.Core.Paper;
using Printendar.Core.Samples;
using Printendar.Core.Scene;
using Printendar.Core.Settings;
using Printendar.Core.Sources;
using Printendar.Core.Text;
using Printendar.Sources.Ics;

namespace Printendar.App;

/// <summary>
/// The state behind the window: what to print, and the page that results.
/// </summary>
/// <remarks>
/// Lays out exactly once per change and keeps the resulting <see cref="ScenePage"/>. The
/// preview draws that object and every export writes that same object, so the two cannot
/// disagree about what is on the page.
/// </remarks>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SkiaTextMeasurer _measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();
    private readonly MonthGridStyle _style = new();

    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private PaperSize _paper = PaperSizes.Letter;
    private Orientation _orientation = Orientation.Landscape;
    private double _marginInches = 0.4;
    private DayOfWeek _weekStart = DayOfWeek.Sunday;
    private WeekendMode _weekendMode = WeekendMode.FullSevenDay;
    private AdjacentDayMode _adjacentDays = AdjacentDayMode.Muted;
    private FitPolicy _fitPolicy = FitPolicy.Hybrid;
    private bool _showStartTimes = true;
    private ScenePage? _scene;
    private string _status = string.Empty;

    private IReadOnlyList<CalendarEvent> _events = [];
    private IReadOnlyList<CalendarLegendEntry> _calendars = [];

    public MainViewModel()
    {
        // Created eagerly so bindings have something to attach to before settings are read.
        // An empty list is the correct starting state; loading replaces it.
        Sources = new CalendarSourcesViewModel(_settingsStore, _settings);
        Relayout();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ITextMeasurer Measurer => _measurer;

    // The pickers bind to Choice wrappers, never to the enums themselves. "FullSevenDay" is a
    // fine identifier and a useless label.
    public ObservableCollection<PaperSize> PaperSizeChoices { get; } = [.. PaperSizes.All];

    public ObservableCollection<Choice<Orientation>> OrientationChoices { get; } = [.. Choices.Orientations];

    public ObservableCollection<Choice<DayOfWeek>> WeekStartChoices { get; } = [.. Choices.WeekStarts];

    public ObservableCollection<Choice<WeekendMode>> WeekendModeChoices { get; } = [.. Choices.WeekendModes];

    public ObservableCollection<Choice<FitPolicy>> FitPolicyChoices { get; } = [.. Choices.FitPolicies];

    public ObservableCollection<Choice<AdjacentDayMode>> AdjacentDayChoices { get; } = [.. Choices.AdjacentDays];

    public Choice<Orientation> SelectedOrientation
    {
        get => Choices.For(Choices.Orientations, Orientation);
        set => Orientation = value.Value;
    }

    public Choice<DayOfWeek> SelectedWeekStart
    {
        get => Choices.For(Choices.WeekStarts, WeekStart);
        set => WeekStart = value.Value;
    }

    public Choice<WeekendMode> SelectedWeekendMode
    {
        get => Choices.For(Choices.WeekendModes, WeekendMode);
        set => WeekendMode = value.Value;
    }

    public Choice<FitPolicy> SelectedFitPolicy
    {
        get => Choices.For(Choices.FitPolicies, FitPolicy);
        set => FitPolicy = value.Value;
    }

    public Choice<AdjacentDayMode> SelectedAdjacentDays
    {
        get => Choices.For(Choices.AdjacentDays, AdjacentDays);
        set => AdjacentDays = value.Value;
    }

    /// <summary>Explains the currently selected fit policy, under the picker.</summary>
    public string? FitPolicyDescription => SelectedFitPolicy.Description;

    /// <summary>Explains the currently selected weekend mode, under the picker.</summary>
    public string? WeekendModeDescription => SelectedWeekendMode.Description;

    public DateOnly Month
    {
        get => _month;
        set => Set(ref _month, value, [nameof(MonthTitle)]);
    }

    public string MonthTitle => _month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>
    /// What is about to be printed, in words, for the print dialog's sidebar.
    /// </summary>
    /// <remarks>
    /// Read once when that dialog opens, so it needs no change notification. Saying the paper
    /// and margin out loud there is the last chance to notice that the page is set to A4 when
    /// the tray holds Letter.
    /// </remarks>
    public string PageSummary =>
        $"{MonthTitle} on {_paper.DisplayName}, " +
        $"{(_orientation == Orientation.Landscape ? "landscape" : "portrait")}, " +
        $"{_marginInches:0.00} inch margin.";

    public PaperSize Paper
    {
        get => _paper;
        set => Set(ref _paper, value);
    }

    public Orientation Orientation
    {
        get => _orientation;
        set => Set(ref _orientation, value, [nameof(SelectedOrientation)]);
    }

    public double MarginInches
    {
        get => _marginInches;
        set => Set(ref _marginInches, value);
    }

    public DayOfWeek WeekStart
    {
        get => _weekStart;
        set => Set(ref _weekStart, value, [nameof(CanCompressWeekend), nameof(SelectedWeekStart)]);
    }

    /// <summary>
    /// Whether compressing the weekend into one column is coherent right now.
    /// </summary>
    /// <remarks>
    /// With a Sunday week start the weekend sits at both ends of the grid, so there is no
    /// single column to compress. The option is disabled rather than allowed to fail.
    /// </remarks>
    public bool CanCompressWeekend => _weekStart is DayOfWeek.Monday;

    public WeekendMode WeekendMode
    {
        get => _weekendMode;
        set => Set(ref _weekendMode, value, [nameof(SelectedWeekendMode), nameof(WeekendModeDescription)]);
    }

    public AdjacentDayMode AdjacentDays
    {
        get => _adjacentDays;
        set => Set(ref _adjacentDays, value, [nameof(SelectedAdjacentDays)]);
    }

    public FitPolicy FitPolicy
    {
        get => _fitPolicy;
        set => Set(ref _fitPolicy, value, [nameof(SelectedFitPolicy), nameof(FitPolicyDescription)]);
    }

    public bool ShowStartTimes
    {
        get => _showStartTimes;
        set => Set(ref _showStartTimes, value);
    }

    /// <summary>The laid-out page. The preview draws this, and every export writes this.</summary>
    public ScenePage? Scene
    {
        get => _scene;
        private set
        {
            _scene = value;
            Raise(nameof(Scene));
        }
    }

    /// <summary>A plain-language account of what fitting the page cost.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Raise(nameof(Status));
        }
    }

    /// <summary>Shows a problem in the status bar, in words the user can act on.</summary>
    public void ReportProblem(string message) => Status = message;

    public void ShowMonth(DateOnly month) => Month = month;

    public void StepMonth(int months)
    {
        Month = _month.AddMonths(months);

        // Events are fetched per visible month, so moving needs a refetch. Fire and forget
        // because the preview already shows the new grid; the events fill in when they arrive.
        _ = RefreshEventsAsync();
    }

    // ------------------------------------------------------------------ calendar source

    private bool _isBusy;

    /// <summary>
    /// The calendars somebody has added, and reading them.
    /// </summary>
    /// <remarks>
    /// Its own object rather than more properties here. This class already carries the page,
    /// the paper and the fit policy; managing which calendars are printed is a separate
    /// concern with its own persistence and its own window.
    /// </remarks>
    public CalendarSourcesViewModel Sources { get; private set; }

    private readonly SettingsStore _settingsStore = new(new DesktopSettingsLocation());
    private AppSettings _settings = new();

    /// <summary>What is saved right now, for windows that need to read it.</summary>
    public AppSettings Settings => _settings;

    /// <summary>Where settings are written, for the windows that change them.</summary>
    public SettingsStore SettingsStore => _settingsStore;

    /// <summary>Loads saved settings and the calendars that were added last time.</summary>
    /// <remarks>
    /// Called once while the window is being built. The calendars are opened afterwards and
    /// asynchronously, because a feed that is slow to answer must not hold up the window
    /// appearing.
    /// </remarks>
    public async Task LoadSettingsAsync()
    {
        _settings = _settingsStore.Load();

        Sources = new CalendarSourcesViewModel(_settingsStore, _settings);
        Sources.Changed += (_, _) =>
        {
            Raise(nameof(HasNoSources));
            _ = RefreshEventsAsync();
        };

        Raise(nameof(Sources));
        Raise(nameof(HasNoSources));

        await Sources.LoadAsync().ConfigureAwait(true);
    }

    /// <summary>True while talking to the provider, so the window can disable its controls.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            Raise(nameof(IsBusy));
            Raise(nameof(IsNotBusy));
        }
    }

    public bool IsNotBusy => !_isBusy;

    /// <summary>Whether anything has been added yet.</summary>
    /// <remarks>
    /// Drives the empty state. Before anything is connected the window shows a sample month
    /// rather than an empty grid, so it is obvious what the program does.
    /// </remarks>
    public bool HasNoSources => Sources.HasNoSources;

    /// <summary>
    /// Re-reads every calendar, so that what is about to be printed is what they say now.
    /// </summary>
    /// <remarks>
    /// The page on screen was laid out whenever the month last changed, which may have been
    /// this morning. Printing it would put a meeting added since onto no sheet at all, and the
    /// failure would look like the calendar being wrong rather than like the app showing an
    /// old copy of it. Paper cannot be corrected afterwards, so this runs before the printer
    /// dialog rather than after.
    ///
    /// It refreshes rather than verifies: there is no way to ask a published feed whether it
    /// changed that is cheaper than reading it, and a feed is a few tens of kilobytes.
    ///
    /// A feed that cannot be reached does not stop the print. Its reason is already against
    /// its own name in the list, the warning before printing says how many are missing, and
    /// refusing to print the calendars that do work would be worked around rather than heeded.
    /// </remarks>
    public async Task RefreshBeforePrintingAsync(CancellationToken cancellationToken = default)
    {
        var wasSaying = Status;

        Status = "Checking calendars for changes…";

        try
        {
            await RefreshEventsAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            // Only if the refresh had nothing of its own to report. Its message names the
            // calendars that failed, which matters more than whatever was there before.
            if (Status == "Checking calendars for changes…")
            {
                Status = wasSaying;
            }
        }
    }

    /// <summary>
    /// Fetches the visible month from every calendar that is ticked.
    /// </summary>
    /// <remarks>
    /// With nothing added at all the sample month stays, so the window never shows an empty
    /// grid that looks like something went wrong. With sources added but nothing ticked, the
    /// grid really is empty, because that is what the user asked for.
    /// </remarks>
    public async Task RefreshEventsAsync(CancellationToken cancellationToken = default)
    {
        if (Sources.HasNoSources)
        {
            SetEvents(SampleCalendar.ForMonth(_month.Year, _month.Month), SampleCalendar.Calendars);
            return;
        }

        if (!Sources.AnythingSelected)
        {
            SetEvents([], []);
            return;
        }

        IsBusy = true;

        try
        {
            // The grid draws days either side of the month, so those are fetched too.
            var result = await Sources
                .ReadAsync(VisibleWindow(), TimeZoneInfo.Local, cancellationToken)
                .ConfigureAwait(true);

            SetEvents(result.Events, Sources.Legend);

            // A source that failed has already put its reason against its own name in the
            // list. The status bar only says how many, because naming four of them here would
            // bury the fitting message that belongs to the page.
            if (result.Failures.Count > 0)
            {
                Status = result.Failures.Count == 1
                    ? $"{result.Failures[0].ProviderName} could not be read. See the calendar list."
                    : $"{result.Failures.Count} calendars could not be read. See the calendar list.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = $"Could not read the calendars: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The days the grid will show, which is more than the month itself.
    /// </summary>
    /// <remarks>
    /// A Sunday-start March 2026 runs to Saturday 4 April, and those cells are drawn, so their
    /// events have to be fetched or the last row looks empty.
    /// </remarks>
    private DateSpan VisibleWindow()
    {
        var first = new DateOnly(_month.Year, _month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var lead = ((int)first.DayOfWeek - (int)_weekStart + 7) % 7;
        var trail = 6 - (((int)last.DayOfWeek - (int)_weekStart + 7) % 7);

        return new DateSpan(first.AddDays(-lead), last.AddDays(trail));
    }

    /// <summary>Replaces the events on show and relays out.</summary>
    public void SetEvents(
        IReadOnlyList<CalendarEvent> events,
        IReadOnlyList<CalendarLegendEntry> calendars)
    {
        _events = events;
        _calendars = calendars;
        Relayout();
    }

    private void Relayout()
    {
        // Guard the combination the grid refuses rather than letting layout throw at the user.
        var weekend = _weekendMode is WeekendMode.CompressedWeekendColumn && !CanCompressWeekend
            ? WeekendMode.FullSevenDay
            : _weekendMode;

        var request = new LayoutRequest(
            Page: new PageSpec(_paper, _orientation, Margins.FromInches((float)_marginInches)),
            Grid: new MonthGridOptions(_month.Year, _month.Month, _weekStart, weekend, _adjacentDays),
            Culture: CultureInfo.CurrentCulture,
            Measurer: _measurer)
        {
            Events = _events,
            Calendars = _calendars,
            Fit = FitOptions.Default with { Policy = _fitPolicy },
            Style = MonthStyleOptions.Default with { ShowStartTime = _showStartTimes },
        };

        try
        {
            var scene = _style.Layout(request);
            Scene = scene;
            Status = DescribeFit(scene);
        }
        catch (ArgumentException ex)
        {
            // Reachable through the margin slider: margins large enough to leave no page.
            Scene = null;
            Status = ex.Message;
        }
    }

    /// <summary>
    /// Says what the layout had to do, and what the user could change about it.
    /// </summary>
    /// <remarks>
    /// A number on its own ("scaled to 80%") tells someone nothing they can act on. Every
    /// message here ends with a lever they can actually pull.
    /// </remarks>
    private static string DescribeFit(ScenePage scene)
    {
        var diagnostics = scene.Diagnostics;

        if (diagnostics.Warnings.Count > 0)
        {
            return string.Join("  ", diagnostics.Warnings);
        }

        return diagnostics.EffectiveScale >= 0.999f
            ? "Everything fits at full size."
            : $"Text scaled to {diagnostics.EffectiveScale:P0} so that everything fits.";
    }

    private void Set<T>(ref T field, T value, string[]? alsoNotify = null, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Raise(property);

        foreach (var other in alsoNotify ?? [])
        {
            Raise(other);
        }

        Relayout();
    }

    private void Raise(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
