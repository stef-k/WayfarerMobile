using Mapsui.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

public class TripInitialDisplayTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task FirstEligibleDisplay_PopulatesOrdinarySegmentsBeforeSelection(bool dataFirst, bool straightConnection)
    {
        var (vm, state, builder) = Create();
        var trip = Trip();
        if (straightConnection)
        {
            var end = new TripPlace { Id = Guid.NewGuid(), Latitude = 2, Longitude = 2 };
            trip.Regions[0].Places.Add(end);
            trip.Segments[0].Geometry = null;
            trip.Segments[0].OriginId = trip.AllPlaces[0].Id;
            trip.Segments[0].DestinationId = end.Id;
        }
        var ready = new TaskCompletionSource();
        if (!dataFirst)
        {
            vm.MapDisplay.EnsureMapInitialized();
            ready.SetResult();
        }
        vm.QueueTripForNavigation(trip);
        var load = vm.LoadPendingTripIfReadyAsync(ready.Task, default);
        if (dataFirst)
        {
            Assert.False(load.IsCompleted);
            vm.MapDisplay.EnsureMapInitialized();
            ready.SetResult();
        }
        await load;

        Assert.Same(trip, state.LoadedTrip);
        Assert.True(vm.HasLoadedTrip);
        AssertSegments(vm, trip);
        Assert.Empty(Layer(vm, "SelectedSegmentBadges").GetFeatures());
        Assert.Empty(Layer(vm, "SelectedSegmentChevrons").GetFeatures());
        builder.Verify(b => b.ZoomToPoints(vm.MapDisplay.Map, It.IsAny<List<Mapsui.MPoint>>(), 0.2), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingReadiness_SwitchOrUnloadRejectsOldRequest(bool unload)
    {
        var (vm, state, _) = Create();
        var old = Trip();
        var next = Trip();
        var ready = new TaskCompletionSource();
        vm.QueueTripForNavigation(old);
        var oldLoad = vm.LoadPendingTripIfReadyAsync(ready.Task, default);
        if (unload) vm.UnloadTrip();
        else vm.QueueTripForNavigation(next);
        vm.MapDisplay.EnsureMapInitialized();
        ready.SetResult();
        await oldLoad;
        await vm.LoadPendingTripIfReadyAsync(ready.Task, default);

        Assert.Same(unload ? null : next, state.LoadedTrip);
        Assert.Equal(!unload, vm.HasLoadedTrip);
        AssertSegments(vm, unload ? null : next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedPlaceIcon_SwitchOrUnloadRejectsOldPublication(bool unload)
    {
        var (vm, state, builder) = Create();
        vm.MapDisplay.EnsureMapInitialized();
        var old = Trip();
        var next = Trip();
        var icon = new TaskCompletionSource<Stream>();
        FileSystem.Current.Open.Value = _ => icon.Task;
        try
        {
            var oldLoad = vm.LoadTripForNavigationAsync(old);
            Assert.False(oldLoad.IsCompleted);
            FileSystem.Current.Open.Value = null;
            if (unload) vm.UnloadTrip();
            else await vm.LoadTripForNavigationAsync(next);
            builder.Invocations.Clear();

            icon.SetException(new FileNotFoundException());
            await oldLoad;

            Assert.Same(unload ? null : next, state.LoadedTrip);
            Assert.Equal(!unload, vm.HasLoadedTrip);
            AssertSegments(vm, unload ? null : next);
            var placeIds = Layer(vm, "TripPlaces").GetFeatures().Select(f => f["PlaceId"]);
            Assert.Equal(unload ? [] : new object[] { next.AllPlaces[0].Id }, placeIds);
            builder.Verify(b => b.ZoomToPoints(It.IsAny<Mapsui.Map>(), It.IsAny<List<Mapsui.MPoint>>(), 0.2), Times.Never);
            builder.Verify(b => b.CenterOnLocation(It.IsAny<Mapsui.Map>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int?>()), Times.Never);
        }
        finally
        {
            FileSystem.Current.Open.Value = null;
        }
    }

    [Fact]
    public async Task TripSheetMapUnload_RejectsPendingReadiness()
    {
        var (vm, state, _) = Create();
        vm.QueueTripForNavigation(Trip());
        var ready = new TaskCompletionSource();
        var load = vm.LoadPendingTripIfReadyAsync(ready.Task, default);
        vm.UnloadTripFromMap();
        vm.MapDisplay.EnsureMapInitialized();
        ready.SetResult();
        await load;

        Assert.Null(state.LoadedTrip);
        Assert.False(vm.HasLoadedTrip);
        AssertSegments(vm, null);
    }

    [Fact]
    public async Task ReplacementWaitingForReadiness_ImmediatelyRejectsInFlightPublication()
    {
        var (vm, state, builder) = Create();
        vm.MapDisplay.EnsureMapInitialized();
        var icon = new TaskCompletionSource<Stream>();
        FileSystem.Current.Open.Value = _ => icon.Task;
        try
        {
            var old = Trip();
            var oldLoad = vm.LoadTripForNavigationAsync(old);
            var next = Trip();
            vm.QueueTripForNavigation(next);
            builder.Invocations.Clear();
            FileSystem.Current.Open.Value = null;
            icon.SetException(new FileNotFoundException());
            await oldLoad;

            Assert.Same(old, state.LoadedTrip); // Replacement is not admitted yet.
            AssertSegments(vm, null);
            Assert.Empty(Layer(vm, "TripPlaces").GetFeatures());
            Assert.Empty(builder.Invocations);
            await vm.LoadPendingTripIfReadyAsync(Task.CompletedTask, default);
            Assert.Same(next, state.LoadedTrip);
            AssertSegments(vm, next);
        }
        finally
        {
            FileSystem.Current.Open.Value = null;
        }
    }

    [Fact]
    public async Task SegmentVisibility_HidesSelectionAndRestoresOnceWithoutChangingPlacesOrNavigation()
    {
        var (vm, _, _) = Create();
        vm.MapDisplay.EnsureMapInitialized();
        var trip = Trip();
        var segment = trip.Segments[0];
        var end = new TripPlace { Id = Guid.NewGuid(), Latitude = 1, Longitude = 1.04 };
        trip.Regions[0].Places.Add(end);
        segment.OriginId = trip.AllPlaces[0].Id;
        segment.DestinationId = end.Id;
        segment.Geometry = """{"type":"LineString","coordinates":[[1,1],[1.04,1]]}""";
        var center = Mapsui.Projections.SphericalMercator.FromLonLat(1.02, 1);
        vm.MapDisplay.Map.Navigator.SetSize(640, 400);
        vm.MapDisplay.Map.Navigator.CenterOn(new Mapsui.MPoint(center.x, center.y));
        vm.MapDisplay.Map.Navigator.ZoomTo(10);
        await vm.MapDisplay.ShowTripLayersAsync(trip);
        Assert.All(vm.MapDisplay.SegmentRows, row => Assert.True(row.ShowOnMap));
        var row = vm.MapDisplay.SegmentRows[0];
        vm.MapDisplay.UpdateSelectedSegmentDecorations(segment);
        var cueCount = Layer(vm, "SelectedSegmentChevrons").GetFeatures().Count();
        var badgeCount = Layer(vm, "SelectedSegmentBadges").GetFeatures().Count();
        Assert.True(cueCount > 0);
        Assert.True(badgeCount > 0);
        var places = Layer(vm, "TripPlaces").GetFeatures().ToArray();
        Assert.Equal(2, places.Length);
        var guidance = new Mapsui.Nts.GeometryFeature();
        Layer(vm, "NavigationRoute").Add(guidance);

        row.ShowOnMap = false;
        vm.MapDisplay.UpdateSelectedSegmentDecorations(segment);
        vm.MapDisplay.RefreshSelectedSegmentDecorations();
        Assert.Empty(Layer(vm, "TripSegments").GetFeatures());
        Assert.Empty(Layer(vm, "SelectedSegmentChevrons").GetFeatures());
        Assert.Empty(Layer(vm, "SelectedSegmentBadges").GetFeatures());
        row.ShowOnMap = true;
        row.ShowOnMap = true;
        vm.MapDisplay.RefreshSelectedSegmentDecorations();
        Assert.Single(Layer(vm, "TripSegments").GetFeatures());
        Assert.Equal(cueCount, Layer(vm, "SelectedSegmentChevrons").GetFeatures().Count());
        Assert.Equal(badgeCount, Layer(vm, "SelectedSegmentBadges").GetFeatures().Count());
        Assert.Equal(places, Layer(vm, "TripPlaces").GetFeatures());
        Assert.Same(guidance, Assert.Single(Layer(vm, "NavigationRoute").GetFeatures()));
        Assert.True(vm.MapDisplay.HasNavigationRoute);

        var callbacks = new Mock<IMapDisplayCallbacks>();
        callbacks.SetupGet(c => c.IsNavigating).Returns(true);
        vm.MapDisplay.SetCallbacks(callbacks.Object);
        row.ShowOnMap = false;
        row.ShowOnMap = true;
        Assert.Single(Layer(vm, "TripSegments").GetFeatures());
        Assert.Empty(Layer(vm, "SelectedSegmentChevrons").GetFeatures());
        Assert.Empty(Layer(vm, "SelectedSegmentBadges").GetFeatures());
        Assert.Same(guidance, Assert.Single(Layer(vm, "NavigationRoute").GetFeatures()));
        Assert.Equal(places, Layer(vm, "TripPlaces").GetFeatures());
    }

    [Fact]
    public async Task SegmentVisibility_SameTripReplacementRetainsIds_RemovalSwitchAndUnloadReset()
    {
        var (vm, _, _) = Create();
        vm.MapDisplay.EnsureMapInitialized();
        var trip = Trip();
        await vm.MapDisplay.ShowTripLayersAsync(trip);
        var detached = vm.MapDisplay.SegmentRows[0];
        detached.ShowOnMap = false;
        var replacement = Trip();
        replacement.Id = trip.Id;
        replacement.Segments[0].Id = trip.Segments[0].Id;
        replacement.Segments.Reverse();
        await vm.MapDisplay.RefreshTripLayersAsync(replacement);
        Assert.False(vm.MapDisplay.SegmentRows.Single(r => r.Segment.Id == detached.Segment.Id).ShowOnMap);
        Assert.All(vm.MapDisplay.SegmentRows.Where(r => r.Segment.Id != detached.Segment.Id), r => Assert.True(r.ShowOnMap));
        detached.ShowOnMap = true;
        Assert.Empty(Layer(vm, "TripSegments").GetFeatures());
        var removed = replacement.Segments.Single(s => s.Id == detached.Segment.Id);
        replacement.Segments.Remove(removed);
        await vm.MapDisplay.RefreshTripLayersAsync(replacement);
        replacement.Segments.Add(removed);
        await vm.MapDisplay.RefreshTripLayersAsync(replacement);
        Assert.True(vm.MapDisplay.SegmentRows.Single(r => r.Segment.Id == removed.Id).ShowOnMap);
        vm.MapDisplay.SegmentRows.Single(r => r.Segment.Id == removed.Id).ShowOnMap = false;
        vm.MapDisplay.ClearTripLayers();
        Assert.Empty(vm.MapDisplay.SegmentRows);
        await vm.MapDisplay.ShowTripLayersAsync(replacement);
        Assert.All(vm.MapDisplay.SegmentRows, r => Assert.True(r.ShowOnMap));
        vm.MapDisplay.SegmentRows.Single(r => r.Segment.Id == removed.Id).ShowOnMap = false;
        // Use a different Trip object; stable Segment identity alone must not carry choices across Trips.
        await vm.MapDisplay.ShowTripLayersAsync(new TripDetails { Id = Guid.NewGuid(), Segments = replacement.Segments });
        Assert.All(vm.MapDisplay.SegmentRows, r => Assert.True(r.ShowOnMap));
    }

    private static (MainViewModel Vm, TripStateManager State, Mock<IMapBuilder> Builder) Create()
    {
        var builder = new Mock<IMapBuilder>();
        builder.Setup(b => b.CreateLayer(It.IsAny<string>()))
            .Returns((string name) => new WritableLayer { Name = name, Style = null });
        builder.Setup(b => b.CreateMap(It.IsAny<WritableLayer[]>())).Returns((WritableLayer[] layers) =>
        {
            var map = new Mapsui.Map();
            foreach (var layer in layers) map.Layers.Add(layer);
            return map;
        });
        var location = Mock.Of<ILocationBridge>();
        var display = new MapDisplayViewModel(builder.Object, location, Mock.Of<ILocationLayerService>(),
            new TripLayerService(NullLogger<TripLayerService>.Instance), Mock.Of<IDroppedPinLayerService>(),
            new WayfarerMobile.Services.LocationIndicatorService(NullLogger<WayfarerMobile.Services.LocationIndicatorService>.Instance), Mock.Of<IToastService>(),
            NullLogger<MapDisplayViewModel>.Instance);
        var state = new TripStateManager(NullLogger<TripStateManager>.Instance);
        return (new MainViewModel(display, state, Mock.Of<ITripNavigationService>(), location), state, builder);
    }

    private static WritableLayer Layer(MainViewModel vm, string name) =>
        vm.MapDisplay.Map.Layers.OfType<WritableLayer>().Single(layer => layer.Name == name);

    private static void AssertSegments(MainViewModel vm, TripDetails? trip) =>
        Assert.Equal(trip == null ? [] : new object[] { trip.Segments[0].Id },
            Layer(vm, "TripSegments").GetFeatures().Select(feature => feature["SegmentId"]));

    private static TripDetails Trip() => new()
    {
        Id = Guid.NewGuid(),
        Regions = [new TripRegion { Places = [new TripPlace { Id = Guid.NewGuid(), Latitude = 1, Longitude = 1 }] }],
        Segments =
        [
            new TripSegment { Id = Guid.NewGuid(), Geometry = "{\"type\":\"LineString\",\"coordinates\":[[1,1],[2,2]]}" },
            new TripSegment { Id = Guid.NewGuid(), Geometry = "malformed" },
            new TripSegment { Id = Guid.NewGuid(), Geometry = null }
        ]
    };
}
