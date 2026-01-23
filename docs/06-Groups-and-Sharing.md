# Groups and Location Sharing

This guide explains how to use groups to share your location with others and view their locations.

---

## What Are Groups?

Groups in Wayfarer allow multiple users to share their locations with each other. Common uses include:

- **Family tracking**: Know where family members are
- **Travel companions**: Stay connected during trips
- **Work teams**: Coordinate field workers
- **Events**: Track participants at gatherings

---

## Group Basics

### Key Concepts

| Term | Definition |
|------|------------|
| Group | A collection of members who can see each other's locations |
| Member | A user who belongs to a group |
| Location | A position shared with the group |
| Live location | Real-time position updated via SSE |

### Group Creation

Groups are created in the Wayfarer web application. The mobile app is for viewing only.

To create a group:
1. Log into the Wayfarer web app
2. Go to Groups section
3. Create a new group
4. Invite members through the web app

### Joining Groups

To join a group, you need an invitation from the group creator or administrator. Accept invitations through the web app.

### Visibility in Friends Groups

For Friends groups, users can toggle their location visibility within the group. This peer visibility setting allows you to control whether other group members can see your location.

---

## Viewing Groups

### Groups Tab

1. Tap the **Groups** tab in the app
2. See all groups you belong to
3. Tap a group to view members

### Group List

Each group shows:
- Group name
- Number of members
- Your membership status

### Selecting a Group

When you tap a group:
1. Group becomes selected
2. View toggles to show members
3. Member locations load

---

## View Modes

The Groups page has two view modes, selectable via the toggle at the top:

### List View

Shows members in a scrollable list:
- Member name
- Last location timestamp
- Current status (online/offline)
- Distance from you

### Map View

Shows all members on the map:
- Colored markers for each member
- Member names on markers
- Your location (blue dot)
- Zoom to fit all members

Switch between views by tapping the segmented control at the top.

---

## Member Locations

### Location Display

Each member's location shows:
- **Marker**: Colored dot on the map
- **Name**: Displayed near marker
- **Time**: When location was updated
- **Status**: Live, recent, or stale

### Location States

| State | Visual | Meaning |
|-------|--------|---------|
| Live | Bright marker | Updated within threshold (e.g., 5 min) |
| Recent | Normal marker | Last known position |
| Stale | Faded marker | Old location (may be inaccurate) |

### Member Colors

Each member has a unique color:
- Colors are deterministic (same color everywhere)
- Based on username hash
- Consistent across web and mobile apps

Example colors:
- User "Alice" might always be blue
- User "Bob" might always be green

---

## Real-Time Updates

### How Live Updates Work

When viewing "Today":
1. App opens SSE (Server-Sent Events) connections
2. Server pushes location updates instantly
3. Map markers update in real-time
4. List refreshes automatically

### Update Frequency

- **SSE events**: Pushed instantly when members log locations
- **Fallback refresh**: Every 30 seconds
- **Historical days**: No live updates (static view)

### SSE Connection

The app maintains connections for live updates:
- One connection per group
- Automatic reconnection on network issues
- Connection closed when leaving Groups page

---

## Date Selection

You can view group locations for different days:

### Today (Default)

- Live updates enabled
- Shows current member positions
- Real-time marker updates

### Historical Days

- Static view of that day's locations
- No live updates
- Useful for reviewing past activity

### Changing Date

1. Tap the date header or calendar icon
2. Select a date from the picker
3. View loads for that day

---

## Member Details

Tap a member's marker or list item to see details:

### Information Shown

- **Name**: Display name and username
- **Location**: Coordinates and address (if available)
- **Time**: Member's local time and your local time
- **Accuracy**: GPS accuracy of the reading

### Actions

| Action | Description |
|--------|-------------|
| Open in Maps | View location in Google/Apple Maps |
| Wikipedia | Search nearby places on Wikipedia |
| Share | Share the location with others |

---

## Privacy Considerations

### What You Share

When you log locations (timeline tracking enabled):
- Your coordinates are visible to group members
- Update timestamps are shared
- Accuracy information is included

### What Others See

Group members can see:
- Your last known location
- When you were last active
- Your location history (if viewing past days)

### Controlling Visibility

To stop sharing your location:
1. Disable timeline tracking in settings
2. Or, leave the group via the web app

> **Note**: Disabling tracking stops new locations from being shared. Historical data may still be visible.

---

## Offline Behavior

### When Offline

- **Last viewed state**: Shows cached member locations
- **No live updates**: Markers don't move
- **Offline banner**: Indicates limited functionality

### When Online Again

- Live updates resume
- Locations refresh
- New data loads automatically

---

## Legend

The legend shows member colors for reference:

### Accessing the Legend

1. In Map View, look for the legend panel
2. Shows all members with their colors
3. Can collapse/expand

### Legend Actions

- **Only this**: Show only one member
- **Select All**: Show all members
- **Select None**: Hide all members

---

## Performance Tips

### Large Groups

For groups with many members:
- Map may need time to load all markers
- Consider filtering to specific members
- Use List View for easier navigation

### Battery Usage

Viewing groups uses network:
- Live updates require active connections
- Consider closing when not needed
- Historical views use less battery

---

## Troubleshooting Groups

### "No Groups Found"

If no groups appear:
1. Check internet connection
2. Verify you've joined groups in the web app
3. Confirm server connection in settings
4. Try pull-to-refresh

### Members Not Updating

If locations seem stale:
1. Check if viewing "Today" (only Today has live updates)
2. Member may have tracking disabled
3. Server may be experiencing issues
4. Try refreshing the page

### Member Markers Missing

If some members don't show:
1. They may not have logged locations recently
2. Check the legend for filtering
3. Zoom out to see wider area
4. Check member's privacy settings

### Connection Issues

If live updates stop:
1. Check internet connection
2. App will auto-reconnect
3. Pull down to force refresh
4. Try closing and reopening Groups page

---

## Common Use Cases

### Family Safety

1. Create a family group in web app
2. Have all family members join
3. Enable timeline tracking on all devices
4. Check Groups tab to see everyone's location

### Travel Coordination

1. Create a trip-specific group
2. Add travel companions
3. Use during the trip to stay connected
4. View past days to see where everyone went

### Event Tracking

1. Create group for event participants
2. Everyone enables tracking during event
3. View real-time locations on map
4. Review movement patterns afterward

---

## Best Practices

1. **Communicate**: Let group members know when you're tracking
2. **Respect privacy**: Only add people who consent
3. **Disable when done**: Turn off tracking after events
4. **Review regularly**: Check who's in your groups
5. **Use meaningful names**: Name groups clearly

---

## Next Steps

- [Troubleshoot common issues](07-Troubleshooting.md)
- [Read the FAQ](08-FAQ.md)
- [Return to feature overview](03-Features.md)
