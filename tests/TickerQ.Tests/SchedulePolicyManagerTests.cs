using NSubstitute;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class SchedulePolicyManagerTests
{
    private readonly MemoryStore _store = new();
    private readonly ITickerQHostScheduler _scheduler = Substitute.For<ITickerQHostScheduler>();

    [Fact]
    public async Task Names_Are_Normalized_For_Uniqueness_And_Cannot_Be_Renamed()
    {
        var manager = CreateManager();
        var created = await manager.CreateAsync(SchedulePolicy.Create(" Business hours ").Build());

        await Assert.ThrowsAsync<DuplicateSchedulePolicyNameException>(() =>
            manager.CreateAsync(SchedulePolicy.Create("business   HOURS").Build()));

        var renamed = created.Copy();
        renamed.Name = "A different policy";
        await Assert.ThrowsAsync<TickerValidatorException>(() =>
            manager.UpdateAsync(renamed, created.Version));
        _scheduler.Received(1).Restart();
    }

    [Fact]
    public async Task Composition_Rejects_Missing_References_And_Cycles()
    {
        var manager = CreateManager();
        await Assert.ThrowsAsync<TickerValidatorException>(() => manager.CreateAsync(
            SchedulePolicy.Create("Orders").Require("Missing").Build()));

        var basePolicy = await manager.CreateAsync(SchedulePolicy.Create("Base").Build());
        var dependent = await manager.CreateAsync(
            SchedulePolicy.Create("Dependent").Require("Base").Build());
        var cyclic = basePolicy.Copy();
        cyclic.RequiredPolicyNames.Add(dependent.Name);

        var error = await Assert.ThrowsAsync<TickerValidatorException>(() =>
            manager.UpdateAsync(cyclic, basePolicy.Version));
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validation_Is_Provider_Neutral_And_Rejects_Contradictory_Composition()
    {
        var manager = CreateManager();
        await manager.CreateAsync(SchedulePolicy.Create("Shared").Build());

        var contradictory = SchedulePolicy.Create("Orders")
            .Require("Shared")
            .ExceptWhen("shared")
            .Build();
        await Assert.ThrowsAsync<TickerValidatorException>(() => manager.CreateAsync(contradictory));

        var longDescription = SchedulePolicy.Create("Description").Build();
        longDescription.Description = new string('x', 2001);
        await Assert.ThrowsAsync<TickerValidatorException>(() => manager.CreateAsync(longDescription));

        var normalized = await manager.CreateAsync(SchedulePolicy.Create("UTC default")
            .InTimeZone("  ")
            .Build());
        Assert.Equal("UTC", normalized.TimeZoneId);
    }

    [Fact]
    public async Task Optimistic_Update_And_Delete_Do_Not_Overwrite_A_Newer_Version()
    {
        var manager = CreateManager();
        var created = await manager.CreateAsync(SchedulePolicy.Create("Window").Build());
        var update = created.Copy();
        update.Description = "first";
        var current = await manager.UpdateAsync(update, created.Version);
        Assert.Equal(2, current.Version);

        var stale = current.Copy();
        stale.Description = "stale";
        Assert.Null(await manager.UpdateAsync(stale, created.Version));
        Assert.False(await manager.DeleteAsync(current.Id, created.Version));
        Assert.True(await manager.DeleteAsync(current.Id, current.Version));
    }

    [Fact]
    public async Task Delete_Is_Blocked_While_A_Ticker_Or_Policy_References_The_Name()
    {
        var manager = CreateManager();
        var shared = await manager.CreateAsync(SchedulePolicy.Create("Shared").Build());
        await manager.CreateAsync(SchedulePolicy.Create("Orders").Require("Shared").Build());

        await Assert.ThrowsAsync<TickerValidatorException>(() =>
            manager.DeleteAsync(shared.Id, shared.Version));

        _store.InUse.Add(SchedulePolicyNames.Normalize("Shared"));
        var orders = (await manager.ListAsync()).Single(x => x.Name == "Orders");
        Assert.True(await manager.DeleteAsync(orders.Id, orders.Version));
        await Assert.ThrowsAsync<TickerValidatorException>(() =>
            manager.DeleteAsync(shared.Id, shared.Version));
    }

    private SchedulePolicyManager CreateManager()
        => new(_store, new SchedulePolicyEvaluator(), _scheduler);

    private sealed class MemoryStore : ISchedulePolicyStore
    {
        private readonly Dictionary<Guid, SchedulePolicy> _items = new();
        public HashSet<string> InUse { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<SchedulePolicy>> ListSchedulePoliciesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SchedulePolicy>>(_items.Values.Select(x => x.Copy()).ToArray());

        public Task<SchedulePolicy> GetSchedulePolicyAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.TryGetValue(id, out var value) ? value.Copy() : null);

        public Task<SchedulePolicy> GetSchedulePolicyByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            var key = SchedulePolicyNames.Normalize(name);
            return Task.FromResult(_items.Values.FirstOrDefault(x =>
                SchedulePolicyNames.Normalize(x.Name) == key)?.Copy());
        }

        public Task<SchedulePolicy> CreateSchedulePolicyAsync(SchedulePolicy policy,
            CancellationToken cancellationToken = default)
        {
            if (_items.Values.Any(x => SchedulePolicyNames.Normalize(x.Name) ==
                                       SchedulePolicyNames.Normalize(policy.Name)))
                throw new DuplicateSchedulePolicyNameException(policy.Name);
            var row = policy.Copy();
            row.Id = row.Id == Guid.Empty ? Guid.NewGuid() : row.Id;
            row.NameKey = SchedulePolicyNames.CreateKey(row.Name);
            row.Version = 1;
            row.CreatedAtUtc = row.UpdatedAtUtc = DateTime.UtcNow;
            _items.Add(row.Id, row);
            return Task.FromResult(row.Copy());
        }

        public Task<SchedulePolicy> UpdateSchedulePolicyAsync(SchedulePolicy policy, long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            if (!_items.TryGetValue(policy.Id, out var current) || current.Version != expectedVersion)
                return Task.FromResult<SchedulePolicy>(null);
            var row = policy.Copy();
            row.Version = expectedVersion + 1;
            row.CreatedAtUtc = current.CreatedAtUtc;
            row.UpdatedAtUtc = DateTime.UtcNow;
            _items[row.Id] = row;
            return Task.FromResult(row.Copy());
        }

        public Task<bool> DeleteSchedulePolicyAsync(Guid id, long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            if (!_items.TryGetValue(id, out var current) || current.Version != expectedVersion)
                return Task.FromResult(false);
            return Task.FromResult(_items.Remove(id));
        }

        public Task<bool> IsSchedulePolicyInUseAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(InUse.Contains(SchedulePolicyNames.Normalize(name)));
    }
}
