using System.Text.RegularExpressions;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Represents a group of timeline items (e.g., by hour).
/// </summary>
public class TimelineGroup : List<TimelineItem>
{
    /// <summary>
    /// Gets the group header text.
    /// </summary>
    public string Header { get; }

    /// <summary>
    /// Creates a new timeline group from server locations.
    /// </summary>
    /// <param name="header">The group header text.</param>
    /// <param name="locations">The locations in this group.</param>
    public TimelineGroup(string header, IEnumerable<TimelineLocation> locations) : base()
    {
        Header = header;
        AddRange(locations.Select(l => new TimelineItem(l)));
    }
}

/// <summary>
/// Represents a single timeline item for display.
/// </summary>
public class TimelineItem
{
    /// <summary>
    /// Gets the underlying location data from server.
    /// </summary>
    public TimelineLocation Location { get; }

    /// <summary>
    /// Gets the originating record identity, separately from numeric API/display IDs.
    /// </summary>
    public TimelineEntryIdentity Identity => Location.Identity;

    /// <summary>
    /// Gets the formatted time.
    /// </summary>
    public string TimeText => Location.LocalTimestamp.ToString("HH:mm:ss");

    /// <summary>
    /// Gets the formatted coordinates.
    /// </summary>
    public string CoordinatesText => Location.Coordinates != null
        ? $"{Location.Coordinates.Y:F6}, {Location.Coordinates.X:F6}"
        : "Unknown";

    /// <summary>
    /// Gets the latitude.
    /// </summary>
    public double? Latitude => Location.Coordinates?.Y;

    /// <summary>
    /// Gets the longitude.
    /// </summary>
    public double? Longitude => Location.Coordinates?.X;

    /// <summary>
    /// Gets the local timestamp.
    /// </summary>
    public DateTime LocalTimestamp => Location.LocalTimestamp;

    /// <summary>
    /// Gets the notes.
    /// </summary>
    public string? Notes => Location.Notes;

    /// <summary>
    /// Gets the accuracy text.
    /// </summary>
    public string AccuracyText => Location.Accuracy.HasValue
        ? $"~{Location.Accuracy.Value:F0}m"
        : "Unknown";

    /// <summary>
    /// Gets the accuracy indicator color.
    /// </summary>
    public Microsoft.Maui.Graphics.Color AccuracyColor => Location.Accuracy switch
    {
        null => Microsoft.Maui.Graphics.Colors.Gray,
        <= 10 => Microsoft.Maui.Graphics.Colors.Green,
        <= 30 => Microsoft.Maui.Graphics.Colors.Orange,
        _ => Microsoft.Maui.Graphics.Colors.Red
    };

    /// <summary>
    /// Gets the sync status icon (always synced for server data).
    /// </summary>
    public string SyncStatusIcon => "check";

    /// <summary>
    /// Gets the provider text.
    /// </summary>
    public string ProviderText => Location.LocationType ?? "Unknown";

    /// <summary>
    /// Gets the speed text if available.
    /// </summary>
    public string? SpeedText => Location.Speed.HasValue
        ? $"{Location.Speed.Value * 3.6:F1} km/h"
        : null;

    /// <summary>
    /// Creates a new timeline item from server location.
    /// </summary>
    /// <param name="location">The server location data.</param>
    public TimelineItem(TimelineLocation location)
    {
        Location = location;
    }
}

/// <summary>
/// Display model for timeline location details in the bottom sheet.
/// </summary>
public class TimelineLocationDisplay
{
    private readonly TimelineLocation _location;
    private readonly string? _serverUrl;

    /// <summary>
    /// Creates a new display model from a timeline location.
    /// </summary>
    /// <param name="location">The timeline location.</param>
    /// <param name="serverUrl">The server URL for image proxy conversion.</param>
    public TimelineLocationDisplay(TimelineLocation location, string? serverUrl = null)
    {
        _location = location;
        _serverUrl = serverUrl;
    }

    /// <summary>
    /// Gets the originating record identity, separately from numeric API/display IDs.
    /// </summary>
    public TimelineEntryIdentity Identity => _location.Identity;

    /// <summary>Gets whether the originating record has server-linked edit and delete actions.</summary>
    public bool CanEdit => Identity.CanMutate;

    /// <summary>Gets whether the entry can only be viewed, shared, and exported.</summary>
    public bool IsReadOnly => !CanEdit;

    /// <summary>Gets the visible explanation when edit and delete actions are unavailable.</summary>
    public string? ReadOnlyExplanation => IsReadOnly ? TimelineEntryIdentity.ReadOnlyExplanation : null;

    /// <summary>
    /// Gets the formatted time text.
    /// </summary>
    public string TimeText => _location.LocalTimestamp.ToString("HH:mm:ss");

    /// <summary>
    /// Gets the formatted date text.
    /// </summary>
    public string DateText => _location.LocalTimestamp.ToString("dddd, MMMM d, yyyy");

    /// <summary>
    /// Gets the local timestamp.
    /// </summary>
    public DateTime LocalTimestamp => _location.LocalTimestamp;

    /// <summary>
    /// Gets the coordinates text.
    /// </summary>
    public string CoordinatesText => $"{Latitude:F6}, {Longitude:F6}";

    /// <summary>
    /// Gets the latitude.
    /// </summary>
    public double Latitude => _location.Latitude;

    /// <summary>
    /// Gets the longitude.
    /// </summary>
    public double Longitude => _location.Longitude;

    /// <summary>
    /// Gets the activity name.
    /// </summary>
    public string? ActivityName => _location.ActivityType;

    /// <summary>
    /// Gets or sets the activity type (used for updates).
    /// </summary>
    public string? ActivityType
    {
        get => _location.ActivityType;
        set => _location.ActivityType = value;
    }

    /// <summary>
    /// Gets whether an activity is set.
    /// </summary>
    public bool HasActivity => !string.IsNullOrEmpty(_location.ActivityType);

    /// <summary>
    /// Gets the address.
    /// </summary>
    public string? Address => _location.FullAddress ?? _location.Address;

    /// <summary>
    /// Gets whether an address is available.
    /// </summary>
    public bool HasAddress => !string.IsNullOrEmpty(Address);

    /// <summary>
    /// Gets the accuracy text.
    /// </summary>
    public string AccuracyText => _location.Accuracy.HasValue
        ? $"~{_location.Accuracy.Value:F0}m"
        : "Unknown";

    /// <summary>
    /// Gets whether speed is available.
    /// </summary>
    public bool HasSpeed => _location.Speed.HasValue;

    /// <summary>
    /// Gets the speed text.
    /// </summary>
    public string SpeedText => _location.Speed.HasValue
        ? $"{_location.Speed.Value * 3.6:F1} km/h"
        : "N/A";

    /// <summary>
    /// Gets whether altitude is available.
    /// </summary>
    public bool HasAltitude => _location.Altitude.HasValue;

    /// <summary>
    /// Gets the altitude text.
    /// </summary>
    public string AltitudeText => _location.Altitude.HasValue
        ? $"{_location.Altitude.Value:F0}m"
        : "N/A";

    /// <summary>
    /// Gets whether notes contain actual visible content.
    /// Returns false for empty notes or Quill's empty markup (e.g., &lt;p&gt;&lt;br&gt;&lt;/p&gt;).
    /// </summary>
    public bool HasNotes
    {
        get
        {
            if (string.IsNullOrEmpty(_location.Notes))
                return false;

            // Strip HTML tags and check for actual text content
            var plainText = Regex.Replace(_location.Notes, "<[^>]+>", " ");
            var hasText = !string.IsNullOrWhiteSpace(plainText);

            // Also check for images (content even without text)
            var hasImages = Regex.IsMatch(_location.Notes, @"<img\s", RegexOptions.IgnoreCase);

            return hasText || hasImages;
        }
    }

    /// <summary>
    /// Gets the raw notes HTML.
    /// </summary>
    public string? Notes => _location.Notes;

    /// <summary>
    /// Gets the notes HTML source for WebView.
    /// </summary>
    public HtmlWebViewSource? NotesHtmlSource
    {
        get
        {
            if (!HasNotes)
                return null;

            // Convert images to proxy URLs for WebView display
            var notesContent = ImageProxyHelper.ConvertImagesToProxyUrls(
                _location.Notes,
                _serverUrl);

            // Wrap notes in basic HTML structure
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            font-size: 17px;
            line-height: 1.5;
            padding: 8px;
            margin: 0;
            color: #333;
        }}
        img {{ max-width: 100%; height: auto; }}
    </style>
</head>
<body>
    {notesContent}
</body>
</html>";
            return new HtmlWebViewSource { Html = html };
        }
    }
}
