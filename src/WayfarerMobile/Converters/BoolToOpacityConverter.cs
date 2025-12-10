using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts a boolean to an opacity value.
/// True = 1.0 (fully visible), False = 0.5 (dimmed).
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to an opacity value.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected)
        {
            return isSelected ? 1.0 : 0.5;
        }
        return 0.5;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
