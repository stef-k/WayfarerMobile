using CommunityToolkit.Mvvm.ComponentModel;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Base class for all ViewModels providing common functionality.
/// Uses CommunityToolkit.Mvvm source generators.
/// Implements IDisposable for proper cleanup of event subscriptions and resources.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Gets or sets whether the ViewModel is busy performing an operation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>
    /// Gets or sets the title for the view.
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Gets whether the ViewModel is not busy.
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Gets whether this ViewModel has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public virtual Task OnAppearingAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public virtual Task OnDisappearingAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs cleanup of resources. Override this method to unsubscribe from events
    /// and dispose of any resources held by the ViewModel.
    /// </summary>
    /// <remarks>
    /// Derived classes should override this method to clean up their own resources,
    /// then call base.Cleanup() to ensure proper disposal chain.
    /// </remarks>
    protected virtual void Cleanup()
    {
        // Base implementation does nothing.
        // Derived classes override to unsubscribe from events and dispose resources.
    }

    /// <summary>
    /// Disposes of resources held by this ViewModel.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of resources held by this ViewModel.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            Cleanup();
        }

        _disposed = true;
    }
}
