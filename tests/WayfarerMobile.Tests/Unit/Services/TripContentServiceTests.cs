using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class TripContentServiceTests
{
    [Fact]
    public async Task SyncTripMetadataAsync_ReportsOnlyTripDataProgressAndCompletion()
    {
        var serverId = Guid.NewGuid();
        var localTrip = new DownloadedTripEntity
        {
            Id = 42,
            ServerId = serverId,
            Name = "Old trip name",
            Version = 1
        };
        var serverTrip = new TripDetails
        {
            Id = serverId,
            Name = "Updated trip name",
            Version = 2,
            BoundingBox = new BoundingBox { North = 54, South = 50, East = 8, West = 3 }
        };
        var areas = new List<OfflineAreaEntity> { new() };
        var places = new List<OfflinePlaceEntity> { new() };
        var segments = new List<OfflineSegmentEntity> { new() };
        var polygons = new List<OfflinePolygonEntity> { new() };

        var api = new Mock<IApiClient>();
        api.Setup(x => x.GetTripDetailsAsync(serverId, It.IsAny<CancellationToken>())).ReturnsAsync(serverTrip);
        var trips = new Mock<ITripRepository>();
        trips.Setup(x => x.GetDownloadedTripByServerIdAsync(serverId)).ReturnsAsync(localTrip);
        var placeRepository = new Mock<IPlaceRepository>();
        var segmentRepository = new Mock<ISegmentRepository>();
        var areaRepository = new Mock<IAreaRepository>();
        var metadata = new Mock<ITripMetadataBuilder>();
        metadata.Setup(x => x.BuildAreas(serverTrip)).Returns(areas);
        metadata.Setup(x => x.BuildPlaces(serverTrip)).Returns(places);
        metadata.Setup(x => x.BuildSegments(serverTrip)).Returns(segments);
        metadata.Setup(x => x.BuildPolygons(serverTrip)).Returns(polygons);
        var connectivity = new Mock<IConnectivity>();
        connectivity.SetupGet(x => x.NetworkAccess).Returns(NetworkAccess.Internet);
        var progress = new SynchronousProgressRecorder<DownloadProgressEventArgs>();
        var service = new TripContentService(
            api.Object,
            trips.Object,
            placeRepository.Object,
            segmentRepository.Object,
            areaRepository.Object,
            metadata.Object,
            connectivity.Object,
            NullLogger<TripContentService>.Instance);

        var result = await service.SyncTripMetadataAsync(serverId, forceSync: true, progress);

        result.Should().BeSameAs(localTrip);
        progress.Values.Select(value => value.ProgressPercent).Should().Equal(5, 15, 35, 55, 65, 100);
        progress.Values.Select(value => value.TripId).Should().OnlyContain(id => id == localTrip.Id);
        progress.Values.Select(value => value.StatusMessage).Should().SatisfyRespectively(
            status => status.Should().ContainEquivalentOf("update"),
            status => status.Should().ContainEquivalentOf("region"),
            status => status.Should().ContainEquivalentOf("place"),
            status => status.Should().ContainEquivalentOf("segment"),
            status => status.Should().MatchEquivalentOf("*polygon*"),
            status => status.Should().ContainEquivalentOf("complete"));
        progress.Values.Select(value => value.StatusMessage).Should().OnlyContain(status =>
            !new[] { "raster", "tile", "coverage", "prefetch", "pause", "resume", "offline map" }
                .Any(term => status.Contains(term, StringComparison.OrdinalIgnoreCase)));

        localTrip.Should().BeEquivalentTo(new
        {
            Name = serverTrip.Name,
            Version = serverTrip.Version,
            RegionCount = 1,
            PlaceCount = 1,
            SegmentCount = 1,
            AreaCount = 1,
            BoundingBoxNorth = 54d,
            BoundingBoxSouth = 50d,
            BoundingBoxEast = 8d,
            BoundingBoxWest = 3d
        });
        areaRepository.Verify(x => x.SaveOfflineAreasAsync(localTrip.Id, areas), Times.Once);
        placeRepository.Verify(x => x.SaveOfflinePlacesAsync(localTrip.Id, places), Times.Once);
        segmentRepository.Verify(x => x.SaveOfflineSegmentsAsync(localTrip.Id, segments), Times.Once);
        areaRepository.Verify(x => x.SaveOfflinePolygonsAsync(localTrip.Id, polygons), Times.Once);
        trips.Verify(x => x.SaveDownloadedTripAsync(localTrip), Times.Once);
    }

    private sealed class SynchronousProgressRecorder<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
