using System.Collections;
using System.Collections.ObjectModel;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// A searchable dropdown control with two-way binding support.
/// Combines SfTextInputLayout with a filtered CollectionView for autocomplete-like functionality.
/// </summary>
public partial class SearchableDropdown : ContentView
{
    private bool _isUpdatingFromSelection;
    private bool _isDropdownOpen;

    #region Bindable Properties

    /// <summary>
    /// Bindable property for the items source.
    /// </summary>
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(SearchableDropdown),
        default(IEnumerable),
        propertyChanged: OnItemsSourceChanged);

    /// <summary>
    /// Bindable property for the selected item.
    /// </summary>
    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem),
        typeof(object),
        typeof(SearchableDropdown),
        default(object),
        BindingMode.TwoWay,
        propertyChanged: OnSelectedItemChanged);

    /// <summary>
    /// Bindable property for the placeholder text.
    /// </summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(SearchableDropdown),
        "Select an item");

    /// <summary>
    /// Bindable property for the display member path (for complex objects).
    /// </summary>
    public static readonly BindableProperty DisplayMemberPathProperty = BindableProperty.Create(
        nameof(DisplayMemberPath),
        typeof(string),
        typeof(SearchableDropdown),
        default(string));

    /// <summary>
    /// Bindable property for minimum characters to trigger search.
    /// </summary>
    public static readonly BindableProperty MinimumSearchLengthProperty = BindableProperty.Create(
        nameof(MinimumSearchLength),
        typeof(int),
        typeof(SearchableDropdown),
        0);

    /// <summary>
    /// Internal property for search text binding.
    /// </summary>
    public static readonly BindableProperty SearchTextProperty = BindableProperty.Create(
        nameof(SearchText),
        typeof(string),
        typeof(SearchableDropdown),
        string.Empty,
        BindingMode.TwoWay);

    /// <summary>
    /// Internal property for filtered items binding.
    /// </summary>
    public static readonly BindableProperty FilteredItemsProperty = BindableProperty.Create(
        nameof(FilteredItems),
        typeof(ObservableCollection<object>),
        typeof(SearchableDropdown),
        default(ObservableCollection<object>));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the items source for the dropdown.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected item. Supports two-way binding.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when no item is selected.
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Gets or sets the property name to display for complex objects.
    /// Leave empty for simple string collections.
    /// </summary>
    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum number of characters before filtering starts.
    /// Default is 0 (show all items immediately).
    /// </summary>
    public int MinimumSearchLength
    {
        get => (int)GetValue(MinimumSearchLengthProperty);
        set => SetValue(MinimumSearchLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the current search text.
    /// </summary>
    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the filtered items collection.
    /// </summary>
    public ObservableCollection<object> FilteredItems
    {
        get => (ObservableCollection<object>)GetValue(FilteredItemsProperty);
        set => SetValue(FilteredItemsProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the selected item changes.
    /// </summary>
    public event EventHandler<object?>? SelectionChanged;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of SearchableDropdown.
    /// </summary>
    public SearchableDropdown()
    {
        InitializeComponent();
        FilteredItems = new ObservableCollection<object>();
    }

    #endregion

    #region Property Changed Handlers

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SearchableDropdown dropdown)
        {
            dropdown.UpdateFilteredItems();
        }
    }

    private static void OnSelectedItemChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SearchableDropdown dropdown)
        {
            dropdown.OnSelectedItemChangedInternal(newValue);
        }
    }

    private void OnSelectedItemChangedInternal(object? newValue)
    {
        if (_isUpdatingFromSelection)
            return;

        // Update the search text to show the selected value
        if (newValue != null)
        {
            SearchText = GetDisplayText(newValue);
        }
        else
        {
            SearchText = string.Empty;
        }

        SelectionChanged?.Invoke(this, newValue);
    }

    #endregion

    #region Event Handlers

    private void OnEntryFocused(object? sender, FocusEventArgs e)
    {
        ShowDropdown();
    }

    private void OnEntryUnfocused(object? sender, FocusEventArgs e)
    {
        // Delay hiding to allow item selection
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
        {
            HideDropdown();

            // If text doesn't match any item, revert to selected item or clear
            if (!IsValidSelection(SearchText))
            {
                if (SelectedItem != null)
                {
                    SearchText = GetDisplayText(SelectedItem);
                }
                else
                {
                    SearchText = string.Empty;
                }
            }
        });
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFromSelection)
            return;

        UpdateFilteredItems();

        if (_isDropdownOpen && FilteredItems.Count == 0 && string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            // Show all items when text is cleared
            UpdateFilteredItems();
        }
    }

    private void OnDropdownButtonClicked(object? sender, EventArgs e)
    {
        if (_isDropdownOpen)
        {
            HideDropdown();
            searchEntry.Unfocus();
        }
        else
        {
            ShowDropdown();
            searchEntry.Focus();
        }
    }

    private void OnSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is { } selectedItem)
        {
            SelectItem(selectedItem);
        }
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid grid && grid.BindingContext is { } item)
        {
            SelectItem(item);
        }
    }

    #endregion

    #region Private Methods

    private void SelectItem(object item)
    {
        _isUpdatingFromSelection = true;
        try
        {
            SearchText = GetDisplayText(item);
            SelectedItem = item;
            HideDropdown();
            searchEntry.Unfocus();
        }
        finally
        {
            _isUpdatingFromSelection = false;
        }

        SelectionChanged?.Invoke(this, item);
    }

    private void ShowDropdown()
    {
        if (_isDropdownOpen)
            return;

        UpdateFilteredItems();
        dropdownBorder.IsVisible = true;
        dropdownButton.Text = "▲";
        _isDropdownOpen = true;
    }

    private void HideDropdown()
    {
        if (!_isDropdownOpen)
            return;

        dropdownBorder.IsVisible = false;
        dropdownButton.Text = "▼";
        _isDropdownOpen = false;
        suggestionsList.SelectedItem = null;
    }

    private void UpdateFilteredItems()
    {
        FilteredItems.Clear();

        if (ItemsSource == null)
            return;

        var searchText = SearchText?.Trim() ?? string.Empty;
        var shouldFilter = searchText.Length >= MinimumSearchLength && !string.IsNullOrWhiteSpace(searchText);

        foreach (var item in ItemsSource)
        {
            if (item == null)
                continue;

            var displayText = GetDisplayText(item);

            if (!shouldFilter || displayText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
    }

    private string GetDisplayText(object item)
    {
        if (item == null)
            return string.Empty;

        if (string.IsNullOrEmpty(DisplayMemberPath))
            return item.ToString() ?? string.Empty;

        var property = item.GetType().GetProperty(DisplayMemberPath);
        if (property != null)
        {
            var value = property.GetValue(item);
            return value?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    private bool IsValidSelection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || ItemsSource == null)
            return false;

        foreach (var item in ItemsSource)
        {
            if (item != null && GetDisplayText(item).Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
