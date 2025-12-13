using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Bottom sheet control for displaying detailed cache status and coverage information.
/// Shows per-zoom level coverage and provides overlay toggle functionality.
/// </summary>
public partial class CacheStatusSheet : ContentView
{
    #region Bindable Properties

    /// <summary>
    /// Bindable property for whether the sheet is open.
    /// </summary>
    public static readonly BindableProperty IsOpenProperty =
        BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(CacheStatusSheet), false,
            BindingMode.TwoWay, propertyChanged: OnIsOpenChanged);

    /// <summary>
    /// Bindable property for the close command.
    /// </summary>
    public static readonly BindableProperty CloseCommandProperty =
        BindableProperty.Create(nameof(CloseCommand), typeof(System.Windows.Input.ICommand), typeof(CacheStatusSheet), null);

    /// <summary>
    /// Bindable property for the view model.
    /// </summary>
    public static readonly BindableProperty ViewModelProperty =
        BindableProperty.Create(nameof(ViewModel), typeof(CacheStatusViewModel), typeof(CacheStatusSheet), null,
            propertyChanged: OnViewModelChanged);

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the sheet is open.
    /// </summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when the sheet is closed.
    /// </summary>
    public System.Windows.Input.ICommand? CloseCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the view model.
    /// </summary>
    public CacheStatusViewModel? ViewModel
    {
        get => (CacheStatusViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of CacheStatusSheet.
    /// </summary>
    public CacheStatusSheet()
    {
        InitializeComponent();
        // Start with InputTransparent=true so touches pass through when closed
        InputTransparent = true;
    }

    #endregion

    #region Property Changed Handlers

    private static void OnIsOpenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        System.Diagnostics.Debug.WriteLine($"[CacheStatusSheet] OnIsOpenChanged: {oldValue} -> {newValue}");

        if (bindable is CacheStatusSheet sheet && newValue is bool isOpen)
        {
            // Control visibility directly via code-behind (avoids x:Reference binding issues)
            sheet.SheetContainer.IsVisible = isOpen;
            System.Diagnostics.Debug.WriteLine($"[CacheStatusSheet] SheetContainer.IsVisible = {isOpen}");

            // When closed, make ContentView input transparent so touches pass through
            // When open, capture touches
            sheet.InputTransparent = !isOpen;

            if (isOpen && sheet.ViewModel != null)
            {
                System.Diagnostics.Debug.WriteLine("[CacheStatusSheet] Calling ViewModel.OnAppearingAsync()");
                // Load data when sheet opens
                _ = sheet.ViewModel.OnAppearingAsync();
            }
            else if (isOpen && sheet.ViewModel == null)
            {
                System.Diagnostics.Debug.WriteLine("[CacheStatusSheet] WARNING: ViewModel is null!");
            }
        }
    }

    private static void OnViewModelChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CacheStatusSheet sheet && newValue is CacheStatusViewModel viewModel)
        {
            sheet.BindingContext = viewModel;
        }
    }

    #endregion

    #region Event Handlers

    private void OnCloseClicked(object sender, EventArgs e)
    {
        IsOpen = false;
        CloseCommand?.Execute(null);
    }

    private void OnBackdropTapped(object sender, TappedEventArgs e)
    {
        IsOpen = false;
        CloseCommand?.Execute(null);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates the location for cache status checking.
    /// </summary>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    public void SetLocation(double latitude, double longitude)
    {
        ViewModel?.SetLocation(latitude, longitude);
    }

    /// <summary>
    /// Refreshes the cache status data.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadDataCommand.ExecuteAsync(null);
        }
    }

    #endregion
}
