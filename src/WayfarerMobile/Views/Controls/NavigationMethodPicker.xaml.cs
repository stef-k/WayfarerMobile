using Syncfusion.Maui.Toolkit.Picker;

namespace WayfarerMobile.Views.Controls;

/// <summary>
/// Navigation method options.
/// </summary>
public enum NavigationMethod
{
    Walk,
    Drive,
    Bike,
    ExternalMaps
}

/// <summary>
/// A picker control for selecting navigation method with large fonts.
/// </summary>
public partial class NavigationMethodPicker : ContentView
{
    private TaskCompletionSource<NavigationMethod?>? _tcs;
    private readonly PickerColumn _column;

    private static readonly List<string> NavigationMethods = new()
    {
        "🚶  Walk",
        "🚗  Drive",
        "🚴  Bike",
        "📍  External Maps"
    };

    /// <summary>
    /// Creates a new instance of NavigationMethodPicker.
    /// </summary>
    public NavigationMethodPicker()
    {
        InitializeComponent();

        // Add column with navigation methods
        _column = new PickerColumn
        {
            ItemsSource = NavigationMethods,
            SelectedIndex = 0
        };
        NavPicker.Columns.Add(_column);

        // Apply theme-aware styling
        ApplyTheme();

        // Re-apply theme when app theme changes
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeChanged += (s, e) => ApplyTheme();
        }
    }

    /// <summary>
    /// Applies theme-aware colors to the picker.
    /// </summary>
    private void ApplyTheme()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        // Get theme-specific primary colors from resources
        var primaryColor = GetResourceColor("Primary", Color.FromArgb("#512BD4"));
        var primaryDarkColor = GetResourceColor("PrimaryDark", Color.FromArgb("#ac99ea"));

        // Theme-specific colors
        Color backgroundColor;
        Color headerFooterBackground;
        Color unselectedTextColor;

        if (isDark)
        {
            // Dark theme: PrimaryDark backgrounds
            backgroundColor = Color.FromArgb("#1C1C1E");
            headerFooterBackground = primaryDarkColor;
            unselectedTextColor = Color.FromArgb("#8E8E93");
        }
        else
        {
            // Light theme: Primary backgrounds
            backgroundColor = Colors.White;
            headerFooterBackground = primaryColor;
            unselectedTextColor = Color.FromArgb("#6C6C70");
        }

        // Apply to picker
        NavPicker.BackgroundColor = backgroundColor;

        // Header styling - primary/primaryDark background with white text
        HeaderView.Background = headerFooterBackground;
        HeaderView.TextStyle = new PickerTextStyle
        {
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };

        // Footer styling - same as header
        FooterView.Background = headerFooterBackground;
        FooterView.TextStyle = new PickerTextStyle
        {
            FontSize = 16,
            TextColor = Colors.White
        };

        // Selection view - same background as header, with white text
        NavPicker.SelectionView = new PickerSelectionView
        {
            Background = headerFooterBackground,
            CornerRadius = 8,
            Padding = new Thickness(8, 4)
        };

        // Text styles for picker items
        NavPicker.TextStyle = new PickerTextStyle
        {
            FontSize = 22,
            TextColor = unselectedTextColor
        };

        // Selected text is white (on the colored background)
        NavPicker.SelectedTextStyle = new PickerTextStyle
        {
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };
    }

    /// <summary>
    /// Gets a color from application resources.
    /// </summary>
    private static Color GetResourceColor(string key, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return color;
        }
        return fallback;
    }

    /// <summary>
    /// Shows the picker and returns the selected navigation method.
    /// </summary>
    /// <returns>The selected navigation method, or null if cancelled.</returns>
    public Task<NavigationMethod?> ShowAsync()
    {
        _tcs = new TaskCompletionSource<NavigationMethod?>();
        _column.SelectedIndex = 0; // Default to Walk
        NavPicker.IsOpen = true;
        return _tcs.Task;
    }

    /// <summary>
    /// Handles OK button click.
    /// </summary>
    private void OnOkClicked(object? sender, EventArgs e)
    {
        var selectedIndex = _column.SelectedIndex;
        var method = selectedIndex switch
        {
            0 => NavigationMethod.Walk,
            1 => NavigationMethod.Drive,
            2 => NavigationMethod.Bike,
            3 => NavigationMethod.ExternalMaps,
            _ => NavigationMethod.Walk
        };

        NavPicker.IsOpen = false;
        _tcs?.TrySetResult(method);
    }

    /// <summary>
    /// Handles Cancel button click.
    /// </summary>
    private void OnCancelClicked(object? sender, EventArgs e)
    {
        NavPicker.IsOpen = false;
        _tcs?.TrySetResult(null);
    }
}
