using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities
{
    public delegate Task TickerFunctionDelegate(CancellationToken cancellationToken, IServiceProvider serviceProvider, TickerFunctionContext context);

    /// <summary>
    /// Provider for managing ticker functions and their request types using FrozenDictionary.
    /// Uses a callback-based approach to collect all registrations and create a single optimized FrozenDictionary.
    /// </summary>
    public static class TickerFunctionProvider
    {
        private static readonly object _buildLock = new();

        // Callback actions to collect registrations
        private static Action<Dictionary<string, (string, Type)>> _requestTypeRegistrations;
        private static Action<Dictionary<string, (string RequestType, string RequestExampleJson)>> _requestInfoRegistrations;
        private static Action<
            Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>,
            Dictionary<string, string>> _functionRegistrations;
        private static Action<Dictionary<string, TickerRuntimeRequestMetadata>> _runtimeRequestRegistrations;
        private static Action<Dictionary<string, TickerRuntimeResultMetadata>> _runtimeResultRegistrations;

        // Pending canonical descriptor registrations, snapshotted at registration time with provenance.
        private static readonly List<(TickerFunctionDescriptor Descriptor, string Origin)> _pendingDescriptors = new();

        // Type → function name mapping for manager.AddAsync<T>() lookups
        private static readonly Dictionary<Type, string> _typeMappings = new();

        // Legacy compatibility mirror fields. These are assigned individually and are NOT claimed to
        // be atomically co-published; the atomic unit is the snapshot below. Retained only so existing
        // callers keep compiling/reading.
        public static FrozenDictionary<string, (string, Type)> TickerFunctionRequestTypes = FrozenDictionary<string, (string, Type)>.Empty;
        public static FrozenDictionary<string, (string RequestType, string RequestExampleJson)> TickerFunctionRequestInfos = FrozenDictionary<string, (string RequestType, string RequestExampleJson)>.Empty;
        public static FrozenDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> TickerFunctions = FrozenDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>.Empty;

        // The one canonical, self-consistent registry snapshot — published as a single unit.
        private static TickerFunctionRegistrySnapshot _snapshot = TickerFunctionRegistrySnapshot.Empty;

        /// <summary>The current canonical registry snapshot (execution functions + descriptors + runtime).</summary>
        internal static TickerFunctionRegistrySnapshot Snapshot => Volatile.Read(ref _snapshot);

        /// <summary>
        /// Canonical, immutable request-contract descriptors keyed by function name — the single wire
        /// metadata source shared by local, dashboard, Hub, and SDK paths. Read from the one registry
        /// snapshot; never reconstructed by joining the legacy mirror fields. Request-less functions
        /// have <c>Request == null</c>.
        /// </summary>
        public static FrozenDictionary<string, TickerFunctionDescriptor> TickerFunctionDescriptors => Snapshot.Descriptors;

        /// <summary>
        /// Internal runtime execution metadata (CLR Type + optional JsonTypeInfo) keyed by function
        /// name, read from the one registry snapshot. Kept separate from the wire descriptors so
        /// runtime/serializer metadata never crosses the transport.
        /// </summary>
        internal static FrozenDictionary<string, TickerRuntimeRequestMetadata> RuntimeRequests => Snapshot.RuntimeRequests;
        internal static FrozenDictionary<string, TickerRuntimeResultMetadata> RuntimeResults => Snapshot.RuntimeResults;

        public static bool IsBuilt { get; private set; }

        /// <summary>
        /// Optional resolver returning the owning node name for a function. Set by the
        /// RemoteExecutor during startup so scheduling code can persist which SDK node
        /// a ticker belongs to. Returns empty string for locally-registered functions.
        /// </summary>
        public static Func<string, string> FunctionNodeResolver { get; set; }

        /// <summary>Resolves the owning node name for a function, or null if unknown.</summary>
        public static string ResolveNodeName(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName)) return null;
            var resolver = FunctionNodeResolver;
            if (resolver == null) return null;
            var name = resolver(functionName);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>
        /// Registers a Type → function name mapping for type-safe manager lookups.
        /// Called by MapTicker&lt;T&gt;() at registration time.
        /// </summary>
        public static void RegisterTypeMapping(Type type, string functionName)
        {
            _typeMappings[type] = functionName;
        }

        /// <summary>
        /// Gets the function name registered for a type, or falls back to Type.Name.
        /// Used by manager.AddAsync&lt;T&gt;() to resolve function names without strings.
        /// </summary>
        public static string GetFunctionName<T>()
        {
            return _typeMappings.TryGetValue(typeof(T), out var name) ? name : typeof(T).Name;
        }

        /// <summary>
        /// Replaces the entire frozen function registry with the provided dictionary.
        /// Unlike <see cref="RegisterFunctions"/>, this REMOVES entries that are absent
        /// from the new set — required for resync-style flows (e.g. RemoteExecutor pulling
        /// the latest active functions from the Hub after a toggle).
        ///
        /// <para><b>Prefer <see cref="MergeRemoteFunctions"/></b> when you only want to
        /// rewrite the remote slice and leave local source-gen registrations alone.
        /// Calling <c>ReplaceFunctions</c> with only the remote slice will wipe the
        /// local source-gen <c>[TickerFunction]</c> entries, which is almost always a bug.</para>
        /// </summary>
        public static void ReplaceFunctions(IDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> functions)
        {
            if (functions == null) throw new ArgumentNullException(nameof(functions));
            lock (_buildLock)
            {
                TickerFunctions = functions.ToFrozenDictionary();
                IsBuilt = true;
            }
        }

        /// <summary>
        /// Replaces the request-info dictionary entirely. See <see cref="ReplaceFunctions"/>.
        /// </summary>
        public static void ReplaceRequestInfo(IDictionary<string, (string RequestType, string RequestExampleJson)> infos)
        {
            if (infos == null) throw new ArgumentNullException(nameof(infos));
            lock (_buildLock)
            {
                TickerFunctionRequestInfos = infos.ToFrozenDictionary();
            }
        }

        /// <summary>
        /// Rewrites only the <i>remote</i> slice of the function registry, preserving
        /// every entry that <paramref name="isCurrentlyRemote"/> returns <c>false</c> for.
        /// Used by the RemoteExecutor's periodic Hub sync — it knows which keys belong to
        /// remote SDK nodes (via <c>RemoteFunctionRegistry</c>) and replaces only those,
        /// so local <c>[TickerFunction]</c> registrations from source-gen are never wiped.
        /// </summary>
        /// <param name="remoteFunctions">Fresh remote slice from the Hub. Keyed by the qualified name (<c>bare@node</c>).</param>
        /// <param name="isCurrentlyRemote">Returns true for keys currently tracked as remote. Those entries are dropped before the new slice is applied.</param>
        public static void MergeRemoteFunctions(
            IDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> remoteFunctions,
            Func<string, bool> isCurrentlyRemote)
        {
            if (remoteFunctions == null) throw new ArgumentNullException(nameof(remoteFunctions));
            if (isCurrentlyRemote == null) throw new ArgumentNullException(nameof(isCurrentlyRemote));

            lock (_buildLock)
            {
                var merged = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>(TickerFunctions.Count + remoteFunctions.Count);

                // Keep only entries NOT currently flagged as remote — that's the local slice.
                foreach (var (k, v) in TickerFunctions)
                {
                    if (!isCurrentlyRemote(k)) merged[k] = v;
                }

                // Apply the fresh remote slice (overwrites if a key collides with a local one;
                // remote-wins-on-collision is intentional because the qualified key shape
                // ("bare@node") shouldn't collide with a bare local name in the first place).
                foreach (var (k, v) in remoteFunctions)
                {
                    merged[k] = v;
                }

                TickerFunctions = merged.ToFrozenDictionary();
                IsBuilt = true;
            }
        }

        /// <summary>Same as <see cref="MergeRemoteFunctions"/> but for the request-info table.</summary>
        public static void MergeRemoteRequestInfo(
            IDictionary<string, (string RequestType, string RequestExampleJson)> remoteInfos,
            Func<string, bool> isCurrentlyRemote)
        {
            if (remoteInfos == null) throw new ArgumentNullException(nameof(remoteInfos));
            if (isCurrentlyRemote == null) throw new ArgumentNullException(nameof(isCurrentlyRemote));

            lock (_buildLock)
            {
                var merged = new Dictionary<string, (string RequestType, string RequestExampleJson)>(TickerFunctionRequestInfos.Count + remoteInfos.Count);

                foreach (var (k, v) in TickerFunctionRequestInfos)
                {
                    if (!isCurrentlyRemote(k)) merged[k] = v;
                }

                foreach (var (k, v) in remoteInfos)
                {
                    merged[k] = v;
                }

                TickerFunctionRequestInfos = merged.ToFrozenDictionary();
            }
        }

        /// <summary>
        /// Atomically applies a remote sync snapshot: rewrites the remote slice of BOTH the function
        /// registry and the canonical descriptor registry together, preserving every local entry
        /// (those for which <paramref name="isCurrentlyRemote"/> returns false). No schema hashing,
        /// downgrade, or payload validation is performed here.
        /// </summary>
        public static void MergeRemoteSnapshot(
            IDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> remoteFunctions,
            IDictionary<string, TickerFunctionDescriptor> remoteDescriptors,
            Func<string, bool> isCurrentlyRemote)
        {
            if (remoteFunctions == null) throw new ArgumentNullException(nameof(remoteFunctions));
            if (remoteDescriptors == null) throw new ArgumentNullException(nameof(remoteDescriptors));
            if (isCurrentlyRemote == null) throw new ArgumentNullException(nameof(isCurrentlyRemote));

            // Snapshot + validate the remote inputs BEFORE any publication: keys must match descriptor
            // names, and every remote function must have a matching descriptor and vice versa.
            var remoteFn = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>(remoteFunctions);
            var remoteDesc = new Dictionary<string, TickerFunctionDescriptor>(remoteDescriptors);

            foreach (var (key, descriptor) in remoteDesc)
            {
                if (descriptor == null)
                    throw new ArgumentException($"Null remote descriptor for '{key}'.", nameof(remoteDescriptors));
                if (!string.Equals(key, descriptor.FunctionName, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Remote descriptor key '{key}' does not match descriptor FunctionName '{descriptor.FunctionName}'.",
                        nameof(remoteDescriptors));
                if (!remoteFn.ContainsKey(key))
                    throw new ArgumentException($"Remote descriptor '{key}' has no matching remote function.", nameof(remoteDescriptors));
            }
            foreach (var key in remoteFn.Keys)
            {
                if (!remoteDesc.ContainsKey(key))
                    throw new ArgumentException($"Remote function '{key}' has no matching remote descriptor.", nameof(remoteFunctions));

                // Every incoming key MUST classify as remote. Otherwise a payload keyed with a local
                // name would be preserved-then-overwritten below, silently redirecting local execution.
                // Reject before any publication so local functions/descriptors stay untouched.
                if (!isCurrentlyRemote(key))
                    throw new ArgumentException(
                        $"Incoming remote key '{key}' is not classified as remote; refusing to overwrite a local entry.",
                        nameof(remoteFunctions));
            }

            lock (_buildLock)
            {
                var current = Snapshot;

                var mergedFunctions = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>(current.Functions.Count + remoteFn.Count);
                foreach (var (k, v) in current.Functions)
                    if (!isCurrentlyRemote(k)) mergedFunctions[k] = v;
                foreach (var (k, v) in remoteFn)
                    mergedFunctions[k] = v;

                var mergedDescriptors = new Dictionary<string, TickerFunctionDescriptor>(current.Descriptors.Count + remoteDesc.Count);
                foreach (var (k, v) in current.Descriptors)
                    if (!isCurrentlyRemote(k)) mergedDescriptors[k] = v;
                foreach (var (k, v) in remoteDesc)
                    // Reconcile scheduling metadata from the matching remote execution entry.
                    mergedDescriptors[k] = v.WithSchedule(remoteFn[k].Priority, remoteFn[k].cronExpression);

                // Preserve local runtime metadata (remote functions carry no CLR Type).
                var mergedRuntime = new Dictionary<string, TickerRuntimeRequestMetadata>(current.RuntimeRequests.Count);
                foreach (var (k, v) in current.RuntimeRequests)
                    if (!isCurrentlyRemote(k)) mergedRuntime[k] = v;
                var mergedResults = new Dictionary<string, TickerRuntimeResultMetadata>(current.RuntimeResults.Count);
                foreach (var (k, v) in current.RuntimeResults)
                    if (!isCurrentlyRemote(k)) mergedResults[k] = v;

                // Publish one coherent canonical snapshot, then update the legacy function mirror.
                Volatile.Write(ref _snapshot, new TickerFunctionRegistrySnapshot(
                    mergedFunctions.ToFrozenDictionary(),
                    mergedDescriptors.ToFrozenDictionary(),
                    mergedRuntime.ToFrozenDictionary(),
                    mergedResults.ToFrozenDictionary()));
                TickerFunctions = mergedFunctions.ToFrozenDictionary();
                IsBuilt = true;
            }
        }

        /// <summary>
        /// Removes a single function from the one canonical registry snapshot — dropping its execution
        /// entry, wire descriptor, and runtime metadata together — and republishes atomically via a
        /// single <c>Volatile.Write</c>. Used by the RemoteExecutor when the Hub pushes a
        /// <c>RemoveFunction</c> webhook, so no reader can ever observe a snapshot where the function
        /// survives in one table but not another. The legacy request mirrors are kept coherent too.
        /// Returns <c>false</c> (and publishes nothing) if the function was not present.
        /// </summary>
        public static bool UnregisterRemoteFunction(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName)) return false;

            lock (_buildLock)
            {
                var current = Snapshot;
                if (!current.Functions.ContainsKey(functionName)) return false;

                var functions = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>(current.Functions);
                functions.Remove(functionName);

                var descriptors = new Dictionary<string, TickerFunctionDescriptor>(current.Descriptors);
                descriptors.Remove(functionName);

                var runtime = new Dictionary<string, TickerRuntimeRequestMetadata>(current.RuntimeRequests);
                runtime.Remove(functionName);
                var runtimeResults = new Dictionary<string, TickerRuntimeResultMetadata>(current.RuntimeResults);
                runtimeResults.Remove(functionName);

                var frozenFunctions = functions.ToFrozenDictionary();

                // Publish one coherent canonical snapshot, then keep the legacy mirrors in step.
                Volatile.Write(ref _snapshot, new TickerFunctionRegistrySnapshot(
                    frozenFunctions, descriptors.ToFrozenDictionary(), runtime.ToFrozenDictionary(), runtimeResults.ToFrozenDictionary()));
                TickerFunctions = frozenFunctions;

                if (TickerFunctionRequestInfos.ContainsKey(functionName))
                {
                    var infos = new Dictionary<string, (string RequestType, string RequestExampleJson)>(TickerFunctionRequestInfos);
                    infos.Remove(functionName);
                    TickerFunctionRequestInfos = infos.ToFrozenDictionary();
                }
                if (TickerFunctionRequestTypes.ContainsKey(functionName))
                {
                    var types = new Dictionary<string, (string, Type)>(TickerFunctionRequestTypes);
                    types.Remove(functionName);
                    TickerFunctionRequestTypes = types.ToFrozenDictionary();
                }

                IsBuilt = true;
                return true;
            }
        }

        /// <summary>
        /// Registers ticker functions during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="functions">The functions to register. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
        public static void RegisterFunctions(
            IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> functions,
            string origin = null,
            [CallerMemberName] string callerMember = null,
            [CallerFilePath] string callerFile = null)
        {
            if (functions == null)
                throw new ArgumentNullException(nameof(functions));
            
            if (functions.Count == 0)
                return;

            // Snapshot the caller's dictionary immediately so later mutation cannot affect Build.
            var snapshot = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>(functions);
            var registrationOrigin = ResolveRegistrationOrigin(origin, callerMember, callerFile);

            lock (_buildLock)
            {
                _functionRegistrations += (dict, origins) =>
                    StageExecutionRegistration(dict, origins, snapshot, registrationOrigin);
            }
        }

        /// <summary>
        /// Registers ticker functions with capacity hint during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="functions">The functions to register. Cannot be null.</param>
        /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
        /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
        public static void RegisterFunctions(
            IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> functions,
            int _,
            string origin = null,
            [CallerMemberName] string callerMember = null,
            [CallerFilePath] string callerFile = null)
        {
            // For callback approach, capacity is calculated automatically in Build()
            RegisterFunctions(functions, origin, callerMember, callerFile);
        }

        private static string ResolveRegistrationOrigin(string origin, string callerMember, string callerFile)
        {
            if (!string.IsNullOrWhiteSpace(origin))
                return origin;

            var fileName = string.IsNullOrWhiteSpace(callerFile) ? null : Path.GetFileName(callerFile);
            if (!string.IsNullOrWhiteSpace(callerMember) && fileName != null)
                return $"{callerMember} ({fileName})";
            if (!string.IsNullOrWhiteSpace(callerMember))
                return callerMember;
            return fileName ?? "unspecified";
        }

        private static void StageExecutionRegistration(
            IDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> functions,
            IDictionary<string, string> origins,
            IReadOnlyDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> registration,
            string origin)
        {
            foreach (var (name, candidate) in registration)
            {
                if (!functions.TryGetValue(name, out var existing))
                {
                    functions.Add(name, candidate);
                    origins[name] = origin;
                    continue;
                }

                var identical = string.Equals(existing.cronExpression, candidate.cronExpression, StringComparison.Ordinal)
                    && existing.Priority == candidate.Priority
                    && existing.MaxConcurrency == candidate.MaxConcurrency
                    && Equals(existing.Delegate, candidate.Delegate);
                if (identical)
                    continue;

                throw new InvalidOperationException(
                    $"Conflicting execution registrations for function '{name}': " +
                    $"[{origins[name]}] conflicts with [{origin}].");
            }
        }

        /// <summary>
        /// Registers request types during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="requestTypes">The request types to register. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
        public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes)
        {
            if (requestTypes == null)
                throw new ArgumentNullException(nameof(requestTypes));
            
            if (requestTypes.Count == 0)
                return;

            var snapshot = new Dictionary<string, (string, Type)>(requestTypes);

            lock (_buildLock)
            {
                _requestTypeRegistrations += dict =>
                {
                    foreach (var (key, value) in snapshot)
                    {
                        dict.TryAdd(key, value); // Preserves existing entries
                    }
                };
            }
        }

        /// <summary>
        /// Registers request types with capacity hint during application startup by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="requestTypes">The request types to register. Cannot be null.</param>
        /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
        /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
        public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes, int _)
        {
            // For callback approach, capacity is calculated automatically in Build()
            RegisterRequestType(requestTypes);
        }

        /// <summary>
        /// Registers source-generated JSON metadata used by both request producers and generated
        /// delegates. Runtime metadata stays local and never crosses the descriptor wire contract.
        /// </summary>
        public static void RegisterRequestTypeInfo(
            IDictionary<string, (Type RequestType, JsonTypeInfo JsonTypeInfo)> requests)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            if (requests.Count == 0)
                return;

            var snapshot = new Dictionary<string, (Type RequestType, JsonTypeInfo JsonTypeInfo)>(requests);
            foreach (var (name, entry) in snapshot)
            {
                if (entry.RequestType == null)
                    throw new ArgumentException($"Null request type registered for function '{name}'.", nameof(requests));
                if (entry.JsonTypeInfo == null)
                    throw new ArgumentException($"Null JsonTypeInfo registered for function '{name}'.", nameof(requests));
                if (entry.JsonTypeInfo.Type != entry.RequestType)
                    throw new ArgumentException(
                        $"JsonTypeInfo type '{entry.JsonTypeInfo.Type}' does not match request type '{entry.RequestType}' for function '{name}'.",
                        nameof(requests));
            }

            lock (_buildLock)
            {
                _runtimeRequestRegistrations += dict =>
                {
                    foreach (var (name, entry) in snapshot)
                    {
                        if (dict.TryGetValue(name, out var existing)
                            && existing.RequestType != entry.RequestType)
                            throw new InvalidOperationException(
                                $"Conflicting runtime request types for function '{name}': '{existing.RequestType}' and '{entry.RequestType}'.");

                        dict[name] = new TickerRuntimeRequestMetadata(entry.RequestType, entry.JsonTypeInfo);
                    }
                };
            }
        }

        internal static bool TryGetRequestTypeInfo<T>(string functionName, out JsonTypeInfo<T> typeInfo)
        {
            if (functionName != null
                && RuntimeRequests.TryGetValue(functionName, out var metadata)
                && metadata.JsonTypeInfo is JsonTypeInfo<T> typed)
            {
                typeInfo = typed;
                return true;
            }

            typeInfo = null;
            return false;
        }

        /// <summary>Returns the resolved serializer metadata for a registered typed function.</summary>
        public static JsonTypeInfo<T> GetRequestTypeInfo<T>(string functionName)
        {
            if (TryGetRequestTypeInfo<T>(functionName, out var typeInfo))
                return typeInfo;

            throw new InvalidOperationException(
                $"No JsonTypeInfo<{typeof(T)}> is registered for ticker function '{functionName}'. " +
                "Ensure TickerFunctionProvider.Build() completed and the configured JSON context includes the request type.");
        }

        /// <summary>
        /// Defers JsonTypeInfo resolution until Build(), after AddTickerQ has finalized request JSON
        /// options. This lets source-generated registrations consume a user-provided AOT context.
        /// </summary>
        public static void RegisterRequestTypeInfoResolver(
            IDictionary<string, (Type RequestType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)> requests)
        {
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            if (requests.Count == 0)
                return;

            var snapshot = new Dictionary<string, (Type RequestType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)>(requests);
            foreach (var (name, entry) in snapshot)
            {
                if (entry.RequestType == null)
                    throw new ArgumentException($"Null request type registered for function '{name}'.", nameof(requests));
                if (entry.Resolve == null)
                    throw new ArgumentException($"Null JsonTypeInfo resolver registered for function '{name}'.", nameof(requests));
            }

            lock (_buildLock)
            {
                _runtimeRequestRegistrations += dict =>
                {
                    var options = TickerHelper.GetEffectiveRequestJsonSerializerOptions();
                    foreach (var (name, entry) in snapshot)
                    {
                        JsonTypeInfo typeInfo;
                        try
                        {
                            typeInfo = entry.Resolve(options);
                        }
                        catch (NotSupportedException exception)
                        {
                            throw new InvalidOperationException(
                                $"No JsonTypeInfo was available for request type '{entry.RequestType}' of function '{name}'. " +
                                "For Native AOT, configure AddTickerQ with WithJsonContext using a context that includes every request type.",
                                exception);
                        }

                        if (typeInfo == null)
                            throw new InvalidOperationException(
                                $"No JsonTypeInfo was available for request type '{entry.RequestType}' of function '{name}'. " +
                                "For Native AOT, configure AddTickerQ with WithJsonContext using a context that includes every request type.");
                        if (typeInfo.Type != entry.RequestType)
                            throw new InvalidOperationException(
                                $"Resolved JsonTypeInfo type '{typeInfo.Type}' does not match request type '{entry.RequestType}' for function '{name}'.");

                        if (dict.TryGetValue(name, out var existing)
                            && existing.RequestType != entry.RequestType)
                            throw new InvalidOperationException(
                                $"Conflicting runtime request types for function '{name}': '{existing.RequestType}' and '{entry.RequestType}'.");

                        dict[name] = new TickerRuntimeRequestMetadata(entry.RequestType, typeInfo);
                    }
                };
            }
        }

        /// <summary>
        /// Defers source-generated result JsonTypeInfo resolution until Build(). No reflection fallback
        /// is used by generated result publication.
        /// </summary>
        public static void RegisterResultTypeInfoResolver(
            IDictionary<string, (Type ResultType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (results.Count == 0) return;

            var snapshot = new Dictionary<string, (Type ResultType, Func<JsonSerializerOptions, JsonTypeInfo> Resolve)>(results);
            foreach (var (name, entry) in snapshot)
            {
                if (entry.ResultType == null)
                    throw new ArgumentException($"Null result type registered for function '{name}'.", nameof(results));
                if (entry.Resolve == null)
                    throw new ArgumentException($"Null result JsonTypeInfo resolver registered for function '{name}'.", nameof(results));
            }

            lock (_buildLock)
            {
                _runtimeResultRegistrations += dict =>
                {
                    var options = TickerHelper.GetEffectiveRequestJsonSerializerOptions();
                    foreach (var (name, entry) in snapshot)
                    {
                        JsonTypeInfo typeInfo;
                        try
                        {
                            typeInfo = entry.Resolve(options);
                        }
                        catch (NotSupportedException exception)
                        {
                            throw new InvalidOperationException(
                                $"No JsonTypeInfo was available for result type '{entry.ResultType}' of function '{name}'. " +
                                "For Native AOT, configure AddTickerQ with WithJsonContext using a context that includes every declared result type.",
                                exception);
                        }

                        if (typeInfo == null || typeInfo.Type != entry.ResultType)
                            throw new InvalidOperationException(
                                $"Resolved result JsonTypeInfo does not match result type '{entry.ResultType}' for function '{name}'.");
                        if (dict.TryGetValue(name, out var existing) && existing.ResultType != entry.ResultType)
                            throw new InvalidOperationException(
                                $"Conflicting runtime result types for function '{name}': '{existing.ResultType}' and '{entry.ResultType}'.");
                        dict[name] = new TickerRuntimeResultMetadata(entry.ResultType, typeInfo);
                    }
                };
            }
        }

        /// <summary>Returns AOT-safe serializer metadata for a declared function result.</summary>
        public static JsonTypeInfo<T> GetResultTypeInfo<T>(string functionName)
        {
            if (functionName != null
                && RuntimeResults.TryGetValue(functionName, out var metadata)
                && metadata.JsonTypeInfo is JsonTypeInfo<T> typed)
                return typed;

            throw new InvalidOperationException(
                $"No JsonTypeInfo<{typeof(T)}> is registered for ticker function result '{functionName}'. " +
                "Ensure Build() completed and WithJsonContext includes the declared result type.");
        }

        /// <summary>Returns the canonical wire contract for a declared function result.</summary>
        public static TickerResultContract GetResultContract(string functionName)
        {
            if (functionName != null
                && TickerFunctionDescriptors.TryGetValue(functionName, out var descriptor)
                && !string.IsNullOrWhiteSpace(descriptor.Result?.ContractId))
                return descriptor.Result;

            throw new InvalidOperationException(
                $"No canonical result contract id is registered for ticker function '{functionName}'. " +
                "Ensure Build() completed and the declared result has a generated schema.");
        }

        /// <summary>
        /// Registers request type metadata (string type + example JSON) for functions.
        /// </summary>
        /// <param name="requestInfos">The request info entries to register. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when requestInfos parameter is null.</exception>
        public static void RegisterRequestInfo(IDictionary<string, (string RequestType, string RequestExampleJson)> requestInfos)
        {
            if (requestInfos == null)
                throw new ArgumentNullException(nameof(requestInfos));

            if (requestInfos.Count == 0)
                return;

            var snapshot = new Dictionary<string, (string RequestType, string RequestExampleJson)>(requestInfos);

            lock (_buildLock)
            {
                _requestInfoRegistrations += dict =>
                {
                    foreach (var (key, value) in snapshot)
                    {
                        dict.TryAdd(key, value);
                    }
                };
            }
        }

        /// <summary>
        /// Registers canonical request-contract descriptors during application startup by adding to
        /// the callback chain. Every typed registration path (source-generated attribute, interface,
        /// fluent, Hub, SDK) should publish through here so the frozen descriptor registry stays the
        /// single source of wire metadata. Must be called before <see cref="Build"/>.
        ///
        /// <para>Re-registering the same function with an <b>equal</b> descriptor is idempotent.
        /// Re-registering with an <b>incompatible</b> descriptor throws during <see cref="Build"/>,
        /// with a diagnostic naming both registrations.</para>
        /// </summary>
        /// <param name="descriptors">The descriptors to register. Cannot be null.</param>
        /// <param name="origin">
        /// Optional provenance label reported in conflict diagnostics (e.g. "source-gen", "fluent-map").
        /// Defaults to a deterministic "unspecified".
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when descriptors parameter is null.</exception>
        public static void RegisterDescriptors(IDictionary<string, TickerFunctionDescriptor> descriptors, string origin = null)
        {
            if (descriptors == null)
                throw new ArgumentNullException(nameof(descriptors));

            if (descriptors.Count == 0)
                return;

            origin = string.IsNullOrWhiteSpace(origin) ? "unspecified" : origin;

            // Fail fast on structurally invalid entries so the diagnostic points at the caller,
            // not at a later Build(). The key must agree with the descriptor's own FunctionName.
            foreach (var (key, value) in descriptors)
            {
                if (value == null)
                    throw new ArgumentException($"Null descriptor registered for function '{key}'.", nameof(descriptors));

                if (!string.Equals(key, value.FunctionName, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Descriptor key '{key}' does not match descriptor FunctionName '{value.FunctionName}'.",
                        nameof(descriptors));
            }

            lock (_buildLock)
            {
                // Snapshot the input immediately (descriptors are immutable; copying the entries
                // detaches us from later mutation of the caller's dictionary).
                foreach (var (_, value) in descriptors)
                    _pendingDescriptors.Add((value, origin));
            }
        }

        /// <summary>
        /// Configures an already-registered function's settings (cron, priority, concurrency).
        /// Called by MapTicker&lt;T&gt;() at runtime to override source-generator defaults.
        /// Must be called before Build().
        /// </summary>
        public static void Configure(string functionName, string cronExpression = null, TickerTaskPriority? priority = null, int? maxConcurrency = null)
        {
            _functionRegistrations += (dict, _) =>
            {
                if (!dict.TryGetValue(functionName, out var existing))
                    return;

                dict[functionName] = (
                    cronExpression ?? existing.cronExpression,
                    priority ?? existing.Priority,
                    existing.Delegate,
                    maxConcurrency ?? existing.MaxConcurrency
                );
            };
        }

        /// <summary>
        /// Updates cron expressions for registered functions by adding to the callback chain.
        /// This method should only be called during application startup before Build() is called.
        /// </summary>
        /// <param name="configuration">IConfiguration to update based on path</param>
        /// <exception cref="ArgumentNullException">Thrown when cronUpdates parameter is null.</exception>
        internal static void UpdateCronExpressionsFromIConfiguration(IConfiguration configuration)
        {
            lock (_buildLock)
            {
                _functionRegistrations += (dict, _) =>
                {
                    foreach (var (key, value) in dict)
                    {
                        if (value.cronExpression.StartsWith('%'))
                        {
                            var configKey = value.cronExpression.Trim('%');
                            var mappedCronExpression = configuration[configKey];

                            if (!string.IsNullOrEmpty(mappedCronExpression))
                            {
                                dict[key] = (mappedCronExpression, value.Priority, value.Delegate,
                                             value.MaxConcurrency);
                            }
                        }
                    }
                };
            }
        }

        /// <summary>
        /// Builds the final FrozenDictionaries by executing all callbacks with optimal capacity.
        /// Uses a single-pass approach: directly creates optimally-sized dictionaries and populates them.
        /// This method should be called once after all registration is complete.
        /// After calling this method, no more registrations should be made.
        /// </summary>
        public static void Build()
        {
            lock (_buildLock)
            {
                // ---- STAGE everything into locals. No published field is touched and no pending
                // registration is cleared until every stage below has succeeded, so any conflict or
                // callback failure leaves ALL registries, IsBuilt, and ALL pending registrations
                // unchanged — a retry sees exactly the same registrations.
                var functionsDict = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>(TickerFunctions);
                var functionOrigins = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in functionsDict.Keys)
                    functionOrigins[name] = "prior-build";
                _functionRegistrations?.Invoke(functionsDict, functionOrigins);

                var requestTypesDict = new Dictionary<string, (string, Type)>(TickerFunctionRequestTypes);
                _requestTypeRegistrations?.Invoke(requestTypesDict);

                var requestInfoDict = new Dictionary<string, (string RequestType, string RequestExampleJson)>(TickerFunctionRequestInfos);
                _requestInfoRegistrations?.Invoke(requestInfoDict);

                // Compile-time schemas are valid only for the source generator's immutable serializer
                // profile. Reject shape-changing global options rather than publish a false contract.
                foreach (var pending in _pendingDescriptors)
                {
                    if (string.Equals(pending.Origin, "source-gen", StringComparison.Ordinal)
                        && (pending.Descriptor.Request?.Schema.HasValue == true
                            || pending.Descriptor.Result?.Schema.HasValue == true))
                    {
                        TickerHelper.ValidateGeneratedSchemaSerializerProfile();
                        break;
                    }
                }

                // Descriptors: fold pending (conflict-checked by provenance), then reconcile
                // scheduling metadata with the authoritative functions registry.
                var descriptorDict = StageDescriptors(functionsDict);

                // Runtime execution metadata (Type + optional JsonTypeInfo) — adapt legacy Type registry,
                // then overlay source-generated metadata for the same function.
                var runtimeDict = new Dictionary<string, TickerRuntimeRequestMetadata>(requestTypesDict.Count);
                foreach (var (name, entry) in requestTypesDict)
                    runtimeDict[name] = new TickerRuntimeRequestMetadata(entry.Item2);
                _runtimeRequestRegistrations?.Invoke(runtimeDict);

                // Unify every typed registration path (source-generated, interface, fluent, Hub/SDK):
                // resolve any still-missing metadata from the final configured serializer profile.
                // With reflection disabled this fails at startup unless WithJsonContext includes the type,
                // rather than deferring a reflection failure until a ticker executes.
                JsonSerializerOptions effectiveRequestOptions = null;
                foreach (var (name, metadata) in runtimeDict.ToArray())
                {
                    if (metadata.JsonTypeInfo != null)
                        continue;

                    effectiveRequestOptions ??= TickerHelper.GetEffectiveRequestJsonSerializerOptions();
                    try
                    {
                        var typeInfo = effectiveRequestOptions.GetTypeInfo(metadata.RequestType);
                        runtimeDict[name] = new TickerRuntimeRequestMetadata(metadata.RequestType, typeInfo);
                    }
                    catch (NotSupportedException exception)
                    {
                        throw new InvalidOperationException(
                            $"No JsonTypeInfo was available for request type '{metadata.RequestType}' of function '{name}'. " +
                            "Configure AddTickerQ with WithJsonContext using a context that includes every request type.",
                            exception);
                    }
                }

                ValidateGeneratedSchemaRuntimeMetadata(descriptorDict, runtimeDict);

                var runtimeResultDict = new Dictionary<string, TickerRuntimeResultMetadata>(Snapshot.RuntimeResults);
                _runtimeResultRegistrations?.Invoke(runtimeResultDict);
                foreach (var (name, descriptor) in descriptorDict)
                {
                    if (descriptor.Result == null)
                        continue;
                    if (!runtimeResultDict.TryGetValue(name, out var metadata)
                        || metadata.JsonTypeInfo == null
                        || !string.Equals(metadata.ResultType.FullName, descriptor.Result.TypeName, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Declared result contract for function '{name}' has no matching source-generated JsonTypeInfo metadata.");
                }

                ValidateGeneratedResultSchemaRuntimeMetadata(runtimeResultDict);

                // ---- FREEZE (still no publish; ToFrozenDictionary can't fail for valid dicts).
                var frozenFunctions = functionsDict.ToFrozenDictionary();
                var frozenTypes = requestTypesDict.ToFrozenDictionary();
                var frozenInfos = requestInfoDict.ToFrozenDictionary();
                var frozenDescriptors = descriptorDict.ToFrozenDictionary();
                var frozenRuntime = runtimeDict.ToFrozenDictionary();
                var frozenRuntimeResults = runtimeResultDict.ToFrozenDictionary();

                // ---- PUBLISH the one canonical snapshot via a single Volatile.Write, then update the
                // legacy mirror fields (assigned individually — NOT claimed atomic), then clear pending.
                Volatile.Write(ref _snapshot, new TickerFunctionRegistrySnapshot(frozenFunctions, frozenDescriptors, frozenRuntime, frozenRuntimeResults));
                TickerFunctions = frozenFunctions;
                TickerFunctionRequestTypes = frozenTypes;
                TickerFunctionRequestInfos = frozenInfos;
                IsBuilt = true;

                _functionRegistrations = null;
                _requestTypeRegistrations = null;
                _requestInfoRegistrations = null;
                _runtimeRequestRegistrations = null;
                _runtimeResultRegistrations = null;
                _pendingDescriptors.Clear();
            }
        }

        private static void ValidateGeneratedSchemaRuntimeMetadata(
            IReadOnlyDictionary<string, TickerFunctionDescriptor> descriptors,
            IReadOnlyDictionary<string, TickerRuntimeRequestMetadata> runtimeRequests)
        {
            foreach (var (descriptor, origin) in _pendingDescriptors)
            {
                if (!string.Equals(origin, "source-gen", StringComparison.Ordinal)
                    || descriptor.Request?.Schema.HasValue != true)
                    continue;

                var functionName = descriptor.FunctionName;
                if (!runtimeRequests.TryGetValue(functionName, out var metadata)
                    || metadata.JsonTypeInfo == null)
                    throw new InvalidOperationException(
                        $"Generated request schema for function '{functionName}' has no matching runtime serializer metadata.");

                TickerHelper.ValidateGeneratedSchemaSerializerProfile(metadata.JsonTypeInfo.Options);

                var originatingResolver = metadata.JsonTypeInfo.OriginatingResolver;
                var supportedResolver = originatingResolver is JsonSerializerContext
                    || originatingResolver is DefaultJsonTypeInfoResolver { Modifiers.Count: 0 };
                if (!supportedResolver)
                    throw new InvalidOperationException(
                        $"Runtime serializer metadata resolver for function '{functionName}' is custom or modified and cannot be " +
                        "proven compatible with its generated schema. Use an unmodified default resolver, a source-generated " +
                        "JsonSerializerContext, or a non-generated explicit request contract.");

                if (metadata.JsonTypeInfo.Kind != JsonTypeInfoKind.Object)
                    throw new InvalidOperationException(
                        $"Runtime serializer metadata for function '{functionName}' has kind '{metadata.JsonTypeInfo.Kind}', " +
                        "which is incompatible with its generated object request schema.");

                var publishedDescriptor = descriptors[functionName];
                var schema = publishedDescriptor.Request.Schema.Value;
                var schemaNames = new HashSet<string>(StringComparer.Ordinal);
                if (schema.TryGetProperty("properties", out var properties)
                    && properties.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in properties.EnumerateObject())
                        schemaNames.Add(property.Name);
                }

                var metadataNames = new HashSet<string>(
                    metadata.JsonTypeInfo.Properties
                        .Where(property => !property.IsExtensionData)
                        .Select(property => property.Name),
                    StringComparer.Ordinal);

                if (!schemaNames.SetEquals(metadataNames))
                    throw new InvalidOperationException(
                        $"Runtime serializer metadata for function '{functionName}' is incompatible with its generated schema: " +
                        $"schema properties [{string.Join(", ", schemaNames.OrderBy(name => name, StringComparer.Ordinal))}] " +
                        $"do not match serializer metadata properties [{string.Join(", ", metadataNames.OrderBy(name => name, StringComparer.Ordinal))}].");
            }
        }

        private static void ValidateGeneratedResultSchemaRuntimeMetadata(
            IReadOnlyDictionary<string, TickerRuntimeResultMetadata> runtimeResults)
        {
            foreach (var (descriptor, origin) in _pendingDescriptors)
            {
                if (!string.Equals(origin, "source-gen", StringComparison.Ordinal)
                    || descriptor.Result?.Schema.HasValue != true)
                    continue;

                var functionName = descriptor.FunctionName;
                if (!runtimeResults.TryGetValue(functionName, out var metadata)
                    || metadata.JsonTypeInfo == null)
                    throw new InvalidOperationException(
                        $"Generated result schema for function '{functionName}' has no matching runtime serializer metadata.");

                TickerHelper.ValidateGeneratedSchemaSerializerProfile(metadata.JsonTypeInfo.Options);

                var originatingResolver = metadata.JsonTypeInfo.OriginatingResolver;
                var supportedResolver = originatingResolver is JsonSerializerContext
                    || originatingResolver is DefaultJsonTypeInfoResolver { Modifiers.Count: 0 };
                if (!supportedResolver)
                    throw new InvalidOperationException(
                        $"Runtime result serializer metadata resolver for function '{functionName}' is custom or modified and cannot be " +
                        "proven compatible with its generated schema. Use an unmodified default resolver or a source-generated JsonSerializerContext.");
            }
        }

        /// <summary>
        /// Folds pending descriptor registrations onto the current published set, failing on any
        /// contract-incompatible duplicate with a diagnostic naming both origins, then reconciles
        /// each descriptor's priority/cron with the authoritative functions registry.
        /// </summary>
        private static Dictionary<string, TickerFunctionDescriptor> StageDescriptors(
            IReadOnlyDictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)> functions)
        {
            var dict = new Dictionary<string, TickerFunctionDescriptor>(Snapshot.Descriptors);
            var origins = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in dict.Keys)
                origins[name] = "prior-build";

            foreach (var (descriptor, origin) in _pendingDescriptors)
            {
                var name = descriptor.FunctionName;
                if (dict.TryGetValue(name, out var existing))
                {
                    // Same contract → idempotent (scheduling reconciled below). Different → conflict.
                    if (!existing.HasSameContract(descriptor))
                        throw new InvalidOperationException(
                            $"Conflicting request-contract registrations for function '{name}': " +
                            $"[{origins[name]}] {existing} conflicts with [{origin}] {descriptor}.");
                }
                else
                {
                    dict[name] = descriptor;
                    origins[name] = origin;
                }
            }

            // Every executable function must have a canonical descriptor. Legacy/manual registrations do
            // not provide one, so represent them explicitly as request-less instead of forcing consumers
            // to join or infer from the execution dictionary.
            foreach (var (name, fn) in functions)
            {
                if (!dict.ContainsKey(name))
                {
                    dict[name] = new TickerFunctionDescriptor(name).WithSchedule(fn.Priority, fn.cronExpression);
                    origins[name] = "legacy-execution-registration";
                }
            }

            // Reconcile scheduling metadata so consumers never join independent dictionaries.
            foreach (var name in new List<string>(dict.Keys))
            {
                if (functions.TryGetValue(name, out var fn))
                    dict[name] = dict[name].WithSchedule(fn.Priority, fn.cronExpression);
            }

            return dict;
        }
    }

    public static class TickerRequestProvider
    {
        [RequiresUnreferencedCode("Legacy request deserialization may use reflection metadata. Use the JsonTypeInfo overload for trimming/AOT.")]
        [RequiresDynamicCode("Legacy request deserialization may require runtime JSON metadata. Use the JsonTypeInfo overload for Native AOT.")]
        public static async Task<T> GetRequestAsync<T>(TickerFunctionContext context, CancellationToken cancellationToken)
        {
            try
            {
                var internalTickerManager = context.ServiceScope.ServiceProvider.GetService<IInternalTickerManager>();
                return await internalTickerManager.GetRequestAsync<T>(context.Id, context.Type, cancellationToken);
            }
            catch (Exception e)
            {
                var logger = context.ServiceScope.ServiceProvider.GetService<ITickerQInstrumentation>();

                logger.LogRequestDeserializationFailure(typeof(T).FullName, context.FunctionName, context.Id, context.Type, e);
            }

            return default;
        }

        public static async Task<TickerFunctionContext<T>> ToGenericContextAsync<T>(TickerFunctionContext context, CancellationToken cancellationToken)
        {
            if (!TickerFunctionProvider.TryGetRequestTypeInfo<T>(context.FunctionName, out var typeInfo))
                throw new InvalidOperationException(
                    $"No JsonTypeInfo was registered for request type '{typeof(T)}' of function '{context.FunctionName}'. " +
                    "Ensure TickerFunctionProvider.Build() completed and WithJsonContext includes every request type.");

            return await ToGenericContextAsync(context, typeInfo, cancellationToken);
        }

        public static async Task<T> GetRequestAsync<T>(TickerFunctionContext context, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        {
            try
            {
                var internalTickerManager = context.ServiceScope.ServiceProvider.GetService<IInternalTickerManager>();
                return await internalTickerManager.GetRequestAsync(context.Id, context.Type, typeInfo, cancellationToken);
            }
            catch (Exception e)
            {
                var logger = context.ServiceScope.ServiceProvider.GetService<ITickerQInstrumentation>();
                logger.LogRequestDeserializationFailure(typeof(T).FullName, context.FunctionName, context.Id, context.Type, e);
            }

            return default;
        }

        public static async Task<TickerFunctionContext<T>> ToGenericContextAsync<T>(TickerFunctionContext context, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        {
            var request = await GetRequestAsync(context, typeInfo, cancellationToken);
            return new TickerFunctionContext<T>(context, request);
        }
    }
}
