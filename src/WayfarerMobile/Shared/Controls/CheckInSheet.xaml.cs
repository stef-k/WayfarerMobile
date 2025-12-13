using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Bottom sheet control for manual location check-in.
/// Overlays on the map and allows users to submit their current location.
/// </summary>
public partial class CheckInSheet : ContentView
{
    #region Bindable Properties

    /// <summary>
    /// Bindable property for whether the sheet is open.
    /// </summary>
    public static readonly BindableProperty IsOpenProperty =
        BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(CheckInSheet), false,
            BindingMode.TwoWay, propertyChanged: OnIsOpenChanged);

    /// <summary>
    /// Bindable property for the CheckInViewModel.
    /// </summary>
    public static readonly BindableProperty ViewModelProperty =
        BindableProperty.Create(nameof(ViewModel), typeof(CheckInViewModel), typeof(CheckInSheet), null,
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
    /// Gets or sets the CheckInViewModel.
    /// </summary>
    public CheckInViewModel? ViewModel
    {
        get => (CheckInViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the sheet is closed.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Event raised when a check-in is successfully submitted.
    /// </summary>
    public event EventHandler? CheckInSubmitted;

    #endregion

    /// <summary>
    /// Creates a new instance of CheckInSheet.
    /// </summary>
    public CheckInSheet()
    {
        InitializeComponent();
    }

    #region Public Methods

    /// <summary>
    /// Shows the check-in sheet.
    /// </summary>
    public async Task ShowAsync()
    {
        IsOpen = true;

        // Initialize the ViewModel when showing
        if (ViewModel != null)
        {
            await ViewModel.OnAppearingAsync();
        }
    }

    /// <summary>
    /// Hides the check-in sheet.
    /// </summary>
    public async Task HideAsync()
    {
        IsOpen = false;

        // Cleanup the ViewModel when hiding
        if (ViewModel != null)
        {
            await ViewModel.OnDisappearingAsync();
        }
    }

    /// <summary>
    /// Notifies that a check-in was submitted successfully.
    /// Called from the ViewModel after successful submission.
    /// </summary>
    public void NotifyCheckInSubmitted()
    {
        CheckInSubmitted?.Invoke(this, EventArgs.Empty);
        _ = HideAsync();
    }

    #endregion

    #region Event Handlers

    private static async void OnIsOpenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CheckInSheet sheet && newValue is bool isOpen)
        {
            if (isOpen)
            {
                // Initialize ViewModel when opening
                if (sheet.ViewModel != null)
                {
                    await sheet.ViewModel.OnAppearingAsync();
                }
            }
            else
            {
                // Closing the sheet - cleanup ViewModel
                if (sheet.ViewModel != null)
                {
                    await sheet.ViewModel.OnDisappearingAsync();
                }
            }
        }
    }

    private static void OnViewModelChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CheckInSheet sheet)
        {
            // Unsubscribe from old ViewModel
            if (oldValue is CheckInViewModel oldVm)
            {
                oldVm.CheckInCompleted -= sheet.OnCheckInCompleted;
            }

            // Subscribe to new ViewModel
            if (newValue is CheckInViewModel newVm)
            {
                sheet.BindingContext = newVm;
                newVm.CheckInCompleted += sheet.OnCheckInCompleted;
            }
        }
    }

    private async void OnCheckInCompleted(object? sender, EventArgs e)
    {
        CheckInSubmitted?.Invoke(this, EventArgs.Empty);
        await HideAsync();

        // Reset form for next use
        ViewModel?.ResetForm();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await HideAsync();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
