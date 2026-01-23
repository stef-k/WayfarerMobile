# Getting Started

This guide walks you through setting up Wayfarer Mobile for the first time.

---

## Prerequisites

Before you begin, you need:

1. **A Wayfarer server** - The app requires a backend server to sync your locations
2. **Server credentials** - Either a QR code or server URL with API token
3. **Android 7.0+ or iOS 15+** device

> **Note**: Contact your server administrator if you do not have access to a Wayfarer server.

---

## First Launch

When you first open Wayfarer Mobile, you will be guided through a step-by-step onboarding wizard.

### Step 1: Welcome

The welcome screen introduces the app and its features. Tap **Next** to continue.

### Step 2: Location Permission (Foreground)

The app needs location access to show your position on the map.

1. Read the explanation of why location is needed
2. Tap **Grant Permission**
3. Select **"While using the app"** or **"Allow"** in the system dialog

> **Tip**: You can grant permissions later in your device settings if you skip this step.

### Step 3: Background Location (24/7 Tracking)

For timeline tracking to work when the app is closed:

1. Read the explanation about background tracking
2. Tap **Grant Permission**
3. Select **"Allow all the time"** (Android) or **"Always"** (iOS)

> **Important**: Without background location, your timeline will only update when the app is open.

### Step 4: Notifications (Android 13+)

On Android 13 and later, notification permission is required to show tracking status:

1. Read the explanation
2. Tap **Grant Permission**
3. Allow notifications in the system dialog

The notification shows:
- Tracking status (active/paused)
- Last update time
- Quick actions to pause or stop tracking

### Step 5: Battery Optimization

Android aggressively kills background apps to save battery. For reliable tracking:

1. Read the explanation about battery optimization
2. Tap **Request Exemption**
3. Allow Wayfarer Mobile to run unrestricted

> **Warning**: Skipping this step may cause tracking to stop randomly on some devices.

### Step 6: Server Configuration

Connect to your Wayfarer server:

#### Option A: QR Code (Recommended)

1. Tap **Scan QR Code**
2. Point your camera at the QR code from your server
3. The app automatically configures the connection

![QR Code Scanning Placeholder]

#### Option B: Manual Entry

1. Enter your server URL (e.g., `https://wayfarer.example.com`)
2. Tap **Save Server URL**
3. Enter your API token when prompted

> **Tip**: Get your QR code from the Wayfarer web app under Settings > Mobile App.

---

## QR Code Details

The QR code contains your server configuration in a secure format:

| Information | Purpose |
|-------------|---------|
| Server URL | Where to send your location data |
| API Token | Authenticates you with the server (user identity determined server-side) |
| Settings | Default tracking thresholds |

> **Security**: The QR code is encrypted. Keep it private like a password.

---

## After Setup

Once onboarding is complete:

1. **Timeline tracking starts** if you enabled it
2. **Map shows your location** when you open the main screen
3. **Settings are synced** from your server

### Main Navigation

The app has five main sections:

![Main Menu](images/main-menu.jpg)

| Icon | Section | Purpose |
|------|---------|---------|
| Map | Main Page | View map and current location |
| History | Timeline | Browse location history |
| Suitcase | Trips | Manage downloaded trips |
| People | Groups | View group member locations |
| Gear | Settings | Configure app behavior |

---

## Verifying Connection

To confirm the app is properly connected:

1. Go to **Settings**
2. Check the **Account** section
3. You should see your email address
4. **Last sync** shows when data was last sent to the server

If you see "Not connected to server", tap **Scan QR Code** to reconnect.

---

## Granting Permissions Later

If you skipped any permissions during onboarding:

### Android

1. Go to device **Settings** > **Apps** > **Wayfarer Mobile**
2. Tap **Permissions**
3. Enable **Location** and set to **"Allow all the time"**
4. Enable **Notifications**

### iOS

1. Go to device **Settings** > **Wayfarer Mobile**
2. Tap **Location**
3. Select **"Always"**

---

## Troubleshooting Setup

### QR Code Not Scanning

- Ensure good lighting
- Hold phone steady, about 6 inches from code
- Clean your camera lens
- Try regenerating the QR code on your server

### "Server Connection Failed"

- Verify the server URL starts with `https://`
- Check your internet connection
- Confirm the server is running
- Try the QR code again

### Permissions Not Appearing

- Restart the app
- Check if permissions were already denied in device settings
- Update your device to the latest OS version

---

## Next Steps

Now that you are set up:

- [Learn about all features](03-Features.md)
- [Set up trip navigation](04-Trips-and-Offline.md)
- [Understand location tracking](05-Location-Tracking.md)
- [Join a group](06-Groups-and-Sharing.md)
