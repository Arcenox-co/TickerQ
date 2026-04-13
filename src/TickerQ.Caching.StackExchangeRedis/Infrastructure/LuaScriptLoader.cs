using System.IO;
using System.Reflection;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal static class LuaScriptLoader
{
    private static readonly Assembly Assembly = typeof(LuaScriptLoader).Assembly;

    /// <summary>
    /// Loads a Lua script from embedded resources as a raw string.
    /// Scripts use KEYS[]/ARGV[] notation for AOT compatibility (no reflection-based parameter mapping).
    /// </summary>
    internal static string Load(string scriptName)
    {
        var resourceName = $"TickerQ.Caching.StackExchangeRedis.Scripts.{scriptName}.lua";

        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded Lua script '{scriptName}' not found. Expected resource: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
