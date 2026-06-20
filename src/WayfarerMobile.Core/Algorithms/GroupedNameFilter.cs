namespace WayfarerMobile.Core.Algorithms;

/// <summary>
/// Represents named items that should remain grouped while filtering.
/// </summary>
public sealed record GroupedItems<T>(string Name, IReadOnlyList<T> Items);

/// <summary>
/// Filters grouped items by a case-insensitive name match.
/// </summary>
public static class GroupedNameFilter
{
    /// <summary>
    /// Filters items while preserving group order and omitting empty groups.
    /// </summary>
    public static IReadOnlyList<GroupedItems<T>> Filter<T>(
        IEnumerable<GroupedItems<T>> groups,
        string? query,
        Func<T, string?> nameSelector)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(nameSelector);

        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return groups.ToList();
        }

        return groups
            .Select(group => new GroupedItems<T>(
                group.Name,
                group.Items
                    .Where(item => nameSelector(item)?.Contains(
                        normalizedQuery,
                        StringComparison.OrdinalIgnoreCase) == true)
                    .ToList()))
            .Where(group => group.Items.Count > 0)
            .ToList();
    }
}
