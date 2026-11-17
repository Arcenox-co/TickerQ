using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Promises;

namespace TickerQ.Tests.Shared;

/// <summary>
/// The promise state store every provider must offer: one row per promise, as large as the state gets, changed only at
/// its current version, whose id includes the runtime partition so the same promise name in two applications never
/// shares a row (MongoDB keys documents by id alone).
/// </summary>
public abstract class PromiseStateStoreContract
{
    private const string PromiseName = "Paid orders are processed";

    /// <summary>A store bound to <paramref name="applicationNamespace"/>; stores for different namespaces share one database.</summary>
    protected abstract Task<ITickerPromiseStateStore> NewStoreAsync(string applicationNamespace);

    private readonly string _namespace = "promises-" + Guid.NewGuid().ToString("N")[..8];

    private static Guid RowId(string applicationNamespace, string name = PromiseName)
        => TickerPromiseIds.StateRow(new TickerQRuntimePartition(applicationNamespace).StorageKey, name);

    private static TickerPromiseStateRow Row(Guid id, string json, string name = PromiseName)
        => new() { Id = id, Name = name, StateJson = json };

    // Promise state holds up to 200 findings and 50 activity entries, so it is far larger than a template.
    private static string State(string marker, int findings = 200)
        => "{\"marker\":\"" + marker + "\",\"result\":{\"findings\":[" +
           string.Join(',', Enumerable.Range(0, findings).Select(i =>
               $"{{\"id\":\"{Guid.NewGuid()}\",\"kind\":\"NoJob\",\"key\":\"ORD-{i}\",\"reason\":\"No job was ever scheduled for it.\"}}")) +
           "]}}";

    [Fact]
    public async Task Promise_row_round_trips_whole_and_changes_only_at_its_current_version()
    {
        var store = await NewStoreAsync(_namespace);
        Assert.True(store.HasPromiseStateTable);
        var id = RowId(_namespace);
        var json = State("first");
        Assert.True(json.Length > 20_000);

        var created = await store.CreatePromiseStateAsync(Row(id, json));
        Assert.Equal((id, 1L, PromiseName), (created.Id, created.Version, created.Name));

        var read = await store.GetPromiseStateAsync(id);
        Assert.NotNull(read);
        Assert.Equal(json, read.StateJson);
        Assert.Equal(PromiseName, read.Name);
        Assert.NotEqual(default, read.CreatedAtUtc);

        var next = read.Copy();
        next.StateJson = State("second");
        var updated = await store.UpdatePromiseStateAsync(next, read.Version);
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Version);
        Assert.Equal(next.StateJson, (await store.GetPromiseStateAsync(id))!.StateJson);

        // A writer that read version 1 loses: promise state is merged by re-reading and retrying.
        var stale = read.Copy();
        stale.StateJson = State("stale", 1);
        Assert.Null(await store.UpdatePromiseStateAsync(stale, read.Version));
        Assert.Equal(next.StateJson, (await store.GetPromiseStateAsync(id))!.StateJson);

        Assert.Contains(await store.ListPromiseStatesAsync(), r => r.Id == id);
    }

    [Fact]
    public async Task Creating_an_existing_promise_row_fails_and_leaves_the_first_one()
    {
        // Two nodes may create a promise's row at once; the loser re-reads the winner's and changes that.
        var store = await NewStoreAsync(_namespace);
        var id = RowId(_namespace);
        var first = State("winner", 1);
        await store.CreatePromiseStateAsync(Row(id, first));

        await Assert.ThrowsAnyAsync<Exception>(() => store.CreatePromiseStateAsync(Row(id, State("loser", 1))));
        var read = await store.GetPromiseStateAsync(id);
        Assert.Equal(first, read!.StateJson);
        Assert.Equal(1, read.Version);
    }

    [Fact]
    public async Task Same_promise_name_in_two_namespaces_keeps_two_isolated_rows()
    {
        var otherNamespace = _namespace + "-other";
        var a = await NewStoreAsync(_namespace);
        var b = await NewStoreAsync(otherNamespace);
        var aId = RowId(_namespace);
        var bId = RowId(otherNamespace);
        Assert.NotEqual(aId, bId);

        await a.CreatePromiseStateAsync(Row(aId, State("a", 1)));
        await b.CreatePromiseStateAsync(Row(bId, State("b", 1)));

        Assert.Contains("\"a\"", (await a.GetPromiseStateAsync(aId))!.StateJson);
        Assert.Contains("\"b\"", (await b.GetPromiseStateAsync(bId))!.StateJson);
        Assert.Null(await a.GetPromiseStateAsync(bId));
        Assert.Null(await b.GetPromiseStateAsync(aId));
        Assert.Equal([aId], (await a.ListPromiseStatesAsync()).Select(r => r.Id));
        Assert.Equal([bId], (await b.ListPromiseStatesAsync()).Select(r => r.Id));

        var changed = (await a.GetPromiseStateAsync(aId))!.Copy();
        changed.StateJson = State("a2", 1);
        Assert.NotNull(await a.UpdatePromiseStateAsync(changed, 1));
        var untouched = await b.GetPromiseStateAsync(bId);
        Assert.Contains("\"b\"", untouched!.StateJson);
        Assert.Equal(1, untouched.Version);
    }

    [Fact]
    public async Task Promise_rows_live_apart_from_saved_definitions()
    {
        var store = await NewStoreAsync(_namespace);
        var promise = await store.CreatePromiseStateAsync(Row(RowId(_namespace), State("promise", 1)));
        var definitions = Assert.IsAssignableFrom<ITickerDefinitionStore>(store);
        var template = await definitions.CreateDefinitionAsync(new SavedTickerDefinition
        {
            Kind = SavedTickerDefinitionKind.Template,
            Target = SavedTickerDefinitionTarget.TimeTicker,
            Name = PromiseName,
            DefinitionJson = "{}",
        });

        Assert.NotEqual(promise.Id, template.Id);
        Assert.Equal([template.Id], (await definitions.ListDefinitionsAsync()).Select(r => r.Id));
        Assert.Equal([promise.Id], (await store.ListPromiseStatesAsync()).Select(r => r.Id));
    }
}
