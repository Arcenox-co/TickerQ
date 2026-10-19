using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoMonitoringQueryTests(MongoTestFixture fixture)
{
    [Fact]
    public async Task IntegrationMonitoring_ProjectsInDatabase_WithoutDeserializingPayloadFields()
    {
        await fixture.DropAllAsync();
        var id = Guid.NewGuid();
        await fixture.Provider.AddTimeTickers([new TimeTickerEntity
        {
            Id = id, Function = "projection", Status = TickerStatus.Done,
            ExecutedAt = fixture.FixedNow, ElapsedTime = 123
        }]);
        // These stored fields cannot be deserialized into a full TimeTickerEntity.
        // A server-side metadata projection must never return them to the driver.
        var raw = fixture.Database.GetCollection<BsonDocument>(fixture.TimeTickers.CollectionNamespace.CollectionName);
        await raw.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("Request", new BsonDocument("ignored", true))
                .Set("ExceptionMessage", new BsonArray { 1, 2, 3 }));
        var snapshot = await fixture.Provider.ReadMonitoringSnapshotAsync("projection", fixture.FixedNow.AddHours(-1), fixture.FixedNow);
        var completed = Assert.Single(snapshot.Completed);
        Assert.Equal(id, completed.Id);
        Assert.Equal(123, completed.ElapsedMilliseconds);
    }
}
