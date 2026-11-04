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

    /// <summary>
    /// Loads <paramref name="scriptName"/> after the shared fragments it calls, as one script. A
    /// fragment only defines local functions, so the scripts that share a rule keep one copy of it.
    /// </summary>
    internal static string Load(string scriptName, params string[] fragments)
    {
        var parts = new string[fragments.Length + 1];
        for (var i = 0; i < fragments.Length; i++) parts[i] = Load(fragments[i]);
        parts[^1] = Load(scriptName);
        return string.Join("\n", parts);
    }
}
