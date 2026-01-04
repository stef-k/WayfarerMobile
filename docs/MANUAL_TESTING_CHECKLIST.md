# Manual Testing Checklist - Post-Refactoring Verification

This checklist verifies that all user-facing features work correctly after the refactoring.
Test each item and mark ✅ when verified or ❌ if broken.

## Pre-Testing Setup

- [ ] Fresh install on device (clear app data)
- [ ] Login to Wayfarer account
- [ ] Grant all permissions (location, notifications)
- [ ] Enable location tracking

---

## 1. Main Page (Map)

### 1.1 Location Display
- [ ] Current location shows on map with indicator
- [ ] Location text displays coordinates correctly
- [ ] Accuracy indicator shows (e.g., ±15m)
- [ ] Heading/compass shows direction when moving
- [ ] Altitude shows when available
- [ ] Copy coordinates button works

### 1.2 Tracking Controls
- [ ] Start tracking button works
- [ ] Stop tracking button works
- [ ] Tracking state indicator updates correctly
- [ ] Status text shows current state
- [ ] Location count increments

### 1.3 Map Controls
- [ ] Reset North button rotates map to north
- [ ] Center on Location button centers map
- [ ] Cache health indicator shows (colored dot)
- [ ] Tap cache health shows cache status popup

### 1.4 Check-In Sheet
- [ ] Check-In button opens bottom sheet
- [ ] Location displays in sheet
- [ ] Activity types load from server
- [ ] Activity selection works
- [ ] Notes field accepts input
- [ ] Submit check-in succeeds
- [ ] Success overlay shows
- [ ] Sheet closes after success
- [ ] Share location button works
- [ ] Copy coordinates button in sheet works

### 1.5 Context Menu (Long Press / Drop Pin)
- [ ] Drop Pin mode activates
- [ ] Tap on map places pin
- [ ] Context menu appears at pin location
- [ ] Navigate to pin (Walk/Drive/Bike) works
- [ ] Open in external maps works
- [ ] Copy coordinates works
- [ ] Share location works
- [ ] Search Wikipedia nearby works
- [ ] Pin clears correctly

---

## 2. Trip Sheet (Trip Loaded)

### 2.1 Trip Loading
- [ ] Load trip from My Trips page
- [ ] Trip name shows in title
- [ ] Trip places appear on map
- [ ] Trip segments show on map
- [ ] Trip areas show on map
- [ ] Map zooms to fit trip

### 2.2 Trip Overview
- [ ] Trip cover image displays
- [ ] Updated date shows
- [ ] Show trip notes works
- [ ] Regions list correctly
- [ ] Places list under each region
- [ ] Areas section shows
- [ ] Segments section shows

### 2.3 Place Selection
- [ ] Tap place in list selects it
- [ ] Map centers on selected place
- [ ] Place details show in sheet
- [ ] Place coordinates display
- [ ] Place address displays (if available)
- [ ] Place notes show (if available)

### 2.4 Place Actions
- [ ] Navigate to place works
- [ ] Open in Maps works
- [ ] Copy coordinates works
- [ ] Share place works
- [ ] Search Wikipedia works
- [ ] Edit place menu opens

### 2.5 Place Editing
- [ ] Edit Name dialog works
- [ ] Edit Notes navigates to editor
- [ ] Edit Coordinates mode activates
  - [ ] Map tap sets pending coordinates
  - [ ] Pending coordinates text updates
  - [ ] Save applies coordinates
  - [ ] Cancel reverts
- [ ] Edit Marker navigates to editor
- [ ] Delete place works (with confirmation)
- [ ] Move place up/down works

### 2.6 Region Management
- [ ] Edit region menu opens
- [ ] Edit region name works
- [ ] Edit region notes works
- [ ] Delete region works
- [ ] Move region up/down works

### 2.7 Area/Segment Details
- [ ] Select area shows details
- [ ] Area notes display
- [ ] Edit area notes works
- [ ] Select segment shows details
- [ ] Segment distance/duration shows
- [ ] Edit segment notes works

### 2.8 Trip Sheet Navigation
- [ ] Back button navigates correctly
- [ ] Close sheet button works
- [ ] Go to My Trips button works
- [ ] Place search works
  - [ ] Search query filters places
  - [ ] Select from search results works

### 2.9 Trip Management
- [ ] Add Region works
- [ ] Add Place at current location works
- [ ] Edit Trip Name works
- [ ] Edit Trip Notes works
- [ ] Unload Trip clears map

---

## 3. Navigation

### 3.1 Turn-by-Turn Navigation
- [ ] Start navigation to place
- [ ] Route displays on map
- [ ] Navigation HUD appears
- [ ] Distance to destination shows
- [ ] ETA updates
- [ ] Remaining time shows
- [ ] Progress updates as you move
- [ ] Stop navigation button works
- [ ] Route clears when stopped

### 3.2 Navigation Modes
- [ ] Walk mode works
- [ ] Drive mode works
- [ ] Bike mode works
- [ ] External maps option opens system maps

---

## 4. Groups Page

### 4.1 Group Loading
- [ ] Groups load on page appear
- [ ] Group picker shows available groups
- [ ] Select group loads members

### 4.2 Members Display
- [ ] Members list shows
- [ ] Live members show indicator
- [ ] Member colors display correctly
- [ ] Member roles show
- [ ] Last location time shows

### 4.3 Map View
- [ ] Toggle to map view works
- [ ] Members show on map with markers
- [ ] Member colors match list

### 4.4 Member Details
- [ ] Tap member opens details sheet
- [ ] Member name/role shows
- [ ] Location timestamp shows
- [ ] Coordinates show
- [ ] Address shows (if available)

### 4.5 Member Actions
- [ ] Open in Maps works
- [ ] Search Wikipedia works
- [ ] Copy coordinates works
- [ ] Share location works
- [ ] Navigate to member works

### 4.6 Date Navigation
- [ ] Previous day button works
- [ ] Next day button works
- [ ] Today button works
- [ ] Date picker opens
- [ ] Select date loads that day's locations

### 4.7 Visibility Controls
- [ ] Select all members works
- [ ] Deselect all members works
- [ ] Individual member toggle works
- [ ] Show historical toggle works

### 4.8 Peer Visibility
- [ ] Peer visibility toggle works
- [ ] Status updates in real-time

---

## 5. Timeline Page

### 5.1 Timeline Loading
- [ ] Timeline loads for today
- [ ] Location points show on map
- [ ] Timeline connects points correctly

### 5.2 Date Navigation
- [ ] Previous day button works
- [ ] Next day button works
- [ ] Today button works
- [ ] Date picker opens and works

### 5.3 Location Selection
- [ ] Tap location on map selects it
- [ ] Location sheet opens
- [ ] Time shows correctly
- [ ] Date shows correctly
- [ ] Coordinates show
- [ ] Accuracy shows
- [ ] Speed shows (if available)
- [ ] Altitude shows (if available)
- [ ] Address shows (if geocoded)
- [ ] Notes show (if any)

### 5.4 Location Actions
- [ ] Open in Maps works
- [ ] Search Wikipedia works
- [ ] Copy coordinates works
- [ ] Share location works

### 5.5 Location Editing
- [ ] Edit menu opens
- [ ] Edit coordinates mode works
  - [ ] Map tap sets pending coords
  - [ ] Save applies changes
  - [ ] Cancel reverts
- [ ] Edit time works
  - [ ] DateTime picker opens
  - [ ] Save applies new time
- [ ] Edit notes navigates to editor
- [ ] Delete location works

### 5.6 Sync Status
- [ ] Pending sync indicator shows
- [ ] Sync complete updates display
- [ ] Failed sync shows retry option

---

## 6. Trips Page

### 6.1 My Trips Tab
- [ ] Trips list loads
- [ ] Pull to refresh works
- [ ] Trip cards show correctly
- [ ] Trip images display
- [ ] Trip names/dates show

### 6.2 Trip Actions
- [ ] Load to Map button works
- [ ] Back to Trip (when loaded) works
- [ ] Quick Download works
- [ ] Full Download works
- [ ] Delete Tiles Only works
- [ ] Delete Download works

### 6.3 Download Status
- [ ] Download progress shows
- [ ] Download status message updates
- [ ] Pause download works
- [ ] Resume download works
- [ ] Cancel download works

### 6.4 Public Trips Tab
- [ ] Public trips load
- [ ] Search works
- [ ] Sort options work
- [ ] Infinite scroll loads more
- [ ] Clone trip works

### 6.5 Sync Status
- [ ] Pending sync banner shows
- [ ] Retry sync button works
- [ ] Cancel sync works
- [ ] Failed sync shows error

---

## 7. Settings Page

### 7.1 Navigation Settings
- [ ] Transport mode selection works
- [ ] Settings persist after restart

### 7.2 Cache Settings
- [ ] Cache size displays
- [ ] Clear cache works
- [ ] Cache limit setting works

### 7.3 Visit Notifications
- [ ] Toggle notifications works
- [ ] Notification settings apply

### 7.4 Appearance
- [ ] Theme selection works (if available)
- [ ] Map style options work

### 7.5 Timeline Data
- [ ] Queue count shows
- [ ] Timeline count shows
- [ ] Refresh counts works

### 7.6 Security
- [ ] PIN lock toggle works
- [ ] Set/change PIN works
- [ ] Lock app immediately works

### 7.7 Account
- [ ] Account info displays
- [ ] Logout works
- [ ] Reset onboarding works

---

## 8. Diagnostics Page

### 8.1 Location Status
- [ ] GPS status shows
- [ ] Last location displays
- [ ] Provider shows
- [ ] Accuracy shows

### 8.2 Tracking Status
- [ ] Tracking state shows
- [ ] Performance mode shows
- [ ] Update count shows

### 8.3 System Info
- [ ] Device info displays
- [ ] App version shows
- [ ] API status shows

---

## 9. Cross-Cutting Concerns

### 9.1 Page Navigation
- [ ] Navigate to all pages works
- [ ] Back navigation works
- [ ] Tab bar navigation works
- [ ] Deep links work (if applicable)

### 9.2 State Persistence
- [ ] App backgrounded and resumed
- [ ] Loaded trip persists
- [ ] Tracking continues in background
- [ ] Settings persist

### 9.3 Error Handling
- [ ] Network errors show toast
- [ ] Invalid data handled gracefully
- [ ] Permission denied shows proper message

### 9.4 Memory/Performance
- [ ] Navigate pages repeatedly (10+ times)
- [ ] No visible memory growth
- [ ] App remains responsive
- [ ] No crashes after extended use

---

## Summary

| Section | Total | Passed | Failed |
|---------|-------|--------|--------|
| Main Page | | | |
| Trip Sheet | | | |
| Navigation | | | |
| Groups Page | | | |
| Timeline Page | | | |
| Trips Page | | | |
| Settings Page | | | |
| Diagnostics | | | |
| Cross-Cutting | | | |
| **TOTAL** | | | |

**Tested By:** _______________
**Date:** _______________
**Build:** _______________
**Device:** _______________
