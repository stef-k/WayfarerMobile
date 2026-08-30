using System.Text.Json;
using SQLite;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Services;

namespace WayfarerMobile.Data.Repositories;

public enum RetainedRouteSaveResult { Saved, Rejected, Superseded, Failed }

public sealed record RetainedRouteSelection(NavigationRoute Route, long RowId);

public sealed class RetainedWayfarerRouteRepository
{
    public const int MaximumRoutes = 200;
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);
    private readonly Func<Task<SQLiteAsyncConnection>> connectionFactory;
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    public RetainedWayfarerRouteRepository(SQLiteAsyncConnection connection)
        : this(() => Task.FromResult(connection)) { }

    public RetainedWayfarerRouteRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<RetainedRouteSaveResult> SaveAsync(HostedRouteCandidate candidate,
        Guid accountPartition, DateTimeOffset receiptTimeUtc, Func<bool> isCurrent,
        CancellationToken cancellationToken = default)
    {
        if (!TryPrepare(candidate, accountPartition, receiptTimeUtc, out var prepared))
            return RetainedRouteSaveResult.Rejected;

        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (!isCurrent()) return RetainedRouteSaveResult.Superseded;
            cancellationToken.ThrowIfCancellationRequested();
            var database = await connectionFactory();
            await database.RunInTransactionAsync(connection => SaveTransaction(
                connection, prepared!, cancellationToken));
            return RetainedRouteSaveResult.Saved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return RetainedRouteSaveResult.Failed;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task<RetainedRouteSelection?> SelectAsync(HostedRouteRequestContext context,
        Guid accountPartition, DateTimeOffset selectionTimeUtc, Func<bool> isCurrent,
        CancellationToken cancellationToken = default)
    {
        if (!TryCreateLookup(context, accountPartition, out var lookup)) return null;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (!isCurrent()) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var database = await connectionFactory();
            RetainedRouteSelection? result = null;
            await database.RunInTransactionAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = SelectTransaction(connection, lookup!, context.DestinationName,
                    RequireUtc(selectionTimeUtc), cancellationToken);
            });
            return result;
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = await connectionFactory();
            await database.RunInTransactionAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.DeleteAll<RetainedWayfarerRouteEntity>();
            });
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private static void SaveTransaction(SQLiteConnection connection, PreparedRoute prepared,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = FindExisting(connection, prepared.Entity);
        prepared.Entity.Id = existing?.Id ?? 0;
        if (existing == null) connection.Insert(prepared.Entity);
        else connection.Update(prepared.Entity);

        cancellationToken.ThrowIfCancellationRequested();
        connection.Execute(@"
            UPDATE RetainedWayfarerRoutes SET IsCurrentAuthority = 0
            WHERE AccountPartition = ? AND NormalizedServer = ? AND TransportProfileId = ?
              AND (Provider <> ? OR ProviderConfigurationId <> ? OR MappingIdentity <> ?
                   OR SelectedProfileAuthorityIdentity <> ?)",
            prepared.Entity.AccountPartition, prepared.Entity.NormalizedServer,
            prepared.Entity.TransportProfileId, prepared.Entity.Provider,
            prepared.Entity.ProviderConfigurationId, prepared.Entity.MappingIdentity,
            prepared.Entity.SelectedProfileAuthorityIdentity);
        connection.Execute(@"
            UPDATE RetainedWayfarerRoutes SET IsCurrentAuthority = 1
            WHERE AccountPartition = ? AND NormalizedServer = ? AND TransportProfileId = ?
              AND Provider = ? AND ProviderConfigurationId = ? AND MappingIdentity = ?
              AND SelectedProfileAuthorityIdentity = ?",
            prepared.Entity.AccountPartition, prepared.Entity.NormalizedServer,
            prepared.Entity.TransportProfileId, prepared.Entity.Provider,
            prepared.Entity.ProviderConfigurationId, prepared.Entity.MappingIdentity,
            prepared.Entity.SelectedProfileAuthorityIdentity);

        cancellationToken.ThrowIfCancellationRequested();
        var excess = Math.Max(0, connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM RetainedWayfarerRoutes") - MaximumRoutes);
        if (excess > 0)
        {
            connection.Execute(@"
                DELETE FROM RetainedWayfarerRoutes WHERE Id IN (
                    SELECT Id FROM RetainedWayfarerRoutes
                    ORDER BY LastUsedAtUnixMilliseconds, StoredAtUnixMilliseconds, Id
                    LIMIT ?)", excess);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static RetainedWayfarerRouteEntity? FindExisting(SQLiteConnection connection,
        RetainedWayfarerRouteEntity value) => connection.Query<RetainedWayfarerRouteEntity>(@"
            SELECT * FROM RetainedWayfarerRoutes
            WHERE AccountPartition = ? AND NormalizedServer = ? AND Provider = ?
              AND ProviderConfigurationId = ? AND MappingIdentity = ? AND TransportProfileId = ?
              AND SelectedProfileAuthorityIdentity = ? AND OriginLongitude = ? AND OriginLatitude = ?
              AND DestinationLongitude = ? AND DestinationLatitude = ? AND AnchorsKey = ?
            LIMIT 1", value.AccountPartition, value.NormalizedServer, value.Provider,
            value.ProviderConfigurationId, value.MappingIdentity, value.TransportProfileId,
            value.SelectedProfileAuthorityIdentity, value.OriginLongitude, value.OriginLatitude,
            value.DestinationLongitude, value.DestinationLatitude, value.AnchorsKey).SingleOrDefault();

    private static RetainedRouteSelection? SelectTransaction(SQLiteConnection connection, Lookup lookup,
        string destinationName, DateTimeOffset selectionTimeUtc, CancellationToken cancellationToken)
    {
        var rows = QueryRows(connection, lookup);
        if (lookup.TransportProfileId == null
            && rows.Select(row => row.TransportProfileId).Distinct(StringComparer.Ordinal).Take(2).Count() != 1)
            return null;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryBuildRoute(row, destinationName, selectionTimeUtc, out var route)) continue;
            connection.Execute("UPDATE RetainedWayfarerRoutes SET LastUsedAtUnixMilliseconds = ? WHERE Id = ?",
                selectionTimeUtc.ToUnixTimeMilliseconds(), row.Id);
            return new(route!, row.Id);
        }
        return null;
    }

    private static List<RetainedWayfarerRouteEntity> QueryRows(SQLiteConnection connection, Lookup lookup)
    {
        const string common = @"
            SELECT * FROM RetainedWayfarerRoutes
            WHERE AccountPartition = ? AND NormalizedServer = ? AND IsCurrentAuthority = 1
              AND OriginLongitude = ? AND OriginLatitude = ?
              AND DestinationLongitude = ? AND DestinationLatitude = ? AND AnchorsKey = ?";
        var arguments = new object[] { lookup.AccountPartition, lookup.NormalizedServer,
            lookup.Canonical[0], lookup.Canonical[1], lookup.Canonical[^2], lookup.Canonical[^1],
            lookup.AnchorsKey };
        var profileClause = lookup.TransportProfileId is not null
            ? " AND TransportProfileId = ?"
            : " AND (ModeKey = ? OR Category = ?)";
        var profileArguments = lookup.TransportProfileId is { } selectedProfileId
            ? new object[] { selectedProfileId.ToString("D") }
            : new object[] { lookup.ModeKey, lookup.Category };
        return connection.Query<RetainedWayfarerRouteEntity>(common + profileClause
            + " ORDER BY LastUsedAtUnixMilliseconds DESC, StoredAtUnixMilliseconds DESC, Id DESC",
            arguments.Concat(profileArguments).ToArray());
    }

    private static bool TryPrepare(HostedRouteCandidate candidate, Guid partition,
        DateTimeOffset receiptTimeUtc, out PreparedRoute? prepared)
    {
        prepared = null;
        if (partition == Guid.Empty || candidate.Metadata.StorageMode != "persistent"
            || candidate.SelectedProfileId == Guid.Empty
            || candidate.Metadata.ProviderConfigurationId == Guid.Empty
            || !HostedOpaqueIdentity.IsValid(candidate.SelectedProfileAuthorityIdentity)
            || !Bounded(candidate.Metadata.Provider, 100) || !Bounded(candidate.Metadata.MappingIdentity, 200)
            || !Bounded(candidate.Context.ModeKey, 100) || !Bounded(candidate.Context.Category, 100)
            || HostedRouteServerIdentity.Normalize(candidate.Context.NormalizedServer) != candidate.Context.NormalizedServer)
            return false;
        var receipt = RequireUtc(receiptTimeUtc);
        var generated = candidate.GeneratedAt.ToUniversalTime();
        if (generated > receipt + MaximumFutureSkew) return false;
        if (!TrySerialize(candidate, out var serialized)) return false;
        IReadOnlyList<int> canonical;
        try { canonical = HostedRouteIdentity.Canonicalize(RequestPoints(candidate.Context)); }
        catch (ArgumentOutOfRangeException) { return false; }
        var entity = new RetainedWayfarerRouteEntity
        {
            NormalizedServer = candidate.Context.NormalizedServer,
            AccountPartition = partition.ToString("D"),
            Provider = candidate.Metadata.Provider,
            ProviderConfigurationId = candidate.Metadata.ProviderConfigurationId.ToString("D"),
            MappingIdentity = candidate.Metadata.MappingIdentity,
            TransportProfileId = candidate.SelectedProfileId.ToString("D"),
            SelectedProfileAuthorityIdentity = candidate.SelectedProfileAuthorityIdentity,
            ModeKey = candidate.Context.ModeKey!,
            Category = candidate.Context.Category!,
            OriginLongitude = canonical[0], OriginLatitude = canonical[1],
            DestinationLongitude = canonical[^2], DestinationLatitude = canonical[^1],
            AnchorsKey = AnchorKey(canonical), GeometryJson = serialized!.Geometry,
            InstructionsJson = serialized.Instructions, AttributionJson = serialized.Attribution,
            DistanceMetres = candidate.Route.TotalDistanceMeters,
            DurationSeconds = candidate.Route.EstimatedDuration.TotalSeconds,
            StoredAtUnixMilliseconds = receipt.ToUnixTimeMilliseconds(),
            LastUsedAtUnixMilliseconds = receipt.ToUnixTimeMilliseconds(),
            GeneratedAtUnixMilliseconds = generated.ToUnixTimeMilliseconds(),
            StorageAuthority = "persistent", IsCurrentAuthority = true
        };
        prepared = new(entity);
        return true;
    }

    private static bool TrySerialize(HostedRouteCandidate candidate, out SerializedRoute? serialized)
    {
        serialized = null;
        var route = candidate.Route;
        if (route.Waypoints.Count is < 2 or > 10000 || route.Steps.Count > 1000
            || !FiniteNonNegative(route.TotalDistanceMeters)
            || !FiniteNonNegative(route.EstimatedDuration.TotalSeconds)
            || route.Waypoints.Any(point => !ValidCoordinate(point.Longitude, point.Latitude))
            || route.Steps.Any(step => !ValidStep(step, route.Waypoints.Count))
            || route.Attribution.Count is < 1 or > 10 || route.Attribution.Any(item => !ValidAttribution(item)))
            return false;
        var geometry = route.Waypoints.Select(point => new StoredCoordinate(point.Longitude, point.Latitude)).ToArray();
        var instructions = route.Steps.Select(step => new StoredInstruction(step.Instruction,
            step.ManeuverType, step.GeometryFromIndex, step.GeometryToIndex,
            step.DistanceMeters, step.DurationSeconds)).ToArray();
        serialized = new(JsonSerializer.Serialize(geometry), JsonSerializer.Serialize(instructions),
            JsonSerializer.Serialize(route.Attribution));
        return serialized.Geometry.Length <= 1_000_000 && serialized.Instructions.Length <= 600_000
            && serialized.Attribution.Length <= 10_000;
    }

    private static bool TryCreateLookup(HostedRouteRequestContext context, Guid partition, out Lookup? lookup)
    {
        lookup = null;
        var server = HostedRouteServerIdentity.Normalize(context.NormalizedServer);
        if (partition == Guid.Empty || server != context.NormalizedServer
            || !Bounded(context.ModeKey, 100) || !Bounded(context.Category, 100)) return false;
        try
        {
            var canonical = HostedRouteIdentity.Canonicalize(RequestPoints(context));
            lookup = new(partition.ToString("D"), server, context.SavedTransportProfileId,
                context.ModeKey!, context.Category!, canonical, AnchorKey(canonical));
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool TryBuildRoute(RetainedWayfarerRouteEntity row, string destinationName,
        DateTimeOffset selectionTimeUtc, out NavigationRoute? route)
    {
        route = null;
        try
        {
            var geometry = JsonSerializer.Deserialize<StoredCoordinate[]>(row.GeometryJson);
            var instructions = JsonSerializer.Deserialize<StoredInstruction[]>(row.InstructionsJson);
            var attribution = JsonSerializer.Deserialize<HostedRouteAttribution[]>(row.AttributionJson);
            if (geometry is not { Length: >= 2 and <= 10000 } || instructions is not { Length: <= 1000 }
                || attribution is not { Length: >= 1 and <= 10 }
                || geometry.Any(point => !ValidCoordinate(point.Longitude, point.Latitude))
                || instructions.Any(step => !ValidStoredStep(step, geometry.Length))
                || attribution.Any(item => !ValidAttribution(item))
                || !FiniteNonNegative(row.DistanceMetres) || !FiniteNonNegative(row.DurationSeconds)
                || row.StorageAuthority != "persistent" || !Guid.TryParse(row.TransportProfileId, out var profileId)
                || !Guid.TryParse(row.ProviderConfigurationId, out var configurationId)
                || !HostedOpaqueIdentity.IsValid(row.SelectedProfileAuthorityIdentity)) return false;
            var generated = DateTimeOffset.FromUnixTimeMilliseconds(row.GeneratedAtUnixMilliseconds);
            var age = selectionTimeUtc <= generated ? TimeSpan.Zero : selectionTimeUtc - generated;
            route = new NavigationRoute
            {
                Waypoints = geometry.Select((point, index) => new NavigationWaypoint
                {
                    Longitude = point.Longitude, Latitude = point.Latitude,
                    Name = index == geometry.Length - 1 ? destinationName : string.Empty
                }).ToList(),
                Steps = instructions.Select(step => new NavigationStep
                {
                    Instruction = step.Text, ManeuverType = step.Type,
                    GeometryFromIndex = step.FromIndex, GeometryToIndex = step.ToIndex,
                    Longitude = geometry[step.FromIndex].Longitude, Latitude = geometry[step.FromIndex].Latitude,
                    DistanceMeters = step.DistanceMetres, DurationSeconds = step.DurationSeconds
                }).ToList(),
                DestinationName = destinationName, TotalDistanceMeters = row.DistanceMetres,
                EstimatedDuration = TimeSpan.FromSeconds(row.DurationSeconds), IsDirectRoute = false,
                Attribution = attribution.ToList(),
                HostedProvenance = new(profileId, row.SelectedProfileAuthorityIdentity, row.Provider,
                    configurationId, row.MappingIdentity, row.StorageAuthority, generated)
                { IsRetained = true, Age = age }
            };
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static IEnumerable<HostedRouteCoordinate> RequestPoints(HostedRouteRequestContext context) =>
        new[] { context.Origin }.Concat(context.Anchors).Append(context.Destination);
    private static string AnchorKey(IReadOnlyList<int> canonical) =>
        JsonSerializer.Serialize(canonical.Skip(2).Take(canonical.Count - 4));
    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.ToUniversalTime();
    private static bool ValidCoordinate(double longitude, double latitude) => double.IsFinite(longitude)
        && double.IsFinite(latitude) && longitude is >= -180 and <= 180 && latitude is >= -90 and <= 90;
    private static bool FiniteNonNegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool ValidStep(NavigationStep value, int geometryCount) => Bounded(value.Instruction, 500)
        && Bounded(value.ManeuverType, 100) && value.GeometryFromIndex >= 0
        && value.GeometryToIndex >= value.GeometryFromIndex && value.GeometryToIndex < geometryCount
        && FiniteNonNegative(value.DistanceMeters) && FiniteNonNegative(value.DurationSeconds);
    private static bool ValidStoredStep(StoredInstruction value, int geometryCount) => Bounded(value.Text, 500)
        && Bounded(value.Type, 100) && value.FromIndex >= 0 && value.ToIndex >= value.FromIndex
        && value.ToIndex < geometryCount && FiniteNonNegative(value.DistanceMetres)
        && FiniteNonNegative(value.DurationSeconds);
    private static bool ValidAttribution(HostedRouteAttribution value) => Bounded(value.Text, 200)
        && Bounded(value.Url, 500) && Uri.TryCreate(value.Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
    private static bool Bounded(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum;

    private sealed record PreparedRoute(RetainedWayfarerRouteEntity Entity);
    private sealed record SerializedRoute(string Geometry, string Instructions, string Attribution);
    private sealed record StoredCoordinate(double Longitude, double Latitude);
    private sealed record StoredInstruction(string Text, string Type, int FromIndex, int ToIndex,
        double DistanceMetres, double DurationSeconds);
    private sealed record Lookup(string AccountPartition, string NormalizedServer, Guid? TransportProfileId,
        string ModeKey, string Category, IReadOnlyList<int> Canonical, string AnchorsKey);
}
