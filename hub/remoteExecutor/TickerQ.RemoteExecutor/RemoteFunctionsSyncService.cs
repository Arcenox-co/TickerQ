using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using TickerQ.RemoteExecutor.Hub;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Pulls active nodes/functions from the Hub on startup via gRPC and registers
/// each as a TickerFunction whose delegate POSTs to the SDK's callback URL.
/// </summary>
public class RemoteFunctionsSyncService : BackgroundService
{
    // Grace window before a node flagged is_dispatchable=false actually gets its
    // functions unregistered. Absorbs SDK redeploy blips (drop → 10s redeploy →
    // reconnect = brief blip) instead of unregistering and re-registering on every
    // bounce. Keyed by NodeName; the value is when the node was FIRST seen as
    // non-dispatchable in the current outage window. Cleared the moment the Hub
    // reports the node alive again.
    private static readonly TimeSpan UnregisterGrace = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, DateTime> _firstSeenOfflineAt =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TickerQRemoteExecutionOptions _options;
    private readonly Func<DefinedCronTickerSeed[], CancellationToken, Task>? _migrateDefinedCronTickers;
    private readonly ILogger<RemoteFunctionsSyncService>? _logger;

    public RemoteFunctionsSyncService(
        TickerQRemoteExecutionOptions options,
        IServiceProvider serviceProvider,
        ILogger<RemoteFunctionsSyncService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        var internalTickerManager = serviceProvider.GetService<IInternalTickerManager>();
        _migrateDefinedCronTickers = internalTickerManager == null
            ? null
            : internalTickerManager.MigrateDefinedCronTickers;
        _logger = logger;
    }

    internal RemoteFunctionsSyncService(
        TickerQRemoteExecutionOptions options,
        Func<DefinedCronTickerSeed[], CancellationToken, Task> migrateDefinedCronTickers,
        ILogger<RemoteFunctionsSyncService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _migrateDefinedCronTickers = migrateDefinedCronTickers
            ?? throw new ArgumentNullException(nameof(migrateDefinedCronTickers));
        _logger = logger;
    }

    // Periodic resync cadence. Acts as the safety net behind event-driven pushes
    // from Hub (SDK connect/disconnect, Apply/Unapply, manual Resync) — guarantees
    // eventual consistency even when push messages are lost (network blip, Hub
    // multi-instance state split, etc.). 30s is well under any reasonable user
    // patience threshold and the gRPC cost is negligible at our scale.
    private static readonly TimeSpan PeriodicResyncInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial sync at startup so the scheduler has a fresh registry before
        // it starts dispatching anything.
        await SyncOnceAsync(stoppingToken).ConfigureAwait(false);

        // 0-30s jitter on the first periodic delay avoids a thundering herd when
        // many schedulers boot simultaneously (e.g. Cloud Run scale-up). Without
        // this they'd all hit Hub at the same 30s tick forever.
        var jitter = TimeSpan.FromMilliseconds(System.Random.Shared.Next(0, (int)PeriodicResyncInterval.TotalMilliseconds));
        try { await Task.Delay(jitter, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Periodic sync failures are not fatal — try again next interval.
                // The event-driven push path is the primary; this is the safety net.
                _logger?.LogWarning(ex, "Periodic remote-functions resync failed; will retry in {Interval}s",
                    (int)PeriodicResyncInterval.TotalSeconds);
            }

            try { await Task.Delay(PeriodicResyncInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            var grpcUrl = _options.HubGrpcEndpointUrl;
            if (string.IsNullOrWhiteSpace(grpcUrl))
            {
                _logger?.LogWarning("HubGrpcEndpointUrl is not configured. Skipping remote functions sync.");
                return;
            }

            // Allow gRPC over plaintext for local dev (http://) Hub deployments.
            if (grpcUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            _logger?.LogInformation("Starting remote functions sync from {EndpointUrl} (gRPC)", grpcUrl);

            using var channel = GrpcChannel.ForAddress(grpcUrl.TrimEnd('/'), new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 16 * 1024 * 1024
            });
            var client = new HubService.HubServiceClient(channel);

            var headers = new Metadata
            {
                { "x-api-key", _options.ApiKey ?? string.Empty }
            };

            GetRegisteredFunctionsResponse response;
            try
            {
                response = await client.GetRegisteredFunctionsAsync(
                    new GetRegisteredFunctionsRequest(),
                    headers,
                    deadline: DateTime.UtcNow.AddSeconds(30),
                    cancellationToken: stoppingToken);
            }
            catch (RpcException rpcEx)
            {
                _logger?.LogError(rpcEx,
                    "gRPC GetRegisteredFunctions failed: {Status} - {Detail}",
                    rpcEx.StatusCode, rpcEx.Status.Detail);
                return;
            }

            _options.WebHookSignature = response.WebhookSignature;
            await RegisterFunctionsFromResponse(response, stoppingToken);

            _logger?.LogInformation("Remote functions sync completed successfully");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Remote functions sync cancelled due to application shutdown.");
        }
        // Other exceptions are NOT caught and will propagate — fail fast on programming errors.
    }

    internal async Task RegisterFunctionsFromResponse(GetRegisteredFunctionsResponse response, CancellationToken cancellationToken)
    {
        // Build only the REMOTE slice from the Hub response. The merged frozen registry is
        // composed by TickerFunctionProvider.MergeRemoteSnapshot, which publishes the remote
        // execution functions AND their canonical wire descriptors together as one snapshot,
        // keeping every local source-gen [TickerFunction] entry untouched and rewriting only the
        // qualified ("bare@node") remote keys.
        var remoteFunctionDict = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>();
        var remoteDescriptorDict = new Dictionary<string, TickerFunctionDescriptor>();
        // Legacy request-info mirror, kept as a compatibility adapter for non-atomic callers only.
        var remoteRequestInfoDict = new Dictionary<string, (string RequestType, string RequestExampleJson)>();
        var remoteRoutingDict = new Dictionary<string, string>();
        var cronSeeds = new List<DefinedCronTickerSeed>();

        // Track every qualified key we register this round so we can reconcile the
        // RemoteFunctionRegistry afterwards: anything previously known but not in
        // (and dispatchable in) this response gets unregistered locally.
        var seenFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in response.Nodes)
        {
            // CallbackUrl is no longer required — pure-client SDKs don't expose one. Dispatch
            // happens over the worker stream the SDK opens to the scheduler. Skip empty-name
            // nodes only.
            if (string.IsNullOrWhiteSpace(node.NodeName))
            {
                _logger?.LogWarning("Node has no name, skipping");
                continue;
            }

            // The Hub flags each node with is_dispatchable based on whether it currently has a
            // live control stream to the SDK. If the SDK is offline we deliberately do NOT
            // register its functions — cron occurrences for those functions land as Skipped
            // (via SdkOfflineSkipException) instead of sitting InProgress waiting for a node
            // that can't answer. When the SDK reconnects the next sync picks them back up.
            //
            // Backward compatibility: only skip when the field is BOTH present AND false.
            // An older Hub that doesn't populate is_dispatchable returns HasIsDispatchable=false;
            // we treat that as "unknown, assume alive" to preserve legacy behavior during the
            // staged rollout (otherwise a new client + old Hub would unregister everything).
            if (node.HasIsDispatchable && !node.IsDispatchable)
            {
                // Grace window: don't unregister on the first sync that reports a node offline.
                // SDK redeploys typically drop and re-register in a few seconds, and bouncing
                // function registrations on every blip causes ticker dispatch flap. Track the
                // first time we saw this outage; only actually skip once UnregisterGrace has
                // elapsed.
                var nowUtc = DateTime.UtcNow;
                var firstSeen = _firstSeenOfflineAt.GetOrAdd(node.NodeName, nowUtc);
                var outageDuration = nowUtc - firstSeen;
                if (outageDuration < UnregisterGrace)
                {
                    _logger?.LogInformation(
                        "Node {NodeName} reported not dispatchable (outage {Outage}s; within {Grace}s grace) — keeping functions registered",
                        node.NodeName, (int)outageDuration.TotalSeconds, (int)UnregisterGrace.TotalSeconds);
                    // Fall through and register the functions normally — the SDK may come back
                    // within the grace window and any tickers fired in the meantime will retry.
                }
                else
                {
                    _logger?.LogInformation(
                        "Skipping node {NodeName}: not dispatchable for {Outage}s (past {Grace}s grace) — unregistering",
                        node.NodeName, (int)outageDuration.TotalSeconds, (int)UnregisterGrace.TotalSeconds);
                    continue;
                }
            }
            else
            {
                // Node is dispatchable (or we don't know — backward compat). Clear any
                // outage tracking so a future drop starts a fresh grace window.
                _firstSeenOfflineAt.TryRemove(node.NodeName, out _);
            }

            if (node.Functions.Count == 0)
            {
                _logger?.LogInformation("Node {NodeName} has no functions", node.NodeName);
                continue;
            }

            foreach (var function in node.Functions)
            {
                if (string.IsNullOrWhiteSpace(function.FunctionName))
                {
                    _logger?.LogWarning("Function has no name, skipping");
                    continue;
                }

                // Node-qualify the registry key so two SDKs can host functions with the same
                // bare name without colliding. Tickers persist the qualified name too (qualified
                // at every creation entry point), so dispatch lookups by ticker.Function still hit.
                // Bare->node mapping is tracked separately via RemoteFunctionRegistry for
                // server-side resolution at ticker creation time.
                var qualifiedName = $"{function.FunctionName}@{node.NodeName}";

                if (!function.IsActive)
                {
                    // Inactive at the Hub = SDK soft-deleted (function was removed from the
                    // SDK's source). Leave it out of the merge; reconcile drops it from the
                    // RemoteFunctionRegistry.
                    _logger?.LogDebug("Skipping inactive function {FunctionName}@{NodeName}",
                        function.FunctionName, node.NodeName);
                    continue;
                }

                // is_enabled = user intent. Optional in proto for backward compat: an old
                // Hub returning the field absent means "unknown → assume enabled" (legacy
                // semantics). Only skip when the field is BOTH present AND false.
                if (function.HasIsEnabled && !function.IsEnabled)
                {
                    _logger?.LogInformation(
                        "Skipping disabled function {FunctionName}@{NodeName} (user manually disabled in dashboard)",
                        function.FunctionName, node.NodeName);
                    continue;
                }

                // Dispatch goes via the worker stream — no callback URL is dialed; the SDK
                // is identified by NodeName and reached through its open stream registered
                // in WorkerStreamRegistry.
                var functionDelegate = RemoteExecutionDelegateFactory.Create(node.NodeName);

                var priority = (TickerTaskPriority)(int)function.TaskPriority;
                var cronExpression = function.NodeExpression ?? string.Empty;

                remoteFunctionDict[qualifiedName] = (cronExpression, priority, functionDelegate, 0);
                remoteRoutingDict[function.FunctionName] = node.NodeName;

                // Canonical wire descriptor for this remote function. Request-less functions carry a
                // null Request (absence is never encoded as empty metadata). Priority/cron are also
                // reconciled inside MergeRemoteSnapshot from the matching execution entry.
                var contractVersion = function.ContractVersion > 0
                    ? function.ContractVersion
                    : TickerRequestContractConstants.InitialContractVersion;
                var requestContract = BuildRemoteRequestContract(
                    qualifiedName,
                    function.RequestContract,
                    function.RequestType,
                    function.RequestExampleJson);
                var remoteDescriptor = new TickerFunctionDescriptor(
                    qualifiedName,
                    priority,
                    cronExpression,
                    contractVersion,
                    requestContract);
                if (function.RequestContract?.HasFingerprint == true
                    && !string.Equals(
                        function.RequestContract.Fingerprint,
                        remoteDescriptor.Request?.Fingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Remote request-contract fingerprint mismatch for '{qualifiedName}'.");
                }
                remoteDescriptorDict[qualifiedName] = remoteDescriptor;

                remoteRequestInfoDict[qualifiedName] = (
                    function.RequestType,
                    function.RequestExampleJson ?? string.Empty);
                seenFunctionNames.Add(qualifiedName);

                if (node.AutoMigrateExpressions && !string.IsNullOrWhiteSpace(cronExpression))
                {
                    // Required contracts cannot be satisfied by an empty auto-seeded payload, but
                    // persistence still needs an explicit blocked command to remove a former seed.
                    cronSeeds.Add(new DefinedCronTickerSeed(
                        qualifiedName,
                        cronExpression,
                        remoteDescriptor.ContractVersion,
                        remoteDescriptor.Request?.Fingerprint,
                        canSeed: remoteDescriptor.Request is not { Required: true }));
                }

                _logger?.LogDebug("Registered function {QualifiedName}", qualifiedName);
            }
        }

        // Publish routing only after every descriptor has reconstructed successfully. A malformed
        // response therefore cannot poison routing before the canonical snapshot is publishable.
        foreach (var (functionName, nodeName) in remoteRoutingDict)
            RemoteFunctionRegistry.MarkRemote(functionName, nodeName);

        // Reconcile: any function we previously registered as remote that did NOT appear
        // (and dispatchable) in the response was disabled, removed, or its owning SDK went
        // offline. Drop it from RemoteFunctionRegistry so router.IsRemote() returns false
        // for the bare name on the next dispatch — local source-gen entries will then be
        // visible again. The Merge call below applies the same deletion to TickerFunctions.
        foreach (var bareName in RemoteFunctionRegistry.SnapshotFunctionNames())
        {
            var node = RemoteFunctionRegistry.GetNodeName(bareName);
            var qualified = string.IsNullOrEmpty(node) ? bareName : $"{bareName}@{node}";
            if (seenFunctionNames.Contains(qualified)) continue;
            RemoteFunctionRegistry.Remove(bareName);
            _logger?.LogInformation(
                "Unapplied stale remote function {QualifiedName} (offline / disabled / removed at Hub)",
                qualified);
        }

        // Merge instead of replace — preserves every local [TickerFunction] from source-gen
        // (the bug we hit before: local SampleCronJob was wiped when a remote SampleCronJob@node
        // arrived in the sync, so dispatch went remote forever and occurrences stuck InProgress).
        // The "isCurrentlyRemote" predicate is satisfied for any qualified key whose bare name
        // is still flagged in RemoteFunctionRegistry post-reconcile.
        bool IsCurrentlyRemote(string key)
        {
            // The "remote slice" is exactly the set of qualified ("bare@node") keys in
            // TickerFunctions. Local source-gen [TickerFunction] entries are bare names
            // (no '@'), so we leave them alone. This rule must NOT depend on the current
            // contents of RemoteFunctionRegistry: the reconcile loop above already cleared
            // bare names that are gone from this sync (offline / disabled / removed), and
            // if we tied the check to the registry we'd LEAVE the stale qualified key in
            // TickerFunctions because the predicate returned false. That was the bug —
            // disabled functions kept getting dispatched even though the registry was clean.
            var atIdx = key.IndexOf('@');
            return atIdx > 0 && atIdx < key.Length - 1;
        }

        // One coherent publication: remote execution functions AND their canonical descriptors are
        // rewritten together in a single snapshot, so execution and wire-metadata readers never see
        // a split generation. The legacy request-info mirror is refreshed afterwards purely as a
        // compatibility adapter for non-atomic callers.
        TickerFunctionProvider.MergeRemoteSnapshot(remoteFunctionDict, remoteDescriptorDict, IsCurrentlyRemote);
        TickerFunctionProvider.MergeRemoteRequestInfo(remoteRequestInfoDict, IsCurrentlyRemote);
        _logger?.LogInformation(
            "Merged {RemoteCount} remote functions (local source-gen entries preserved)",
            remoteFunctionDict.Count);

        if (_migrateDefinedCronTickers != null)
        {
            await _migrateDefinedCronTickers(
                cronSeeds.ToArray(),
                cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation("Migrated {Count} cron tickers", cronSeeds.Count);
        }
    }

    /// <summary>
    /// Builds the canonical wire request contract for a remote function from the Hub's metadata.
    /// Returns null for request-less functions (absence is never encoded as empty metadata).
    /// <para>
    /// Transport policy: request-bearing remote registrations MUST carry a canonical Draft 2020-12
    /// full schema. A canonical contract without a schema, and a legacy (<c>request_type</c>-only)
    /// descriptor with no canonical schema, are both rejected here — <b>before</b> the merge — so a
    /// schema-less request-bearing descriptor can never reach publication. Request-less descriptors
    /// (no canonical contract and no legacy request type) are the only ones that yield a null contract.
    /// </para>
    /// </summary>
    private static TickerRequestContract? BuildRemoteRequestContract(
        string qualifiedName,
        TickerQ.RemoteExecutor.Hub.RequestContract? canonical,
        string? legacyRequestType,
        string? legacyRequestExampleJson)
    {
        if (canonical == null)
        {
            // No canonical contract on the wire. A legacy request-bearing descriptor (request_type set)
            // without a canonical full schema is rejected; request-less descriptors are fine.
            if (!string.IsNullOrWhiteSpace(legacyRequestType))
                throw new InvalidOperationException(
                    $"Remote function '{qualifiedName}' declares a legacy request type '{legacyRequestType}' " +
                    "but carries no canonical request schema. Request-bearing remote registrations must supply " +
                    "a full JSON Schema Draft 2020-12 contract.");
            return null;
        }

        // Canonical contract present ⇒ request-bearing ⇒ a full schema is mandatory.
        if (!canonical.HasSchemaJson || string.IsNullOrWhiteSpace(canonical.SchemaJson))
            throw new InvalidOperationException(
                $"Remote function '{qualifiedName}' has a request contract without a schema. Request-bearing " +
                "remote registrations must supply a full JSON Schema Draft 2020-12 contract.");

        var examples = canonical.Examples.Select(example =>
            new TickerRequestExample(example.Key, example.HasSummary ? example.Summary : null, example.ValueJson))
            .ToArray();
        var request = new TickerRequestContract(
            canonical.TypeName,
            canonical.MediaType,
            canonical.Required,
            canonical.SchemaDialect,
            canonical.SchemaJson,
            examples: examples);
        return request;
    }
}
