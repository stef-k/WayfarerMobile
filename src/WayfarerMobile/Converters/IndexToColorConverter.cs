using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts an index value to a color based on whether it matches the parameter.
/// Returns Primary color when selected, Gray when not selected.
/// </summary>
public class IndexToColorConverter : IValueConverter
{
    /// <summary>
    /// Converts an index to a color based on matching the parameter.
    /// </summary>
    /// <param name="value">The current index value.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The index to match against (as string).</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>Primary color if matched, Gray otherwise.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int currentIndex && parameter is string paramStr && int.TryParse(paramStr, out var targetIndex))
        {
            if (currentIndex == targetIndex)
            {
                return Application.Current?.Resources["Primary"] as Color ?? Colors.Blue;
            }
        }

        // Return Gray for non-selected state
        return Application.Current?.Resources["Gray500"] as Color ?? Colors.Gray;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
