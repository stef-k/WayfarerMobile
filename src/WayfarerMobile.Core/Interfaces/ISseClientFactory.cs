namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Factory for creating SSE client instances.
/// Each subscription requires its own client since SSE connections are long-lived.
/// </summary>
public interface ISseClientFactory
{
    /// <summary>
    /// Creates a new SSE client instance.
    /// </summary>
    /// <returns>A new <see cref="ISseClient"/> instance.</returns>
    ISseClient Create();
}
