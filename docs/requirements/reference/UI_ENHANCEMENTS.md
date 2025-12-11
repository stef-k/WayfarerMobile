# UI Enhancement Opportunities

This document tracks Syncfusion MAUI Toolkit adoption and UI enhancement opportunities.

**Last Updated:** December 11, 2025

---

## Current Syncfusion Usage (10 Components)

| Component | Where Used | Purpose |
|-----------|------------|---------|
| **SfNavigationDrawer** | TripsPage | Trip sidebar with places/segments overview |
| **SfExpander** | SettingsPage (9x), DiagnosticsPage (6x) | Collapsible sections |
| **SfSwitch** | SettingsPage (8x) | Toggle settings (Cupertino style) |
| **SfLinearProgressBar** | OnboardingPage | Step progress indicator |
| **SfBusyIndicator** | OnboardingPage | Loading spinner |
| **SfBottomSheet** | PlaceDetailsSheet, TimelineEntrySheet | Modal detail views |
| **SfSegmentedControl** | GroupsPage | List/Map view toggle |
| **SfShimmer** | TripsPage, TimelinePage, GroupsPage | Loading placeholders |
| **SfDatePicker** | TimelinePage | Date navigation |
| **SfTextInputLayout** | SearchableDropdown | Floating label input |

---

## Available Syncfusion Controls (Not Yet Used)

### Data Visualization
- Cartesian Charts, Circular Charts, Funnel Charts, Polar Charts, Pyramid Charts, Spark Charts, Sunburst Charts

### Calendars
- Calendar

### Editors
- Date Picker, Date Time Picker, Numeric Entry, Numeric Up Down, OTP Input, Picker, Time Picker

### Navigation
- **Bottom Sheet** ⭐, Tab View

### Layout
- Accordion, Cards, Carousel, Popup, Text Input Layout

### Buttons
- Button, Chips, Segmented Control

### Notification
- **Circular Progress Bar** ⭐, Pull to Refresh

### Miscellaneous
- Effects View, **Shimmer** ⭐

---

## Priority 1: Place Details & Editing (Bottom Sheet Pattern) ✅ COMPLETE

### Background

The old Wayfarer.Mobile app had:
1. **Trip Sidebar** - Overview of trip places/segments (regions → places → segments hierarchy)
2. **Place Details Modal** - Full-screen modal showing place details with actions (navigate, Google Maps, Wikipedia, share)
3. **Notes Editor** - WebView with Quill.js for rich text editing of place notes
4. **Timeline Details** - Location details modal for timeline entries

### Current State in WayfarerMobile

| Feature | Status | Notes |
|---------|--------|-------|
| Trip sidebar (overview) | ✅ Done | SfNavigationDrawer with places + segments list |
| Trip place details | ✅ Done | SfBottomSheet with PlaceDetailsSheet |
| Trip place editing | ✅ Done | Edit name, coordinates, notes |
| Trip place notes | ✅ Done | Rich text via NotesEditorControl (Quill.js) |
| Timeline entry details | ✅ Done | SfBottomSheet with TimelineEntrySheet |
| Timeline entry editing | ✅ Done | Edit date/time, coordinates, notes |

### Recommended Implementation: SfBottomSheet

Use `SfBottomSheet` for a modern, Google Maps-style experience:

```
┌─────────────────────────────────────┐
│              MAP                     │
│                                      │
│                                      │
├─────────────────────────────────────┤  ← Grabber (drag handle)
│ ▔▔▔                                 │
│ Place Name                          │  ← Collapsed state (peek)
│ Category • Distance                 │
└─────────────────────────────────────┘

Expand to half:
┌─────────────────────────────────────┐
│              MAP                     │
├─────────────────────────────────────┤
│ ▔▔▔                                 │
│ Place Name                          │
│ Category • Distance                 │
│                                      │
│ 📍 Address line 1                   │
│    Address line 2                   │
│                                      │
│ ⏱️ Open now • Closes 10 PM         │
│                                      │
│ [Navigate] [Directions] [Share]     │
└─────────────────────────────────────┘

Expand to full:
┌─────────────────────────────────────┐
│ ▔▔▔        Place Name          ✕   │
├─────────────────────────────────────┤
│ Mini-map preview                    │
│ ┌───────────────────────────────┐   │
│ │         📍                    │   │
│ └───────────────────────────────┘   │
│                                      │
│ 📍 Full address                     │
│ 🌐 Coordinates: 45.123, -122.456   │
│                                      │
│ ─────────────────────────────────   │
│ Notes                           ✏️  │
│ Rich text content here...           │
│                                      │
│ ─────────────────────────────────   │
│ [Navigate] [Google Maps] [Wikipedia]│
│ [Share Location]                    │
└─────────────────────────────────────┘
```

### Implementation Plan

#### A. Trip Place Details Bottom Sheet

1. Create `Controls/PlaceDetailsSheet.xaml` using `SfBottomSheet`
2. Properties:
   - Place name, category, coordinates
   - Mini-map showing location
   - Notes section (read-only initially)
   - Action buttons: Navigate, Open in Google Maps, Wikipedia, Share
3. Bindable properties for place data
4. Three states: Collapsed (peek), Half, Full
5. Integrate into TripsPage - show when place tapped in sidebar

#### B. Trip Place Editing

1. Add "Edit" button in PlaceDetailsSheet header
2. When editing:
   - Coordinates become editable
   - Notes section switches to edit mode
   - Save/Cancel buttons appear
3. Rich text for notes (future - start with plain Editor)

#### C. Timeline Entry Details Bottom Sheet

1. Create `Controls/TimelineEntrySheet.xaml` using `SfBottomSheet`
2. Properties:
   - Timestamp, coordinates, accuracy, speed
   - Activity type with icon
   - Sync status
   - Mini-map showing location
3. Integrate into TimelinePage - show when entry tapped

#### D. Timeline Entry Editing

1. Editable fields:
   - Date/Time (use `SfDateTimePicker`)
   - Activity type (use `SfPicker` or `SfChips`)
   - Notes
2. Cannot edit coordinates (GPS data should be immutable)
3. Can delete entry

---

## Priority 2: General UI Enhancements

### 2.1 SearchableDropdown - Activity Type Selection ✅ COMPLETE

**Where:** CheckInPage activity type selection

**Implementation:** Custom `SearchableDropdown` control combining:
- `SfTextInputLayout` for floating label styling
- `CollectionView` for filtered dropdown suggestions
- Two-way binding support for programmatic selection
- Replaces SfChips (searchable dropdown is more appropriate for activity lists)

### 2.2 SfSegmentedControl - View Mode Toggle ✅ COMPLETE

**Where:** GroupsPage List/Map toggle

**Implementation:**
```xaml
<segmented:SfSegmentedControl
    SelectedIndex="{Binding ViewModeIndex, Mode=TwoWay}"
    CornerRadius="16"
    SegmentHeight="36"
    SegmentWidth="100">
```

### 2.3 SfDatePicker - Timeline Navigation ✅ COMPLETE

**Where:** TimelinePage date selection

**Implementation:** Tap date header to open `SfDatePicker` in dialog mode + Previous/Today/Next buttons

### 2.4 SfCircularProgressBar - Diagnostics & Downloads

**Where:** DiagnosticsPage cache usage, TripsPage download progress

**Current:** Linear progress bar / text percentages

**Status:** Not implemented (low priority)

### 2.5 SfShimmer - Loading Placeholders ✅ COMPLETE

**Where:** TripsPage, TimelinePage, GroupsPage

**Implementation:** `ShimmerLoadingView` reusable control with `CirclePersona` type

### 2.6 SfPopup - Dialogs & Confirmations

**Where:** Replace custom dialog implementations

**Current:** IDialogService with DisplayAlert

**Status:** Not implemented (DisplayAlert is sufficient)

---

## Priority 3: Nice-to-Have

| Enhancement | Component | Location |
|-------------|-----------|----------|
| Numeric PIN entry | SfNumericEntry | LockScreenPage |
| Pull-to-refresh styling | SfPullToRefresh | All list pages |
| Touch ripple effects | SfEffectsView | Buttons, list items |
| Onboarding carousel | SfCarousel | OnboardingPage |
| Settings accordion | SfAccordion | SettingsPage (alternative to Expander) |

---

## Implementation Order

### Phase 1: Bottom Sheet Foundation ✅ COMPLETE
1. [x] Add SfBottomSheet to a test page
2. [x] Create PlaceDetailsSheet control
3. [x] Integrate with TripsPage
4. [x] Test expand/collapse behavior

### Phase 2: Timeline Integration ✅ COMPLETE
1. [x] Create TimelineEntrySheet control
2. [x] Integrate with TimelinePage
3. [x] Add date/time picker for editing

### Phase 3: General Enhancements ✅ COMPLETE
1. [x] SfSegmentedControl for GroupsPage
2. [x] SearchableDropdown for activity selection (replaces SfChips)
3. [x] SfShimmer for loading states
4. [ ] SfCircularProgressBar for diagnostics (deferred - low priority)

### Phase 4: Polish
1. [x] SfDatePicker for timeline
2. [ ] SfPopup for dialogs (deferred - DisplayAlert sufficient)
3. [ ] Touch effects and animations (future enhancement)

---

## Notes on Rich Text Editing

The old app used **Quill.js** via WebView for rich text notes. Options for new app:

1. **Plain Editor** (MVP) - Simple multi-line text, no formatting
2. **Markdown Editor** - Plain text with markdown preview
3. **WebView + Quill.js** (Old approach) - Full rich text, requires HTML handling
4. **Third-party MAUI control** - Evaluate available options

**Recommendation:** Start with plain Editor for MVP. Rich text can be added later if needed.

---

## References

- [Syncfusion MAUI Toolkit GitHub](https://github.com/syncfusion/maui-toolkit)
- [Syncfusion MAUI Toolkit Docs](https://help.syncfusion.com/maui-toolkit/)
- Old app reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Controls\`
