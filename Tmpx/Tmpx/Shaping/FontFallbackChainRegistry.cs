namespace Tmpx.Shaping;

public class FontFallbackChainRegistry(string defaultName)
{
    private readonly Dictionary<string, string[]> fallbackChains = new();
    private readonly string defaultName = defaultName.ToLowerInvariant().Trim();
    
    public void RegisterFallbackChain(string name, params IEnumerable<string> fallbackFamilies)
    {
        fallbackChains[name.ToLowerInvariant().Trim()] = fallbackFamilies.Select(x => x.Trim().ToLowerInvariant()).ToArray();
    }

    public IEnumerable<string> GetFontNames(string family)
    {
        family = family.ToLowerInvariant().Trim();
        if (family.EndsWith("sdf"))
            family = family[..^3].Trim();
        
        if (fallbackChains.TryGetValue(family, out var fallbackFamilies))
            return fallbackFamilies;
        
        if (fallbackChains.TryGetValue(defaultName, out var defaultFallbackFamilies))
            return defaultFallbackFamilies;
        
        throw new InvalidOperationException($"No fallback chain registered for family '{family}' and no default fallback chain registered with name '{defaultName}'.");
    }
}