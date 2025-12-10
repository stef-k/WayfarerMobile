using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts a value to true if it is not null.
/// </summary>
public class IsNotNullConverter : IValueConverter
{
    /// <summary>
    /// Converts a value to true if not null.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a value to true if it is null.
/// </summary>
public class IsNullConverter : IValueConverter
{
    /// <summary>
    /// Converts a value to true if null.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean to one of two text values.
/// Parameter format: "TrueText|FalseText"
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to one of two text values.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return string.Empty;

        var texts = (parameter as string)?.Split('|');
        if (texts == null || texts.Length < 2)
            return boolValue.ToString();

        return boolValue ? texts[0] : texts[1];
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a percentage (0-100) to a decimal (0.0-1.0).
/// </summary>
public class PercentToDecimalConverter : IValueConverter
{
    /// <summary>
    /// Converts a percentage to a decimal.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
            return intValue / 100.0;

        if (value is double doubleValue)
            return doubleValue / 100.0;

        return 0.0;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
