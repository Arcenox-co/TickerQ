using NSubstitute;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public sealed class DashboardCronLockHolderTests
{
    [Fact]
    public async Task Occurrence_page_batches_root_holders_and_preserves_the_actual_trigger_lock()
    {
        var cron = new CronTickerEntity { Function = "Prepare" };
        var root = new TimeTickerEntity { OriginCronTickerId = cron.Id, LockHolder = "chain-worker" };
        var unrelated = new TimeTickerEntity { OriginCronTickerId = Guid.NewGuid(), LockHolder = "unrelated-worker" };
        var released = new CronTickerOccurrenceEntity<CronTickerEntity> { Id = Guid.NewGuid(), CronTickerId = cron.Id, ChainRootId = root.Id };
        var claimed = new CronTickerOccurrenceEntity<CronTickerEntity> { Id = Guid.NewGuid(), CronTickerId = cron.Id, ChainRootId = root.Id, LockHolder = "trigger-worker" };
        var missing = new CronTickerOccurrenceEntity<CronTickerEntity> { Id = Guid.NewGuid(), CronTickerId = cron.Id, ChainRootId = Guid.NewGuid() };
        var mismatch = new CronTickerOccurrenceEntity<CronTickerEntity> { Id = Guid.NewGuid(), CronTickerId = cron.Id, ChainRootId = unrelated.Id };
        var standalone = new CronTickerOccurrenceEntity<CronTickerEntity> { Id = Guid.NewGuid(), CronTickerId = cron.Id };
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var reads = 0;
        provider.GetCronTickerById(cron.Id, Arg.Any<CancellationToken>()).Returns(cron);
        provider.CronTickerOccurrencesQuery().Returns(_ => new InMemoryTickerQueryable<CronTickerOccurrenceEntity<CronTickerEntity>>(
            _ => Task.FromResult(new List<CronTickerOccurrenceEntity<CronTickerEntity>> { released, claimed, missing, mismatch, standalone })));
        provider.TimeTickersQuery().Returns(_ => new InMemoryTickerQueryable<TimeTickerEntity>(
            _ => { reads++; return Task.FromResult(new List<TimeTickerEntity> { root, unrelated }); }));
        var service = new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(provider);

        var page = await service.GetCronOccurrencesFlatAsync(cron.Id, new CronOccurrenceQueryFilter());

        Assert.Equal(2, reads); // one root batch and one descendant batch for all occurrences
        Assert.Equal("chain-worker", page.Items.Single(x => x.Id == released.Id).ChainLockHolder);
        Assert.Null(page.Items.Single(x => x.Id == released.Id).LockHolder);
        Assert.Equal("trigger-worker", page.Items.Single(x => x.Id == claimed.Id).LockHolder);
        Assert.All(page.Items.Where(x => x.Id != released.Id), item => Assert.Null(item.ChainLockHolder));
        Assert.Null(released.LockHolder);

        root.LockHolder = "replacement-worker";
        Assert.Equal("replacement-worker", (await service.GetCronOccurrenceByIdAsync(released.Id)).ChainLockHolder);
        root.LockHolder = null;
        Assert.Null((await service.GetCronOccurrenceByIdAsync(released.Id)).ChainLockHolder);
        Assert.Null(released.LockHolder);
    }
}
