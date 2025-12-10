using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;
using ZXing.Net.Maui;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the QR code scanner page.
/// Handles QR code scanning and server configuration.
/// </summary>
public partial class QrScannerViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly ApiClient _apiClient;
    private readonly ILogger<QrScannerViewModel> _logger;

    /// <summary>
    /// Gets or sets the status message shown to the user.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Point camera at QR code";

    /// <summary>
    /// Gets or sets whether the scanner is processing a QR code.
    /// </summary>
    [ObservableProperty]
    private bool _isProcessing;

    /// <summary>
    /// Gets or sets whether scanning was successful.
    /// </summary>
    [ObservableProperty]
    private bool _isSuccess;

    /// <summary>
    /// Gets or sets whether an error occurred.
    /// </summary>
    [ObservableProperty]
    private bool _isError;

    /// <summary>
    /// Gets or sets whether the scanner is active.
    /// </summary>
    [ObservableProperty]
    private bool _isScannerActive = true;

    /// <summary>
    /// Event raised when the scanner should be enabled/disabled.
    /// </summary>
    public event EventHandler<bool>? ScannerStateChanged;

    /// <summary>
    /// Creates a new instance of QrScannerViewModel.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="apiClient">The API client.</param>
    /// <param name="logger">The logger instance.</param>
    public QrScannerViewModel(
        ISettingsService settingsService,
        ApiClient apiClient,
        ILogger<QrScannerViewModel> logger)
    {
        _settingsService = settingsService;
        _apiClient = apiClient;
        _logger = logger;
        Title = "Scan QR Code";
    }

    /// <summary>
    /// Processes a scanned barcode result.
    /// </summary>
    /// <param name="result">The barcode result.</param>
    public async Task ProcessBarcodeAsync(BarcodeResult result)
    {
        if (IsProcessing || result == null || string.IsNullOrEmpty(result.Value))
            return;

        try
        {
            IsProcessing = true;
            IsScannerActive = false;
            ScannerStateChanged?.Invoke(this, false);
            StatusMessage = "Processing QR code...";

            _logger.LogInformation("Processing scanned QR code");

            // Parse the QR code JSON
            var config = ParseQrCode(result.Value);
            if (config == null)
            {
                ShowError("Invalid QR code format. Expected JSON with serverUrl and apiToken.");
                return;
            }

            // Validate URL
            if (!Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var serverUri) ||
                (serverUri.Scheme != "http" && serverUri.Scheme != "https"))
            {
                ShowError("Invalid server URL format.");
                return;
            }

            StatusMessage = "Testing connection...";

            // Test the connection
            var connectionValid = await TestConnectionAsync(config.ServerUrl, config.ApiToken);
            if (!connectionValid)
            {
                ShowError("Could not connect to server. Please check the QR code and try again.");
                return;
            }

            // Save configuration
            _settingsService.ServerUrl = config.ServerUrl;
            _settingsService.ApiToken = config.ApiToken;

            _logger.LogInformation("Server configuration saved successfully");

            IsSuccess = true;
            StatusMessage = "Connected successfully!";

            // Navigate back after a brief delay
            await Task.Delay(1500);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing QR code");
            ShowError("An error occurred while processing the QR code.");
        }
        finally
        {
            if (!IsSuccess)
            {
                IsProcessing = false;
            }
        }
    }

    /// <summary>
    /// Retries scanning after an error.
    /// </summary>
    [RelayCommand]
    private void RetryScanning()
    {
        IsError = false;
        IsProcessing = false;
        IsScannerActive = true;
        StatusMessage = "Point camera at QR code";
        ScannerStateChanged?.Invoke(this, true);
    }

    /// <summary>
    /// Navigates back without saving.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Shows an error message and updates state.
    /// </summary>
    private void ShowError(string message)
    {
        IsError = true;
        StatusMessage = message;
        _logger.LogWarning("QR scan error: {Message}", message);
    }

    /// <summary>
    /// Parses the QR code JSON content.
    /// </summary>
    private ServerConfig? ParseQrCode(string qrContent)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<ServerConfig>(qrContent, options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse QR code JSON");
            return null;
        }
    }

    /// <summary>
    /// Tests the connection to the server.
    /// </summary>
    private async Task<bool> TestConnectionAsync(string serverUrl, string apiToken)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Temporarily configure the API client for testing
            var originalUrl = _settingsService.ServerUrl;
            var originalToken = _settingsService.ApiToken;

            try
            {
                _settingsService.ServerUrl = serverUrl;
                _settingsService.ApiToken = apiToken;

                // Test the connection by calling the health endpoint or similar
                var result = await _apiClient.TestConnectionAsync(cts.Token);
                return result;
            }
            finally
            {
                // Restore original settings if test fails
                if (!await _apiClient.TestConnectionAsync(CancellationToken.None))
                {
                    _settingsService.ServerUrl = originalUrl;
                    _settingsService.ApiToken = originalToken;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Configuration parsed from QR code.
    /// </summary>
    private class ServerConfig
    {
        /// <summary>
        /// Gets or sets the server URL.
        /// </summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the API token.
        /// </summary>
        public string ApiToken { get; set; } = string.Empty;
    }
}
