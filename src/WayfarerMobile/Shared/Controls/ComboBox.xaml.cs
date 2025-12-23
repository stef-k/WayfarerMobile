using System.Collections;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// A clean ComboBox control with search/filter capability.
/// Shows collapsed state with selected value, expands to show searchable list.
/// </summary>
public partial class ComboBox : ContentView
{
    private bool _isExpanded;
    private bool _isUpdatingFromSelection;
    private CancellationTokenSource? _debounceTokenSource;

    #region Bindable Properties

    /// <summary>
    /// The items source for the dropdown list.
    /// </summary>
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(ComboBox),
        default(IEnumerable),
        propertyChanged: OnItemsSourceChanged);

    /// <summary>
    /// The currently selected item (two-way bindable).
    /// </summary>
    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem),
        typeof(object),
        typeof(ComboBox),
        default(object),
        BindingMode.TwoWay,
        propertyChanged: OnSelectedItemChanged);

    /// <summary>
    /// Property path to display for complex objects.
    /// </summary>
    public static readonly BindableProperty DisplayMemberPathProperty = BindableProperty.Create(
        nameof(DisplayMemberPath),
        typeof(string),
        typeof(ComboBox),
        default(string),
        propertyChanged: OnDisplayMemberPathChanged);

    /// <summary>
    /// Property path for the icon image source (e.g., PNG path).
    /// When set, items will show an icon alongside the text.
    /// </summary>
    public static readonly BindableProperty IconMemberPathProperty = BindableProperty.Create(
        nameof(IconMemberPath),
        typeof(string),
        typeof(ComboBox),
        default(string),
        propertyChanged: OnIconMemberPathChanged);

    /// <summary>
    /// Property path for the value (optional, for SelectedValue binding).
    /// </summary>
    public static readonly BindableProperty ValueMemberPathProperty = BindableProperty.Create(
        nameof(ValueMemberPath),
        typeof(string),
        typeof(ComboBox),
        default(string));

    /// <summary>
    /// The selected value based on ValueMemberPath.
    /// </summary>
    public static readonly BindableProperty SelectedValueProperty = BindableProperty.Create(
        nameof(SelectedValue),
        typeof(object),
        typeof(ComboBox),
        default(object),
        BindingMode.TwoWay,
        propertyChanged: OnSelectedValueChanged);

    /// <summary>
    /// Placeholder text shown when no item is selected.
    /// </summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(ComboBox),
        "Select an item");

    /// <summary>
    /// Default value to select on initialization.
    /// </summary>
    public static readonly BindableProperty DefaultValueProperty = BindableProperty.Create(
        nameof(DefaultValue),
        typeof(object),
        typeof(ComboBox),
        default(object),
        propertyChanged: OnDefaultValueChanged);

    /// <summary>
    /// Number of visible items in the dropdown (affects max height).
    /// </summary>
    public static readonly BindableProperty VisibleItemCountProperty = BindableProperty.Create(
        nameof(VisibleItemCount),
        typeof(int),
        typeof(ComboBox),
        5);

    /// <summary>
    /// The text to display in collapsed state.
    /// </summary>
    public static readonly BindableProperty DisplayTextProperty = BindableProperty.Create(
        nameof(DisplayText),
        typeof(string),
        typeof(ComboBox),
        string.Empty);

    /// <summary>
    /// The text color for display text.
    /// </summary>
    public static readonly BindableProperty DisplayTextColorProperty = BindableProperty.Create(
        nameof(DisplayTextColor),
        typeof(Color),
        typeof(ComboBox),
        Colors.Gray);

    /// <summary>
    /// Whether an item is currently selected.
    /// </summary>
    public static readonly BindableProperty HasSelectionProperty = BindableProperty.Create(
        nameof(HasSelection),
        typeof(bool),
        typeof(ComboBox),
        false);

    /// <summary>
    /// The filtered items collection for the dropdown.
    /// </summary>
    public static readonly BindableProperty FilteredItemsProperty = BindableProperty.Create(
        nameof(FilteredItems),
        typeof(ObservableCollection<object>),
        typeof(ComboBox),
        default(ObservableCollection<object>));

    /// <summary>
    /// Maximum height for the dropdown list.
    /// </summary>
    public static readonly BindableProperty ListMaxHeightProperty = BindableProperty.Create(
        nameof(ListMaxHeight),
        typeof(double),
        typeof(ComboBox),
        200.0);

    /// <summary>
    /// The icon source for the selected item (computed from IconMemberPath).
    /// </summary>
    public static readonly BindableProperty SelectedIconSourceProperty = BindableProperty.Create(
        nameof(SelectedIconSource),
        typeof(string),
        typeof(ComboBox),
        default(string));

    /// <summary>
    /// Whether the selected item has an icon to display.
    /// </summary>
    public static readonly BindableProperty HasSelectedIconProperty = BindableProperty.Create(
        nameof(HasSelectedIcon),
        typeof(bool),
        typeof(ComboBox),
        false);

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the items source.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected item.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the display member path.
    /// </summary>
    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon member path for displaying images alongside text.
    /// </summary>
    public string? IconMemberPath
    {
        get => (string?)GetValue(IconMemberPathProperty);
        set => SetValue(IconMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets the value member path.
    /// </summary>
    public string? ValueMemberPath
    {
        get => (string?)GetValue(ValueMemberPathProperty);
        set => SetValue(ValueMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected value.
    /// </summary>
    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text.
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    public object? DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the visible item count.
    /// </summary>
    public int VisibleItemCount
    {
        get => (int)GetValue(VisibleItemCountProperty);
        set => SetValue(VisibleItemCountProperty, value);
    }

    /// <summary>
    /// Gets the display text for collapsed state.
    /// </summary>
    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    /// <summary>
    /// Gets the display text color.
    /// </summary>
    public Color DisplayTextColor
    {
        get => (Color)GetValue(DisplayTextColorProperty);
        private set => SetValue(DisplayTextColorProperty, value);
    }

    /// <summary>
    /// Gets whether an item is selected.
    /// </summary>
    public bool HasSelection
    {
        get => (bool)GetValue(HasSelectionProperty);
        private set => SetValue(HasSelectionProperty, value);
    }

    /// <summary>
    /// Gets the filtered items collection.
    /// </summary>
    public ObservableCollection<object> FilteredItems
    {
        get => (ObservableCollection<object>)GetValue(FilteredItemsProperty);
        private set => SetValue(FilteredItemsProperty, value);
    }

    /// <summary>
    /// Gets the list max height.
    /// </summary>
    public double ListMaxHeight
    {
        get => (double)GetValue(ListMaxHeightProperty);
        private set => SetValue(ListMaxHeightProperty, value);
    }

    /// <summary>
    /// Gets the selected item's icon source.
    /// </summary>
    public string? SelectedIconSource
    {
        get => (string?)GetValue(SelectedIconSourceProperty);
        private set => SetValue(SelectedIconSourceProperty, value);
    }

    /// <summary>
    /// Gets whether the selected item has an icon.
    /// </summary>
    public bool HasSelectedIcon
    {
        get => (bool)GetValue(HasSelectedIconProperty);
        private set => SetValue(HasSelectedIconProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when selection changes.
    /// </summary>
    public event EventHandler<object?>? SelectionChanged;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new ComboBox instance.
    /// </summary>
    public ComboBox()
    {
        InitializeComponent();
        FilteredItems = new ObservableCollection<object>();
        SetupItemTemplate();
        UpdateDisplayState();
        UpdateListMaxHeight();
    }

    #endregion

    #region Property Changed Handlers

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ComboBox comboBox)
        {
            comboBox.UpdateFilteredItems(string.Empty);
            comboBox.ApplyDefaultValue();
        }
    }

    private static void OnSelectedItemChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ComboBox comboBox)
        {
            comboBox.OnSelectedItemChangedInternal(newValue);
        }
    }

    private static void OnSelectedValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ComboBox comboBox && !comboBox._isUpdatingFromSelection)
        {
            comboBox.SelectItemByValue(newValue);
        }
    }

    private static void OnDefaultValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ComboBox comboBox)
        {
            comboBox.ApplyDefaultValue();
        }
    }

    private static void OnDisplayMemberPathChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ComboBox comboBox)
        {
            comboBox.SetupItemTemplate();
        }
    }

    private static void OnIconMemberPathChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ComboBox comboBox)
        {
            comboBox.SetupItemTemplate();
        }
    }

    private void OnSelectedItemChangedInternal(object? newValue)
    {
        if (_isUpdatingFromSelection)
            return;

        _isUpdatingFromSelection = true;
        try
        {
            // Update SelectedValue if ValueMemberPath is set
            if (!string.IsNullOrEmpty(ValueMemberPath) && newValue != null)
            {
                SelectedValue = GetPropertyValue(newValue, ValueMemberPath);
            }
            else if (newValue == null)
            {
                SelectedValue = null;
            }

            UpdateDisplayState();
            SelectionChanged?.Invoke(this, newValue);
        }
        finally
        {
            _isUpdatingFromSelection = false;
        }
    }

    #endregion

    #region Event Handlers

    private void OnCollapsedTapped(object? sender, TappedEventArgs e)
    {
        ToggleDropdown();
    }

    private void OnClearTapped(object? sender, TappedEventArgs e)
    {
        ClearSelection();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Show/hide clear search button
        clearSearchButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

        // Debounce filter operation
        _debounceTokenSource?.Cancel();
        _debounceTokenSource = new CancellationTokenSource();

        var token = _debounceTokenSource.Token;
        Task.Delay(100, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() => UpdateFilteredItems(e.NewTextValue));
            }
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private void OnClearSearchTapped(object? sender, TappedEventArgs e)
    {
        searchEntry.Text = string.Empty;
        UpdateFilteredItems(string.Empty);
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid grid && grid.BindingContext is { } item)
        {
            SelectItem(item);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Programmatically opens the dropdown.
    /// </summary>
    public void Open()
    {
        if (!_isExpanded)
        {
            ToggleDropdown();
        }
    }

    /// <summary>
    /// Programmatically closes the dropdown.
    /// </summary>
    public void Close()
    {
        if (_isExpanded)
        {
            ToggleDropdown();
        }
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _isUpdatingFromSelection = true;
        try
        {
            SelectedItem = null;
            SelectedValue = null;
            UpdateDisplayState();
            SelectionChanged?.Invoke(this, null);
        }
        finally
        {
            _isUpdatingFromSelection = false;
        }

        if (_isExpanded)
        {
            Close();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Sets up the ItemTemplate dynamically to support DisplayMemberPath and IconMemberPath.
    /// </summary>
    private void SetupItemTemplate()
    {
        var displayMemberPath = DisplayMemberPath;
        var iconMemberPath = IconMemberPath;
        var hasIcon = !string.IsNullOrEmpty(iconMemberPath);

        itemsList.ItemTemplate = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                Padding = new Thickness(12, 10),
                BackgroundColor = Colors.Transparent,
                ColumnSpacing = 10
            };

            // Configure columns based on whether we have icons
            if (hasIcon)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(28)));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            // Add tap gesture
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += OnItemTapped;
            grid.GestureRecognizers.Add(tapGesture);

            // Add icon image if IconMemberPath is set
            if (hasIcon)
            {
                var image = new Image
                {
                    WidthRequest = 28,
                    HeightRequest = 28,
                    VerticalOptions = LayoutOptions.Center,
                    Aspect = Aspect.AspectFit
                };
                image.SetBinding(Image.SourceProperty, new Binding(iconMemberPath));
                Grid.SetColumn(image, 0);
                grid.Add(image);
            }

            // Create label with theme-aware colors
            var label = new Label
            {
                FontSize = 14,
                VerticalOptions = LayoutOptions.Center
            };
            label.SetAppThemeColor(Label.TextColorProperty,
                Color.FromArgb("#212121"),  // Gray900 light
                Colors.White);              // White dark

            // Bind to DisplayMemberPath or ToString
            if (!string.IsNullOrEmpty(displayMemberPath))
            {
                label.SetBinding(Label.TextProperty, new Binding(displayMemberPath));
            }
            else
            {
                label.SetBinding(Label.TextProperty, new Binding("."));
            }

            if (hasIcon)
            {
                Grid.SetColumn(label, 1);
            }
            grid.Add(label);

            // Visual states for hover effect
            VisualStateManager.SetVisualStateGroups(grid, new VisualStateGroupList
            {
                new VisualStateGroup
                {
                    Name = "CommonStates",
                    States =
                    {
                        new VisualState
                        {
                            Name = "Normal",
                            Setters = { new Setter { Property = Grid.BackgroundColorProperty, Value = Colors.Transparent } }
                        },
                        new VisualState
                        {
                            Name = "PointerOver",
                            Setters =
                            {
                                new Setter
                                {
                                    Property = Grid.BackgroundColorProperty,
                                    Value = Application.Current?.RequestedTheme == AppTheme.Dark
                                        ? Color.FromArgb("#424242")  // Gray800
                                        : Color.FromArgb("#F5F5F5")  // Gray100
                                }
                            }
                        }
                    }
                }
            });

            return grid;
        });
    }

    private void ToggleDropdown()
    {
        _isExpanded = !_isExpanded;

        // Get Primary color from resources
        var primaryColor = Color.FromArgb("#e45243");
        if (Application.Current?.Resources.TryGetValue("Primary", out var colorValue) == true && colorValue is Color color)
        {
            primaryColor = color;
        }

        // Get default stroke color based on theme
        var defaultStroke = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#9E9E9E")  // Gray500
            : Color.FromArgb("#BDBDBD"); // Gray400

        if (_isExpanded)
        {
            // Expand - use Primary color for focus indication
            UpdateFilteredItems(string.Empty);
            expandedBorder.IsVisible = true;
            dropdownArrow.Text = "▲";
            collapsedBorder.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8, 8, 0, 0) };
            collapsedBorder.Stroke = primaryColor;
            expandedBorder.Stroke = primaryColor;

            // Don't auto-focus the search entry - let user tap it if they want to search
            // This prevents keyboard from appearing when user just wants to browse the list
        }
        else
        {
            // Collapse - restore default stroke
            expandedBorder.IsVisible = false;
            dropdownArrow.Text = "▼";
            collapsedBorder.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) };
            collapsedBorder.Stroke = defaultStroke;
            searchEntry.Text = string.Empty;
            searchEntry.Unfocus();
        }
    }

    private void SelectItem(object item)
    {
        _isUpdatingFromSelection = true;
        try
        {
            SelectedItem = item;

            if (!string.IsNullOrEmpty(ValueMemberPath))
            {
                SelectedValue = GetPropertyValue(item, ValueMemberPath);
            }

            UpdateDisplayState();
        }
        finally
        {
            _isUpdatingFromSelection = false;
        }

        Close();
        SelectionChanged?.Invoke(this, item);
    }

    private void SelectItemByValue(object? value)
    {
        if (value == null || ItemsSource == null)
        {
            SelectedItem = null;
            return;
        }

        foreach (var item in ItemsSource)
        {
            if (item == null) continue;

            var itemValue = !string.IsNullOrEmpty(ValueMemberPath)
                ? GetPropertyValue(item, ValueMemberPath)
                : item;

            if (Equals(itemValue, value))
            {
                _isUpdatingFromSelection = true;
                try
                {
                    SelectedItem = item;
                    UpdateDisplayState();
                }
                finally
                {
                    _isUpdatingFromSelection = false;
                }
                return;
            }
        }
    }

    private void ApplyDefaultValue()
    {
        if (DefaultValue == null || SelectedItem != null || ItemsSource == null)
            return;

        // Try to find matching item
        foreach (var item in ItemsSource)
        {
            if (item == null) continue;

            // Check by value member
            if (!string.IsNullOrEmpty(ValueMemberPath))
            {
                var itemValue = GetPropertyValue(item, ValueMemberPath);
                if (Equals(itemValue, DefaultValue))
                {
                    SelectItem(item);
                    return;
                }
            }

            // Check by display member
            if (!string.IsNullOrEmpty(DisplayMemberPath))
            {
                var displayValue = GetPropertyValue(item, DisplayMemberPath);
                if (Equals(displayValue, DefaultValue))
                {
                    SelectItem(item);
                    return;
                }
            }

            // Check direct equality
            if (Equals(item, DefaultValue))
            {
                SelectItem(item);
                return;
            }
        }
    }

    private void UpdateDisplayState()
    {
        if (SelectedItem != null)
        {
            DisplayText = GetDisplayText(SelectedItem);
            HasSelection = true;
            // Use theme-appropriate text color
            DisplayTextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Colors.White
                : Color.FromArgb("#1F1F1F");

            // Update selected icon if IconMemberPath is set
            if (!string.IsNullOrEmpty(IconMemberPath))
            {
                SelectedIconSource = GetPropertyValue(SelectedItem, IconMemberPath)?.ToString();
                HasSelectedIcon = !string.IsNullOrEmpty(SelectedIconSource);
            }
            else
            {
                SelectedIconSource = null;
                HasSelectedIcon = false;
            }
        }
        else
        {
            DisplayText = Placeholder;
            HasSelection = false;
            SelectedIconSource = null;
            HasSelectedIcon = false;
            // Placeholder color
            DisplayTextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#9CA3AF")
                : Color.FromArgb("#6B7280");
        }
    }

    private void UpdateFilteredItems(string? searchText)
    {
        FilteredItems.Clear();

        if (ItemsSource == null)
            return;

        var search = searchText?.Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrWhiteSpace(search);

        foreach (var item in ItemsSource)
        {
            if (item == null)
                continue;

            var displayText = GetDisplayText(item);

            // Include if no search or if matches search
            if (!hasSearch || displayText.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
    }

    private void UpdateListMaxHeight()
    {
        // Approximate 44px per item
        ListMaxHeight = VisibleItemCount * 44;
    }

    private string GetDisplayText(object item)
    {
        if (item == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(DisplayMemberPath))
        {
            var value = GetPropertyValue(item, DisplayMemberPath);
            return value?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    private static object? GetPropertyValue(object item, string propertyPath)
    {
        var property = item.GetType().GetProperty(propertyPath);
        return property?.GetValue(item);
    }

    #endregion
}
