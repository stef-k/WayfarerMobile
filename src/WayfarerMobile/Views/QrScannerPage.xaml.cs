using WayfarerMobile.ViewModels;
using ZXing.Net.Maui;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for scanning QR codes to configure server settings.
/// </summary>
public partial class QrScannerPage : ContentPage
{
    private readonly QrScannerViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of QrScannerPage.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public QrScannerPage(QrScannerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to barcode detection
        BarcodeReader.BarcodesDetected += OnBarcodesDetected;

        // Subscribe to scanner state changes
        _viewModel.ScannerStateChanged += OnScannerStateChanged;
    }

    /// <summary>
    /// Handles barcode detection events.
    /// </summary>
    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var firstResult = e.Results?.FirstOrDefault();
        if (firstResult != null)
        {
            // Disable detection while processing
            BarcodeReader.IsDetecting = false;

            // Process on main thread
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await _viewModel.ProcessBarcodeAsync(firstResult);
            });
        }
    }

    /// <summary>
    /// Handles scanner state change requests from the view model.
    /// </summary>
    private void OnScannerStateChanged(object? sender, bool isEnabled)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BarcodeReader.IsDetecting = isEnabled;
        });
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        BarcodeReader.IsDetecting = true;
    }

    /// <summary>
    /// Called when the page disappears.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        BarcodeReader.IsDetecting = false;
    }

    /// <summary>
    /// Cleanup when page is unloaded.
    /// </summary>
    ~QrScannerPage()
    {
        _viewModel.ScannerStateChanged -= OnScannerStateChanged;
        BarcodeReader.BarcodesDetected -= OnBarcodesDetected;
    }
}
