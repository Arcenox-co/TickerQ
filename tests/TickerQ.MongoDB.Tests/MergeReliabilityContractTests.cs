using System;
using System.Collections.Generic;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;

namespace TickerQ.MongoDB.Tests;

/// <summary>
/// Pure projection/mapping unit tests for the Mongo provider that need no live store — the
/// deep child-chain hydration and the queue-projection expression trees. The provider's
/// lease/recovery capability and its runtime reliability behavior are covered as real
/// behavior by <see cref="MongoProviderReliabilityContractTests"/> (the shared contract),
/// which replaced the former reflection-only "SupportsLeaseBasedRecovery" and
/// "overrides method X" assertions.
/// </summary>
public class MergeReliabilityContractTests
{
    private static TimeTickerEntity ChildDefinition(Guid parentId)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "child-fn",
            ParentId = parentId,
            ExecutionTime = null, // child definitions carry no ExecutionTime
        };

    [Fact]
    public void BuildQueuedEntity_HydratesRootChildGrandchildAndDeeper()
    {
        // One chain four levels deep: root → child → grandchild → great-grandchild.
        // The one-level lookup this replaces truncated everything past the child.
        var root = new TimeTickerEntity { Id = Guid.NewGuid(), Function = "root-fn", ExecutionTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc) };
        var child = ChildDefinition(root.Id);
        var grandchild = ChildDefinition(child.Id);
        var greatGrandchild = ChildDefinition(grandchild.Id);

        var byParent = TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>
            .BuildChildLookup(new[] { child, grandchild, greatGrandchild });
        var projected = TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>
            .BuildQueuedEntity(root, byParent);

        var pChild = Assert.Single(projected.Children!);
        Assert.Equal(child.Id, pChild.Id);
        Assert.Equal(root.Id, pChild.ParentId);
        var pGrandchild = Assert.Single(pChild.Children!);
        Assert.Equal(grandchild.Id, pGrandchild.Id);
        Assert.Equal(child.Id, pGrandchild.ParentId);
        var pGreatGrandchild = Assert.Single(pGrandchild.Children!);
        Assert.Equal(greatGrandchild.Id, pGreatGrandchild.Id);
        Assert.Equal(grandchild.Id, pGreatGrandchild.ParentId);
    }

    [Fact]
    public void BuildQueuedEntity_TerminatesOnCyclicChildData()
    {
        // Malformed data: grandchild points back up the chain, forming a cycle the
        // schema should prevent. Hydration must terminate (visited-set guard) and
        // still surface the legitimate chain rather than looping forever.
        var root = new TimeTickerEntity { Id = Guid.NewGuid(), Function = "root-fn", ExecutionTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc) };
        var child = ChildDefinition(root.Id);
        var grandchild = ChildDefinition(child.Id);

        var byParent = TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>
            .BuildChildLookup(new[] { child, grandchild });
        // Inject a back-edge grandchild → child (same id as child) so an unguarded
        // deep walk would revisit child forever.
        var backEdge = ChildDefinition(grandchild.Id);
        backEdge.Id = child.Id;
        byParent[grandchild.Id] = new List<TimeTickerEntity> { backEdge };

        var projected = TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>
            .BuildQueuedEntity(root, byParent);

        var pChild = Assert.Single(projected.Children!);
        Assert.Equal(child.Id, pChild.Id);
        Assert.Equal(grandchild.Id, Assert.Single(pChild.Children!).Id);
    }

    [Fact]
    public void PublicMappings_PreserveConfiguredTimeouts()
    {
        var cron = new CronTickerEntity { TimeoutSeconds = 17 };
        var projectedCron = MappingExtensions.ForCronTickerExpressions<CronTickerEntity>().Compile()(cron);

        var token = Guid.NewGuid();
        var grandchild = new TimeTickerEntity { TimeoutSeconds = 3 };
        var child = new TimeTickerEntity { TimeoutSeconds = 5, Children = [grandchild] };
        var root = new TimeTickerEntity { TimeoutSeconds = 11, AcquisitionToken = token, Children = [child] };
        var projectedTime = MappingExtensions.ForQueueTimeTickers<TimeTickerEntity>().Compile()(root);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity> { AcquisitionToken = token, CronTicker = cron };
        var projectedOccurrence = MappingExtensions
            .ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<CronTickerEntity>, CronTickerEntity>()
            .Compile()(occurrence);
        var projectedLatestOccurrence = MappingExtensions
            .ForLatestQueuedCronTickerOccurrence<CronTickerOccurrenceEntity<CronTickerEntity>, CronTickerEntity>()
            .Compile()(occurrence);

        Assert.Equal(17, projectedCron.TimeoutSeconds);
        Assert.Equal(11, projectedTime.TimeoutSeconds);
        Assert.Equal(5, Assert.Single(projectedTime.Children!).TimeoutSeconds);
        Assert.Equal(3, Assert.Single(Assert.Single(projectedTime.Children!).Children!).TimeoutSeconds);
        Assert.Equal(token, projectedTime.AcquisitionToken);
        Assert.Equal(17, projectedOccurrence.CronTicker.TimeoutSeconds);
        Assert.Equal(token, projectedOccurrence.AcquisitionToken);
        Assert.Equal(17, projectedLatestOccurrence.CronTicker.TimeoutSeconds);
        Assert.Equal(token, projectedLatestOccurrence.AcquisitionToken);
    }
}
