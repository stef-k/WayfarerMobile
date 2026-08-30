using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>Attaches the current server-owned Segment profile identity without persisting it.</summary>
public static class HostedSegmentProfileIdentity
{
    private sealed class Holder { public Guid? Value { get; set; } }
    private static readonly ConditionalWeakTable<TripSegment, Holder> Values = new();

    public static Guid? Get(TripSegment? segment) =>
        segment != null && Values.TryGetValue(segment, out var holder) ? holder.Value : null;

    public static void Configure(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(TripSegment)) return;
        var property = typeInfo.CreateJsonPropertyInfo(typeof(Guid?), "transportProfileId");
        property.Get = value => Get((TripSegment)value);
        property.Set = (value, profileId) => Values.GetOrCreateValue((TripSegment)value).Value = (Guid?)profileId;
        typeInfo.Properties.Add(property);
    }
}
