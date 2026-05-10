using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.RemoteExecutor.WorkerStream;

/// <summary>
/// Non-generic indirection over <c>ITickerPersistenceProvider&lt;TTimeTicker, TCronTicker&gt;</c>
/// for fetching a ticker's stored Request bytes at dispatch time. Lets
/// <see cref="RemoteExecutionDelegateFactory"/> stay non-generic while still loading the
/// payload inline into <c>ExecuteFunction</c> (eliminates the SDK's old GetTimeTickerRequest
/// round-trip).
/// </summary>
internal interface IRemotePayloadLoader
{
    Task<byte[]?> LoadPayloadAsync(Guid tickerId, TickerType type, CancellationToken ct);
}

internal sealed class RemotePayloadLoader<TTimeTicker, TCronTicker> : IRemotePayloadLoader
    where TTimeTicker : Utilities.Entities.TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : Utilities.Entities.CronTickerEntity, new()
{
    private readonly Utilities.Interfaces.ITickerPersistenceProvider<TTimeTicker, TCronTicker> _provider;

    public RemotePayloadLoader(Utilities.Interfaces.ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider)
    {
        _provider = provider;
    }

    public async Task<byte[]?> LoadPayloadAsync(Guid tickerId, TickerType type, CancellationToken ct)
        => type == TickerType.CronTickerOccurrence
            ? await _provider.GetCronTickerOccurrenceRequest(tickerId, ct).ConfigureAwait(false)
            : await _provider.GetTimeTickerRequest(tickerId, ct).ConfigureAwait(false);
}
