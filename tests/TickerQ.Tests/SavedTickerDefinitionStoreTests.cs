using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class SavedTickerDefinitionStoreTests : IDisposable
{
    private sealed class Job : TimeTickerEntity<Job> { }
    private sealed class Cron : CronTickerEntity { }

    private readonly ITickerDefinitionStore _store;
    private readonly DateTime _now = new(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

    public SavedTickerDefinitionStoreTests()
    {
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var services = new ServiceCollection()
            .AddSingleton(clock)
            .AddSingleton(new SchedulerOptionsBuilder())
            .BuildServiceProvider();
        _store = new TickerInMemoryPersistenceProvider<Job, Cron>(services);
    }

    [Fact]
    public async Task Draft_is_not_a_ticker_and_activation_claim_is_single_winner()
    {
        var draft = await _store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Draft,
            Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = "Invoice workflow",
            DefinitionJson = "{\"root\":{\"function\":\"Invoices.Create\"}}",
            IncludesRequestData = false,
        });

        Assert.Equal(1, draft.Version);
        Assert.Single(await _store.ListDefinitionsAsync(SavedTickerDefinitionKind.Draft));
        Assert.Empty(await _store.ListDefinitionsAsync(SavedTickerDefinitionKind.Template));

        var rootId = Guid.NewGuid();
        var claims = await Task.WhenAll(
            _store.TryClaimDraftActivationAsync(draft.Id, draft.Version, Guid.NewGuid(), rootId, _now),
            _store.TryClaimDraftActivationAsync(draft.Id, draft.Version, Guid.NewGuid(), rootId, _now));
        var winner = Assert.Single(claims, row => row != null);
        Assert.NotNull(winner.ActivationToken);
        Assert.Equal(rootId, winner.PendingRootTickerId);

        Assert.True(await _store.CompleteDraftActivationAsync(
            draft.Id, winner.ActivationToken!.Value, rootId, _now.AddSeconds(1)));
        var activated = await _store.GetDefinitionAsync(draft.Id);
        Assert.Equal(rootId, activated.ActivatedRootTickerId);
        Assert.Null(activated.ActivationToken);
        Assert.Null(await _store.UpdateDefinitionAsync(new SavedTickerDefinition
        {
            Id = activated.Id,
            Kind = activated.Kind,
            Target = activated.Target,
            Name = "Changed after publish",
            DefinitionJson = activated.DefinitionJson,
        }, activated.Version));
    }

    [Fact]
    public async Task Update_and_delete_require_the_current_version()
    {
        var template = await _store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template,
            Target = SavedTickerDefinitionTarget.CronTicker,
            Name = "Daily close",
            DefinitionJson = "{}",
        });
        var changed = await _store.UpdateDefinitionAsync(new SavedTickerDefinition
        {
            Id = template.Id,
            Kind = template.Kind,
            Target = template.Target,
            Name = "Daily close v2",
            DefinitionJson = "{\"schema\":1}",
        }, template.Version);

        Assert.NotNull(changed);
        Assert.Equal(2, changed.Version);
        Assert.False(await _store.DeleteDefinitionAsync(changed.Id, template.Version));
        Assert.True(await _store.DeleteDefinitionAsync(changed.Id, changed.Version));
        Assert.Null(await _store.GetDefinitionAsync(changed.Id));
    }

    [Fact]
    public async Task Template_names_are_unique_per_target_and_listed_by_creation_date()
    {
        var older = await _store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template,
            Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = "Daily  Close",
            DefinitionJson = "{}",
            CreatedAtUtc = _now.AddDays(-1),
        });
        var newer = await _store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template,
            Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = "Invoice import",
            DefinitionJson = "{}",
            CreatedAtUtc = _now,
        });

        await Assert.ThrowsAsync<DuplicateSavedTickerDefinitionNameException>(() =>
            _store.CreateDefinitionAsync(new SavedTickerDefinition
            {
                Kind = SavedTickerDefinitionKind.Template,
                Target = SavedTickerDefinitionTarget.TimeTicker,
                Name = " daily\tclose ",
                DefinitionJson = "{}",
            }));
        await Assert.ThrowsAsync<DuplicateSavedTickerDefinitionNameException>(() =>
            _store.UpdateDefinitionAsync(new SavedTickerDefinition
            {
                Id = newer.Id,
                Kind = SavedTickerDefinitionKind.Template,
                Target = SavedTickerDefinitionTarget.TimeTicker,
                Name = "DAILY CLOSE",
                DefinitionJson = "{}",
            }, newer.Version));

        await _store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template,
            Target = SavedTickerDefinitionTarget.CronTicker,
            Name = "Daily Close",
            DefinitionJson = "{}",
        });
        await _store.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Draft,
            Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = "Daily Close",
            DefinitionJson = "{}",
        });

        var rows = await _store.ListDefinitionsAsync(
            SavedTickerDefinitionKind.Template, SavedTickerDefinitionTarget.TimeTicker);
        Assert.Equal(new[] { newer.Id, older.Id }, rows.Select(x => x.Id));
    }

    public void Dispose() => TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
}
