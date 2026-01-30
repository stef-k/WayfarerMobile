using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts a boolean value to a Thickness (margin/padding).
/// Parameter format: "TrueMargins|FalseMargins" (e.g., "8,44,0,0|8,8,0,0")
/// where margins are in the format "left,top,right,bottom".
/// </summary>
public class BoolToThicknessConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to a Thickness based on the parameter.
    /// </summary>
    /// <param name="value">The boolean value.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Parameter in format "TrueMargins|FalseMargins".</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>A Thickness value.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue || parameter is not string paramString)
            return new Thickness(0);

        var parts = paramString.Split('|');
        if (parts.Length != 2)
            return new Thickness(0);

        var marginString = boolValue ? parts[0] : parts[1];
        return ParseThickness(marginString);
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Parses a comma-separated string into a Thickness.
    /// </summary>
    /// <param name="marginString">String in format "left,top,right,bottom" or "all" or "horizontal,vertical".</param>
    /// <returns>The parsed Thickness.</returns>
    private static Thickness ParseThickness(string marginString)
    {
        var values = marginString.Split(',');

        return values.Length switch
        {
            1 when double.TryParse(values[0], out var all) => new Thickness(all),
            2 when double.TryParse(values[0], out var h) && double.TryParse(values[1], out var v) =>
                new Thickness(h, v),
            4 when double.TryParse(values[0], out var l) &&
                   double.TryParse(values[1], out var t) &&
                   double.TryParse(values[2], out var r) &&
                   double.TryParse(values[3], out var b) =>
                new Thickness(l, t, r, b),
            _ => new Thickness(0)
        };
    }
}
