# Developer Guide

Welcome to the WayfarerMobile Developer Guide. This documentation provides comprehensive information for developers working on or contributing to the Wayfarer Mobile application.

## What is WayfarerMobile?

WayfarerMobile is a .NET MAUI cross-platform mobile application for location tracking, trip management, and group location sharing. It targets Android and iOS platforms with a focus on reliable background location tracking, offline capabilities, and turn-by-turn navigation.

## Documentation Structure

| Section | Description |
|---------|-------------|
| [Development Setup](10-Setup.md) | Environment setup, prerequisites, and build instructions |
| [Architecture](11-Architecture.md) | System architecture, project structure, MVVM patterns |
| [Services](12-Services.md) | Core services documentation and responsibilities |
| [API Integration](13-API.md) | Backend API communication, endpoints, authentication |
| [Testing](14-Testing.md) | Test framework, running tests, testing strategies |
| [Security](15-Security.md) | Security implementation, data protection, authentication |
| [Contributing](16-Contributing.md) | Contribution guidelines, code style, PR process |

## Technology Stack

| Category | Technology | Version |
|----------|------------|---------|
| Framework | .NET MAUI | 10.0 |
| Runtime | .NET | 10.0 |
| Maps | Mapsui | 5.0.0 |
| UI Components | Syncfusion MAUI Toolkit | 1.0.8 |
| MVVM | CommunityToolkit.Mvvm | 8.4.0 |
| Database | SQLite-net-pcl | 1.9.172 |
| QR Scanning | ZXing.Net.MAUI | 0.4.0 |
| HTTP | Microsoft.Extensions.Http | 10.0.0 |
| Resilience | Polly | 8.5.2 |
| Logging | Serilog | 4.2.0 |

## Platform Support

| Platform | Minimum Version | Target Version |
|----------|----------------|----------------|
| Android | API 24 (Android 7.0) | Latest |
| iOS | 15.0 | Latest |

> **Note:** Windows and macOS are not supported. The application is designed specifically for mobile devices with GPS capabilities.

## Key Features

- **Background Location Tracking**: 24/7 location tracking using platform-native foreground services
- **Offline Maps**: Download trip areas for offline use with SQLite tile caching
- **Turn-by-Turn Navigation**: OSRM-based routing with audio announcements
- **Group Location Sharing**: Real-time location sharing via Server-Sent Events (SSE)
- **PIN Security**: Optional app lock with salted SHA256 PIN hashing
- **Timeline History**: View and manage location history synchronized with the server

## Quick Links

- **Source Code**: `src/WayfarerMobile/` - Main MAUI project
- **Core Library**: `src/WayfarerMobile.Core/` - Platform-agnostic code
- **Tests**: `tests/WayfarerMobile.Tests/` - Unit tests
- **Documentation**: `docs/` - Design specs and reference documentation

## Getting Started

1. **Setup Development Environment** - Follow [Development Setup](10-Setup.md)
2. **Understand Architecture** - Review [Architecture](11-Architecture.md)
3. **Explore Services** - Study [Services](12-Services.md)
4. **Run Tests** - See [Testing](14-Testing.md)
5. **Contribute** - Follow [Contributing](16-Contributing.md)

## Architecture Overview

```
WayfarerMobile Solution
    |
    +-- WayfarerMobile.Core (Pure C#, no MAUI dependencies)
    |       +-- Models
    |       +-- Interfaces
    |       +-- Algorithms
    |       +-- Enums
    |
    +-- WayfarerMobile (MAUI Application)
    |       +-- Platforms/
    |       |       +-- Android/
    |       |       |       +-- Services/LocationTrackingService.cs
    |       |       |       +-- Services/LocationBridge.cs
    |       |       +-- iOS/
    |       |               +-- Services/LocationTrackingService.cs
    |       |               +-- Services/LocationBridge.cs
    |       +-- Services/
    |       +-- ViewModels/
    |       +-- Views/
    |       +-- Data/
    |       +-- Shared/
    |
    +-- WayfarerMobile.Tests (xUnit Tests)
```

## Design Principles

1. **MVVM Strict**: All business logic in ViewModels, Views contain only UI-specific code
2. **Service Owns GPS**: The foreground service directly owns GPS acquisition and filtering
3. **Single Source of Truth**: LocationBridge pattern for cross-platform location communication
4. **Offline First**: Local SQLite storage with server synchronization
5. **XML Documentation**: All public APIs documented with XML comments

## Support

For questions or issues, please refer to:
- [Troubleshooting Guide](07-Troubleshooting.md)
- [FAQ](08-FAQ.md)
- Project issues on the source repository
