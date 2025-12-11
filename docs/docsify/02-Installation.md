# Installation

This guide covers platform requirements and how to install Wayfarer Mobile on your device.

---

## Platform Requirements

### Android

| Requirement | Minimum |
|-------------|---------|
| Android Version | 7.0 (Nougat, API 24) |
| RAM | 2 GB |
| Storage | 100 MB + space for offline maps |
| Play Services | Recommended (for best GPS accuracy) |

**Recommended devices:**
- Any modern Android phone from 2017 or later
- Devices with Google Play Services for enhanced location accuracy

### iOS

| Requirement | Minimum |
|-------------|---------|
| iOS Version | 15.0 |
| Device | iPhone 6s or later |
| Storage | 100 MB + space for offline maps |

---

## Installation Methods

Currently distributed as APK. Download from the project's GitHub releases page.

### Android APK Installation

1. Download the APK file from the project's GitHub releases page
2. You may need to enable "Install from unknown sources":
   - Go to **Settings** > **Security**
   - Enable **Unknown sources** or **Install unknown apps**
3. Open the downloaded APK file
4. Tap **Install**
5. Wait for installation to complete

> **Security Note**: Only install APK files from the official GitHub releases.

### iOS Installation (TestFlight)

For TestFlight:
1. Install TestFlight from the App Store
2. Accept the invitation link
3. Install the app through TestFlight

---

## Permissions Overview

During installation and first launch, the app will request several permissions:

| Permission | Purpose | Required? |
|------------|---------|-----------|
| Location (Foreground) | Show your position on the map | Yes |
| Location (Background) | 24/7 timeline tracking | For timeline |
| Notifications | Show tracking status | Recommended |
| Camera | Scan QR codes for setup | For QR setup |

---

## Storage Requirements

The app uses storage for:

| Item | Typical Size |
|------|--------------|
| App installation | 50-80 MB |
| Live tile cache | Up to 500 MB (configurable) |
| Downloaded trips | 50-500 MB per trip |
| Location database | 1-10 MB |
| Logs | Up to 70 MB |

**Total recommended**: 500 MB - 2 GB free space, depending on offline map usage.

### Managing Storage

In **Settings** > **Cache**, you can:
- View current cache sizes
- Clear live tile cache
- Delete individual trip downloads
- Set maximum cache limits

---

## Updating the App

### Updating (APK)

1. Download the new version from the GitHub releases page
2. Install over the existing app
3. Your data and settings are preserved

### Updating (TestFlight)

TestFlight will notify you when updates are available. Install updates through the TestFlight app.

---

## Uninstalling

### Android

1. Long-press the Wayfarer Mobile icon
2. Drag to **Uninstall** or tap the info icon
3. Tap **Uninstall** > **OK**

Or via Settings:
1. Go to **Settings** > **Apps** > **Wayfarer Mobile**
2. Tap **Uninstall**

### iOS

1. Long-press the Wayfarer Mobile icon
2. Tap **Remove App**
3. Tap **Delete App** > **Delete**

> **Note**: Uninstalling removes local data. Your server data remains intact.

---

## Troubleshooting Installation

### "App Not Installed" (Android)

This can happen if:
- Not enough storage space - free up space and retry
- Corrupt download - re-download the APK
- Conflicting signatures - uninstall any previous version first
- Architecture mismatch - ensure you have the correct APK for your device

### "Unable to Install" (iOS)

Common causes:
- iOS version too old - update your device
- Not enough storage - free up space
- Enterprise certificate not trusted - trust it in Settings
- TestFlight build expired - request a new build

### Play Services Issues (Android)

If GPS accuracy is poor:
1. Open Play Store
2. Search for "Google Play Services"
3. Tap **Update** if available
4. Restart your device

---

## Device-Specific Notes

### Samsung Devices

Samsung's aggressive battery management can stop background tracking:
1. Go to **Settings** > **Apps** > **Wayfarer Mobile**
2. Tap **Battery**
3. Select **Unrestricted**
4. Go to **Settings** > **Device care** > **Battery**
5. Tap **App power management**
6. Add Wayfarer Mobile to **Apps that won't be put to sleep**

### Huawei/Honor Devices

Similar to Samsung:
1. Go to **Settings** > **Battery** > **App launch**
2. Find Wayfarer Mobile
3. Toggle off **Manage automatically**
4. Enable all manual options

### Xiaomi/Redmi/POCO Devices

1. Go to **Settings** > **Apps** > **Manage apps** > **Wayfarer Mobile**
2. Enable **Autostart**
3. Set **Battery saver** to **No restrictions**
4. In **Security** app > **Permissions** > **Autostart**, enable the app

### OnePlus Devices

1. Go to **Settings** > **Battery** > **Battery optimization**
2. Select **All apps** from dropdown
3. Find Wayfarer Mobile > **Don't optimize**

---

## Next Steps

After installation:
- [Complete the initial setup](01-Getting-Started.md)
- [Learn about app features](03-Features.md)
