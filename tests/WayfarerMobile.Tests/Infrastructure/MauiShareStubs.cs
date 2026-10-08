public static class FileSystem
{
    public static PackageFiles Current { get; } = new();
    public sealed class PackageFiles
    {
        public AsyncLocal<Func<string, Task<Stream>>?> Open { get; } = new();
        public Task<Stream> OpenAppPackageFileAsync(string path) => Open.Value?.Invoke(path)
            ?? Task.FromException<Stream>(new FileNotFoundException());
    }

    // Per-async-flow isolation prevents migration cleanup from touching another test's files.
    public static AsyncLocal<string?> DatabaseTestRoot { get; } = new();
    public static string AppDataDirectory => DatabaseTestRoot.Value
        ?? throw new InvalidOperationException("A database test must set its isolated root.");
    public static string CacheDirectory => DatabaseTestRoot.Value ?? Path.GetTempPath();
}

public sealed class Share
{
    public static Share Default { get; } = new();

    public Task RequestAsync(ShareFileRequest request) => Task.CompletedTask;
    public Task RequestAsync(ShareTextRequest request) => Task.CompletedTask;
}

public sealed class ShareTextRequest
{
    public string? Text { get; init; }
    public string? Title { get; init; }
}

public sealed class ShareFileRequest
{
    public string? Title { get; init; }
    public ShareFile? File { get; init; }
}

public sealed class ShareFile(string fullPath)
{
    public string FullPath { get; } = fullPath;
}

public sealed record Location(double Latitude, double Longitude);

public sealed class MapLaunchOptions
{
    public string? Name { get; init; }
    public NavigationMode NavigationMode { get; init; }
}

public enum NavigationMode { None, Walking }

public sealed class PlatformMap
{
    public static PlatformMap Default { get; } = new();

    public Task OpenAsync(Location location, MapLaunchOptions options) => Task.CompletedTask;
}

public sealed class Clipboard
{
    public static Clipboard Default { get; } = new();

    public Task SetTextAsync(string text) => Task.CompletedTask;
}

public sealed class Launcher
{
    public static Launcher Default { get; } = new();

    public Task<bool> OpenAsync(Uri uri) => Task.FromResult(true);
}

public sealed class HtmlWebViewSource
{
    public string Html { get; set; } = string.Empty;
}

public enum AppTheme { Unspecified, Light, Dark }

/// <summary>Exposes navigation parameters to the linked notes ViewModel without a mounted MAUI shell.</summary>
public interface IQueryAttributable
{
    void ApplyQueryAttributes(IDictionary<string, object> query);
}

public sealed class Shell
{
    public static Shell Current { get; } = new();
    public Task GoToAsync(string route) => Task.CompletedTask;
}

public class Application
{
    private static readonly AsyncLocal<Application?> current = new();
    public static Application? Current { get => current.Value; set => current.Value = value; }
    public List<Window> Windows { get; } = [];
    public AppTheme RequestedTheme { get; set; }
}

public enum NetworkAccess { None, Internet, ConstrainedInternet }
public sealed class ConnectivityChangedEventArgs(NetworkAccess networkAccess) : EventArgs { public NetworkAccess NetworkAccess { get; } = networkAccess; }
public interface IConnectivity
{
    NetworkAccess NetworkAccess { get; }
    event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
}
/// <summary>Mirrors MAUI's static event forwarding for the linked production SSE client.</summary>
public static class Connectivity
{
    public static IConnectivity Current { get; set; } = new ConnectivityStub();
    public static event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged
    {
        add => Current.ConnectivityChanged += value;
        remove => Current.ConnectivityChanged -= value;
    }

    private sealed class ConnectivityStub : IConnectivity
    {
        public NetworkAccess NetworkAccess => NetworkAccess.Internet;
        public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
    }
}
namespace Microsoft.Maui.ApplicationModel
{
    public sealed class Map
    {
        public static global::PlatformMap Default { get; } = global::PlatformMap.Default;
        public static Task OpenAsync(global::Location location, global::MapLaunchOptions options) =>
            Task.CompletedTask;
    }

    public static class MainThread
    {
        public static bool IsMainThread => true;
        public static void BeginInvokeOnMainThread(Action action) => action();

        public static Task InvokeOnMainThreadAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public static Task InvokeOnMainThreadAsync(Func<Task> action) => action();
    }
}

namespace Microsoft.Maui.ApplicationModel.DataTransfer
{
}

namespace Microsoft.Maui.Graphics
{
    public sealed class Color { }
    public static class Colors
    {
        public static Color Gray { get; } = new();
        public static Color Green { get; } = new();
        public static Color Orange { get; } = new();
        public static Color Red { get; } = new();
    }
}

namespace WayfarerMobile.Helpers
{
    public static class NotesViewerHelper
    {
        public static global::HtmlWebViewSource PrepareNotesHtml(
            string html, string? backendBaseUrl, bool isDark) => new() { Html = html };
    }
}

public sealed class FeatureNotSupportedException : Exception { }

public sealed class Window { public Page? Page { get; set; } }
public sealed class Page
{
    public Func<string, string, string?, string[], Task<string>>? ActionSheet { get; set; }
    public Task<string> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] choices) =>
        ActionSheet!(title, cancel, destruction, choices);
    public Task DisplayAlertAsync(string title, string message, string accept) => Task.CompletedTask;
    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) => Task.FromResult(false);
}
