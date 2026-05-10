using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TickerQ.RemoteExecutor.Grpc;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.RemoteExecutor.GrpcServices;

/// <summary>
/// gRPC write operations service for dashboard controls (toggle, cancel, host start/stop/restart).
/// Delegates to <see cref="ITickerDashboardDataService{TTimeTicker,TCronTicker}"/>.
/// </summary>
public sealed class DashboardOperationGrpcService<TTimeTicker, TCronTicker> : DashboardOperationService.DashboardOperationServiceBase
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly ITickerDashboardDataService<TTimeTicker, TCronTicker> _service;
    private readonly ITimeTickerManager<TTimeTicker> _timeTickerManager;
    private readonly ICronTickerManager<TCronTicker> _cronTickerManager;
    private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistence;
    private readonly Microsoft.Extensions.Logging.ILogger<DashboardOperationGrpcService<TTimeTicker, TCronTicker>> _logger;
    private readonly TickerQ.RemoteExecutor.WorkerStream.WorkerStreamRegistry _workerRegistry;

    public DashboardOperationGrpcService(
        ITickerDashboardDataService<TTimeTicker, TCronTicker> service,
        ITimeTickerManager<TTimeTicker> timeTickerManager,
        ICronTickerManager<TCronTicker> cronTickerManager,
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
        Microsoft.Extensions.Logging.ILogger<DashboardOperationGrpcService<TTimeTicker, TCronTicker>> logger,
        TickerQ.RemoteExecutor.WorkerStream.WorkerStreamRegistry workerRegistry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _timeTickerManager = timeTickerManager ?? throw new ArgumentNullException(nameof(timeTickerManager));
        _cronTickerManager = cronTickerManager ?? throw new ArgumentNullException(nameof(cronTickerManager));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger;
        _workerRegistry = workerRegistry ?? throw new ArgumentNullException(nameof(workerRegistry));
    }

    public override async Task<Empty> ToggleCronTicker(ToggleCronTickerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CronTickerId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid cron_ticker_id"));
        var ok = await _service.ToggleCronTickerAsync(id, request.IsEnabled, context.CancellationToken).ConfigureAwait(false);
        if (!ok) throw new RpcException(new Status(StatusCode.NotFound, "Cron ticker not found"));
        return new Empty();
    }

    public override async Task<Empty> RunCronTickerOnDemand(OperationIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));
        await _service.RunCronTickerOnDemandAsync(id, context.CancellationToken).ConfigureAwait(false);
        return new Empty();
    }

    public override async Task<Empty> CancelTicker(OperationIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        // 1. Try the local cancel — works for combined-mode (scheduler + SDK
        //    in same process) where the user's CancellationTokenSource is
        //    registered in the in-proc TickerCancellationTokenManager.
        var cancelledLocally = _service.CancelTicker(id);

        // 2. Broadcast a CancelExecution command to every connected SDK worker
        //    stream too. Pure-client SDKs run [TickerFunction] bodies in their
        //    own process, so the local CTS dict doesn't have them — the SDK
        //    that's actually running this ticker matches by id and signals its
        //    own CTS. SDKs without the ticker silently drop the message.
        await BroadcastCancelExecutionAsync(id, context.CancellationToken).ConfigureAwait(false);

        // We treat the cancel as "accepted" if either path could apply:
        //  - locally cancelled, or
        //  - at least one worker stream is connected (cancel may have landed
        //    on a remote SDK we can't directly confirm here).
        if (!cancelledLocally && _workerRegistry.All().Count == 0)
            throw new RpcException(new Status(StatusCode.NotFound, "Ticker not running"));

        return new Empty();
    }

    private async Task BroadcastCancelExecutionAsync(Guid tickerId, System.Threading.CancellationToken ct)
    {
        // Broadcast is fine because tickerId is globally unique. We could narrow
        // by env later if multi-env schedulers become a thing, but today every
        // scheduler instance is single-env so the worker registry is already
        // env-scoped to whatever this process serves.
        var workers = _workerRegistry.All();
        if (workers.Count == 0) return;

        var cmd = new TickerQ.Worker.V1.SchedulerCommand
        {
            CancelExecution = new TickerQ.Worker.V1.CancelExecution
            {
                RequestId = Guid.NewGuid().ToString("N"),
                TickerId = tickerId.ToString(),
            },
        };

        // Fan out in parallel; failures are non-fatal — one SDK being slow or
        // disconnected shouldn't block the others.
        var sendTasks = new List<Task>();
        foreach (var w in workers)
        {
            sendTasks.Add(SafeWriteAsync(w, cmd, ct));
        }
        await Task.WhenAll(sendTasks).ConfigureAwait(false);
    }

    private async Task SafeWriteAsync(TickerQ.RemoteExecutor.WorkerStream.SchedulerWorkerConnection conn,
        TickerQ.Worker.V1.SchedulerCommand cmd, System.Threading.CancellationToken ct)
    {
        try
        {
            await conn.WriteAsync(cmd, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to push CancelExecution to worker {WorkerId}", conn.WorkerId);
        }
    }

    public override async Task<Empty> StartHost(Empty request, ServerCallContext context)
    {
        await _service.StartHostAsync(context.CancellationToken).ConfigureAwait(false);
        return new Empty();
    }

    public override async Task<Empty> StopHost(Empty request, ServerCallContext context)
    {
        await _service.StopHostAsync(context.CancellationToken).ConfigureAwait(false);
        return new Empty();
    }

    public override Task<Empty> RestartHost(Empty request, ServerCallContext context)
    {
        _service.RestartHost();
        return Task.FromResult(new Empty());
    }

    public override async Task<AddTimeTickerResponse> AddTimeTicker(AddTimeTickerRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Function))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Function is required"));

        // Qualify the function name with @nodeName so dispatch lookups hit the right
        // registry entry. If the dashboard already sent a qualified name we trust it;
        // otherwise resolve the owning node from the registry.
        var function = request.Function;
        if (!function.Contains('@'))
        {
            var nodeName = RemoteFunctionRegistry.GetNodeName(function);
            if (!string.IsNullOrEmpty(nodeName))
                function = $"{function}@{nodeName}";
        }

        var entity = new TTimeTicker
        {
            Function = function,
            ExecutionTime = request.ExecutionTime?.ToDateTime(),
            Description = request.HasDescription ? request.Description : null,
            Retries = request.HasRetries ? request.Retries : 0,
            Request = request.Request?.Length > 0 ? request.Request.ToByteArray() : null,
            RetryIntervals = request.RetryIntervalsSeconds.Count > 0
                ? request.RetryIntervalsSeconds.ToArray()
                : null,
        };

        var result = await _timeTickerManager.AddAsync(entity, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded || result.Result == null)
            throw new RpcException(new Status(StatusCode.Internal, result.Exception?.Message ?? "Failed to add time ticker"));

        return new AddTimeTickerResponse { Id = result.Result.Id.ToString() };
    }

    public override async Task<Empty> DeleteTimeTicker(OperationIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        var result = await _timeTickerManager.DeleteAsync(id, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded)
            throw new RpcException(new Status(StatusCode.NotFound, result.Exception?.Message ?? "Ticker not found"));
        return new Empty();
    }

    public override async Task<Empty> RunTimeTickerOnDemand(OperationIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        var entity = await _persistence.GetTimeTickerById(id, context.CancellationToken).ConfigureAwait(false);
        if (entity == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Time ticker not found"));

        // Reschedule to now so the polling loop picks it up on the next sweep.
        entity.ExecutionTime = DateTime.UtcNow;

        var result = await _timeTickerManager.UpdateAsync(entity, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded)
            throw new RpcException(new Status(StatusCode.Internal, result.Exception?.Message ?? "Failed to reschedule"));
        return new Empty();
    }

    public override async Task<Empty> UpdateTimeTicker(UpdateTimeTickerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        var entity = await _persistence.GetTimeTickerById(id, context.CancellationToken).ConfigureAwait(false);
        if (entity == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Time ticker not found"));

        // Edit is only valid for tickers that haven't started running yet — modifying
        // a Done/Failed/InProgress row would conflict with framework-managed state
        // (Status, RetryCount, ExecutedAt, ElapsedTime). Hub controller also enforces
        // this, but we double-check at the scheduler boundary.
        if (entity.Status != TickerQ.Utilities.Enums.TickerStatus.Idle
            && entity.Status != TickerQ.Utilities.Enums.TickerStatus.Queued)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Time ticker can only be edited while Idle or Queued"));
        }

        // Apply only fields the client explicitly set. proto3 `optional` flags handle
        // primitives; for collections / bytes we use companion *_set bools (proto3 can't
        // distinguish "default empty" from "absent" on `repeated`/`bytes`).
        // Message-typed fields are nullable in proto3 by default — `optional` here
        // doesn't generate a `Has*` for the Timestamp; null check is the signal.
        if (request.ExecutionTime != null)
            entity.ExecutionTime = request.ExecutionTime.ToDateTime();
        if (request.HasDescription)
            entity.Description = request.Description;
        if (request.HasRetries)
            entity.Retries = request.Retries;
        if (request.RetryIntervalsSet)
            entity.RetryIntervals = request.RetryIntervalsSeconds.Count > 0
                ? request.RetryIntervalsSeconds.ToArray()
                : null;
        if (request.RequestSet)
            entity.Request = request.Request?.Length > 0 ? request.Request.ToByteArray() : null;
        if (request.HasFunction && !string.IsNullOrWhiteSpace(request.Function))
        {
            // Mirror the Add path: qualify bare names with @nodeName so dispatch
            // lookups hit the right registry entry. Pre-qualified names trusted as-is.
            var function = request.Function;
            if (!function.Contains('@'))
            {
                var nodeName = RemoteFunctionRegistry.GetNodeName(function);
                if (!string.IsNullOrEmpty(nodeName))
                    function = $"{function}@{nodeName}";
            }
            entity.Function = function;
        }
        if (request.HasRunCondition)
        {
            // Only meaningful for non-root chain children (gates dispatch on
            // parent's terminal status). Roots and standalone tickers ignore it
            // at runtime, so we accept the field on any ticker — the framework
            // already treats unset RunCondition on a root as a no-op.
            entity.RunCondition = (TickerQ.Utilities.Enums.RunCondition)request.RunCondition;
        }

        var result = await _timeTickerManager.UpdateAsync(entity, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded)
            throw new RpcException(new Status(StatusCode.Internal, result.Exception?.Message ?? "Failed to update"));
        return new Empty();
    }

    public override async Task<AddTimeTickerChainResponse> AddTimeTickerChain(AddTimeTickerChainRequest request, ServerCallContext context)
    {
        if (request.Root == null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Root node is required"));
        if (request.ExecutionTime == null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ExecutionTime is required"));

        // Build the entity graph in memory and let the manager + EF cascade do
        // the persistence. Children must keep ExecutionTime = null — the
        // framework loads them via .Include(Children.Where(ExecutionTime==null))
        // when the parent is picked up by polling, then dispatches each child
        // based on its RunCondition. If we set ExecutionTime on a child, the
        // polling layer would treat it as a standalone ticker.
        var rootEntity = BuildEntityForChain(request.Root, isRoot: true, parentId: null);
        rootEntity.ExecutionTime = request.ExecutionTime.ToDateTime();

        var createdCount = 1 + CountDescendants(rootEntity);

        var result = await _timeTickerManager.AddAsync(rootEntity, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded || result.Result == null)
            throw new RpcException(new Status(StatusCode.Internal, result.Exception?.Message ?? "Failed to add chain"));

        return new AddTimeTickerChainResponse
        {
            RootId = result.Result.Id.ToString(),
            CreatedCount = createdCount,
        };
    }

    private static TTimeTicker BuildEntityForChain(TimeTickerNode node, bool isRoot, Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(node.Function))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Each chain node needs a function"));

        // Mirror the qualification logic from AddTimeTicker so bare names hit
        // the right registry entry on dispatch.
        var function = node.Function;
        if (!function.Contains('@'))
        {
            var nodeName = RemoteFunctionRegistry.GetNodeName(function);
            if (!string.IsNullOrEmpty(nodeName))
                function = $"{function}@{nodeName}";
        }

        var entity = new TTimeTicker
        {
            Id = Guid.NewGuid(),
            Function = function,
            Description = node.HasDescription ? node.Description : null,
            Retries = node.HasRetries ? node.Retries : 0,
            Request = node.Request?.Length > 0 ? node.Request.ToByteArray() : null,
            RetryIntervals = node.RetryIntervalsSeconds.Count > 0
                ? node.RetryIntervalsSeconds.ToArray()
                : null,
        };

        if (!isRoot)
        {
            // ParentId on the entity has internal setter — TickerQ.RemoteExecutor
            // is in InternalsVisibleTo so this assignment compiles. Setting it
            // explicitly lets EF wire the FK without relying on the navigation.
            entity.ParentId = parentId;
            if (node.HasRunCondition)
                entity.RunCondition = (TickerQ.Utilities.Enums.RunCondition)node.RunCondition;
            // ExecutionTime stays null on children — see comment in AddTimeTickerChain.
        }

        foreach (var childNode in node.Children)
        {
            var childEntity = BuildEntityForChain(childNode, isRoot: false, parentId: entity.Id);
            entity.Children.Add(childEntity);
        }

        return entity;
    }

    private static int CountDescendants(TTimeTicker entity)
    {
        var count = entity.Children.Count;
        foreach (var child in entity.Children)
            count += CountDescendants(child);
        return count;
    }

    public override async Task<AddTimeTickerResponse> DuplicateTimeTicker(DuplicateTimeTickerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        // Walk the source tree and build a fresh entity graph with new IDs.
        // For chain roots this preserves children + their RunCondition; for
        // standalone tickers it falls back to a single-row copy. EF cascade
        // persists the whole graph in one AddAsync.
        var copy = await CloneSubtreeAsync(id, isRoot: true, context.CancellationToken).ConfigureAwait(false);
        if (copy == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Time ticker not found"));

        // Root gets the caller-provided ExecutionTime (or "now" by default);
        // children stay null so the framework dispatches them on parent
        // completion via their RunCondition (same semantics as AddTimeTickerChain).
        copy.ExecutionTime = request.ExecutionTime != null
            ? request.ExecutionTime.ToDateTime()
            : DateTime.UtcNow;

        var directChildCount = copy.Children?.Count ?? 0;
        var totalCount = 1 + CountDescendants(copy);
        _logger?.LogWarning("[Duplicate] source={SourceId} → newRoot={NewId} directChildren={Direct} total={Total}",
            id, copy.Id, directChildCount, totalCount);
        // Dump every node's id + parentId so we can confirm the whole tree is
        // wired correctly before EF takes over.
        DumpTree(copy, depth: 0);

        var added = await _timeTickerManager.AddAsync(copy, context.CancellationToken).ConfigureAwait(false);
        if (!added.IsSucceeded || added.Result == null)
            throw new RpcException(new Status(StatusCode.Internal, added.Exception?.Message ?? "Failed to duplicate"));

        _logger?.LogWarning("[Duplicate] persisted root={RootId} expectedRows={Total}",
            added.Result.Id, totalCount);

        // After persistence, re-read the descendants to see what actually
        // landed in the DB. If any child's ParentId came back as null, the
        // EF graph-add lost the relationship somewhere.
        await DumpPersistedAsync(added.Result.Id, context.CancellationToken).ConfigureAwait(false);
        return new AddTimeTickerResponse { Id = added.Result.Id.ToString() };
    }

    private void DumpTree(TTimeTicker node, int depth)
    {
        var indent = new string(' ', depth * 2);
        _logger?.LogWarning("[Duplicate-Tree] {Indent}id={Id} parentId={Parent} fn={Fn} children={Count}",
            indent, node.Id, node.ParentId, node.Function, node.Children?.Count ?? 0);
        if (node.Children == null) return;
        foreach (var c in node.Children) DumpTree(c, depth + 1);
    }

    private async Task DumpPersistedAsync(Guid rootId, System.Threading.CancellationToken ct)
    {
        var root = await _persistence.GetTimeTickerById(rootId, ct).ConfigureAwait(false);
        if (root == null) { _logger?.LogWarning("[Duplicate-Persisted] root not found {Id}", rootId); return; }
        _logger?.LogWarning("[Duplicate-Persisted] root={Id} parentId={Parent} children={Count}",
            root.Id, root.ParentId, root.Children?.Count ?? 0);
        if (root.Children == null) return;
        foreach (var c in root.Children)
            _logger?.LogWarning("[Duplicate-Persisted]   child={Id} parentId={Parent}", c.Id, c.ParentId);
    }

    /// <summary>
    /// Recursively clones a ticker subtree by re-fetching each node. The
    /// persistence layer's <c>GetTimeTickerById</c> only eager-loads a single
    /// level of children, so we walk depth-first to hydrate every level.
    /// User-controlled fields (Function, Description, Retries, RetryIntervals,
    /// Request, RunCondition) are copied; internal/runtime fields (Status,
    /// RetryCount, ExecutedAt, ElapsedTime, ExceptionMessage, …) start fresh.
    /// </summary>
    private async Task<TTimeTicker> CloneSubtreeAsync(Guid sourceId, bool isRoot, System.Threading.CancellationToken ct)
    {
        var src = await _persistence.GetTimeTickerById(sourceId, ct).ConfigureAwait(false);
        if (src == null) return null;

        var copy = new TTimeTicker
        {
            Id = Guid.NewGuid(),
            Function = src.Function,
            Description = src.Description,
            Retries = src.Retries,
            RetryIntervals = src.RetryIntervals,
            Request = src.Request,
            RunCondition = src.RunCondition,
        };
        // ExecutionTime stays null on children (set on the root by the caller).

        foreach (var child in src.Children)
        {
            var childCopy = await CloneSubtreeAsync(child.Id, isRoot: false, ct).ConfigureAwait(false);
            if (childCopy == null) continue;
            // ParentId on TTimeTicker has internal setter; this assembly is
            // listed in InternalsVisibleTo so the assignment compiles.
            childCopy.ParentId = copy.Id;
            copy.Children.Add(childCopy);
        }

        return copy;
    }

    // ==================== Cron CRUD ====================

    public override async Task<AddCronTickerResponse> AddCronTicker(AddCronTickerRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Function))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Function is required"));
        if (string.IsNullOrWhiteSpace(request.Expression))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Expression is required"));

        // Same node-qualification rule as AddTimeTicker: bare names get the
        // owning node appended so dispatch hits the right registry entry.
        var function = request.Function;
        if (!function.Contains('@'))
        {
            var nodeName = RemoteFunctionRegistry.GetNodeName(function);
            if (!string.IsNullOrEmpty(nodeName))
                function = $"{function}@{nodeName}";
        }

        var entity = new TCronTicker
        {
            Function = function,
            Expression = request.Expression,
            Description = request.HasDescription ? request.Description : null,
            Retries = request.HasRetries ? request.Retries : 0,
            Request = request.Request?.Length > 0 ? request.Request.ToByteArray() : null,
            RetryIntervals = request.RetryIntervalsSeconds.Count > 0
                ? request.RetryIntervalsSeconds.ToArray()
                : null,
            IsEnabled = !request.HasIsEnabled || request.IsEnabled,
        };

        var result = await _cronTickerManager.AddAsync(entity, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded || result.Result == null)
            throw new RpcException(new Status(StatusCode.Internal, result.Exception?.Message ?? "Failed to add cron ticker"));

        return new AddCronTickerResponse { Id = result.Result.Id.ToString() };
    }

    public override async Task<Empty> UpdateCronTicker(UpdateCronTickerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        var entity = await _persistence.GetCronTickerById(id, context.CancellationToken).ConfigureAwait(false);
        if (entity == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Cron ticker not found"));

        // Apply only fields the client explicitly set. Same proto3 pattern the
        // time-ticker update uses: `optional` flags for primitives, *_set bools
        // for `repeated`/`bytes` (which can't distinguish "default empty" from
        // "absent" otherwise).
        if (request.HasFunction && !string.IsNullOrWhiteSpace(request.Function))
        {
            var function = request.Function;
            if (!function.Contains('@'))
            {
                var nodeName = RemoteFunctionRegistry.GetNodeName(function);
                if (!string.IsNullOrEmpty(nodeName))
                    function = $"{function}@{nodeName}";
            }
            entity.Function = function;
        }
        if (request.HasExpression && !string.IsNullOrWhiteSpace(request.Expression))
            entity.Expression = request.Expression;
        if (request.HasDescription)
            entity.Description = request.Description;
        if (request.HasRetries)
            entity.Retries = request.Retries;
        if (request.RetryIntervalsSet)
            entity.RetryIntervals = request.RetryIntervalsSeconds.Count > 0
                ? request.RetryIntervalsSeconds.ToArray()
                : null;
        if (request.RequestSet)
            entity.Request = request.Request?.Length > 0 ? request.Request.ToByteArray() : null;
        if (request.HasIsEnabled)
            entity.IsEnabled = request.IsEnabled;

        var result = await _cronTickerManager.UpdateAsync(entity, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded)
            throw new RpcException(new Status(StatusCode.Internal, result.Exception?.Message ?? "Failed to update"));
        return new Empty();
    }

    public override async Task<Empty> DeleteCronTicker(OperationIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        var result = await _cronTickerManager.DeleteAsync(id, context.CancellationToken).ConfigureAwait(false);
        if (!result.IsSucceeded)
            throw new RpcException(new Status(StatusCode.NotFound, result.Exception?.Message ?? "Cron ticker not found"));
        return new Empty();
    }

    public override async Task<Empty> DeleteCronOccurrence(OperationIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid id"));

        // Persistence provider exposes a batch delete keyed by occurrence id.
        // Returns the number of rows actually removed — 0 means the occurrence
        // didn't exist, which we surface as NotFound for parity with the
        // single-id delete endpoints (DeleteTimeTicker, DeleteCronTicker).
        var removed = await _persistence.RemoveCronTickerOccurrences(new[] { id }, context.CancellationToken).ConfigureAwait(false);
        if (removed == 0)
            throw new RpcException(new Status(StatusCode.NotFound, "Cron occurrence not found"));
        return new Empty();
    }
}
