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

### Privacy & Data Handling

Wayfarer Mobile is a privacy-first companion app for self-hosted Wayfarer servers. Location history is stored locally (SQLite) and synced only to your configured Wayfarer server. The app does not use third-party analytics or tracking by default. Background location tracking requires elevated OS permissions; review and configure tracking settings carefully before enabling continuous logging.

## Features

### Core Features

| Feature | Description |
|---------|-------------|
| **Timeline Tracking** | Automatic background location logging with sleep/wake battery optimization |
| **Timeline Export/Import** | Export to CSV or GeoJSON, import with duplicate detection |
| **Trip Management** | Browse trips, places, segments, and polygon zones from your server |
| **Offline Maps** | Download map tiles (zoom 8-17) per trip for offline use |
| **Turn-by-Turn Navigation** | Voice-guided navigation with multi-tier route fallback |
| **Group Sharing** | Real-time location sharing via SSE with colored member markers |
| **Activity Types** | 20 built-in activities with icons, editable per-location, server sync every 6 hours |
| **Manual Check-ins** | Quick location logging with activity type and notes |
| **QR Setup** | Scan a QR code to instantly configure server connection |
| **PIN Lock** | Protect your location data with app-level security |
| **Queue Management** | Monitor sync status, configurable limits, export queue data |

### Key Highlights

- **Offline-First Architecture**: Local SQLite storage with background sync, works without internet
- **Smart Battery Usage**: Three-phase sleep/wake optimization for background tracking (~1-3% per hour)
- **Dual Navigation Modes**: Trip navigation (user segments → cached → OSRM → direct) and ad-hoc navigation (OSRM → direct)
- **Queue Resilience**: Configurable queue limit (default 25,000), fast sync (12s/location), export to CSV/GeoJSON

> **Offline Maps Note**: Offline tile downloads can consume significant storage depending on trip area and zoom levels. The app adapts maximum zoom based on trip size and provides cache limits (default 500 MB live, 2 GB trip tiles). Monitor device storage on older devices.

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

### Platform Permissions

To enable background location tracking:

- **Android**: Requires foreground service and background location permissions
- **iOS**: Requires "Always" location permission and background modes enabled

Refer to the [User Guide](docs/00-User-Guide.md) for platform-specific setup and troubleshooting.

### Connect to Server

1. Open the Wayfarer Mobile app
2. Go to **Settings** → **Scan QR Code**
3. Scan the configuration QR from your [Wayfarer server](https://github.com/stef-k/Wayfarer)
4. The app will automatically configure the server URL and authentication token

> **Security Note**: Treat your server token like a password. If you believe it was exposed, revoke and regenerate it on the Wayfarer server, then re-scan the QR code to re-pair the app.

## Project Structure

```
WayfarerMobile/
├── src/
│   ├── WayfarerMobile/              # Main MAUI application
│   │   ├── Data/                    # Database entities and services
│   │   ├── Services/                # Business logic services
│   │   │   └── TileCache/           # Tile caching services
│   │   ├── ViewModels/              # MVVM view models
│   │   ├── Views/                   # XAML pages and controls
│   │   │   └── Controls/            # Reusable UI controls
│   │   ├── Shared/                  # Converters, behaviors
│   │   ├── Platforms/               # Platform-specific code
│   │   │   ├── Android/Services/    # Foreground location service
│   │   │   └── iOS/Services/        # CLLocationManager integration
│   │   └── Resources/               # Images, fonts, raw assets
│   │
│   └── WayfarerMobile.Core/         # Platform-agnostic library
│       ├── Algorithms/              # Geo calculations, pathfinding
│       ├── Enums/                   # Shared enumerations
│       ├── Helpers/                 # Utility classes
│       ├── Interfaces/              # Service contracts
│       ├── Models/                  # Domain models, DTOs
│       └── Navigation/              # Navigation graph and routing
│
├── tests/
│   └── WayfarerMobile.Tests/        # Unit tests (xUnit)
│
└── docs/                            # User and developer documentation (Docsify)
```

## Technology Stack

| Category | Technology |
|----------|------------|
| Framework | .NET 10 MAUI |
| Maps | Mapsui 5.0 with OpenStreetMap tiles |
| Routing | OSRM (Open Source Routing Machine) |
| UI Components | Syncfusion MAUI Toolkit (MIT) |
| MVVM | CommunityToolkit.Mvvm |
| Database | SQLite-net-pcl |
| HTTP Resilience | Polly |
| Logging | Serilog |
| QR Scanning | ZXing.Net.MAUI |
| Real-time | Server-Sent Events (SSE) |

## Documentation

Full documentation is available in the [`docs/`](docs/) directory and online at **[stef-k.github.io/WayfarerMobile](https://stef-k.github.io/WayfarerMobile/)**.

- **[User Guide](docs/00-User-Guide.md)** - Getting started, features, troubleshooting
- **[Developer Guide](docs/09-Developer-Guide.md)** - Architecture, services, API integration

To view the documentation locally with Docsify:

```bash
# Install docsify-cli globally
npm i -g docsify-cli

# Serve the documentation
cd docs
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

See the [Contributing Guide](docs/16-Contributing.md) for more details.

### Branding

**WayfarerMobile** refers to the official companion app for the Wayfarer project.
Forks and modified redistributions should use a **different name** to avoid confusion or false association.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  <sub>Map data © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors</sub>
</p>
