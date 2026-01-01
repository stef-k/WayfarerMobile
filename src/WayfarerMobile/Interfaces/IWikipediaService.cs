namespace WayfarerMobile.Interfaces;

/// <summary>
/// Service for searching Wikipedia articles by geographic location.
/// Uses the MediaWiki Geosearch API to find articles near coordinates.
/// </summary>
public interface IWikipediaService
{
    /// <summary>
    /// Searches for a Wikipedia article near the given coordinates.
    /// </summary>
    /// <param name="latitude">Latitude coordinate.</param>
    /// <param name="longitude">Longitude coordinate.</param>
    /// <returns>Wikipedia article URL if found, null otherwise.</returns>
    Task<WikipediaSearchResult?> SearchNearbyAsync(double latitude, double longitude);

    /// <summary>
    /// Opens a Wikipedia article in the system browser.
    /// </summary>
    /// <param name="latitude">Latitude coordinate.</param>
    /// <param name="longitude">Longitude coordinate.</param>
    /// <returns>True if article was found and opened, false otherwise.</returns>
    Task<bool> OpenNearbyArticleAsync(double latitude, double longitude);
}

/// <summary>
/// Result of a Wikipedia geosearch query.
/// </summary>
public class WikipediaSearchResult
{
    /// <summary>
    /// Gets or sets the article title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Wikipedia page ID.
    /// </summary>
    public int PageId { get; set; }

    /// <summary>
    /// Gets or sets the distance in meters from the search coordinates.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Gets the Wikipedia article URL.
    /// </summary>
    public string Url => $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(Title.Replace(' ', '_'))}";
}
