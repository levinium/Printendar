using System.Windows.Input;

namespace Printendar.App.Sources;

/// <summary>
/// A command that runs an async action and cannot be started twice at once.
/// </summary>
/// <remarks>
/// The re-entrancy guard is the point. Remove and Reconnect both talk to a provider and both
/// change the list they were invoked from; a second click while the first is still running
/// would remove an entry twice or sign in twice. Disabling while busy is simpler than making
/// every operation idempotent.
/// </remarks>
public sealed class RelayCommand(Func<object?, Task> execute) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await execute(parameter).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Every caller already reports its own failure against the source it belongs to.
            // Letting it escape here would reach Avalonia as an unhandled exception on the UI
            // thread and take the window down.
        }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
