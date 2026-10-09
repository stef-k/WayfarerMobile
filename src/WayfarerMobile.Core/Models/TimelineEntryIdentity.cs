namespace WayfarerMobile.Core.Models;

/// <summary>
/// Identifies the originating Timeline record independently of numeric display IDs.
/// Local identities must be revalidated against the same SQLite row before mutation.
/// Server identities may be created only from an actual server response.
/// </summary>
public sealed record TimelineEntryIdentity
{
    private TimelineEntryIdentity(int? localEntryId, int? serverId)
    {
        LocalEntryId = localEntryId;
        ServerId = serverId;
    }

    /// <summary>Gets the originating SQLite record ID, when selected from local storage.</summary>
    public int? LocalEntryId { get; }

    /// <summary>Gets the proven server ID, or null for local-only or unknown records.</summary>
    public int? ServerId { get; }

    /// <summary>Gets whether this identity carries server linkage, subject to source revalidation.</summary>
    public bool CanMutate => ServerId is > 0;

    /// <summary>Gets the identity used when provenance is unavailable.</summary>
    public static TimelineEntryIdentity Unknown { get; } = new(null, null);

    /// <summary>Explains the read-only behavior of entries without server linkage.</summary>
    public const string ReadOnlyExplanation = "Stored only on this device; editing and deletion are unavailable.";

    /// <summary>Creates an identity from the selected SQLite row, never from a display fallback.</summary>
    public static TimelineEntryIdentity FromLocal(int localEntryId, int? serverId, bool serverLinkageConfirmed) =>
        localEntryId > 0 ? new(localEntryId, serverLinkageConfirmed && serverId is > 0 ? serverId : null) : Unknown;

    /// <summary>Creates an identity from a location returned by the server API.</summary>
    public static TimelineEntryIdentity FromServer(int serverId) =>
        serverId > 0 ? new(null, serverId) : Unknown;

    /// <summary>
    /// Matches a display selection after reload. API-origin selections may gain a local cache copy
    /// of the same proven server record; local selections require their exact original row and linkage.
    /// This presentation match does not replace mutation-source revalidation.
    /// </summary>
    public bool MatchesSelection(TimelineEntryIdentity candidate) =>
        this != Unknown && (LocalEntryId.HasValue
            ? this == candidate
            : CanMutate && candidate.ServerId == ServerId);
}
