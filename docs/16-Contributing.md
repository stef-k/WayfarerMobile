# Contributing

Thank you for your interest in contributing to WayfarerMobile! This guide covers the contribution process, code standards, and best practices.

## Project Status

This is a spare-time project that currently meets my needs. I'll improve it when I can, but there's no guaranteed schedule or roadmap.

- **Issues & feature requests**: Please open them - I'll read when I can, but response time varies
- **Pull requests**: Welcomed, but reviews and merges may be delayed or declined if they don't fit the project's goals

To improve your chances:
- Keep PRs small and focused
- Explain the motivation and user impact
- Include tests and documentation updates

This project is MIT-licensed and provided "as is" without warranty.

## Getting Started

1. **Fork the Repository**: Create your own fork of the project
2. **Clone Your Fork**: `git clone <your-fork-url>`
3. **Set Up Development Environment**: Follow [Development Setup](10-Setup.md)
4. **Create a Branch**: `git checkout -b feature/your-feature-name`

## Code Style

### General Guidelines

- **XML Documentation**: All public APIs must have XML documentation comments
- **MVVM Pattern**: Strict separation - ViewModels contain logic, Views contain only UI
- **Single Responsibility**: Each class has one clear purpose
- **Meaningful Names**: Use descriptive, self-documenting names

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `LocationTrackingService` |
| Interfaces | I + PascalCase | `ILocationBridge` |
| Methods | PascalCase | `StartTrackingAsync` |
| Properties | PascalCase | `CurrentLocation` |
| Private fields | _camelCase | `_locationService` |
| Parameters | camelCase | `locationData` |
| Constants | PascalCase | `MaxRetryAttempts` |
| Enums | PascalCase | `TrackingState.Active` |

### XML Documentation

All public classes, methods, and properties require XML documentation:

```csharp
/// <summary>
/// Service for managing the location marker layer on the map.
/// Supports smooth heading calculation, pulsing animation, and navigation state colors.
/// </summary>
public class LocationLayerService : ILocationLayerService, IDisposable
{
    /// <summary>
    /// Updates the current location marker on the map.
    /// </summary>
    /// <param name="location">The current location data.</param>
    /// <param name="centerMap">Whether to center the map on the location.</param>
    public void UpdateLocation(LocationData location, bool centerMap = false)
    {
        // Implementation
    }
}
```

### Async/Await Guidelines

- Async methods end with `Async` suffix
- Use `ConfigureAwait(false)` in library code
- Prefer `ValueTask` for hot paths that often complete synchronously
- Handle cancellation tokens appropriately

```csharp
public async Task<TripDetails?> GetTripAsync(
    string tripId,
    CancellationToken cancellationToken = default)
{
    await SomeOperationAsync().ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    return await FetchTripAsync(tripId, cancellationToken).ConfigureAwait(false);
}
```

### Error Handling

- Use specific exception types
- Log errors with appropriate context
- Don't swallow exceptions silently
- Provide meaningful error messages

```csharp
public async Task ProcessLocationAsync(LocationData location)
{
    ArgumentNullException.ThrowIfNull(location);

    try
    {
        await _databaseService.QueueLocationAsync(location);
        _logger.LogDebug("Location queued: {Lat}, {Lon}", location.Latitude, location.Longitude);
    }
    catch (SQLiteException ex)
    {
        _logger.LogError(ex, "Failed to queue location");
        throw new LocationProcessingException("Unable to save location", ex);
    }
}
```

### MVVM Guidelines

**ViewModel Rules**:
- Use `[ObservableProperty]` for bindable properties
- Use `[RelayCommand]` for command methods
- No direct UI references (no `Page`, `View`, or `Control` types)
- Handle state changes through observable properties

```csharp
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILocationBridge _locationBridge;

    [ObservableProperty]
    private bool _trackingEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private TrackingState _trackingState;

    public string StatusText => _trackingState switch
    {
        TrackingState.Active => "Tracking active",
        TrackingState.Paused => "Tracking paused",
        _ => "Not tracking"
    };

    partial void OnTrackingEnabledChanged(bool value)
    {
        _settings.TimelineTrackingEnabled = value;
    }

    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (TrackingEnabled)
            await _locationBridge.StartAsync();
        else
            await _locationBridge.StopAsync();
    }
}
```

**View Rules**:
- No business logic in code-behind
- Only UI-specific code (animations, gestures, visual state)
- Use data binding for all data display

```csharp
// Acceptable in code-behind
public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    // UI-specific: Map gesture handling
    private void OnMapPinched(object sender, PinchGestureUpdatedEventArgs e)
    {
        _mapControl.Navigator.ZoomTo(_mapControl.Map.Navigator.Viewport.Resolution * e.Scale);
    }
}
```

## Git Workflow

### Branch Naming

| Type | Pattern | Example |
|------|---------|---------|
| Feature | `feature/description` | `feature/offline-maps` |
| Bug Fix | `fix/description` | `fix/sync-crash` |
| Refactor | `refactor/description` | `refactor/location-service` |
| Documentation | `docs/description` | `docs/api-guide` |

### Commit Messages

Write clear, descriptive commit messages:

**Format**:
```
type: short description

Longer description of what changed and why.
Include context that helps reviewers understand the change.
```

**Types**:
| Type | Purpose |
|------|---------|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation changes |
| `refactor` | Code refactoring |
| `test` | Adding or updating tests |
| `chore` | Build, config, tooling changes |
| `perf` | Performance improvements |

**Examples**:
```
feat: add offline map download for trips

Implemented tile caching for trip areas allowing offline navigation.
Uses SQLite MBTiles format for storage.
Includes progress tracking and download cancellation.

fix: resolve crash when GPS unavailable

Handle null location gracefully in LocationLayerService.UpdateLocation.
Show last known location with gray indicator when GPS stale.

refactor: extract navigation graph from TripNavigationService

Created TripNavigationGraph class for A* pathfinding.
Improves testability and separation of concerns.
```

**Important**: Do NOT include author, co-author, or credit information in commit messages.

### Pull Request Process

1. **Update Your Branch**
   ```bash
   git fetch origin
   git rebase origin/main
   ```

2. **Run Tests**
   ```bash
   dotnet test
   ```

3. **Check Code Style**
   - Ensure XML documentation is complete
   - Verify no warnings in build output

4. **Create Pull Request**
   - Use a descriptive title
   - Fill out the PR template
   - Reference related issues

5. **Address Review Feedback**
   - Respond to all comments
   - Make requested changes
   - Re-request review when ready

### PR Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Refactoring
- [ ] Documentation

## Testing
- [ ] Unit tests added/updated
- [ ] Tested on Android emulator
- [ ] Tested on iOS simulator

## Checklist
- [ ] Code follows style guidelines
- [ ] XML documentation added
- [ ] No new warnings
- [ ] Tests pass locally
```

## Testing Requirements

### For New Features

- Unit tests for all business logic
- Test both success and failure paths
- Mock external dependencies

### For Bug Fixes

- Add test that reproduces the bug
- Verify test fails before fix
- Verify test passes after fix

### Test Coverage

- Aim for meaningful coverage, not arbitrary percentages
- Focus on critical paths and edge cases
- Don't test trivial code (auto-properties, simple getters)

```csharp
// Good: Test meaningful behavior
[Fact]
public void ThresholdFilter_BelowTimeThreshold_RejectsLocation()
{
    var filter = new ThresholdFilter(timeMinutes: 1, distanceMeters: 50);
    var loc1 = CreateLocation(timestamp: DateTime.UtcNow);
    var loc2 = CreateLocation(timestamp: DateTime.UtcNow.AddSeconds(30));

    filter.ShouldAccept(loc1);
    var result = filter.ShouldAccept(loc2);

    result.Should().BeFalse();
}

// Avoid: Testing trivial property
[Fact]
public void LocationData_Latitude_ReturnsSetValue() // Not useful
{
    var loc = new LocationData { Latitude = 51.5 };
    loc.Latitude.Should().Be(51.5);
}
```

## Documentation Requirements

### Code Documentation

- XML comments on all public APIs
- Inline comments for complex logic
- Update relevant documentation files

### User-Facing Changes

- Update relevant guide sections
- Add troubleshooting entries if needed
- Include screenshots for UI changes

## Review Process

### What Reviewers Look For

1. **Correctness**: Does the code do what it should?
2. **Style**: Does it follow conventions?
3. **Testing**: Are tests adequate?
4. **Documentation**: Is it documented?
5. **Performance**: Any performance concerns?
6. **Security**: Any security implications?

### Responding to Reviews

- Be respectful and open to feedback
- Explain your reasoning when disagreeing
- Ask clarifying questions
- Thank reviewers for their time

## Common Pitfalls

### Avoid These Issues

1. **Business Logic in Views**
   ```csharp
   // BAD - in Page.xaml.cs
   private void OnButtonClicked(object sender, EventArgs e)
   {
       if (_settings.IsConfigured && _connectivity.IsConnected)
           _apiClient.SyncLocations();
   }

   // GOOD - in ViewModel
   [RelayCommand]
   private async Task SyncAsync()
   {
       if (_settings.IsConfigured && _connectivity.IsConnected)
           await _syncService.SyncAsync();
   }
   ```

2. **Missing Null Checks**
   ```csharp
   // BAD
   public void ProcessLocation(LocationData location)
   {
       _database.Save(location); // NullReferenceException if location is null
   }

   // GOOD
   public void ProcessLocation(LocationData location)
   {
       ArgumentNullException.ThrowIfNull(location);
       _database.Save(location);
   }
   ```

3. **Swallowing Exceptions**
   ```csharp
   // BAD
   try { await SyncAsync(); }
   catch { } // Silent failure

   // GOOD
   try { await SyncAsync(); }
   catch (Exception ex)
   {
       _logger.LogError(ex, "Sync failed");
       throw; // Or handle appropriately
   }
   ```

4. **Hardcoded Values**
   ```csharp
   // BAD
   if (distance > 50) // Magic number

   // GOOD
   private const double DistanceThresholdMeters = 50;
   if (distance > DistanceThresholdMeters)
   ```

## Getting Help

- **Questions**: Open a Discussion on the repository
- **Bugs**: Open an Issue with reproduction steps
- **Features**: Open an Issue describing the use case

## Recognition

Contributors are recognized in:
- Release notes mentioning significant contributions
- CONTRIBUTORS file (if maintained)

Thank you for helping improve WayfarerMobile!
