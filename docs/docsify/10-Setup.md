# Development Setup

This guide covers the setup of your development environment for WayfarerMobile.

## Prerequisites

### .NET SDK

WayfarerMobile requires .NET 10 SDK. Download and install from:
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

Verify installation:
```bash
dotnet --version
# Should output: 10.0.x
```

### IDE Options

#### Option 1: Visual Studio 2022 (Recommended for Windows)

1. Download [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) (Community, Professional, or Enterprise)
2. Run the installer and select the following workloads:
   - **.NET Multi-platform App UI development** (MAUI)
   - **Mobile development with .NET**

3. Under **Individual Components**, ensure these are selected:
   - Android SDK setup (API 24+)
   - Android NDK
   - Java Development Kit
   - .NET MAUI templates

#### Option 2: Visual Studio Code

1. Download [Visual Studio Code](https://code.visualstudio.com/)
2. Install extensions:
   - C# Dev Kit
   - .NET MAUI Extension
   - Polyglot Notebooks (optional, for testing)

3. Install MAUI workload via terminal:
```bash
dotnet workload install maui
```

#### Option 3: JetBrains Rider

1. Download [JetBrains Rider](https://www.jetbrains.com/rider/)
2. Install the .NET MAUI plugin from the plugin marketplace

## Platform SDKs

### Android SDK

**Minimum Requirements:**
- Android SDK Platform API 24 (Android 7.0 Nougat)
- Android SDK Build-Tools
- Google Play Services (for FusedLocationProvider)

**Installation via Visual Studio:**
1. Open Visual Studio Installer
2. Select **Modify** on your Visual Studio installation
3. Navigate to **Individual Components**
4. Select required Android SDK versions

**Manual Installation:**
1. Download [Android Studio](https://developer.android.com/studio) or [Android Command Line Tools](https://developer.android.com/studio#command-tools)
2. Use SDK Manager to install:
   ```
   platforms;android-34
   build-tools;34.0.0
   platform-tools
   extras;google;google_play_services
   ```

### iOS SDK (macOS only)

**Requirements:**
- macOS 12.0 or later
- Xcode 14 or later
- iOS SDK 15+

**Installation:**
1. Install [Xcode](https://apps.apple.com/app/xcode/id497799835) from the App Store
2. Open Xcode and install command line tools when prompted
3. Accept the license agreement:
   ```bash
   sudo xcodebuild -license accept
   ```

## Getting the Source Code

### Clone the Repository

```bash
git clone <repository-url> WayfarerMobile
cd WayfarerMobile
```

### Project Structure

```
WayfarerMobile/
+-- src/
|   +-- WayfarerMobile/          # Main MAUI application
|   +-- WayfarerMobile.Core/     # Platform-agnostic library
+-- tests/
|   +-- WayfarerMobile.Tests/    # Unit tests
+-- docs/
|   +-- docsify/                 # Documentation site
|   +-- reference/               # Technical reference docs
+-- CLAUDE.md                    # Project instructions
```

## Building the Application

### Restore Dependencies

```bash
cd src/WayfarerMobile
dotnet restore
```

### Build for Android

```bash
# Debug build
dotnet build -f net10.0-android

# Release build
dotnet build -f net10.0-android -c Release
```

### Build for iOS (macOS only)

```bash
# Debug build
dotnet build -f net10.0-ios

# Release build
dotnet build -f net10.0-ios -c Release
```

## Running the Application

### Android Emulator

1. Create an Android Virtual Device (AVD) via Android Device Manager
2. Start the emulator
3. Run the application:
```bash
dotnet run -f net10.0-android
```

Or in Visual Studio: Select Android target and press F5.

### Physical Android Device

1. Enable Developer Options on your device
2. Enable USB Debugging
3. Connect device via USB
4. Run:
```bash
dotnet run -f net10.0-android --device <device-id>
```

### iOS Simulator (macOS only)

```bash
dotnet run -f net10.0-ios
```

### Physical iOS Device (macOS only)

Requires an Apple Developer account for device provisioning.

## Configuration

### Server Connection

The app requires a backend server URL and API token. These can be configured via:

1. **QR Code Scanning**: The easiest method - scan a QR code containing server configuration
2. **Manual Entry**: Enter server URL and token in Settings

Configuration format for QR code:
```json
{
  "serverUrl": "https://your-server.com",
  "apiToken": "your-api-token"
}
```

### Android Permissions

The following permissions are declared in `AndroidManifest.xml`:

```xml
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_BACKGROUND_LOCATION" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_LOCATION" />
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
```

### iOS Permissions

The following keys must be in `Info.plist`:

```xml
<key>NSLocationWhenInUseUsageDescription</key>
<string>WayfarerMobile needs location access to show your position on the map.</string>
<key>NSLocationAlwaysAndWhenInUseUsageDescription</key>
<string>WayfarerMobile needs background location for 24/7 tracking.</string>
<key>UIBackgroundModes</key>
<array>
    <string>location</string>
</array>
```

## Development Workflow

### Daily Development

1. Pull latest changes: `git pull origin main`
2. Restore packages: `dotnet restore`
3. Build: `dotnet build`
4. Run tests: `dotnet test`
5. Deploy to device/emulator for testing

### Hot Reload

MAUI supports Hot Reload for rapid development:
- **XAML Hot Reload**: Changes to XAML reflect immediately
- **.NET Hot Reload**: Code changes apply without full restart

Enable in Visual Studio: Tools > Options > Debugging > .NET/C++ Hot Reload

### Debugging

**Visual Studio:**
1. Set breakpoints in your code
2. Select target device/emulator
3. Press F5 to start debugging

**VS Code:**
1. Open launch.json and configure MAUI debugging
2. Set breakpoints
3. Press F5

## Common Issues

### Android Build Errors

**Issue**: `Could not find android.jar`
**Solution**: Ensure Android SDK Platform for target API is installed

**Issue**: `Java heap space`
**Solution**: Increase Java heap size in `gradle.properties`:
```properties
org.gradle.jvmargs=-Xmx4096m
```

### iOS Build Errors

**Issue**: `Provisioning profile not found`
**Solution**: Configure signing identity in project properties or use automatic signing

**Issue**: `Simulator not found`
**Solution**: Open Xcode > Preferences > Components and download simulators

### NuGet Package Errors

**Issue**: Package restore fails
**Solution**: Clear NuGet cache:
```bash
dotnet nuget locals all --clear
dotnet restore
```

## Next Steps

- Review [Architecture](11-Architecture.md) to understand the codebase structure
- Explore [Services](12-Services.md) for service implementation details
- Check [API Integration](13-API.md) for backend communication
- Run [Tests](14-Testing.md) to verify your setup
