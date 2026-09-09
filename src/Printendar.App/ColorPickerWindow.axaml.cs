using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Printendar.Core.Sources;
using SkiaSharp;

namespace Printendar.App;

/// <summary>
/// Picking a colour off a spectrum, the way every graphics program does it.
/// </summary>
/// <remarks>
/// This replaced a box you typed six hex digits into. That was defensible as a hurried
/// escape hatch and indefensible as the only way to choose a colour in a program whose whole
/// argument is that it is simpler than the alternative.
/// </remarks>
public partial class ColorPickerWindow : Window
{
    private readonly IReadOnlyList<NamedColor> _others = [];

    /// <summary>Parameterless constructor for the XAML previewer only.</summary>
    public ColorPickerWindow() => InitializeComponent();

    /// <param name="calendarName">Whose colour is being chosen, so the window says so.</param>
    /// <param name="current">Where the picker starts.</param>
    /// <param name="others">The colours the other calendars print in, to warn about clashes.</param>
    public ColorPickerWindow(string calendarName, SKColor current, IReadOnlyList<NamedColor> others)
    {
        _others = others;

        InitializeComponent();

        this.FindControl<TextBlock>("Explanation")!.Text =
            $"What colour should {calendarName} print in?";

        var picker = this.FindControl<ColorView>("Picker")!;
        picker.Color = Color.FromRgb(current.Red, current.Green, current.Blue);

        // Live, because the two things worth saying about a colour are things you would
        // otherwise find out from the printer.
        picker.ColorChanged += (_, _) => Refresh();

        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
        this.FindControl<Button>("ApplyButton")!.Click += (_, _) =>
        {
            Chosen = ToSkia(picker.Color);
            Close();
        };

        Refresh();
    }

    /// <summary>The colour picked, or null if the window was closed without choosing.</summary>
    public SKColor? Chosen { get; private set; }

    private void Refresh()
    {
        var color = this.FindControl<ColorView>("Picker")!.Color;

        this.FindControl<Border>("Preview")!.Background = new SolidColorBrush(color);

        var warning = this.FindControl<TextBlock>("Warning")!;
        var advice = ColorAdvice.Describe(ToSkia(color), _others);

        warning.Text = advice;
        warning.IsVisible = advice is not null;
    }

    private static SKColor ToSkia(Color color) => new(color.R, color.G, color.B);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
