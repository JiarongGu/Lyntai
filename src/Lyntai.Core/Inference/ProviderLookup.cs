namespace Lyntai.Inference;

/// <summary>"Find a provider by id" in the one form every router states: case-insensitive, and the FIRST
/// registration wins on a duplicate id. Nothing refuses a duplicate at registration (two default-id ONNX
/// registrations share <c>"onnx"</c>), so a lookup built another way — <c>ToDictionary</c> throws on one —
/// fails where the routers do not.</summary>
internal static class ProviderLookup
{
    /// <summary>Every provider by id, first registration winning.</summary>
    internal static Dictionary<string, IModelProvider> ById(IEnumerable<IModelProvider> providers)
    {
        var map = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers) map.TryAdd(p.Id, p);
        return map;
    }

    /// <summary>The first provider registered under <paramref name="id"/>, or null.</summary>
    internal static IModelProvider? Find(IEnumerable<IModelProvider> providers, string id) =>
        providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
