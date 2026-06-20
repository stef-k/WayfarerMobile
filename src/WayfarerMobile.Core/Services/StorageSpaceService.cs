using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Core.Services;

/// <summary>
/// Applies storage thresholds to platform-provided filesystem information.
/// </summary>
public sealed class StorageSpaceService : IStorageSpaceService
{
    private readonly IStorageSpaceProvider _provider;
    private readonly ILogger<StorageSpaceService> _logger;

    public StorageSpaceService(
        IStorageSpaceProvider provider,
        ILogger<StorageSpaceService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool HasSufficientStorage(string path, long requiredBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredBytes);

        try
        {
            if (!_provider.TryGetAvailableBytes(path, out var availableBytes))
            {
                _logger.LogWarning(
                    "Could not determine available storage for {StoragePath}; allowing operation",
                    path);
                return true;
            }

            if (availableBytes < requiredBytes)
            {
                _logger.LogInformation(
                    "Insufficient storage for {StoragePath}: {AvailableBytes} bytes available, {RequiredBytes} bytes required",
                    path,
                    availableBytes,
                    requiredBytes);
                return false;
            }

            _logger.LogDebug(
                "Available storage for {StoragePath}: {AvailableBytes} bytes available, {RequiredBytes} bytes required",
                path,
                availableBytes,
                requiredBytes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to determine available storage for {StoragePath}; allowing operation",
                path);
            return true;
        }
    }
}
