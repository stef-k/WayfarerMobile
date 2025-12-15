# Wayfarer Mobile

<p align="center">
  <strong>Your personal location timeline and trip companion</strong>
</p>

<p align="center">
  <a href="https://github.com/stef-k/WayfarerMobile/actions/workflows/ci.yml"><img src="https://github.com/stef-k/WayfarerMobile/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/MAUI-Cross--Platform-512BD4?style=flat-square" alt="MAUI">
  <img src="https://img.shields.io/badge/Platform-Android%20%7C%20iOS-green?style=flat-square" alt="Platforms">
  <img src="https://img.shields.io/badge/License-MIT-blue?style=flat-square" alt="MIT License">
</p>

Wayfarer Mobile is a cross-platform .NET MAUI app for Android and iOS that serves as the mobile companion for the [Wayfarer](https://github.com/stef-k/Wayfarer) platform — a self-hosted trip planning and personal location timeline system. Track your location history, navigate trips offline, and share your location with groups.

## Features

| Feature | Description |
|---------|-------------|
| **Timeline Tracking** | Automatic background location logging with privacy controls |
| **Trip Management** | Browse trips, places, and route segments from your server |
| **Offline Maps** | Download map tiles per trip for offline navigation |
| **Turn-by-Turn Navigation** | Voice-guided navigation with OSRM routing integration |
| **Group Sharing** | Real-time location sharing with family and friends |
| **Manual Check-ins** | Quick location logging with custom notes |
| **QR Setup** | Scan a QR code to instantly configure server connection |
| **PIN Lock** | Protect your location data with app-level security |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- MAUI workload: `dotnet workload install maui`
- Platform SDK:
  - **Android**: Android SDK (API 24+) with emulator or device
  - **iOS**: Xcode 15+ with iOS 15+ SDK (macOS required)

### Build & Run

```bash
# Restore dependencies
dotnet restore

# Build for Android
dotnet build -f net10.0-android

# Build for iOS (macOS only)
dotnet build -f net10.0-ios

# Run on Android emulator/device
dotnet build -t:Run -f net10.0-android
```

### Connect to Server

1. Open the Wayfarer Mobile app
2. Go to **Settings** → **Scan QR Code**
3. Scan the configuration QR from your [Wayfarer server](https://github.com/stef-k/Wayfarer)
4. The app will automatically configure the server URL and authentication token

## Project Structure

```
WayfarerMobile/
├── src/
│   ├── WayfarerMobile/          # Main MAUI application
│   │   ├── Core/                # Models, interfaces, algorithms
│   │   ├── Data/                # Database entities and services
│   │   ├── Services/            # Business logic services
│   │   ├── ViewModels/          # MVVM view models
│   │   ├── Views/               # XAML pages and controls
│   │   └── Platforms/           # Platform-specific code
│   └── WayfarerMobile.Core/     # Shared core library
├── tests/
│   └── WayfarerMobile.Tests/    # Unit tests (xUnit)
└── docs/
    ├── docsify/                 # User and developer documentation
    └── requirements/            # Design specs and architecture docs
```

## Technology Stack

| Category | Technology |
|----------|------------|
| Framework | .NET 10 MAUI |
| Maps | Mapsui 5.0 with OpenStreetMap tiles |
| UI Components | Syncfusion MAUI Toolkit (MIT) |
| MVVM | CommunityToolkit.Mvvm |
| Database | SQLite-net-pcl |
| HTTP Resilience | Polly |
| Logging | Serilog |
| QR Scanning | ZXing.Net.MAUI |

## Documentation

Full documentation is available in the `docs/docsify/` directory:

- **[User Guide](docs/docsify/00-User-Guide.md)** - Getting started, features, troubleshooting
- **[Developer Guide](docs/docsify/09-Developer-Guide.md)** - Architecture, services, API integration

To view the documentation locally with Docsify:

```bash
# Install docsify-cli globally
npm i -g docsify-cli

# Serve the documentation
cd docs/docsify
docsify serve
```

## Related Projects

- **[Wayfarer](https://github.com/stef-k/Wayfarer)** - The backend web application
- **[Live Demo](https://wayfarer.stefk.me/)** - Try the hosted version

## Contributing

Contributions are welcome! This is a spare-time project, so responses may be delayed.

**Before contributing:**
- Open an issue to discuss major changes
- Keep pull requests small and focused
- Follow the existing code style (XML comments, strict MVVM)
- Include tests for new functionality

See the [Contributing Guide](docs/docsify/16-Contributing.md) for more details.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  <sub>Map data © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors</sub>
</p>
