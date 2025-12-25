# Trip Edit Feature

## Editable models and properties

1. **Trip**
   - Name
   - Notes

2. **Region**
   - Name
   - Notes
   - Display order

3. **Place**
   - Name
   - Notes
   - Coordinates
   - Marker color and icon name
   - Display order

4. **Segment**
   - Notes

5. **Area**
   - Notes

---

## Logic

There should be queue DB tables for the offline entities. They will contain the original data for the changed properties so restoration will be possible if the user cancels or the server rejects. The queue will have a set number of tries and will back off (in time) between tries up to a cap.

- Tries **won't exhaust** and syncs will **pause** if the app is offline.
- Tries **exhaust** if the server fails (server issues) or rejects, but **not** for reasons of bad data.

### When a user updates one or more properties

1. Changes are saved in `offline*` tables.
2. UI syncs everywhere with the new data.
3. Changes are saved in their respective queue tables (new tables) for sync to server.

   - **3a.** If online and sync succeeds, the server sync entry is removed from the queue.
   - **3b.** If offline, the app tracks and retries:
     - **3b1.** When the app goes online and syncs.
     - **3b2.** At app startup.
     - **3b3.** At the user's manual invocation.
   - **3c.** Upon successful sync, the queue is cleaned.
4. If sync fails for server-related issues, the queue remains.
5. If sync fails for server rejections due to bad data, the queue is marked as failed.
6. Upon server rejection, offline tables are restored with their original values from queue tables and the queue clears.
7. Trip deletion clears the related queue.
8. In **My Trips**, offer visual feedback showing:
   - **a.** `n` items queued for sync.
   - **b.** If the queue exhausted its tries:
     - **b1.** **Retry** button that resets queue tries.
     - **b2.** **Cancel** button that restores offline tables from the queue and clears queued items.

### Things to check / enhance / pay attention to

- Update UI and offline tables first, then try to sync the server.
- Consider other scenarios and combinations of (3) regarding syncing scenarios and race conditions.
- Check old project for subtle reference, but implementation will use best practices like the new project does.
- Check with server API for the correct and respective endpoints and parameters for each sync action.
- Server back end (read-only) for reference: `C:\Users\stef\source\repos\Wayfarer`
- Old project (read-only) for reference: `C:\Users\stef\source\repos\Wayfarer.Mobile`
- All work will be done in a new branch.
- After each feature completion, use reviewer and architect agents to examine and fix potential issues or enhance the code if needed (no over-engineering).
- Work will be submitted as PRs.
- After feature completions, use QA agents to check if more test coverage is needed and implement tests.

---

## UI

1. All destructive buttons will have danger background color and will require confirmations explaining what the selected action does.
2. All “edit notes” views will use the same editor as the Timeline place notes.
3. All FABs will have the same background color as the FABs in the map.
4. All trip/region/place/area/segment names everywhere in the trip feature should be able to wrap text if they have long names (expect the row to expand to another line).

### Map

1. All FAB buttons will have the same background (likely gray 900) — no primary/danger or other colors.
2. All overlays (whether overlapping other elements or not) will have the same background color as the ones in Timeline (likely black, like the top overlay on the map page).

### My Trips

1. Conditionally (if present) show visual feedback of items pending syncing.
2. Conditionally (if present) show **Cancel** and **Retry** buttons for user-initiated queue actions.
3. **Load to Map** and **Download** buttons will have the same background color as the **+ Offline Maps** button.
4. My Trips currently shows under the trip name: place count and tiles (or no tiles if only metadata downloaded). Missing: regions, segments, areas count.

### Trip Overview

1. If regions have a cover image, show it under the region name in the same way as the trip image.
2. If regions have notes, show them under the image in the same manner as the trip does.
3. Regions and places (right side) will have up/down arrow buttons for manipulating their `DisplayOrder`. When tapped, follow the update flow described in **Logic** (update UI → save in offline table → save in queue). Buttons will be small FABs.
4. Regions (right side of name) will have 2 FABs: **Edit** (PNG) and **Delete**. Delete uses danger background and asks confirmation. When deleting a region, all child objects will also be deleted (places, areas, etc.) — explain this in the confirmation.
   - On **Edit** tap, show a dialog asking what to edit: **Name** or **Notes**.
     - If **Name**: an input element is enough.
     - If **Notes**: use the same note editor as Timeline notes.
5. Trip Overview should have a top-left back button to go to the My Trips tab, styled like the trip details back navigation.
6. Trip notes tap area has left margin/padding and does not reach the leftmost area in the list.
7. In the trip bottom area containing the **Unload Trip** button:
   - **a. Add** button: when tapped, ask what to add (**Region** or **Place**). After selection, ask for a name (`Region.Name` or `Place.Name`) and create the respective element for the user to add to the trip.
   - **b. Edit** button: ask what to edit via a dialog (**Trip.Name** or **Trip.Notes**).
     - If **Name**: show an input element.
     - If **Notes**: use the same notes editor as Timeline.
   - **c.** Upon successful CRU(D), use the UI/offline*/queue* sync/update system mentioned in **Logic**.
   - **d.** Both **Add** and **Edit** buttons will be FABs with the same background as all other FABs (PNGs only).
   - **e.** The **Unload Trip** button will have the same background color as the FABs.

### Place editing and Place details view

1. Place coordinates will use the same system Timeline does:
   - When the user taps to edit coordinates, lower the bottom sheet.
   - Present an overlay with one-line info and **Cancel**/**Save** buttons at the top of the map.
   - Focus on the current place marker and add a temp marker (Timeline style) so the user knows the start point.
   - Each tap updates the temp marker until the user taps **Save**.

2. Place marker icon edit:

   - Present a view with 2 elements and **Cancel**/**Save** buttons. Buttons are fixed at the bottom.

   - **a1.** The first element will be circled color buttons showcasing available marker colors used by trip places.
   - **a2.** The second element will be the custom combo box offering all available icons used by trip places.

   - **b.** Tapping a color changes the offerings in the icon combo box.
   - **c.** Icon combo box (selected and listed items) renders the actual PNGs.
   - **d.** Combo box can filter icons by name.
   - **e.** Values offered in color and icon names won’t be hardcoded. Use the existing dict cache used by the trip map.
   - **f.** Check with the server API for how it expects `color` and `iconName` parameters and naming conventions (PascalCase, etc.).
   - **g.** Colors should be in order: blue, purple, black, green, red — with blue as default. Guard for existence.
   - **h.** Icons should have `marker` as default, then (if present) order: star, camera, museum, eat, drink, hotel, info, help. Guard for existence.

3. All Place details bottom buttons will be FABs.
4. Place edit button offers options for what to edit:
   - **a.** Name → input element, prefilled with current name
   - **b.** Notes → same editor as Timeline
   - **c.** Coordinates → on map as Timeline
   - **d.** Marker → as described above (Place marker icon edit)
   - **e.** Delete place → confirmation + danger color
5. Bottom buttons should be FABs.
6. Internal navigation button:
   - When tapped, start navigation.
   - Ask the user what navigation type to use (from backend offers for time estimations and what OSRM supports).
   - Internal navigation for trips should use the priority mentioned in `claude.md` **Route Calculation Priority** section.
7. Upon internal navigation launch, lower the bottom sheet.

---

## General behavior

1. Deleting a trip unloads the map.
2. Tapping other main menu entries unloads the map.
