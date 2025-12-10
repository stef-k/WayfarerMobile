using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts a boolean value to a color (green for true, primary for false).
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to a color.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Green for granted/success, Primary for pending
            return boolValue
                ? Color.FromArgb("#4CAF50") // Green
                : Application.Current?.Resources["Primary"] as Color ?? Colors.Blue;
        }
        return Application.Current?.Resources["Primary"] as Color ?? Colors.Blue;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
