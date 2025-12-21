using System.Text.RegularExpressions;
using System.Windows.Input;
using Syncfusion.Maui.Toolkit.BottomSheet;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Bottom sheet control for displaying and editing trip place details.
/// Supports view mode (read-only) and edit mode with rich text notes.
/// </summary>
public partial class PlaceDetailsSheet : ContentView
{
    #region Bindable Properties

    /// <summary>
    /// Bindable property for the place being displayed.
    /// </summary>
    public static readonly BindableProperty PlaceProperty =
        BindableProperty.Create(nameof(Place), typeof(TripPlace), typeof(PlaceDetailsSheet), null,
            propertyChanged: OnPlaceChanged);

    /// <summary>
    /// Bindable property for whether the sheet is open.
    /// </summary>
    public static readonly BindableProperty IsOpenProperty =
        BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(PlaceDetailsSheet), false,
            BindingMode.TwoWay);

    /// <summary>
    /// Bindable property for the sheet state.
    /// </summary>
    public static readonly BindableProperty SheetStateProperty =
        BindableProperty.Create(nameof(SheetState), typeof(BottomSheetState), typeof(PlaceDetailsSheet),
            BottomSheetState.Hidden, BindingMode.TwoWay);

    /// <summary>
    /// Bindable property for the navigate command.
    /// </summary>
    public static readonly BindableProperty NavigateCommandProperty =
        BindableProperty.Create(nameof(NavigateCommand), typeof(ICommand), typeof(PlaceDetailsSheet), null);

    /// <summary>
    /// Bindable property for the close command.
    /// </summary>
    public static readonly BindableProperty CloseCommandProperty =
        BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(PlaceDetailsSheet), null);

    /// <summary>
    /// Bindable property for the save command.
    /// </summary>
    public static readonly BindableProperty SaveCommandProperty =
        BindableProperty.Create(nameof(SaveCommand), typeof(ICommand), typeof(PlaceDetailsSheet), null);

    /// <summary>
    /// Bindable property for edit mode state.
    /// </summary>
    public static readonly BindableProperty IsEditingProperty =
        BindableProperty.Create(nameof(IsEditing), typeof(bool), typeof(PlaceDetailsSheet), false);

    /// <summary>
    /// Bindable property for saving state.
    /// </summary>
    public static readonly BindableProperty IsSavingProperty =
        BindableProperty.Create(nameof(IsSaving), typeof(bool), typeof(PlaceDetailsSheet), false);

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the place being displayed.
    /// </summary>
    public TripPlace? Place
    {
        get => (TripPlace?)GetValue(PlaceProperty);
        set => SetValue(PlaceProperty, value);
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
    /// Gets or sets the command to execute when navigate is tapped.
    /// </summary>
    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when close is tapped.
    /// </summary>
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when saving changes.
    /// </summary>
    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
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
    /// Gets the coordinates as a display string.
    /// </summary>
    public string CoordinatesText =>
        Place != null ? $"{Place.Latitude:F5}, {Place.Longitude:F5}" : string.Empty;

    /// <summary>
    /// Gets whether the place has notes.
    /// </summary>
    public bool HasNotes => !string.IsNullOrWhiteSpace(Place?.Notes);

    /// <summary>
    /// Gets the notes as plain text (strips HTML).
    /// </summary>
    public string PlainNotes => StripHtml(Place?.Notes);

    // Edit mode backing fields
    private string? _editName;
    private string? _editLatitude;
    private string? _editLongitude;
    private string? _editNotes;

    /// <summary>
    /// Gets or sets the name being edited.
    /// </summary>
    public string? EditName
    {
        get => _editName;
        set
        {
            _editName = value;
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
    public event EventHandler<PlaceUpdateEventArgs>? SaveRequested;

    /// <summary>
    /// Event raised when more actions is requested.
    /// </summary>
    public event EventHandler<TripPlace>? MoreActionsRequested;

    #endregion

    /// <summary>
    /// Creates a new instance of PlaceDetailsSheet.
    /// </summary>
    public PlaceDetailsSheet()
    {
        InitializeComponent();

        // Wire up bottom sheet StateChanged event (done in code-behind for XamlC compatibility)
        // SfBottomSheet uses StateChanged, not Closed - we detect closure via Hidden state
        BottomSheet.StateChanged += OnBottomSheetStateChanged;
    }

    #region Public Methods

    /// <summary>
    /// Shows the sheet with the specified place.
    /// </summary>
    public void Show(TripPlace place)
    {
        Place = place;
        IsEditing = false;
        SheetState = BottomSheetState.FullExpanded;
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
    /// Enters edit mode for the current place.
    /// </summary>
    public void EnterEditMode()
    {
        if (Place == null) return;

        EditName = Place.Name;
        EditLatitude = Place.Latitude.ToString("F6");
        EditLongitude = Place.Longitude.ToString("F6");
        EditNotes = Place.Notes;
        IsEditing = true;
        SheetState = BottomSheetState.FullExpanded;
    }

    /// <summary>
    /// Exits edit mode without saving.
    /// </summary>
    public void ExitEditMode()
    {
        IsEditing = false;
        SheetState = BottomSheetState.FullExpanded;
    }

    #endregion

    #region Event Handlers

    private static void OnPlaceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PlaceDetailsSheet sheet)
        {
            sheet.OnPropertyChanged(nameof(CoordinatesText));
            sheet.OnPropertyChanged(nameof(HasNotes));
            sheet.OnPropertyChanged(nameof(PlainNotes));
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
            CloseCommand?.Execute(Place);
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Handles the bottom sheet state changes.
    /// Detects when sheet is closed (Hidden state) to run cleanup logic.
    /// </summary>
    private void OnBottomSheetStateChanged(object? sender, Syncfusion.Maui.Toolkit.BottomSheet.StateChangedEventArgs e)
    {
        // Only handle when sheet becomes hidden (closed)
        if (e.NewState == BottomSheetState.Hidden)
        {
            IsOpen = false;
            IsEditing = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
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
        if (Place == null) return;

        // Validate inputs
        if (string.IsNullOrWhiteSpace(EditName))
        {
            await ShowAlertAsync("Validation Error", "Please enter a name for the place.");
            return;
        }

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
            var updateArgs = new PlaceUpdateEventArgs
            {
                PlaceId = Place.Id,
                Name = EditName!.Trim(),
                Latitude = lat,
                Longitude = lon,
                Notes = EditNotes
            };

            SaveRequested?.Invoke(this, updateArgs);

            if (SaveCommand?.CanExecute(updateArgs) == true)
            {
                SaveCommand.Execute(updateArgs);
            }

            // Update local place after successful save
            Place.Name = updateArgs.Name;
            Place.Latitude = updateArgs.Latitude;
            Place.Longitude = updateArgs.Longitude;
            Place.Notes = updateArgs.Notes;

            OnPropertyChanged(nameof(CoordinatesText));
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(PlainNotes));

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

    private async void OnMoreActionsClicked(object? sender, EventArgs e)
    {
        if (Place == null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var action = await page.DisplayActionSheetAsync(
            Place.Name,
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

        MoreActionsRequested?.Invoke(this, Place);
    }

    #endregion

    #region Private Methods

    private async Task ShowAlertAsync(string title, string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page != null)
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
    }

    private async Task OpenInGoogleMaps()
    {
        if (Place == null) return;
        var uri = new Uri($"https://www.google.com/maps/search/?api=1&query={Place.Latitude},{Place.Longitude}");
        await Launcher.OpenAsync(uri);
    }

    private async Task OpenInAppleMaps()
    {
        if (Place == null) return;
        var uri = new Uri($"https://maps.apple.com/?q={Place.Latitude},{Place.Longitude}");
        await Launcher.OpenAsync(uri);
    }

    private async Task CopyCoordinates()
    {
        if (Place == null) return;
        await Clipboard.SetTextAsync($"{Place.Latitude}, {Place.Longitude}");
    }

    private async Task ShareLocation()
    {
        if (Place == null) return;
        await Share.RequestAsync(new ShareTextRequest
        {
            Title = Place.Name,
            Text = $"{Place.Name}\n{Place.Latitude}, {Place.Longitude}\nhttps://www.google.com/maps/search/?api=1&query={Place.Latitude},{Place.Longitude}"
        });
    }

    private async Task SearchWikipedia()
    {
        if (Place == null) return;

        try
        {
            var wikipediaService = Application.Current?.Handler?.MauiContext?.Services.GetService<Services.IWikipediaService>();
            if (wikipediaService == null)
            {
                await ShowAlertAsync("Error", "Wikipedia service not available.");
                return;
            }

            var found = await wikipediaService.OpenNearbyArticleAsync(Place.Latitude, Place.Longitude);
            if (!found)
            {
                await ShowAlertAsync("No Results", $"No Wikipedia article found near {Place.Name}.");
            }
        }
        catch (Exception ex)
        {
            await ShowAlertAsync("Error", $"Failed to search Wikipedia: {ex.Message}");
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
/// Event arguments for place update requests.
/// </summary>
public class PlaceUpdateEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the place ID.
    /// </summary>
    public Guid PlaceId { get; set; }

    /// <summary>
    /// Gets or sets the new name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the new latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the new longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the new notes (HTML).
    /// </summary>
    public string? Notes { get; set; }
}
