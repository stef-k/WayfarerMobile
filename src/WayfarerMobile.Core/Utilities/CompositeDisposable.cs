namespace WayfarerMobile.Core.Utilities;

/// <summary>
/// A container for managing multiple <see cref="IDisposable"/> resources.
/// When disposed, all contained disposables are disposed in reverse order.
/// </summary>
/// <remarks>
/// Common usage in ViewModels:
/// <code>
/// private readonly CompositeDisposable _subscriptions = new();
///
/// public void Initialize()
/// {
///     _subscriptions.Add(() => _service.SomeEvent -= OnSomeEvent);
///     _service.SomeEvent += OnSomeEvent;
/// }
///
/// public void Cleanup()
/// {
///     _subscriptions.Dispose();
/// }
/// </code>
/// </remarks>
public sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _disposables = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Gets whether this container has been disposed.
    /// </summary>
    public bool IsDisposed
    {
        get
        {
            lock (_lock)
            {
                return _disposed;
            }
        }
    }

    /// <summary>
    /// Gets the number of disposables in this container.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _disposed ? 0 : _disposables.Count;
            }
        }
    }

    /// <summary>
    /// Adds a disposable to this container.
    /// If the container is already disposed, the disposable is disposed immediately.
    /// </summary>
    /// <param name="disposable">The disposable to add.</param>
    public void Add(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);

        bool shouldDispose;
        lock (_lock)
        {
            shouldDispose = _disposed;
            if (!_disposed)
            {
                _disposables.Add(disposable);
            }
        }

        if (shouldDispose)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// Adds an action to be executed on disposal.
    /// This is a convenience method for event unsubscription.
    /// </summary>
    /// <param name="action">The action to execute on disposal.</param>
    /// <example>
    /// <code>
    /// // Subscribe to event
    /// _service.DataChanged += OnDataChanged;
    /// // Add cleanup action
    /// _subscriptions.Add(() => _service.DataChanged -= OnDataChanged);
    /// </code>
    /// </example>
    public void Add(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Add(new ActionDisposable(action));
    }

    /// <summary>
    /// Removes a disposable from this container without disposing it.
    /// </summary>
    /// <param name="disposable">The disposable to remove.</param>
    /// <returns>True if the disposable was found and removed.</returns>
    public bool Remove(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);

        lock (_lock)
        {
            if (_disposed)
            {
                return false;
            }

            return _disposables.Remove(disposable);
        }
    }

    /// <summary>
    /// Disposes all contained disposables in reverse order and clears the container.
    /// Can be called multiple times safely.
    /// </summary>
    public void Dispose()
    {
        List<IDisposable> toDispose;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            toDispose = new List<IDisposable>(_disposables);
            _disposables.Clear();
        }

        // Dispose in reverse order (LIFO)
        for (int i = toDispose.Count - 1; i >= 0; i--)
        {
            try
            {
                toDispose[i].Dispose();
            }
            catch
            {
                // Swallow exceptions during disposal
            }
        }
    }

    /// <summary>
    /// Clears the container, disposing all contained disposables.
    /// The container can be reused after calling this method.
    /// </summary>
    public void Clear()
    {
        List<IDisposable> toDispose;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            toDispose = new List<IDisposable>(_disposables);
            _disposables.Clear();
        }

        // Dispose in reverse order (LIFO)
        for (int i = toDispose.Count - 1; i >= 0; i--)
        {
            try
            {
                toDispose[i].Dispose();
            }
            catch
            {
                // Swallow exceptions during disposal
            }
        }
    }

    /// <summary>
    /// Simple IDisposable wrapper for an action.
    /// </summary>
    private sealed class ActionDisposable : IDisposable
    {
        private Action? _action;

        public ActionDisposable(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _action, null);
            action?.Invoke();
        }
    }
}
