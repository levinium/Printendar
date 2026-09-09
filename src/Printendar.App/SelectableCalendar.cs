using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media;
using Printendar.App.Sources;
using Printendar.Core.Sources;
using SkiaSharp;

namespace Printendar.App;

/// <summary>
/// One calendar in the picker: whether it is being printed, and what colour it prints in.
/// </summary>
public sealed class SelectableCalendar : INotifyPropertyChanged
{
    private bool _isSelected;
    private SKColor _color;

    public SelectableCalendar(CalendarRef reference, SKColor color)
    {
        Reference = reference;
        _color = color;
        _isSelected = reference.IsDefault;

        Choices = [.. CalendarPalette.Colors.Select(c => new CalendarColorChoice(c, this))];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the printed colour changes, so it can be remembered.</summary>
    public event EventHandler? ColorChanged;

    public CalendarRef Reference { get; }

    public string DisplayName => Reference.DisplayName;

    /// <summary>The colours offered when the swatch is clicked.</summary>
    public IReadOnlyList<CalendarColorChoice> Choices { get; }

    public SKColor Color
    {
        get => _color;
        set
        {
            if (_color == value)
            {
                return;
            }

            _color = value;
            Raise(nameof(Color));
            Raise(nameof(Swatch));

            foreach (var choice in Choices)
            {
                choice.RefreshIsCurrent();
            }

            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The swatch beside the name, matching the colour it prints in.</summary>
    public IBrush Swatch => new SolidColorBrush(Avalonia.Media.Color.FromArgb(
        Color.Alpha, Color.Red, Color.Green, Color.Blue));

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            Raise(nameof(IsSelected));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One colour on the swatch menu.
/// </summary>
/// <remarks>
/// Carries its own command rather than being applied by a handler on the window. A colour
/// button lives three templates deep, inside a calendar, inside a source; binding it back up to
/// the window's view model needs a parent lookup and a cast that compiled bindings cannot check,
/// so a typo there fails silently at runtime instead of at build time.
/// </remarks>
public sealed class CalendarColorChoice : INotifyPropertyChanged
{
    private readonly SelectableCalendar _calendar;

    public CalendarColorChoice(SKColor color, SelectableCalendar calendar)
    {
        _calendar = calendar;
        Color = color;

        Choose = new RelayCommand(_ =>
        {
            calendar.Color = color;
            return Task.CompletedTask;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SKColor Color { get; }

    public ICommand Choose { get; }

    public IBrush Swatch => new SolidColorBrush(Avalonia.Media.Color.FromArgb(
        Color.Alpha, Color.Red, Color.Green, Color.Blue));

    /// <summary>Marks the colour this calendar is currently using, so the menu shows state.</summary>
    public bool IsCurrent => _calendar.Color == Color;

    internal void RefreshIsCurrent() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
}
