using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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
using Printendar.Sources.Microsoft365;

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

    public MainViewModel() => Relayout();

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

    private ICalendarSource? _source;
    private bool _isBusy;
    private string? _accountLabel;

    public ObservableCollection<SelectableCalendar> Calendars { get; } = [];

    // ------------------------------------------------------------------ Microsoft setup

    private readonly SettingsStore _settingsStore = new(new DesktopSettingsLocation());
    private AppSettings _settings = new();
    private bool _showMicrosoftSetup;

    /// <summary>The registration sign-in will use, given what is saved right now.</summary>
    public Microsoft365Options MicrosoftOptions => Microsoft365Options.Resolve(_settings);

    /// <summary>
    /// Whether a Microsoft application id is available.
    /// </summary>
    /// <remarks>
    /// Read from the resolved options rather than fixed at construction, so entering an id in
    /// the window enables the button immediately instead of after a restart.
    /// </remarks>
    public bool IsMicrosoftConfigured => MicrosoftOptions.IsConfigured;

    public bool IsMicrosoftNotConfigured => !IsMicrosoftConfigured;

    /// <summary>Whether the "use our own registration" panel is open.</summary>
    public bool ShowMicrosoftSetup
    {
        get => _showMicrosoftSetup;
        set
        {
            _showMicrosoftSetup = value;
            Raise(nameof(ShowMicrosoftSetup));
        }
    }

    /// <summary>An organisation's own application id, as typed into the window.</summary>
    public string? MicrosoftClientId
    {
        get => _settings.MicrosoftClientId;
        set
        {
            _settings = _settings with { MicrosoftClientId = value };
            Raise(nameof(MicrosoftClientId));
        }
    }

    public string? MicrosoftTenant
    {
        get => _settings.MicrosoftTenant;
        set
        {
            _settings = _settings with { MicrosoftTenant = value };
            Raise(nameof(MicrosoftTenant));
        }
    }

    /// <summary>Where an administrator approves the registration currently in use.</summary>
    public string MicrosoftAdminConsentUrl =>
        MicrosoftOptions.IsConfigured
            ? Microsoft365Diagnostics.BuildAdminConsentUrl(MicrosoftOptions.ClientId)
            : string.Empty;

    /// <summary>Where an administrator creates a registration, if they want their own.</summary>
    public static string PortalNewRegistrationUrl => Microsoft365Options.PortalNewRegistrationUrl;

    /// <summary>The exact settings a new registration needs, shown so nothing is guessed at.</summary>
    public static string RegistrationRecipe =>
        "Name: anything, for example Printendar\n" +
        "Supported account types: accounts in any organizational directory and personal Microsoft accounts\n" +
        "Redirect URI: Public client/native (mobile & desktop) -> http://localhost\n" +
        "API permissions, delegated: User.Read, Calendars.Read, Calendars.Read.Shared\n" +
        "No client secret. Printendar is a public client and holds none.";

    /// <summary>Saves the entered registration and reports what it means.</summary>
    public void SaveMicrosoftRegistration()
    {
        _settingsStore.Save(_settings);
        _settings = _settingsStore.Load();

        Raise(nameof(MicrosoftClientId));
        Raise(nameof(MicrosoftTenant));
        Raise(nameof(IsMicrosoftConfigured));
        Raise(nameof(IsMicrosoftNotConfigured));
        Raise(nameof(MicrosoftAdminConsentUrl));

        Status = IsMicrosoftConfigured
            ? "Saved. Click Connect Microsoft 365 to sign in."
            : "Cleared. Printendar will use its own registration, if this build has one.";

        ShowMicrosoftSetup = false;
    }

    /// <summary>Loads saved settings. Called once, when the window is being built.</summary>
    public void LoadSettings()
    {
        _settings = _settingsStore.Load();

        Raise(nameof(MicrosoftClientId));
        Raise(nameof(MicrosoftTenant));
        Raise(nameof(IsMicrosoftConfigured));
        Raise(nameof(IsMicrosoftNotConfigured));
        Raise(nameof(MicrosoftAdminConsentUrl));
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

    /// <summary>Who is signed in, or null when nobody is.</summary>
    public string? AccountLabel
    {
        get => _accountLabel;
        private set
        {
            _accountLabel = value;
            Raise(nameof(AccountLabel));
            Raise(nameof(IsConnected));
            Raise(nameof(IsNotConnected));
        }
    }

    public bool IsConnected => _accountLabel is not null;

    public bool IsNotConnected => _accountLabel is null;

    /// <summary>
    /// Signs in and loads the account's calendars.
    /// </summary>
    /// <remarks>
    /// The sample month stays on screen until real events arrive, so the window never falls
    /// back to an empty grid that looks like something went wrong.
    /// </remarks>
    public async Task ConnectAsync(ICalendarSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        IsBusy = true;

        try
        {
            var account = await source.ConnectAsync(cancellationToken).ConfigureAwait(true);
            var calendars = await source.ListCalendarsAsync(cancellationToken).ConfigureAwait(true);

            _source = source;
            AccountLabel = account.Email ?? account.DisplayName;

            Calendars.Clear();

            for (var i = 0; i < calendars.Count; i++)
            {
                var selectable = new SelectableCalendar(calendars[i], CalendarPalette.At(i));
                selectable.SelectionChanged += (_, _) => _ = RefreshEventsAsync();
                Calendars.Add(selectable);
            }

            await RefreshEventsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Microsoft365SignInException ex)
        {
            // Already translated into advice at the source boundary.
            AdminConsentUrl = ex.Diagnosis.AdminConsentUrl;

            if (ex.Diagnosis.Problem is not SignInProblem.Cancelled)
            {
                Status = ex.Diagnosis.Message;
            }
        }
        catch (OperationCanceledException)
        {
            // The user closed the browser window. Not an error worth reporting.
        }
        catch (Exception ex)
        {
            Status = $"Could not connect: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? _adminConsentUrl;

    /// <summary>
    /// Where an administrator approves Printendar for the whole organisation, when that is
    /// what is standing in the way.
    /// </summary>
    /// <remarks>
    /// Only set when the identity platform actually said an administrator is needed. Offering
    /// the link speculatively would send people to a page most of them do not need and cannot
    /// use.
    /// </remarks>
    public string? AdminConsentUrl
    {
        get => _adminConsentUrl;
        private set
        {
            _adminConsentUrl = value;
            Raise(nameof(AdminConsentUrl));
            Raise(nameof(NeedsAdminConsent));
        }
    }

    public bool NeedsAdminConsent => _adminConsentUrl is not null;

    /// <summary>Clears the administrator prompt, after they have been sent to approve it.</summary>
    public void ClearAdminConsentPrompt() => AdminConsentUrl = null;

    /// <summary>
    /// Opens one or more .ics files as the calendars to print.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="ConnectAsync"/> because there is nothing to connect to:
    /// no account, no approval, no network. That is the whole appeal of this route, and
    /// routing it through a sign-in flow would imply otherwise.
    /// </remarks>
    public async Task OpenCalendarFilesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        IsBusy = true;

        try
        {
            var source = new IcsFileCalendarSource();
            var added = new List<CalendarRef>();

            foreach (var path in paths)
            {
                added.Add(source.AddFile(path));
            }

            _source = source;
            AccountLabel = added.Count == 1
                ? added[0].DisplayName
                : $"{added.Count} calendar files";

            Calendars.Clear();

            for (var i = 0; i < added.Count; i++)
            {
                var selectable = new SelectableCalendar(added[i], CalendarPalette.At(i)) { IsSelected = true };
                selectable.SelectionChanged += (_, _) => _ = RefreshEventsAsync();
                Calendars.Add(selectable);
            }

            await RefreshEventsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_source is null)
        {
            return;
        }

        await _source.SignOutAsync(cancellationToken).ConfigureAwait(true);
        await _source.DisposeAsync().ConfigureAwait(true);

        _source = null;
        AccountLabel = null;
        Calendars.Clear();

        // Back to the sample month rather than an empty grid.
        SetEvents(SampleCalendar.ForMonth(_month.Year, _month.Month), SampleCalendar.Calendars);
    }

    /// <summary>Fetches the visible month from the selected calendars.</summary>
    public async Task RefreshEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_source is null)
        {
            return;
        }

        var selected = Calendars.Where(c => c.IsSelected).ToList();

        if (selected.Count == 0)
        {
            SetEvents([], []);
            return;
        }

        IsBusy = true;

        try
        {
            // The grid draws days either side of the month, so those are fetched too.
            var window = VisibleWindow();

            var events = await _source.GetEventsAsync(
                [.. selected.Select(c => c.Reference)],
                window,
                TimeZoneInfo.Local,
                cancellationToken).ConfigureAwait(true);

            SetEvents(
                events,
                [.. selected.Select(c => new CalendarLegendEntry(
                    c.Reference.CalendarId,
                    c.DisplayName,
                    c.Color))]);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = $"Could not read the calendar: {ex.Message}";
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
