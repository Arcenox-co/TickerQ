using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.MongoDB.Tests;

/// <summary>
/// The three reads a promise check makes between full reloads must each be served by an index, or a busy
/// collection would be scanned every check. Asks the server for its plan rather than trusting the index list.
/// </summary>
[Collection("Mongo")]
public sealed class MongoPromiseDeltaReadPlanTests(MongoTestFixture fixture)
{
    [Fact]
    public async Task PromiseDeltaReads_AreServedByAnIndex_ForPendingJobsAndFinishedRootsAndSteps()
    {
        await fixture.DropAllAsync();
        var function = "promised";
        var root = new TimeTickerEntity { Id = Guid.NewGuid(), Function = function, ExecutionTime = fixture.FixedNow, Status = TickerStatus.Done, ExecutedAt = fixture.FixedNow };
        var step = new TimeTickerEntity { Id = Guid.NewGuid(), Function = function, Status = TickerStatus.Done, ExecutedAt = fixture.FixedNow };
        root.Children.Add(step);
        await fixture.Provider.AddTimeTickers([root, new TimeTickerEntity { Id = Guid.NewGuid(), Function = function, ExecutionTime = fixture.FixedNow.AddHours(1) }]);
        var partition = (await fixture.Database.GetCollection<BsonDocument>(fixture.TimeTickers.CollectionNamespace.CollectionName)
            .Find(new BsonDocument()).FirstAsync())["ApplicationNamespaceKey"].AsString;
        var since = fixture.FixedNow.AddMinutes(-30);

        Expression<Func<TimeTickerEntity, bool>> pending = x => x.ApplicationNamespaceKey == partition && x.Function == function &&
            x.OriginCronTickerId == null &&
            (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued || x.Status == TickerStatus.InProgress);
        Expression<Func<TimeTickerEntity, bool>> finishedRoots = x => x.ApplicationNamespaceKey == partition && x.Function == function &&
            x.OriginCronTickerId == null && x.ParentId == null && x.ExecutedAt >= since &&
            (x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone || x.Status == TickerStatus.Failed ||
             x.Status == TickerStatus.Cancelled || x.Status == TickerStatus.Skipped);
        Expression<Func<TimeTickerEntity, bool>> finishedSteps = x => x.ApplicationNamespaceKey == partition && x.Function == function &&
            x.OriginCronTickerId == null && x.ParentId != null && x.ExecutedAt >= since &&
            (x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone || x.Status == TickerStatus.Failed ||
             x.Status == TickerStatus.Cancelled || x.Status == TickerStatus.Skipped);

        foreach (var (name, filter) in new[] { ("pending", pending), ("finished roots", finishedRoots), ("finished steps", finishedSteps) })
        {
            var plan = await WinningPlanAsync(filter);
            Assert.True(plan.Contains("IXSCAN") && !plan.Contains("COLLSCAN"), $"{name} is not served by an index:\n{plan}");
        }
    }

    private async Task<string> WinningPlanAsync(Expression<Func<TimeTickerEntity, bool>> predicate)
    {
        var collection = fixture.TimeTickers;
        var filter = Builders<TimeTickerEntity>.Filter.Where(predicate)
            .Render(new RenderArgs<TimeTickerEntity>(collection.DocumentSerializer, collection.Settings.SerializerRegistry));
        var explain = await fixture.Database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "find", collection.CollectionNamespace.CollectionName },
                    { "filter", filter },
                    { "sort", new BsonDocument("CreatedAt", -1) },
                    { "limit", 50_001 },
                } },
            { "verbosity", "queryPlanner" },
        });
        return explain["queryPlanner"]["winningPlan"].ToJson();
    }
}
