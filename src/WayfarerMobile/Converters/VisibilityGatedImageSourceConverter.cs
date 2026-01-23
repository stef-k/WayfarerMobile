using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Multi-value converter that returns an image source only when the page is visible.
/// Use with MultiBinding to gate image loading by page visibility state.
/// This prevents ObjectDisposedException crashes from async image load callbacks
/// when the page disappears (issue #185).
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;Image&gt;
///     &lt;Image.Source&gt;
///         &lt;MultiBinding Converter="{StaticResource VisibilityGatedImageSourceConverter}"&gt;
///             &lt;Binding Path="CleanCoverImageUrl" /&gt;
///             &lt;Binding Path="IsPageVisible" Source="{RelativeSource AncestorType={x:Type viewmodels:MainViewModel}}" /&gt;
///         &lt;/MultiBinding&gt;
///     &lt;/Image.Source&gt;
/// &lt;/Image&gt;
/// </code>
/// </remarks>
public class VisibilityGatedImageSourceConverter : IMultiValueConverter
{
    /// <summary>
    /// Converts multiple values to an image source.
    /// </summary>
    /// <param name="values">
    /// values[0]: The image URL or source (string or ImageSource)
    /// values[1]: The visibility flag (bool) - true if page is visible
    /// </param>
    /// <param name="targetType">The target type (ImageSource).</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Culture info.</param>
    /// <returns>The image source if visible, null otherwise.</returns>
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Need at least 2 values: source and visibility
        if (values == null || values.Length < 2)
            return null;

        var source = values[0];
        var isVisible = values[1] is bool visible && visible;

        // If not visible, return null to cancel/prevent image loading
        if (!isVisible)
            return null;

        // Return the source as-is (MAUI will handle string URLs and ImageSource objects)
        return source;
    }

    /// <summary>
    /// Not supported - one-way conversion only.
    /// </summary>
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
