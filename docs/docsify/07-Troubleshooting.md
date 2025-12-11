# Troubleshooting

This guide helps you resolve common issues with Wayfarer Mobile.

---

## Quick Diagnostics

Before diving into specific issues, try these general steps:

### Basic Troubleshooting

1. **Restart the app**: Close completely and reopen
2. **Check internet**: Ensure WiFi or cellular is connected
3. **Check permissions**: Verify all permissions are granted
4. **Update the app**: Install the latest version
5. **Restart device**: Power cycle your phone

### Diagnostic Report

For detailed troubleshooting:

1. Go to **Settings** > **About** > **Diagnostics**
2. Review each section for issues
3. Tap **Share** to export the report
4. Send to your administrator for support

---

## GPS and Location Issues

### GPS Not Working

**Symptoms:**
- Map shows no location
- Blue dot is missing
- "Location unavailable" message

**Solutions:**

1. **Check location permission**
   - Android: Settings > Apps > Wayfarer Mobile > Permissions > Location = "Allow all the time"
   - iOS: Settings > Wayfarer Mobile > Location = "Always"

2. **Enable GPS/Location Services**
   - Android: Settings > Location > Turn on
   - iOS: Settings > Privacy > Location Services > On

3. **Go outside**
   - GPS works poorly indoors
   - Stand outside for 30-60 seconds

4. **Restart location services**
   - Toggle Location off and on in device settings
   - Restart the app

### GPS Accuracy Poor

**Symptoms:**
- Large accuracy circle
- Location jumps around
- Position seems wrong

**Solutions:**

1. **Wait for GPS warmup**: First fix can take 30-60 seconds
2. **Move to open area**: Away from buildings and trees
3. **Check for interference**: Remove phone case if metal
4. **Update Play Services** (Android): Open Play Store, update Google Play Services
5. **Reset network settings**: May help with assisted GPS

### Location Not Updating

**Symptoms:**
- Blue dot is stuck
- Map doesn't follow movement
- Timestamp in notification doesn't change

**Solutions:**

1. **Check battery optimization**: Ensure app can run in background
2. **Verify tracking is enabled**: Settings > Timeline Tracking > Enabled
3. **Check notification**: Is service actually running?
4. **Restart tracking service**: Disable and re-enable tracking

---

## Tracking Issues

### Background Tracking Stops

**Symptoms:**
- Tracking works briefly then stops
- No locations logged when app is closed
- Large gaps in timeline

**Solutions:**

1. **Battery optimization** (Critical for Android)

   | Device | Setting Path |
   |--------|--------------|
   | Samsung | Settings > Apps > Wayfarer > Battery > Unrestricted |
   | Huawei | Settings > Battery > App launch > Manual, enable all |
   | Xiaomi | Settings > Apps > Manage apps > Autostart enabled |
   | OnePlus | Settings > Battery > Battery optimization > Don't optimize |
   | Pixel | Settings > Apps > Wayfarer > Battery > Unrestricted |

2. **Don't force close**: Swipe-closing the app can stop services
3. **Keep memory free**: Close other heavy apps
4. **Check recent apps settings**: Some launchers kill background apps

### Timeline Not Syncing

**Symptoms:**
- Locations logged locally but not on server
- Web timeline doesn't update
- Queue keeps growing

**Solutions:**

1. **Check internet connection**
2. **Verify server URL**: Settings > Account > Server URL
3. **Check authentication**: Try logging out and back in
4. **View queue status**: Diagnostics > Location Queue
5. **Check server status**: Is the server online?

### No Locations Being Logged

**Symptoms:**
- Timeline is empty
- No new entries appearing
- Queue is empty

**Solutions:**

1. **Verify tracking is enabled**: Settings > Enable Timeline Tracking
2. **Check thresholds**: Time/distance thresholds may be too high
3. **Wait for threshold**: Stay in one place won't trigger logging
4. **Move around**: Walking should trigger new locations
5. **Check accuracy filter**: Very poor GPS may be filtered out

---

## Server Connection Issues

### "Not Connected to Server"

**Symptoms:**
- Settings shows no connection
- Can't fetch trips or timeline
- Sync fails

**Solutions:**

1. **Check internet connection**
2. **Verify server URL**
   - Must start with `https://`
   - No trailing slash
   - Domain must be correct

3. **Re-scan QR code**
   - Settings > Account > Scan QR Code
   - Get fresh QR from web app

4. **Check token expiration**
   - Tokens may expire
   - Log out and log back in

5. **Test server in browser**
   - Open server URL in web browser
   - Should load web interface

### "Connection Timed Out"

**Solutions:**

1. Check server is online
2. Check your network allows HTTPS (port 443)
3. Try different network (WiFi vs cellular)
4. Server may be overloaded - try again later

### "Authentication Failed"

**Solutions:**

1. Re-scan QR code for fresh token
2. Log out and log back in
3. Check account is still active on server
4. Check your Wayfarer server dashboard for account status

---

## Map and Display Issues

### Map Not Loading

**Symptoms:**
- Gray/blank tiles
- "Loading" never completes
- Partial map display

**Solutions:**

1. **Check internet**: Online tiles need connection
2. **Wait for download**: First load may be slow
3. **Clear tile cache**: Settings > Cache > Clear Live Cache
4. **Check storage space**: Need space for cached tiles
5. **Try different zoom level**: Some levels may be cached

### Trip Not Showing on Map

**Solutions:**

1. **Verify trip is downloaded**: Look for checkmark
2. **Select the trip**: Tap to make it active
3. **Check trip has places**: Empty trips show nothing
4. **Re-download trip**: Delete and download again
5. **Check trip area**: Zoom to trip's location

### Icons Not Appearing

**Solutions:**

1. Wait for trip data to load
2. Check trip has places with icons defined
3. Re-download the trip
4. Clear app cache and restart

---

## Navigation Issues

### Navigation Won't Start

**Symptoms:**
- "Navigate" button doesn't work
- No navigation overlay appears
- Stuck on "Calculating..."

**Solutions:**

1. **Ensure trip is loaded**: Download and select the trip
2. **Wait for GPS fix**: Need current location first
3. **Check destination**: Place must have valid coordinates
4. **Try different place**: Some places may have issues

### Off-Route Constantly

**Symptoms:**
- Always shows "Off Route"
- Route line doesn't match your path
- Constant rerouting

**Solutions:**

1. **Check you're following the planned route**
2. **50m tolerance**: Must be within 100m of route
3. **Reroute manually**: Tap Reroute button
4. **Use direct navigation**: May not have detailed route

### No Audio Announcements

**Solutions:**

1. **Check volume**: Media volume must be up
2. **Check settings**: Settings > Navigation > Audio Enabled
3. **Check Do Not Disturb**: May block audio
4. **Test TTS**: Try using text-to-speech elsewhere
5. **Restart app**: May need to reinitialize TTS

---

## App Crashes and Performance

### App Crashes on Launch

**Solutions:**

1. **Clear app data**: Settings > Apps > Wayfarer > Clear Data
2. **Reinstall app**: Uninstall and install fresh
3. **Check storage space**: Need adequate free space
4. **Update OS**: May need newer Android/iOS version

### App Running Slowly

**Solutions:**

1. **Close other apps**: Free up memory
2. **Restart device**: Clear system memory
3. **Clear tile cache**: Large cache can slow performance
4. **Reduce trips**: Too many downloaded trips impact performance
5. **Check storage**: Nearly full storage causes slowdowns

### High Battery Drain

**Solutions:**

1. **Check tracking frequency**: High performance mode drains faster
2. **Use normal mode**: Let app manage GPS frequency
3. **Disable when not needed**: Turn off tracking temporarily
4. **Check for wake locks**: Other apps may keep GPS active
5. **View battery usage**: Check what's actually using power

---

## Permission Issues

### Permission Denied

If you denied a permission:

**Android:**
1. Go to Settings > Apps > Wayfarer Mobile
2. Tap Permissions
3. Enable the denied permission

**iOS:**
1. Go to Settings > Wayfarer Mobile
2. Enable the required permission

### "Allow All the Time" Not Available

On some Android versions:

1. First grant "While using the app"
2. Open Settings > Apps > Wayfarer > Permissions > Location
3. Select "Allow all the time"

### Camera Permission for QR

If QR scanning doesn't work:

1. Check camera permission is granted
2. Ensure camera works in other apps
3. Clean camera lens
4. Try in better lighting

---

## Sync and Data Issues

### Duplicate Entries in Timeline

**Solutions:**

1. This may be intentional (server received twice)
2. Check sync settings
3. Manage via the web application

### Missing Timeline Data

**Solutions:**

1. **Check date filter**: May be viewing wrong day
2. **Wait for sync**: Data may not have synced yet
3. **Check tracking was enabled**: No logging = no data
4. **Verify server has data**: Check web app timeline

### Check-In Failed

**Solutions:**

1. Check internet connection
2. Verify location is available
3. Try again after a moment
4. Check server status

---

## PIN Lock Issues

### Forgot PIN

**Warning**: There's no recovery without clearing data.

1. Clear app data: Settings > Apps > Wayfarer > Clear Data
2. Set up app again
3. Your server data is preserved

### PIN Not Working

If correct PIN fails:

1. Restart the app
2. Check keyboard isn't stuck
3. Clear data if necessary

---

## Getting Help

### Self-Help Resources

1. Review this troubleshooting guide
2. Check the [FAQ](08-FAQ.md)
3. Export diagnostic report
4. Search for your error message online

### Reporting Issues

When reporting issues, include:

1. **Device model** and **OS version**
2. **App version** (Settings > About)
3. **Diagnostic report** (exported)
4. **Steps to reproduce** the issue
5. **Screenshots** if applicable

For potential bugs:

1. Note exact steps to reproduce
2. Include error messages (exact text)
3. Describe expected vs actual behavior
4. Report via GitHub issues
