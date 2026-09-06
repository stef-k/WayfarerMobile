using Mapsui.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

public class TripInitialDisplayTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstEligibleDisplay_PopulatesOrdinarySegmentsBeforeSelection(bool dataFirst)
    {
        var (vm, state, builder) = Create();
        var trip = Trip();
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
            new WayfarerMobile.Services.LocationIndicatorService(), Mock.Of<IToastService>(),
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
