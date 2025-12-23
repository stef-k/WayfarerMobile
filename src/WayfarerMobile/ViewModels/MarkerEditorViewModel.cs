using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Represents a selectable marker color option.
/// </summary>
public partial class MarkerColorOption : ObservableObject
{
    /// <summary>
    /// Gets the color name (e.g., "bg-blue").
    /// </summary>
    public string ColorName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display color for the UI.
    /// </summary>
    public Color DisplayColor { get; init; } = Colors.Gray;

    /// <summary>
    /// Gets or sets whether this color is selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Represents a selectable marker icon option.
/// </summary>
public partial class MarkerIconOption : ObservableObject
{
    /// <summary>
    /// Gets the icon name (e.g., "marker").
    /// </summary>
    public string IconName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name for the UI.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the icon image source for rendering.
    /// </summary>
    public string IconImageSource { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this icon is selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// ViewModel for the marker editor page.
/// Allows editing place marker color and icon.
/// </summary>
public partial class MarkerEditorViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ITripSyncService _tripSyncService;
    private readonly TripDownloadService _downloadService;
    private readonly IToastService _toastService;

    private Guid _tripId;
    private Guid _placeId;
    private string _originalColor = IconCatalog.DefaultColor;
    private string _originalIcon = IconCatalog.DefaultIcon;

    /// <summary>
    /// Gets the available marker colors.
    /// Order: blue, purple, black, green, red (per requirements).
    /// </summary>
    public ObservableCollection<MarkerColorOption> Colors { get; } = new();

    /// <summary>
    /// Gets the available marker icons for the selected color.
    /// </summary>
    public ObservableCollection<MarkerIconOption> Icons { get; } = new();

    /// <summary>
    /// Gets or sets the currently selected color.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(PreviewImageSource))]
    private string _selectedColor = IconCatalog.DefaultColor;

    /// <summary>
    /// Gets or sets the currently selected icon.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(PreviewImageSource))]
    private string _selectedIcon = IconCatalog.DefaultIcon;

    /// <summary>
    /// Gets or sets the icon filter text.
    /// </summary>
    [ObservableProperty]
    private string _iconFilter = string.Empty;

    /// <summary>
    /// Gets or sets whether saving is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Gets whether there are unsaved changes.
    /// </summary>
    public bool HasChanges =>
        !string.Equals(SelectedColor, _originalColor, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(SelectedIcon, _originalIcon, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the preview image source for the current selection.
    /// </summary>
    public string PreviewImageSource =>
        IconCatalog.GetIconResourcePath(SelectedIcon, SelectedColor);

    /// <summary>
    /// Creates a new instance of MarkerEditorViewModel.
    /// </summary>
    public MarkerEditorViewModel(
        ITripSyncService tripSyncService,
        TripDownloadService downloadService,
        IToastService toastService)
    {
        _tripSyncService = tripSyncService;
        _downloadService = downloadService;
        _toastService = toastService;
        Title = "Edit Marker";

        InitializeColors();
    }

    /// <summary>
    /// Applies query attributes from navigation.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("tripId", out var tripIdObj) &&
            Guid.TryParse(tripIdObj?.ToString(), out var tripId))
        {
            _tripId = tripId;
        }

        if (query.TryGetValue("placeId", out var placeIdObj) &&
            Guid.TryParse(placeIdObj?.ToString(), out var placeId))
        {
            _placeId = placeId;
        }

        if (query.TryGetValue("currentColor", out var colorObj))
        {
            var color = IconCatalog.CoerceColor(colorObj?.ToString());
            _originalColor = color;
            SelectedColor = color;
        }

        if (query.TryGetValue("currentIcon", out var iconObj))
        {
            var icon = IconCatalog.CoerceIcon(iconObj?.ToString());
            _originalIcon = icon;
            SelectedIcon = icon;
        }

        // Update UI selections
        UpdateColorSelection();
        LoadIcons();
    }

    /// <summary>
    /// Initializes the color options.
    /// </summary>
    private void InitializeColors()
    {
        // Order per requirements: blue, purple, black, green, red
        Colors.Add(new MarkerColorOption
        {
            ColorName = "bg-blue",
            DisplayColor = Color.FromArgb("#2196F3")
        });
        Colors.Add(new MarkerColorOption
        {
            ColorName = "bg-purple",
            DisplayColor = Color.FromArgb("#9C27B0")
        });
        Colors.Add(new MarkerColorOption
        {
            ColorName = "bg-black",
            DisplayColor = Color.FromArgb("#212121")
        });
        Colors.Add(new MarkerColorOption
        {
            ColorName = "bg-green",
            DisplayColor = Color.FromArgb("#4CAF50")
        });
        Colors.Add(new MarkerColorOption
        {
            ColorName = "bg-red",
            DisplayColor = Color.FromArgb("#F44336")
        });
    }

    /// <summary>
    /// Updates the color selection state in the UI.
    /// </summary>
    private void UpdateColorSelection()
    {
        foreach (var color in Colors)
        {
            color.IsSelected = string.Equals(color.ColorName, SelectedColor, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Loads the icon options for the selected color.
    /// </summary>
    private void LoadIcons()
    {
        Icons.Clear();

        // Priority icons per requirements: marker, star, camera, museum, eat, drink, hotel, info, help
        var priorityIcons = new[]
        {
            "marker", "star", "camera", "museum", "eat", "drink", "hotel", "info", "help"
        };

        // Add additional icons from the catalog
        var allIcons = priorityIcons
            .Concat(IconCatalog.PriorityIconNames.Except(priorityIcons))
            .Distinct()
            .ToList();

        foreach (var iconName in allIcons)
        {
            // Apply filter if set
            if (!string.IsNullOrEmpty(IconFilter) &&
                !iconName.Contains(IconFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var displayName = char.ToUpper(iconName[0]) + iconName[1..];
            var option = new MarkerIconOption
            {
                IconName = iconName,
                DisplayName = displayName,
                IconImageSource = IconCatalog.GetIconResourcePath(iconName, SelectedColor),
                IsSelected = string.Equals(iconName, SelectedIcon, StringComparison.OrdinalIgnoreCase)
            };
            Icons.Add(option);
        }
    }

    /// <summary>
    /// Called when IconFilter changes.
    /// </summary>
    partial void OnIconFilterChanged(string value)
    {
        LoadIcons();
    }

    /// <summary>
    /// Called when SelectedColor changes.
    /// </summary>
    partial void OnSelectedColorChanged(string value)
    {
        UpdateColorSelection();
        // Reload icons to update image sources with new color
        LoadIcons();
    }

    /// <summary>
    /// Selects a color.
    /// </summary>
    [RelayCommand]
    private void SelectColor(MarkerColorOption? colorOption)
    {
        if (colorOption == null)
            return;

        SelectedColor = colorOption.ColorName;
    }

    /// <summary>
    /// Selects an icon.
    /// </summary>
    [RelayCommand]
    private void SelectIcon(MarkerIconOption? iconOption)
    {
        if (iconOption == null)
            return;

        // Update selection state
        foreach (var icon in Icons)
        {
            icon.IsSelected = icon == iconOption;
        }

        SelectedIcon = iconOption.IconName;
    }

    /// <summary>
    /// Saves the marker changes.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_tripId == Guid.Empty || _placeId == Guid.Empty)
        {
            await _toastService.ShowErrorAsync("Invalid place reference");
            return;
        }

        if (!HasChanges)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        try
        {
            IsSaving = true;

            // Queue server sync
            await _tripSyncService.UpdatePlaceAsync(
                _placeId,
                _tripId,
                iconName: SelectedIcon,
                markerColor: SelectedColor);

            await _toastService.ShowSuccessAsync("Marker updated");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MarkerEditorViewModel] Save error: {ex.Message}");
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Cancels editing and navigates back.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (HasChanges)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                var discard = await page.DisplayAlertAsync(
                    "Unsaved Changes",
                    "You have unsaved changes. Discard them?",
                    "Discard",
                    "Keep Editing");

                if (!discard)
                {
                    return;
                }
            }
        }

        await Shell.Current.GoToAsync("..");
    }
}
