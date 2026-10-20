using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Infrastructure;

namespace TickerQ.Tests;

public sealed class ClusterControlStoreTests
{
    [Fact]
    public async Task Dashboard_nodes_remove_build_metadata_from_the_display_version()
    {
        var options = Options("cluster-version-" + Guid.NewGuid().ToString("N"), "node-a");
        var provider = Provider(options);
        var now = DateTime.UtcNow;
        await provider.UpsertNodeAsync(new TickerQNodeRecord
        {
            NodeKey = TickerQClusterIdentity.NodeKey(options.ExecutionOwnerId),
            ExecutionOwnerId = options.ExecutionOwnerId,
            NodeIdentifier = options.NodeIdentifier,
            DisplayName = options.NodeIdentifier,
            Version = "10.5.0-rc.1+d9f321",
            StartedAtUtc = now,
            LastSeenAtUtc = now
        });

        var dashboard = new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(provider);
        Assert.Equal("10.5.0-rc.1", Assert.Single(await dashboard.GetNodesAsync()).Version);
    }

    [Fact]
    public async Task In_memory_nodes_and_commands_are_shared_within_one_application_namespace()
    {
        var namespaceName = "cluster-test-" + Guid.NewGuid().ToString("N");
        var firstOptions = Options(namespaceName, "node-a");
        var secondOptions = Options(namespaceName, "node-b");
        var first = Provider(firstOptions);
        var second = Provider(secondOptions);
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var targetNodeKey = TickerQClusterIdentity.NodeKey(secondOptions.ExecutionOwnerId);

        await first.UpsertNodeAsync(new TickerQNodeRecord
        {
            NodeKey = targetNodeKey,
            ExecutionOwnerId = secondOptions.ExecutionOwnerId,
            NodeIdentifier = secondOptions.NodeIdentifier,
            DisplayName = "Worker B",
            Version = "10.5.0-rc.1",
            StartedAtUtc = now,
            LastSeenAtUtc = now,
            IsRunning = true,
            MaxConcurrency = 4,
            FunctionCount = 7
        });

        var nodes = await second.GetNodesAsync();
        var node = Assert.Single(nodes);
        Assert.Equal("Worker B", node.DisplayName);
        Assert.Equal(7, node.FunctionCount);

        var command = new TickerQControlCommand
        {
            Id = Guid.NewGuid(),
            TargetNodeKey = targetNodeKey,
            TargetExecutionOwnerId = secondOptions.ExecutionOwnerId,
            TickerType = TickerType.TimeTicker,
            TickerId = Guid.NewGuid(),
            AcquisitionToken = Guid.NewGuid(),
            RequestedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(30),
            Status = TickerQControlCommandStatus.Pending
        };
        Assert.True(await first.RequestCancellationAsync(command));

        var pending = await second.GetPendingCommandsAsync(secondOptions.ExecutionOwnerId, 10);
        Assert.Equal(command.Id, Assert.Single(pending).Id);
        Assert.True(await second.CompleteCommandAsync(command.Id, TickerQControlCommandStatus.Completed,
            "signalled", now.AddSeconds(1)));
        Assert.Empty(await first.GetPendingCommandsAsync(secondOptions.ExecutionOwnerId, 10));
    }

    [Fact]
    public async Task In_memory_cluster_control_is_partitioned_by_application_namespace()
    {
        var firstOptions = Options("cluster-one-" + Guid.NewGuid().ToString("N"), "node-a");
        var secondOptions = Options("cluster-two-" + Guid.NewGuid().ToString("N"), "node-b");
        var first = Provider(firstOptions);
        var second = Provider(secondOptions);
        var now = DateTime.UtcNow;

        await first.UpsertNodeAsync(new TickerQNodeRecord
        {
            NodeKey = TickerQClusterIdentity.NodeKey(firstOptions.ExecutionOwnerId),
            ExecutionOwnerId = firstOptions.ExecutionOwnerId,
            NodeIdentifier = firstOptions.NodeIdentifier,
            DisplayName = firstOptions.NodeIdentifier,
            Version = "test",
            StartedAtUtc = now,
            LastSeenAtUtc = now
        });

        Assert.Empty(await second.GetNodesAsync());
    }

    private static SchedulerOptionsBuilder Options(string applicationNamespace, string nodeIdentifier)
    {
        var options = new SchedulerOptionsBuilder { NodeIdentifier = nodeIdentifier };
        options.BindRuntimeActivationScope(applicationNamespace, 1, true);
        return options;
    }

    private static ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider(
        SchedulerOptionsBuilder options)
    {
        var services = new ServiceCollection().AddSingleton(options).BuildServiceProvider();
        return new TickerInMemoryPersistenceProvider<TimeTickerEntity, CronTickerEntity>(services);
    }
}
