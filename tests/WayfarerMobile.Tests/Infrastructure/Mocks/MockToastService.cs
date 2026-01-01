using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of IToastService for testing.
/// Captures all toast messages for verification.
/// </summary>
public class MockToastService : IToastService
{
    private readonly List<ToastCall> _calls = new();

    /// <summary>
    /// Gets all recorded toast calls.
    /// </summary>
    public IReadOnlyList<ToastCall> Calls => _calls;

    /// <summary>
    /// Gets all messages shown.
    /// </summary>
    public IEnumerable<string> Messages => _calls.Select(c => c.Message);

    /// <summary>
    /// Gets all error messages.
    /// </summary>
    public IEnumerable<string> ErrorMessages => _calls
        .Where(c => c.Type == ToastType.Error)
        .Select(c => c.Message);

    /// <summary>
    /// Gets all success messages.
    /// </summary>
    public IEnumerable<string> SuccessMessages => _calls
        .Where(c => c.Type == ToastType.Success)
        .Select(c => c.Message);

    /// <summary>
    /// Gets all warning messages.
    /// </summary>
    public IEnumerable<string> WarningMessages => _calls
        .Where(c => c.Type == ToastType.Warning)
        .Select(c => c.Message);

    /// <inheritdoc/>
    public Task ShowAsync(string message, int duration = 3000)
    {
        _calls.Add(new ToastCall(ToastType.Info, message, duration));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ShowSuccessAsync(string message)
    {
        _calls.Add(new ToastCall(ToastType.Success, message, 3000));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ShowErrorAsync(string message)
    {
        _calls.Add(new ToastCall(ToastType.Error, message, 3000));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ShowWarningAsync(string message)
    {
        _calls.Add(new ToastCall(ToastType.Warning, message, 3000));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears all recorded calls.
    /// </summary>
    public void Reset() => _calls.Clear();

    /// <summary>
    /// Verifies that a message was shown.
    /// </summary>
    public bool WasShown(string message) => _calls.Any(c => c.Message == message);

    /// <summary>
    /// Verifies that an error was shown.
    /// </summary>
    public bool WasErrorShown(string message) =>
        _calls.Any(c => c.Type == ToastType.Error && c.Message == message);
}

/// <summary>
/// Record of a toast call.
/// </summary>
public record ToastCall(ToastType Type, string Message, int Duration);

/// <summary>
/// Type of toast message.
/// </summary>
public enum ToastType
{
    Info,
    Success,
    Error,
    Warning
}
