using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels
{
    // Only dependencies outside the linked production Trip-management partial.
    public partial class MainViewModel : BaseViewModel
    {
        private readonly ILogger<MainViewModel> _logger = NullLogger<MainViewModel>.Instance;
        private readonly ITripStateManager _tripStateManager;
        private readonly ITripNavigationService _tripNavigationService;
        private readonly ILocationBridge _locationBridge;
        private readonly bool _isPageVisible = true;
        public MapDisplayViewModel MapDisplay { get; }
        public TripSheetDependency TripSheet { get; } = new();
        public NavigationDependency Navigation { get; } = new();
        public bool HasLoadedTrip { get; set; }
        public string PageTitle => "Trip";
        public LocationData? CurrentLocation => null;

        public MainViewModel(MapDisplayViewModel map, ITripStateManager state,
            ITripNavigationService navigation, ILocationBridge location)
        {
            MapDisplay = map;
            _tripStateManager = state;
            _tripNavigationService = navigation;
            _locationBridge = location;
        }

        public sealed class TripSheetDependency
        {
            public bool HasLoadedTrip => LoadedTrip != null;
            public TripDetails? LoadedTrip { get; set; }
            public TripPlace? SelectedPlace { get; set; }
            public void ClearTripSheetSelection() { }
        }

        public sealed class NavigationDependency
        {
            public bool IsNavigating => false;
            public void StopNavigation() { }
        }
    }
}

