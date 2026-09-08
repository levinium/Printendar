using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Printendar.App.Controls;
using Printendar.Core.Text;

namespace Printendar.App;

public partial class PrintDialogWindow : Window
{
    private readonly PrintDialogViewModel _model = null!;
    private readonly ITextMeasurer _measurer = null!;
    private readonly string _title = string.Empty;

    /// <summary>Parameterless constructor for the XAML previewer only.</summary>
    public PrintDialogWindow() => InitializeComponent();

    public PrintDialogWindow(PrintDialogViewModel model, ITextMeasurer measurer, string title)
    {
        _model = model;
        _measurer = measurer;
        _title = title;

        InitializeComponent();
        DataContext = model;

        // The preview must draw with the measurer the scene was laid out against, or it would
        // be drawing the page with different fonts than it was measured with.
        this.FindControl<ScenePreview>("Preview")!.Measurer = measurer;

        this.FindControl<Button>("Cancel")!.Click += (_, _) => Close(null);
        this.FindControl<Button>("Confirm")!.Click += (_, _) => Close(_model.Print(_measurer, _title));
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
