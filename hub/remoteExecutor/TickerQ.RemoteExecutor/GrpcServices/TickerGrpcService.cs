using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Grpc.Contracts;
using TickerQ.RemoteExecutor.GrpcServices.Mappers;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.RemoteExecutor.GrpcServices;

internal sealed class TickerGrpcService : TickerService.TickerServiceBase
{
    private readonly IServiceProvider _serviceProvider;

    public TickerGrpcService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    #region Time Ticker CRUD

    public override async Task<AffectedResponse> AddTimeTickers(AddTimeTickersRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var tickers = request.Tickers.Select(TimeTickerMapper.ToEntity).ToArray();

        if (tickers.Length == 0)
            return new AffectedResponse { Affected = 0 };

        var affected = await provider.AddTimeTickers(tickers, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = affected };
    }

    public override async Task<AffectedResponse> UpdateTimeTickers(UpdateTimeTickersRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var tickers = request.Tickers.Select(TimeTickerMapper.ToEntity).ToArray();

        if (tickers.Length == 0)
            return new AffectedResponse { Affected = 0 };

        var affected = await provider.UpdateTimeTickers(tickers, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = affected };
    }

    public override async Task<AffectedResponse> DeleteTimeTickers(IdsRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var ids = request.Ids.Select(Guid.Parse).ToArray();

        var affected = await provider.RemoveTimeTickers(ids, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = affected };
    }

    public override async Task<BytesResponse> GetTimeTickerRequest(IdRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var id = Guid.Parse(request.Id);

        var bytes = await provider.GetTimeTickerRequest(id, context.CancellationToken).ConfigureAwait(false);
        return new BytesResponse { Data = bytes != null ? ByteString.CopyFrom(bytes) : ByteString.Empty };
    }

    public override async Task<AffectedResponse> UpdateTimeTickerContext(FunctionContext request, ServerCallContext context)
    {
        var manager = GetInternalTickerManager();
        var internalCtx = FunctionContextMapper.ToInternal(request);

        await manager.UpdateTickerAsync(internalCtx, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = 1 };
    }

    public override async Task<Empty> UpdateTimeTickersUnifiedContext(UnifiedContextRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var ids = request.Ids.Select(Guid.Parse).ToArray();
        var internalCtx = FunctionContextMapper.ToInternal(request.Context);

        await provider.UpdateTimeTickersWithUnifiedContext(ids, internalCtx, context.CancellationToken)
            .ConfigureAwait(false);
        return new Empty();
    }

    #endregion

    #region Cron Ticker CRUD

    public override async Task<AffectedResponse> AddCronTickers(AddCronTickersRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var tickers = request.Tickers.Select(CronTickerMapper.ToEntity).ToArray();

        if (tickers.Length == 0)
            return new AffectedResponse { Affected = 0 };

        var affected = await provider.InsertCronTickers(tickers, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = affected };
    }

    public override async Task<AffectedResponse> UpdateCronTickers(UpdateCronTickersRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var tickers = request.Tickers.Select(CronTickerMapper.ToEntity).ToArray();

        if (tickers.Length == 0)
            return new AffectedResponse { Affected = 0 };

        var affected = await provider.UpdateCronTickers(tickers, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = affected };
    }

    public override async Task<AffectedResponse> DeleteCronTickers(IdsRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var ids = request.Ids.Select(Guid.Parse).ToArray();

        var affected = await provider.RemoveCronTickers(ids, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = affected };
    }

    #endregion

    #region Cron Ticker Occurrence

    public override async Task<AffectedResponse> UpdateCronOccurrenceContext(FunctionContext request, ServerCallContext context)
    {
        var manager = GetInternalTickerManager();
        var internalCtx = FunctionContextMapper.ToInternal(request);

        await manager.UpdateTickerAsync(internalCtx, context.CancellationToken).ConfigureAwait(false);
        return new AffectedResponse { Affected = 1 };
    }

    public override async Task<BytesResponse> GetCronOccurrenceRequest(IdRequest request, ServerCallContext context)
    {
        var provider = GetPersistenceProvider();
        var id = Guid.Parse(request.Id);

        var bytes = await provider.GetCronTickerOccurrenceRequest(id, context.CancellationToken).ConfigureAwait(false);
        return new BytesResponse { Data = bytes != null ? ByteString.CopyFrom(bytes) : ByteString.Empty };
    }

    #endregion

    private dynamic GetPersistenceProvider()
    {
        var providerType = _serviceProvider.GetServices<object>()
            .FirstOrDefault(s => s?.GetType().GetInterfaces()
                .Any(i => i.IsGenericType &&
                          i.GetGenericTypeDefinition() == typeof(ITickerPersistenceProvider<,>)) == true);

        return providerType ?? throw new InvalidOperationException(
            "No ITickerPersistenceProvider<,> registered. Ensure TickerQ is configured.");
    }

    private IInternalTickerManager GetInternalTickerManager()
    {
        return _serviceProvider.GetRequiredService<IInternalTickerManager>();
    }
}
