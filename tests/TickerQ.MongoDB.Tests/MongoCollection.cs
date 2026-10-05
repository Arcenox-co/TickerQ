namespace TickerQ.MongoDB.Tests;

/// <summary>
/// Shares one MongoDB container across every "Mongo" test class (the provider tests and
/// the shared reliability contract), so the expensive Testcontainers startup happens once
/// instead of per class. Each test still resets to a clean database in its own setup.
/// </summary>
[CollectionDefinition("Mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoTestFixture>
{
}
