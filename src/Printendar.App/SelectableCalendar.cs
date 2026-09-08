using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Printendar.Core.Sources;
using SkiaSharp;

namespace Printendar.App;

/// <summary>
/// One calendar in the picker: whether it is being printed, and what colour it prints in.
/// </summary>
public sealed class SelectableCalendar : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableCalendar(CalendarRef reference, SKColor color)
    {
        Reference = reference;
        Color = color;
        _isSelected = reference.IsDefault;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CalendarRef Reference { get; }

    public SKColor Color { get; }

    public string DisplayName => Reference.DisplayName;

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;
}

/// <summary>
/// The colours calendars are printed in.
/// </summary>
/// <remarks>
/// Assigned by Printendar rather than read from the provider. Graph exposes category names but
/// not the colour values Outlook actually shows, and Google's palette is different again, so
/// reading them would give an inconsistent set that still had to be remapped.
///
/// Chosen to stay distinguishable for the common forms of colour blindness, and to separate
/// into different greys when printed in black and white, which is how most of these end up on
/// a wall.
/// </remarks>
public static class CalendarPalette
{
    private static readonly SKColor[] Colors =
    [
        new(0x1F, 0x77, 0xB4),
        new(0xD6, 0x27, 0x28),
        new(0x2C, 0xA0, 0x2C),
        new(0x94, 0x67, 0xBD),
        new(0xFF, 0x7F, 0x0E),
        new(0x8C, 0x56, 0x4B),
        new(0x17, 0xBE, 0xCF),
        new(0x7F, 0x7F, 0x7F),
    ];

    /// <summary>Assigns a colour by position, wrapping once the palette runs out.</summary>
    public static SKColor At(int index) => Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
}
