using System.Collections.Concurrent;

public static class Preferences
{
    private static readonly ConcurrentDictionary<string, object?> Values = new();

    public static T Get<T>(string key, T defaultValue) =>
        Values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;

    public static void Set<T>(string key, T value) => Values[key] = value;

    public static void Remove(string key) => Values.TryRemove(key, out _);
}
