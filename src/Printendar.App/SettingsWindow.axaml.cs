using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Printendar.Core;
using Printendar.Core.Settings;

namespace Printendar.App;

/// <summary>
/// The few things about Printendar itself that are worth being able to change.
/// </summary>
/// <remarks>
/// Deliberately short. Everything about what gets printed belongs beside the preview where the
/// effect is visible; this is for the window rather than the page, plus the version somebody
/// needs when reporting a problem.
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store = null!;
    private AppSettings _settings = new();

    /// <summary>True while the boxes are being set up, so doing that does not save anything.</summary>
    private bool _loading;

    /// <summary>Parameterless constructor for the XAML previewer only.</summary>
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;

        InitializeComponent();

        _loading = true;

        Radio(AppTheme.System).IsChecked = settings.Theme == AppTheme.System;
        Radio(AppTheme.Light).IsChecked = settings.Theme == AppTheme.Light;
        Radio(AppTheme.Dark).IsChecked = settings.Theme == AppTheme.Dark;

        _loading = false;

        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            // Captured, because the handler outlives the loop and the loop variable would
            // otherwise be whatever it finished as.
            var chosen = theme;
            Radio(theme).IsCheckedChanged += (_, _) => OnThemeChanged(chosen);
        }

        this.FindControl<TextBlock>("VersionValue")!.Text = AppVersion.Current ?? "unknown";

        // The real path, not a description of it. Somebody looking for this file is usually
        // trying to fix something or to copy their calendars to another machine, and "your
        // application data folder" is a scavenger hunt.
        this.FindControl<TextBlock>("SettingsPath")!.Text = store.Path;

        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();

        this.FindControl<Button>("OpenRepository")!.Click += (_, _) =>
            Open("https://github.com/levinium/Printendar");
    }

    /// <summary>Whether anything was changed, so the caller can take it on board.</summary>
    public bool SettingsChanged { get; private set; }

    private RadioButton Radio(AppTheme theme) => this.FindControl<RadioButton>(theme switch
    {
        AppTheme.Light => "ThemeLight",
        AppTheme.Dark => "ThemeDark",
        _ => "ThemeSystem",
    })!;

    /// <summary>
    /// Applies and saves a theme the moment it is picked.
    /// </summary>
    /// <remarks>
    /// Radio buttons in a group raise this twice per change, once for the one being cleared and
    /// once for the one being set, so only the checked one is acted on. Without that the theme
    /// would be set to whatever was just turned off.
    /// </remarks>
    private void OnThemeChanged(AppTheme theme)
    {
        if (_loading || Radio(theme).IsChecked != true || _settings.Theme == theme)
        {
            return;
        }

        _settings = _settings with { Theme = theme };

        _store.Save(_settings);
        SettingsChanged = true;

        App.ApplyTheme(theme);
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser, or the shell refused. Not worth taking the window down over.
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
