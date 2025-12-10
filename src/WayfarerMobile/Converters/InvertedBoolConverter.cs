using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts a boolean value to its inverse.
/// </summary>
public class InvertedBoolConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to its inverse.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }

    /// <summary>
    /// Converts back (inverse of inverse).
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}
