using System.Text.RegularExpressions;
using Syncfusion.Maui.Toolkit.BottomSheet;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Bottom sheet control for displaying and editing timeline entry details.
/// Supports view mode (read-only) and edit mode with timestamp, coordinates, and notes editing.
/// </summary>
public partial class TimelineEntrySheet : ContentView
{
    #region Bindable Properties

    /// <summary>
    /// Bindable property for the timeline item being displayed.
    /// </summary>
    public static readonly BindableProperty EntryProperty =
        BindableProperty.Create(nameof(Entry), typeof(TimelineItem), typeof(TimelineEntrySheet), null,
            propertyChanged: OnEntryChanged);

    /// <summary>
    /// Bindable property for whether the sheet is open.
    /// </summary>
    public static readonly BindableProperty IsOpenProperty =
        BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(TimelineEntrySheet), false,
            BindingMode.TwoWay);

    /// <summary>
    /// Bindable property for the sheet state.
    /// </summary>
    public static readonly BindableProperty SheetStateProperty =
        BindableProperty.Create(nameof(SheetState), typeof(BottomSheetState), typeof(TimelineEntrySheet),
            BottomSheetState.Hidden, BindingMode.TwoWay);

    /// <summary>
    /// Bindable property for edit mode state.
    /// </summary>
    public static readonly BindableProperty IsEditingProperty =
        BindableProperty.Create(nameof(IsEditing), typeof(bool), typeof(TimelineEntrySheet), false);

    /// <summary>
    /// Bindable property for saving state.
    /// </summary>
    public static readonly BindableProperty IsSavingProperty =
        BindableProperty.Create(nameof(IsSaving), typeof(bool), typeof(TimelineEntrySheet), false);

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the timeline item being displayed.
    /// </summary>
    public TimelineItem? Entry
    {
        get => (TimelineItem?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the sheet is open.
    /// </summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the sheet state.
    /// </summary>
    public BottomSheetState SheetState
    {
        get => (BottomSheetState)GetValue(SheetStateProperty);
        set => SetValue(SheetStateProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the sheet is in edit mode.
    /// </summary>
    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the sheet is currently saving.
    /// </summary>
    public bool IsSaving
    {
        get => (bool)GetValue(IsSavingProperty);
        set => SetValue(IsSavingProperty, value);
    }

    /// <summary>
    /// Gets the time text.
    /// </summary>
    public string TimeText => Entry?.TimeText ?? string.Empty;

    /// <summary>
    /// Gets the date text.
    /// </summary>
    public string DateText => Entry?.Location.Timestamp.ToLocalTime().ToString("dddd, MMMM d, yyyy") ?? string.Empty;

    /// <summary>
    /// Gets the coordinates text.
    /// </summary>
    public string CoordinatesText => Entry?.CoordinatesText ?? string.Empty;

    /// <summary>
    /// Gets the accuracy text.
    /// </summary>
    public string AccuracyText => Entry?.AccuracyText ?? "Unknown";

    /// <summary>
    /// Gets the accuracy color.
    /// </summary>
    public Color AccuracyColor => Entry?.AccuracyColor ?? Colors.Gray;

    /// <summary>
    /// Gets the speed text.
    /// </summary>
    public string SpeedText => Entry?.SpeedText ?? "N/A";

    /// <summary>
    /// Gets whether altitude is available.
    /// </summary>
    public bool HasAltitude => Entry?.Location.Altitude.HasValue ?? false;

    /// <summary>
    /// Gets the altitude text.
    /// </summary>
    public string AltitudeText => Entry?.Location.Altitude.HasValue == true
        ? $"{Entry.Location.Altitude.Value:F1} m"
        : "N/A";

    /// <summary>
    /// Gets the provider text.
    /// </summary>
    public string ProviderText => Entry?.ProviderText ?? "Unknown";

    /// <summary>
    /// Gets the sync status icon.
    /// </summary>
    public string SyncStatusIcon => Entry?.SyncStatusIcon ?? "?";

    /// <summary>
    /// Gets the sync status text (server data is always synced).
    /// </summary>
    public string SyncStatusText => "Synced";

    // Edit mode backing fields
    private DateTime _editDate;
    private TimeSpan _editTime;
    private string? _editLatitude;
    private string? _editLongitude;
    private string? _editNotes;

    /// <summary>
    /// Gets or sets the date being edited.
    /// </summary>
    public DateTime EditDate
    {
        get => _editDate;
        set
        {
            _editDate = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the time being edited.
    /// </summary>
    public TimeSpan EditTime
    {
        get => _editTime;
        set
        {
            _editTime = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the latitude being edited.
    /// </summary>
    public string? EditLatitude
    {
        get => _editLatitude;
        set
        {
            _editLatitude = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the longitude being edited.
    /// </summary>
    public string? EditLongitude
    {
        get => _editLongitude;
        set
        {
            _editLongitude = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the notes HTML being edited.
    /// </summary>
    public string? EditNotes
    {
        get => _editNotes;
        set
        {
            _editNotes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditNotesPreview));
            OnPropertyChanged(nameof(HasEditNotes));
        }
    }

    /// <summary>
    /// Gets whether there are notes being edited.
    /// </summary>
    public bool HasEditNotes => !string.IsNullOrWhiteSpace(_editNotes);

    /// <summary>
    /// Gets a preview of the notes being edited.
    /// </summary>
    public string EditNotesPreview
    {
        get
        {
            var plain = StripHtml(_editNotes);
            return string.IsNullOrWhiteSpace(plain) ? "Tap 'Edit Rich Text' to add notes..." : plain;
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the sheet is closed.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Event raised when changes are saved.
    /// </summary>
    public event EventHandler<TimelineEntryUpdateEventArgs>? SaveRequested;

    #endregion

    /// <summary>
    /// Creates a new instance of TimelineEntrySheet.
    /// </summary>
    public TimelineEntrySheet()
    {
        InitializeComponent();
    }

    #region Public Methods

    /// <summary>
    /// Shows the sheet with the specified entry.
    /// </summary>
    public void Show(TimelineItem entry)
    {
        Entry = entry;
        IsEditing = false;
        SheetState = BottomSheetState.HalfExpanded;
        IsOpen = true;
    }

    /// <summary>
    /// Hides the sheet.
    /// </summary>
    public void Hide()
    {
        IsEditing = false;
        SheetState = BottomSheetState.Hidden;
        IsOpen = false;
    }

    /// <summary>
    /// Enters edit mode for the current entry.
    /// </summary>
    public void EnterEditMode()
    {
        if (Entry == null) return;

        var localTimestamp = Entry.Location.Timestamp.ToLocalTime();
        EditDate = localTimestamp.Date;
        EditTime = localTimestamp.TimeOfDay;
        EditLatitude = Entry.Location.Latitude.ToString("F6");
        EditLongitude = Entry.Location.Longitude.ToString("F6");
        EditNotes = Entry.Location.Notes;
        IsEditing = true;
        SheetState = BottomSheetState.FullExpanded;
    }

    /// <summary>
    /// Exits edit mode without saving.
    /// </summary>
    public void ExitEditMode()
    {
        IsEditing = false;
        SheetState = BottomSheetState.HalfExpanded;
    }

    #endregion

    #region Event Handlers

    private static void OnEntryChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is TimelineEntrySheet sheet)
        {
            sheet.OnPropertyChanged(nameof(TimeText));
            sheet.OnPropertyChanged(nameof(DateText));
            sheet.OnPropertyChanged(nameof(CoordinatesText));
            sheet.OnPropertyChanged(nameof(AccuracyText));
            sheet.OnPropertyChanged(nameof(AccuracyColor));
            sheet.OnPropertyChanged(nameof(SpeedText));
            sheet.OnPropertyChanged(nameof(HasAltitude));
            sheet.OnPropertyChanged(nameof(AltitudeText));
            sheet.OnPropertyChanged(nameof(ProviderText));
            sheet.OnPropertyChanged(nameof(SyncStatusIcon));
            sheet.OnPropertyChanged(nameof(SyncStatusText));
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        if (IsEditing)
        {
            ExitEditMode();
        }
        else
        {
            Hide();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBottomSheetClosed(object? sender, EventArgs e)
    {
        IsOpen = false;
        IsEditing = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        EnterEditMode();
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        ExitEditMode();
    }

    private async void OnSaveEditClicked(object? sender, EventArgs e)
    {
        if (Entry == null) return;

        // Validate inputs
        if (!double.TryParse(EditLatitude, out var lat) || lat < -90 || lat > 90)
        {
            await ShowAlertAsync("Validation Error", "Please enter a valid latitude (-90 to 90).");
            return;
        }

        if (!double.TryParse(EditLongitude, out var lon) || lon < -180 || lon > 180)
        {
            await ShowAlertAsync("Validation Error", "Please enter a valid longitude (-180 to 180).");
            return;
        }

        IsSaving = true;

        try
        {
            var newTimestamp = EditDate.Add(EditTime);

            var updateArgs = new TimelineEntryUpdateEventArgs
            {
                LocationId = Entry.Location.Id,
                Latitude = lat,
                Longitude = lon,
                LocalTimestamp = newTimestamp,
                Notes = EditNotes
            };

            SaveRequested?.Invoke(this, updateArgs);

            // Update local entry after save event (TimelineLocation stores coords in Coordinates)
            if (Entry.Location.Coordinates != null)
            {
                Entry.Location.Coordinates.Y = updateArgs.Latitude;
                Entry.Location.Coordinates.X = updateArgs.Longitude;
            }
            Entry.Location.Notes = updateArgs.Notes;

            OnPropertyChanged(nameof(CoordinatesText));
            OnPropertyChanged(nameof(TimeText));
            OnPropertyChanged(nameof(DateText));

            ExitEditMode();
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async void OnEditNotesClicked(object? sender, EventArgs e)
    {
        await NotesEditor.ShowEditorAsync(EditNotes);
    }

    private void OnNotesSaved(object? sender, string newNotes)
    {
        EditNotes = newNotes;
    }

    private void OnNotesEditorClosed(object? sender, EventArgs e)
    {
        // Notes editor was closed without saving - no action needed
    }

    private async void OnOpenInMapsClicked(object? sender, EventArgs e)
    {
        if (Entry == null) return;

        var location = Entry.Location;
        var uri = new Uri($"https://www.google.com/maps/search/?api=1&query={location.Latitude},{location.Longitude}");
        await Launcher.OpenAsync(uri);
    }

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        if (Entry == null) return;

        var location = Entry.Location;
        await Clipboard.SetTextAsync($"{location.Latitude}, {location.Longitude}");
    }

    private async void OnMoreActionsClicked(object? sender, EventArgs e)
    {
        if (Entry == null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var action = await page.DisplayActionSheetAsync(
            $"Location at {TimeText}",
            "Cancel",
            null,
            "Wikipedia Search",
            "Open in Google Maps",
            "Open in Apple Maps",
            "Copy Coordinates",
            "Share Location");

        switch (action)
        {
            case "Wikipedia Search":
                await SearchWikipedia();
                break;
            case "Open in Google Maps":
                await OpenInGoogleMaps();
                break;
            case "Open in Apple Maps":
                await OpenInAppleMaps();
                break;
            case "Copy Coordinates":
                await CopyCoordinates();
                break;
            case "Share Location":
                await ShareLocation();
                break;
        }
    }

    #endregion

    #region Private Methods

    private async Task SearchWikipedia()
    {
        if (Entry == null) return;

        try
        {
            var wikipediaService = Application.Current?.Handler?.MauiContext?.Services.GetService<Services.IWikipediaService>();
            if (wikipediaService == null)
            {
                await ShowAlertAsync("Error", "Wikipedia service not available.");
                return;
            }

            var found = await wikipediaService.OpenNearbyArticleAsync(Entry.Location.Latitude, Entry.Location.Longitude);
            if (!found)
            {
                await ShowAlertAsync("No Results", "No Wikipedia article found near this location.");
            }
        }
        catch (Exception ex)
        {
            await ShowAlertAsync("Error", $"Failed to search Wikipedia: {ex.Message}");
        }
    }

    private async Task OpenInGoogleMaps()
    {
        if (Entry == null) return;
        var location = Entry.Location;
        var uri = new Uri($"https://www.google.com/maps/search/?api=1&query={location.Latitude},{location.Longitude}");
        await Launcher.OpenAsync(uri);
    }

    private async Task OpenInAppleMaps()
    {
        if (Entry == null) return;
        var location = Entry.Location;
        var uri = new Uri($"https://maps.apple.com/?q={location.Latitude},{location.Longitude}");
        await Launcher.OpenAsync(uri);
    }

    private async Task CopyCoordinates()
    {
        if (Entry == null) return;
        var location = Entry.Location;
        await Clipboard.SetTextAsync($"{location.Latitude}, {location.Longitude}");
    }

    private async Task ShareLocation()
    {
        if (Entry == null) return;
        var location = Entry.Location;
        await Share.RequestAsync(new ShareTextRequest
        {
            Title = $"Location at {TimeText}",
            Text = $"Location at {TimeText}\n{location.Latitude}, {location.Longitude}\nhttps://www.google.com/maps/search/?api=1&query={location.Latitude},{location.Longitude}"
        });
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page != null)
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    #endregion
}

/// <summary>
/// Event arguments for timeline entry update requests.
/// </summary>
public class TimelineEntryUpdateEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the location ID.
    /// </summary>
    public int LocationId { get; set; }

    /// <summary>
    /// Gets or sets the new latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the new longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the new local timestamp.
    /// </summary>
    public DateTime LocalTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the new notes (HTML).
    /// </summary>
    public string? Notes { get; set; }
}
