using System.Collections.Concurrent;
using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Visibility-gated version of MauiAssetImageConverter.
/// Returns null when page is not visible, preventing async image load crashes (issue #185).
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;Image&gt;
///     &lt;Image.Source&gt;
///         &lt;MultiBinding Converter="{StaticResource VisibilityGatedMauiAssetImageConverter}"&gt;
///             &lt;Binding Path="IconPath" /&gt;
///             &lt;Binding Path="IsPageVisible" Source="{RelativeSource AncestorType={x:Type viewmodels:MainViewModel}}" /&gt;
///         &lt;/MultiBinding&gt;
///     &lt;/Image.Source&gt;
/// &lt;/Image&gt;
/// </code>
/// </remarks>
public class VisibilityGatedMauiAssetImageConverter : IMultiValueConverter
{
    /// <summary>
    /// Shared cache with MauiAssetImageConverter to avoid duplicate loading.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]?> _bytesCache = new();

    /// <summary>
    /// Converts resource path and visibility to an ImageSource.
    /// </summary>
    /// <param name="values">
    /// values[0]: The resource path (string)
    /// values[1]: The visibility flag (bool) - true if page is visible
    /// </param>
    /// <param name="targetType">The target type (ImageSource).</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Culture info.</param>
    /// <returns>A StreamImageSource for the resource if visible, null otherwise.</returns>
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Need at least 2 values: path and visibility
        if (values == null || values.Length < 2)
            return null;

        // Check visibility first - if not visible, return null to prevent async loads
        var isVisible = values[1] is bool visible && visible;
        if (!isVisible)
            return null;

        // Get resource path
        if (values[0] is not string resourcePath || string.IsNullOrWhiteSpace(resourcePath))
            return null;

        // Check bytes cache first (synchronous path - no async callback risk)
        if (_bytesCache.TryGetValue(resourcePath, out var cachedBytes))
        {
            if (cachedBytes == null)
                return null;

            return ImageSource.FromStream(() => new MemoryStream(cachedBytes));
        }

        // Return a StreamImageSource that loads asynchronously
        return new StreamImageSource
        {
            Stream = async (cancellationToken) =>
            {
                try
                {
                    // Check cache again (another thread might have loaded it)
                    if (_bytesCache.TryGetValue(resourcePath, out var bytes))
                    {
                        return bytes != null ? new MemoryStream(bytes) : null;
                    }

                    // Load from app package
                    using var stream = await FileSystem.Current.OpenAppPackageFileAsync(resourcePath);
                    if (stream == null)
                    {
                        _bytesCache[resourcePath] = null;
                        return null;
                    }

                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream, cancellationToken);
                    var imageBytes = memoryStream.ToArray();

                    // Cache the bytes
                    _bytesCache[resourcePath] = imageBytes;

                    return new MemoryStream(imageBytes);
                }
                catch
                {
                    _bytesCache[resourcePath] = null;
                    return null;
                }
            }
        };
    }

    /// <summary>
    /// Not supported - one-way conversion only.
    /// </summary>
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Clears the image cache.
    /// </summary>
    public static void ClearCache()
    {
        _bytesCache.Clear();
    }
}
