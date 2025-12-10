using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts a boolean (expanded state) to an expand/collapse icon.
/// </summary>
public class BoolToExpandIconConverter : IValueConverter
{
    /// <summary>
    /// Converts expanded state to icon text.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "▲" : "▼";
        }
        return "▼";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a percentage (0-100) to a progress value (0-1).
/// </summary>
public class PercentToProgressConverter : IValueConverter
{
    /// <summary>
    /// Converts percentage to progress value.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            return Math.Clamp(percent / 100.0, 0.0, 1.0);
        }
        return 0.0;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean (muted state) to a mute/unmute icon.
/// </summary>
public class BoolToMuteIconConverter : IValueConverter
{
    /// <summary>
    /// Converts muted state to icon text.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isMuted)
        {
            return isMuted ? "🔇" : "🔊";
        }
        return "🔊";
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
