using Microsoft.Extensions.Logging;
using Moq;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public class StorageSpaceServiceTests
{
    private const string CachePath = "/app/cache";
    private const long RequiredBytes = 50L * 1024 * 1024;

    private readonly Mock<IStorageSpaceProvider> _provider = new();
    private readonly Mock<ILogger<StorageSpaceService>> _logger = new();

    [Fact]
    public void HasSufficientStorage_WhenAvailableSpaceExceedsRequirement_ReturnsTrue()
    {
        _provider
            .Setup(provider => provider.TryGetAvailableBytes(CachePath, out It.Ref<long>.IsAny))
            .Returns((string _, out long availableBytes) =>
            {
                availableBytes = RequiredBytes + 1;
                return true;
            });

        var service = CreateService();

        service.HasSufficientStorage(CachePath, RequiredBytes).Should().BeTrue();
    }

    [Fact]
    public void HasSufficientStorage_WhenAvailableSpaceIsBelowRequirement_ReturnsFalse()
    {
        _provider
            .Setup(provider => provider.TryGetAvailableBytes(CachePath, out It.Ref<long>.IsAny))
            .Returns((string _, out long availableBytes) =>
            {
                availableBytes = RequiredBytes - 1;
                return true;
            });

        var service = CreateService();

        service.HasSufficientStorage(CachePath, RequiredBytes).Should().BeFalse();
        VerifyLogLevel(LogLevel.Information);
    }

    [Fact]
    public void HasSufficientStorage_WhenAvailableSpaceEqualsRequirement_ReturnsTrue()
    {
        _provider
            .Setup(provider => provider.TryGetAvailableBytes(CachePath, out It.Ref<long>.IsAny))
            .Returns((string _, out long availableBytes) =>
            {
                availableBytes = RequiredBytes;
                return true;
            });

        var service = CreateService();

        service.HasSufficientStorage(CachePath, RequiredBytes).Should().BeTrue();
    }

    [Fact]
    public void HasSufficientStorage_WhenProviderCannotDetermineSpace_FailsOpen()
    {
        _provider
            .Setup(provider => provider.TryGetAvailableBytes(CachePath, out It.Ref<long>.IsAny))
            .Returns((string _, out long availableBytes) =>
            {
                availableBytes = 0;
                return false;
            });

        var service = CreateService();

        service.HasSufficientStorage(CachePath, RequiredBytes).Should().BeTrue();
        VerifyLogLevel(LogLevel.Warning);
    }

    [Fact]
    public void HasSufficientStorage_WhenProviderThrows_FailsOpen()
    {
        _provider
            .Setup(provider => provider.TryGetAvailableBytes(CachePath, out It.Ref<long>.IsAny))
            .Throws<IOException>();

        var service = CreateService();

        service.HasSufficientStorage(CachePath, RequiredBytes).Should().BeTrue();
        VerifyLogLevel(LogLevel.Warning);
    }

    private StorageSpaceService CreateService()
    {
        return new StorageSpaceService(_provider.Object, _logger.Object);
    }

    private void VerifyLogLevel(LogLevel level)
    {
        _logger.Verify(
            logger => logger.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
