using System.Windows;

namespace StopWastingTime.App.Infrastructure;

/// <summary>Asking the user to confirm something, without the view models knowing what a window is.</summary>
public interface IDialogService
{
    bool Confirm(string title, string message);
}

/// <summary>The WPF answer: a modal box owned by whichever window is in front.</summary>
public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message)
    {
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive) ?? Application.Current.MainWindow;

        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            : MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        return result == MessageBoxResult.OK;
    }
}
