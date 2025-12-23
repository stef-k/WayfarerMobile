using System.Globalization;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts a boolean (HasFailed) to sync banner background color.
/// Red for failed, blue/primary for pending.
/// </summary>
public class BoolToSyncBannerColorConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to a sync banner color.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool hasFailed && hasFailed)
        {
            return Color.FromArgb("#D32F2F"); // Red for failed
        }
        return Color.FromArgb("#1976D2"); // Blue for pending
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
/// Converts a boolean to a stroke thickness (3 for true/selected, 0 for false).
/// </summary>
public class BoolToStrokeThicknessConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to a stroke thickness.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            return 3.0;
        }
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

/// <summary>
/// Converts a boolean to a selection background color.
/// </summary>
public class BoolToSelectionColorConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean to a selection color.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            // Return a subtle highlight color for selected state
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return isDark
                ? Color.FromArgb("#2C2C2C") // Dark theme selection
                : Color.FromArgb("#E3F2FD"); // Light theme selection (blue tint)
        }
        return Colors.Transparent;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
