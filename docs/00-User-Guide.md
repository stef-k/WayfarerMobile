# Wayfarer Mobile User Guide

Welcome to the Wayfarer Mobile User Guide. This documentation will help you get the most out of your location tracking and trip planning experience.

---

## What is Wayfarer Mobile?

Wayfarer Mobile is a companion app for the Wayfarer server that lets you:

- **Track your location** automatically in the background to build a personal timeline
- **Plan and navigate trips** with offline map support
- **Share your location** with groups in real-time
- **Check in** at memorable places with notes

The app runs on Android (7.0+) and iOS (15+) devices.

---

## Documentation Overview

| Guide | Description |
|-------|-------------|
| [Installation](02-Installation.md) | Platform requirements and installation steps |
| [Getting Started](01-Getting-Started.md) | Initial setup, QR scanning, and permissions |
| [Features](03-Features.md) | Complete overview of all app features |
| [Trips and Offline](04-Trips-and-Offline.md) | Trip management and offline map usage |
| [Location Tracking](05-Location-Tracking.md) | Background tracking and privacy controls |
| [Groups and Sharing](06-Groups-and-Sharing.md) | Group membership and live location sharing |
| [Troubleshooting](07-Troubleshooting.md) | Solutions to common issues |
| [FAQ](08-FAQ.md) | Frequently asked questions |

---

## Quick Start

### 1. Install the App

Download Wayfarer Mobile from your device's app store or side-load the APK/IPA from your server.

### 2. Connect to Your Server

You need a Wayfarer server to use this app. Get a QR code from your server administrator or the web app.

1. Open the app
2. Follow the onboarding wizard
3. Scan the QR code when prompted
4. Grant the requested permissions

### 3. Start Using the App

Once connected:

- **Map**: View your current location on the map
- **Timeline**: See your location history
- **Trips**: Download trips for offline navigation
- **Groups**: View shared locations
- **Settings**: Configure tracking and preferences

---

## Key Concepts

### Timeline vs Live Location

Wayfarer Mobile separates two concepts:

| Feature | Purpose | When Active |
|---------|---------|-------------|
| **Timeline Tracking** | Records locations to your server for history | When enabled in settings |
| **Live Location** | Shows your position on the map | Always when app is open |

You can disable timeline tracking while still seeing your live position on the map.

### Offline Support

The app works offline for:

- **Downloaded trips**: Navigate using cached map tiles
- **Location queue**: Locations are saved locally and synced when online
- **Basic navigation**: Direct bearing and distance to destinations

Internet is required for:

- Timeline sync to server
- Group location sharing
- Downloading new trips or tiles

---

## Need Help?

- Check the [Troubleshooting](07-Troubleshooting.md) guide for common issues
- Review the [FAQ](08-FAQ.md) for quick answers
- Contact your server administrator for server-related issues

---

## App Information

| Item | Details |
|------|---------|
| App Name | Wayfarer Mobile |
| Platforms | Android 7.0+, iOS 15+ |
| Map Provider | OpenStreetMap via Mapsui |
| Offline Support | Yes (downloaded trips) |
| Open Source | Yes (MIT License) |

---

*This documentation is for Wayfarer Mobile. For server documentation, refer to your Wayfarer server's admin guide.*
