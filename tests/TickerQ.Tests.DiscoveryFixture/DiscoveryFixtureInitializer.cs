using System.Runtime.CompilerServices;

namespace TickerQ.Tests.DiscoveryFixture;

/// <summary>
/// Mirrors the module initializer TickerQ's source generator emits. It records that it ran in
/// AppDomain data so tests can observe it without touching a type in this assembly, which would
/// itself trigger the initializer.
/// </summary>
internal static class DiscoveryFixtureInitializer
{
    public const string MarkerKey = "TickerQ.Tests.DiscoveryFixture.Initialized";

    [ModuleInitializer]
    internal static void Initialize() => AppDomain.CurrentDomain.SetData(MarkerKey, true);
}
