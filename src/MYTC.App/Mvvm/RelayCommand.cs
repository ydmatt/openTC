using System.Windows.Input;

namespace MYTC.App.Mvvm;

public sealed class RelayCommand(
    Action execute,
    Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T>(
    Action<T?> execute,
    Predicate<T?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke(ConvertParameter(parameter)) ?? true;
    }

    public void Execute(object? parameter)
    {
        execute(ConvertParameter(parameter));
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? ConvertParameter(object? parameter)
    {
        if (parameter is null)
        {
            return default;
        }

        return parameter is T value
            ? value
            : (T?)Convert.ChangeType(parameter, typeof(T));
    }
}
