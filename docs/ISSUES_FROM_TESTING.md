# Issues Found During Manual Testing (2026-01-04)

## Log File Exceptions

### EXC-1: Image Loading After Activity Destroyed (CRITICAL)
- **Error**: `Java.Lang.IllegalArgumentException: You cannot start a load for a destroyed activity`
- **Context**: Loading trip image (PHILIPPINES) after activity destroyed
- **Cause**: Glide tries to load image after activity lifecycle ended
- **Fix**: Cancel image loading on activity/page disposal

### EXC-2: ObjectDisposedException on Image Handler (CRITICAL)
- **Error**: `System.ObjectDisposedException: ObjectDisposed_Generic` on IServiceProvider
- **Context**: Image handler callback after app disposal
- **Cause**: Async image load callback runs after DI container disposed
- **Fix**: Related to EXC-1 - need to cancel pending image loads

---

## Section 1.2: Tracking Controls

### TC-1: Status text always shows "Tracking"
- **Issue**: Top-left overlay always shows "Tracking" regardless of toggle state
- **Expected**: Should show "Tracking" when on, "Paused"/"Ready" when off
- **Severity**: Medium

---

## Section 2.4: Place Actions

### PA-1: Search Wikipedia does not work
- **Issue**: Wikipedia search for places fails silently
- **Severity**: Medium

---

## Section 2.5: Place Editing

### PE-1: Edit Notes shows empty notes
- **Issue**: Notes editor opens with empty content instead of existing notes
- **Additional**: Header shows "Edit Notes" instead of "Edit [place name]"
- **Additional**: Save/cancel returns to trip overview instead of place details
- **Severity**: High

### PE-2: Edit Marker loads wrong color
- **Issue**: Marker editor shows wrong initial color
- **Additional**: Changing marker color/icon saves to server but doesn't update UI
- **Severity**: Medium

### PE-3: Delete place breaks trip state
- **Issue**: After delete, returns to current location instead of trip overview
- **Additional**: Trip FAB button disappears but markers still show on map
- **Severity**: Critical

### PE-4: Move place up/down doesn't update UI
- **Issue**: Reordering saves to server but trip overview doesn't refresh
- **Severity**: Medium

### PE-5: Edit region notes shows empty
- **Issue**: Region notes not loading in editor
- **Additional**: Header shows "Edit Notes" instead of "Edit [region name]"
- **Severity**: High

### PE-6: Delete region doesn't update UI
- **Issue**: Region deleted on server but trip overview still shows it
- **Severity**: Medium

### PE-7: Move region up/down doesn't work
- **Issue**: Region reordering completely broken
- **Severity**: Medium

---

## Section 2.7: Area/Segment Details

### AS-1: Edit area notes not loading
- **Issue**: Area notes empty in editor
- **Additional**: Save/cancel returns to trip overview instead of area details
- **Severity**: High

### AS-2: Edit segment notes not loading
- **Issue**: Segment notes empty in editor
- **Additional**: Save/cancel returns to trip overview instead of segment details
- **Severity**: High

---

## Section 2.9: Trip Management

### TM-1: Add Region doesn't update UI
- **Issue**: New region added on server but trip overview doesn't show it
- **Severity**: Medium

### TM-2: Add Place at location doesn't update UI
- **Issue**: New place added on server but trip overview doesn't show it
- **Severity**: Medium

### TM-3: Edit Trip Name doesn't update UI
- **Issue**: Trip name changed on server but not reflected in UI
- **Severity**: Medium

### TM-4: Edit Trip Notes not loading
- **Issue**: Trip notes empty in editor
- **Severity**: High

---

## Section 3.2: Navigation Modes

### NM-1: All modes show same ETA
- **Issue**: Walking/Cycling/Driving should show different ETAs
- **Severity**: Low

---

## Section 4.2: Members Display

### MD-1: Live indicator missing pulsing ring
- **Issue**: Live members don't show pulsing red ring based on time threshold
- **Severity**: Low

---

## Section 4.5: Member Actions

### MA-1: Navigate to member doesn't work
- **Issue**: Internal navigation to member location fails
- **Severity**: Medium

---

## Section 4.6: Date Navigation

### DN-1: Historic dates show wrong timestamp
- **Issue**: Tapping past location shows today's date, not recording date
- **Severity**: Medium

---

## Section 4.7: Visibility Controls

### VC-1: Select All/None buttons don't work
- **Issue**: Bulk member visibility toggle broken (individual works)
- **Severity**: Medium

### VC-2: Show historical toggle doesn't work
- **Issue**: Historical locations toggle has no effect
- **Severity**: Medium

---

## Section 4.8: Peer Visibility

### PV-1: Peer visibility feature missing
- **Issue**: Entire peer visibility section not implemented in mobile
- **Severity**: Medium

---

## Section 5.2: Date Navigation (Timeline)

### TDN-1: Date picker doesn't work
- **Issue**: Date picker opens but selection has no effect
- **Severity**: Medium

---

## Section 5.5: Location Editing

### LE-1: Edit coordinates doesn't update map
- **Issue**: Coordinates save to server but map doesn't move marker
- **Severity**: Medium

### LE-2: Edit time doesn't update UI
- **Issue**: Time saves to server but UI doesn't reflect change
- **Severity**: Medium

### LE-3: Delete location missing
- **Issue**: No delete button/action for timeline locations
- **Severity**: Medium

---

## Section 6.2: Trip Actions

### TA-1: Full Download only downloads metadata
- **Issue**: Tile download appears broken, only metadata downloaded
- **Severity**: High

### TA-2: Delete Tiles Only option missing
- **Issue**: Only "Remove All" shown, no tiles-only delete
- **Severity**: Low

---

## Section 7.7: Account

### AC-1: QR button style inconsistent
- **Issue**: "Scan QR code to connect" button has wrong colors when logged out
- **Severity**: Low

---

## Priority Summary

| Priority | Count | Issues |
|----------|-------|--------|
| Critical | 3 | EXC-1, EXC-2, PE-3 |
| High | 5 | PE-1, PE-5, AS-1, AS-2, TM-4, TA-1 |
| Medium | 17 | Most UI sync issues |
| Low | 4 | NM-1, MD-1, TA-2, AC-1 |

## Common Root Causes

1. **UI not refreshing after server operations** - Many issues (PE-4, PE-6, TM-1, TM-2, TM-3, LE-1, LE-2)
2. **Notes not loading in editors** - Multiple (PE-1, PE-5, AS-1, AS-2, TM-4)
3. **Navigation returning to wrong page** - Several (PE-1, PE-3, AS-1, AS-2)
4. **Image loading lifecycle** - EXC-1, EXC-2
