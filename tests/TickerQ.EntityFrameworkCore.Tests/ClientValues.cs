namespace TickerQ.EntityFrameworkCore.Tests;

/// <summary>
/// Values for ExecuteUpdate setters that must be computed in .NET. EF Core 10 evaluates setter values
/// before translation, but EF Core 8 and 9 receive an expression tree and translate Guid.NewGuid() and
/// DateTime.UtcNow to SQL (failing, or using the database clock). EF evaluates other methods client-side.
/// </summary>
internal static class ClientValues
{
    public static Guid NewGuid() => Guid.NewGuid();
    public static DateTime UtcNow => DateTime.UtcNow;
}
