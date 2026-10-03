using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace IndustrialVision.App.Commands;

/// <summary>
/// Base ViewModel class implementing INotifyPropertyChanged.
/// All ViewModels in the application must inherit from this.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Generic async relay command for MVVM binding.
/// Supports async execution and CanExecute logic.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
    }

    /// <summary>
    /// Receives any exception that escaped a command. Without this, an exception thrown from an
    /// async void handler would terminate the whole application.
    /// </summary>
    public static Action<Exception>? UnhandledException { get; set; }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            try
            {
                if (UnhandledException != null) UnhandledException(ex);
                else System.Diagnostics.Debug.WriteLine($"[AsyncRelayCommand] {ex}");
            }
            catch
            {
                // The error reporter itself must never crash the app.
            }
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
