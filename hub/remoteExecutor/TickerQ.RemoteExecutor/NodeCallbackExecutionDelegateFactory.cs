using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

/// <summary>Proof-backed, replay-safe Node callback transport. Core remains the sole durable finalizer.</summary>
internal static class NodeCallbackExecutionDelegateFactory
{
    private const int MaxResponseBodyBytes = 2 * 1024 * 1024;
    private const int MaxResultPayloadBytes = 1024 * 1024;
    private const int MaxRequestPayloadBytes = 1024 * 1024;
    private static readonly TimeSpan TransportAttemptTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient PublicAddressClient = CreateClient(false);
    private static readonly HttpClient LocalDevelopmentClient = CreateClient(true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TickerFunctionDelegate Create(string callbackUrl, Func<string?> signatureResolver,
        Guid nodeEpoch, bool allowPrivateCallbackAddressesForLocalDevelopment = false,
        HttpClient? transportOverride = null)
    {
        if (nodeEpoch == Guid.Empty) throw new ArgumentException("A non-empty Node process epoch is required.", nameof(nodeEpoch));
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var baseUri)) throw new ArgumentException("A valid absolute callback URL is required.", nameof(callbackUrl));
        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("Node callback URLs must use http or https.", nameof(callbackUrl));
        if (!string.IsNullOrEmpty(baseUri.UserInfo)) throw new ArgumentException("Node callback URLs must not contain credentials.", nameof(callbackUrl));
        if (!string.IsNullOrEmpty(baseUri.Query)) throw new ArgumentException("Node callback URLs must not contain query strings.", nameof(callbackUrl));
        if (!string.IsNullOrEmpty(baseUri.Fragment)) throw new ArgumentException("Node callback URLs must not contain fragments.", nameof(callbackUrl));
        ArgumentNullException.ThrowIfNull(signatureResolver);
        var root = baseUri.ToString().TrimEnd('/');
        var executeUri = new Uri(root + "/execute"); var cancelUri = new Uri(root + "/cancel"); var finalizeUri = new Uri(root + "/finalize");
        var client = transportOverride ??
            (allowPrivateCallbackAddressesForLocalDevelopment ? LocalDevelopmentClient : PublicAddressClient);

        return async (ct, serviceProvider, context) =>
        {
            if (!context.AcquisitionToken.HasValue || context.AcquisitionToken == Guid.Empty) throw new InvalidOperationException("Node callback dispatch requires an acquisition token.");
            _ = ResolveSecret(signatureResolver);
            var payload = await serviceProvider.GetRequiredService<IRemotePayloadLoader>().LoadPayloadAsync(context.Id, context.Type, ct).ConfigureAwait(false);
            if (payload is { Length: > MaxRequestPayloadBytes }) throw new InvalidOperationException("Node callback request payload exceeded the 1 MiB limit.");
            if (payload is not null) try { using var _ = JsonDocument.Parse(payload); } catch (JsonException ex) { throw new InvalidOperationException("Node callback request payload must contain exactly one valid JSON value.", ex); }

            var identity = new NodeIdentity(context.Type, context.Id, context.AcquisitionToken.Value, Guid.NewGuid(), nodeEpoch);
            var executeNonce = Guid.NewGuid();
            var execute = new NodeExecuteRequest(identity.TickerType, identity.TickerId, identity.AcquisitionToken, identity.DispatchId, identity.NodeEpoch,
                identity.TickerId, identity.DispatchId, ToBareFunctionName(context.FunctionName), context.Type, context.RetryCount, context.IsDue,
                context.ScheduledFor, context.ParentId, payload is not null, payload,
                context.ParentResultEnvelope is null ? null : NodeResultEnvelope.From(context.ParentResultEnvelope));
            var executeBody = JsonSerializer.SerializeToUtf8Bytes(execute, JsonOptions);
            var cancellationSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = ct.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), cancellationSignal);
            NodeExecutionOutcome outcome;
            var allPriorExecuteAttemptsProvedNotStarted = true;

            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    try
                    {
                        outcome = await ReconcileCancellationAsync(client, executeUri, cancelUri, signatureResolver, identity,
                            executeNonce, executeBody, initialExecute: null,
                            allPriorExecuteAttemptsProvedNotStarted).ConfigureAwait(false);
                    }
                    catch (RemoteExecutionNotStartedException)
                    {
                        context.IsRemoteCallbackExecution = true;
                        throw;
                    }
                    break;
                }

                Task<AuthenticatedReply> executeAttempt;
                try
                {
                    executeAttempt = SendAuthenticatedAsync(client, executeUri, ResolveSecret(signatureResolver), executeNonce, executeBody);
                }
                catch when (!allPriorExecuteAttemptsProvedNotStarted)
                {
                    await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }
                var winner = ct.CanBeCanceled ? await Task.WhenAny(executeAttempt, cancellationSignal.Task).ConfigureAwait(false) : executeAttempt;
                if (winner == cancellationSignal.Task)
                {
                    try
                    {
                        outcome = await ReconcileCancellationAsync(client, executeUri, cancelUri, signatureResolver, identity,
                            executeNonce, executeBody, executeAttempt,
                            allPriorExecuteAttemptsProvedNotStarted).ConfigureAwait(false);
                    }
                    catch (RemoteExecutionNotStartedException)
                    {
                        context.IsRemoteCallbackExecution = true;
                        throw;
                    }
                    break;
                }
                try
                {
                    var reply = await executeAttempt.ConfigureAwait(false);
                    var proof = ClassifyNotStartedProof(reply, identity, registrationDependentCodesAreSafe: true);
                    if (proof == NotStartedProof.Permanent)
                    {
                        if (allPriorExecuteAttemptsProvedNotStarted) ThrowPermanentlyNotStarted(reply);
                        await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    if (proof == NotStartedProof.Transient)
                    {
                        await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    if (reply.StatusCode != HttpStatusCode.OK)
                    {
                        allPriorExecuteAttemptsProvedNotStarted = false;
                        await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    try { outcome = ParseOutcome(reply.Body, identity); }
                    catch
                    {
                        allPriorExecuteAttemptsProvedNotStarted = false;
                        throw;
                    }
                    break;
                }
                catch (RemoteExecutionNotStartedException)
                {
                    context.IsRemoteCallbackExecution = true;
                    throw;
                }
                catch (HttpRequestException ex) when (IsForbiddenNetwork(ex))
                {
                    if (allPriorExecuteAttemptsProvedNotStarted) throw;
                    await QuarantineAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Once an execute may have left this process, every transport/auth/body/identity
                    // uncertainty is reconciled with the exact same identity, bytes and nonce.
                    allPriorExecuteAttemptsProvedNotStarted = false;
                    await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                }
            }

            context.IsRemoteCallbackExecution = true;
            var finalizeNonce = Guid.NewGuid();
            var controlNonce = Guid.NewGuid();
            var finalizeBody = JsonSerializer.SerializeToUtf8Bytes(new NodeControlRequest(identity.TickerType, identity.TickerId,
                identity.AcquisitionToken, identity.DispatchId, identity.NodeEpoch, controlNonce), JsonOptions);
            context.RemoteFinalizationIntent = new NodeFinalizationIntent(
                NodeFinalizationIntent.CurrentSchemaVersion,
                identity.DispatchId,
                identity.TickerType,
                identity.TickerId,
                identity.AcquisitionToken,
                identity.DispatchId,
                identity.NodeEpoch,
                finalizeUri.AbsoluteUri,
                finalizeUri.PathAndQuery,
                allowPrivateCallbackAddressesForLocalDevelopment,
                finalizeNonce,
                controlNonce,
                finalizeBody,
                DateTime.UtcNow);

            ApplyOutcome(context, outcome);
        };
    }

    private static async Task<NodeExecutionOutcome> ReconcileCancellationAsync(HttpClient client, Uri executeUri, Uri cancelUri,
        Func<string?> signatureResolver, NodeIdentity identity, Guid executeNonce, byte[] executeBody,
        Task<AuthenticatedReply>? initialExecute, bool allPriorExecuteAttemptsProvedNotStarted)
    {
        var cancelNonce = Guid.NewGuid(); var controlNonce = Guid.NewGuid();
        var cancelBody = JsonSerializer.SerializeToUtf8Bytes(new NodeControlRequest(identity.TickerType, identity.TickerId,
            identity.AcquisitionToken, identity.DispatchId, identity.NodeEpoch, controlNonce), JsonOptions);
        Task<AuthenticatedReply>? execute = initialExecute;
        Task<AuthenticatedReply>? cancel = null;
        try { cancel = SendAuthenticatedAsync(client, cancelUri, ResolveSecret(signatureResolver), cancelNonce, cancelBody); }
        catch { }
        if (execute is null)
        {
            // For an already-cancelled invocation, give the cancellation tombstone request a
            // deterministic head start before issuing the exact execute identity. The execute is
            // still required for response-loss recovery, but it must not win the cancel-first race.
            await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
            try { execute = SendAuthenticatedAsync(client, executeUri, ResolveSecret(signatureResolver), executeNonce, executeBody); }
            catch { }
        }
        NodeExecutionOutcome? settledOutcome = null;
        var exactRejectedProofCount = 0;

        // This cancellation can stop the already-issued execute, but its delayed 202 is not proof
        // that cancelPending still exists: the concurrent execute may have deleted that record.
        // Observe both operations before starting serialized proof cycles.
        while (execute is not null || cancel is not null)
        {
            var pending = new[] { execute, cancel }.Where(task => task is not null).Cast<Task<AuthenticatedReply>>().ToArray();
            await Task.WhenAny(pending).ConfigureAwait(false);
            if (cancel is { IsCompleted: true })
            {
                try
                {
                    var reply = await cancel.ConfigureAwait(false);
                    if (reply.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted)
                    {
                        var ack = JsonSerializer.Deserialize<NodeCancelAcknowledgement>(reply.Body, JsonOptions)
                            ?? throw new InvalidOperationException("Node cancellation returned an empty acknowledgement.");
                        ValidateIdentity(ack.Identity, identity);
                        if (ack.ControlNonce != controlNonce) throw new InvalidOperationException("Node cancellation control nonce did not match.");
                        if (reply.StatusCode == HttpStatusCode.Accepted)
                        {
                            if (!string.Equals(ack.State, "cancellation_registered", StringComparison.Ordinal))
                                throw new InvalidOperationException("Node cancellation registration acknowledgement was invalid.");
                        }
                        else
                        {
                            settledOutcome = ValidateOutcome(ack.Outcome ?? throw new InvalidOperationException("Node cancellation did not carry a settled outcome."), identity);
                        }
                    }
                }
                catch (HttpRequestException ex) when (IsForbiddenNetwork(ex))
                {
                    await QuarantineAsync().ConfigureAwait(false);
                }
                catch { }
                cancel = null;
            }

            if (execute is { IsCompleted: true })
            {
                try
                {
                    var reply = await execute.ConfigureAwait(false);
                    if (IsExactCancelPendingRejection(reply, identity)) exactRejectedProofCount++;
                    else if (ClassifyNotStartedProof(reply, identity, registrationDependentCodesAreSafe: false) == NotStartedProof.None)
                        allPriorExecuteAttemptsProvedNotStarted = false;
                    if (reply.StatusCode == HttpStatusCode.OK) settledOutcome = ParseOutcome(reply.Body, identity);
                }
                catch (HttpRequestException ex) when (IsForbiddenNetwork(ex))
                {
                    await QuarantineAsync().ConfigureAwait(false);
                }
                catch { allPriorExecuteAttemptsProvedNotStarted = false; }
                execute = null;
            }
        }

        if (settledOutcome is not null) return settledOutcome;

        while (true)
        {
            await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
            try
            {
                var cancelReply = await SendAuthenticatedAsync(client, cancelUri, ResolveSecret(signatureResolver), cancelNonce, cancelBody)
                    .ConfigureAwait(false);
                if (cancelReply.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted)) continue;
                var ack = JsonSerializer.Deserialize<NodeCancelAcknowledgement>(cancelReply.Body, JsonOptions)
                    ?? throw new InvalidOperationException("Node cancellation returned an empty acknowledgement.");
                ValidateIdentity(ack.Identity, identity);
                if (ack.ControlNonce != controlNonce) throw new InvalidOperationException("Node cancellation control nonce did not match.");
                if (cancelReply.StatusCode == HttpStatusCode.OK)
                    return ValidateOutcome(ack.Outcome ?? throw new InvalidOperationException("Node cancellation did not carry a settled outcome."), identity);
                if (!string.Equals(ack.State, "cancellation_registered", StringComparison.Ordinal))
                    throw new InvalidOperationException("Node cancellation registration acknowledgement was invalid.");
            }
            catch (HttpRequestException ex) when (IsForbiddenNetwork(ex))
            {
                await QuarantineAsync().ConfigureAwait(false);
                continue;
            }
            catch { continue; }

            // A fresh exact 202 completed before this execute starts. No cancellation request is
            // in flight that can recreate state after rejected_not_started deletes the record.
            try
            {
                var reply = await SendAuthenticatedAsync(client, executeUri, ResolveSecret(signatureResolver), executeNonce, executeBody)
                    .ConfigureAwait(false);
                if (reply.StatusCode == HttpStatusCode.OK) return ParseOutcome(reply.Body, identity);
                if (IsExactCancelPendingRejection(reply, identity))
                {
                    exactRejectedProofCount++;
                    if (exactRejectedProofCount >= 2) ThrowPermanentlyNotStarted(reply);
                    continue;
                }
                var proof = ClassifyNotStartedProof(reply, identity, registrationDependentCodesAreSafe: false);
                if (proof == NotStartedProof.Permanent && allPriorExecuteAttemptsProvedNotStarted)
                    ThrowPermanentlyNotStarted(reply);
                if (proof == NotStartedProof.None)
                    allPriorExecuteAttemptsProvedNotStarted = false;
            }
            catch (RemoteExecutionNotStartedException) { throw; }
            catch (HttpRequestException ex) when (IsForbiddenNetwork(ex))
            {
                await QuarantineAsync().ConfigureAwait(false);
            }
            catch { allPriorExecuteAttemptsProvedNotStarted = false; }
        }
    }

    private static string ResolveSecret(Func<string?> signatureResolver)
    {
        var secret = signatureResolver();
        return string.IsNullOrWhiteSpace(secret)
            ? throw new InvalidOperationException("Webhook signature is not configured for Node callback dispatch.")
            : secret;
    }

    private static bool IsForbiddenNetwork(HttpRequestException exception)
        => exception.Message.Contains("forbidden network address", StringComparison.OrdinalIgnoreCase);

    private static Task QuarantineAsync() => Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);

    private static void ApplyOutcome(TickerFunctionContext context, NodeExecutionOutcome outcome)
    {
        if (outcome.Status == TickerStatus.Cancelled) throw new TaskCanceledException(outcome.ExceptionDetails ?? "Node execution was cancelled.");
        if (outcome.Status == TickerStatus.Failed) throw new InvalidOperationException(outcome.ExceptionDetails ?? "Node execution failed.");
        if (outcome.Status is not (TickerStatus.Done or TickerStatus.DueDone)) throw new InvalidOperationException($"Node callback returned non-terminal status '{outcome.Status}'.");
        if (outcome.ResultEnvelope is null) return;
        if (outcome.ResultEnvelope.Payload is null || outcome.ResultEnvelope.Payload.Length > MaxResultPayloadBytes) throw new InvalidOperationException("Node callback result payload exceeded the 1 MiB limit.");
        context.ResultSink.Set(outcome.ResultEnvelope.ToCore());
    }

    private static NodeExecutionOutcome ParseOutcome(byte[] body, NodeIdentity expected)
    {
        var outcome = JsonSerializer.Deserialize<NodeExecutionOutcome>(body, JsonOptions) ?? throw new InvalidOperationException("Node callback returned an empty terminal outcome.");
        return ValidateOutcome(outcome, expected);
    }
    private static NodeExecutionOutcome ValidateOutcome(NodeExecutionOutcome outcome, NodeIdentity expected)
    {
        ValidateIdentity(outcome.Identity, expected);
        if (outcome.TickerId != expected.TickerId || outcome.AcquisitionToken != expected.AcquisitionToken) throw new InvalidOperationException("Node outcome projection did not match its signed identity.");
        if (outcome.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed or TickerStatus.Cancelled))
            throw new InvalidOperationException($"Node callback returned non-terminal status '{outcome.Status}'.");
        if (outcome.ResultEnvelope?.Payload is { } payload && payload.Length > MaxResultPayloadBytes)
            throw new InvalidOperationException("Node callback result payload exceeded the 1 MiB limit.");
        return outcome;
    }
    private static NotStartedProof ClassifyNotStartedProof(AuthenticatedReply reply, NodeIdentity identity,
        bool registrationDependentCodesAreSafe)
    {
        if (reply.StatusCode == HttpStatusCode.OK) return NotStartedProof.None;
        string? code = null;
        try
        {
            using var json = JsonDocument.Parse(reply.Body);
            if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                code = error.GetString();
        }
        catch (JsonException) { return NotStartedProof.None; }
        if (IsExactCancelPendingRejection(reply, identity)) return NotStartedProof.Permanent;
        return (reply.StatusCode, code) switch
        {
            (HttpStatusCode.BadRequest, "invalid_json" or "invalid_identity") => NotStartedProof.Permanent,
            (HttpStatusCode.BadRequest, "invalid_execution" or "missing_request" or "unexpected_request")
                when registrationDependentCodesAreSafe => NotStartedProof.Permanent,
            (HttpStatusCode.NotFound, "function_not_found") when registrationDependentCodesAreSafe
                => NotStartedProof.Permanent,
            (HttpStatusCode.Conflict, "node_epoch_mismatch") => NotStartedProof.Permanent,
            (HttpStatusCode.ServiceUnavailable, "shutting_down") => NotStartedProof.Permanent,
            (HttpStatusCode.ServiceUnavailable, "registry_full") => NotStartedProof.Transient,
            ((HttpStatusCode)429, "replay_capacity") => NotStartedProof.Transient,
            _ => NotStartedProof.None
        };
    }

    private static bool IsExactCancelPendingRejection(AuthenticatedReply reply, NodeIdentity identity)
    {
        if (reply.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound)) return false;
        try
        {
            var proof = JsonSerializer.Deserialize<NodeRejectedAcknowledgement>(reply.Body, JsonOptions);
            var exactStatusAndCode =
                (reply.StatusCode == HttpStatusCode.BadRequest && proof?.Error == "invalid_execution") ||
                (reply.StatusCode == HttpStatusCode.NotFound && proof?.Error == "function_not_found");
            return exactStatusAndCode && string.Equals(proof!.State, "rejected_not_started", StringComparison.Ordinal) &&
                   proof.Identity == identity;
        }
        catch (JsonException) { return false; }
    }

    private static void ThrowPermanentlyNotStarted(AuthenticatedReply reply)
    {
        string code = "not_started";
        try
        {
            using var json = JsonDocument.Parse(reply.Body);
            if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                code = error.GetString() ?? code;
        }
        catch (JsonException) { }
        throw new RemoteExecutionNotStartedException(
            $"Node rejected the exact dispatch before execution ({(int)reply.StatusCode} {code}).");
    }

    internal static async Task<NodeFinalizationAttemptResult> TryFinalizeAsync(
        NodeFinalizationIntent intent,
        Func<string?> signatureResolver,
        HttpClient? transportOverride,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(signatureResolver);
        if (!Uri.TryCreate(intent.FinalizeUri, UriKind.Absolute, out var finalizeUri) ||
            (finalizeUri.Scheme != Uri.UriSchemeHttp && finalizeUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(finalizeUri.UserInfo) || !string.IsNullOrEmpty(finalizeUri.Query) ||
            !string.IsNullOrEmpty(finalizeUri.Fragment) ||
            !string.Equals(finalizeUri.PathAndQuery, intent.FinalizePathAndQuery, StringComparison.Ordinal))
            return NodeFinalizationAttemptResult.Retry;

        var identity = new NodeIdentity(intent.TickerType, intent.TickerId, intent.AcquisitionToken,
            intent.DispatchId, intent.NodeEpoch);
        var currentSecret = signatureResolver();
        if (string.IsNullOrWhiteSpace(currentSecret)) return NodeFinalizationAttemptResult.Retry;
        var client = transportOverride ?? (intent.AllowPrivateCallbackAddressesForLocalDevelopment
            ? LocalDevelopmentClient
            : PublicAddressClient);
        var reply = await SendAuthenticatedAsync(client, finalizeUri, currentSecret, intent.RequestNonce,
            intent.ExactBody, stoppingToken).ConfigureAwait(false);
        if (reply.StatusCode == HttpStatusCode.Conflict)
        {
            try
            {
                var ack = JsonSerializer.Deserialize<NodeFinalizeConflictAcknowledgement>(reply.Body, JsonOptions);
                if (ack is not null &&
                    string.Equals(ack.Error, "node_epoch_mismatch", StringComparison.Ordinal) &&
                    ack.ControlNonce == intent.ControlNonce && ack.Identity == identity)
                    return NodeFinalizationAttemptResult.Completed;
            }
            catch (JsonException) { }
            return NodeFinalizationAttemptResult.Retry;
        }
        if (reply.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            var ack = JsonSerializer.Deserialize<NodeFinalizeAcknowledgement>(reply.Body, JsonOptions)
                ?? throw new InvalidOperationException("Node finalize returned an empty acknowledgement.");
            ValidateIdentity(ack.Identity, identity);
            if (ack.ControlNonce != intent.ControlNonce)
                throw new InvalidOperationException("Node finalize acknowledgement did not match the exact control request.");
            if ((reply.StatusCode == HttpStatusCode.OK && string.Equals(ack.State, "finalized", StringComparison.Ordinal)) ||
                (reply.StatusCode == HttpStatusCode.NotFound && string.Equals(ack.State, "unknown", StringComparison.Ordinal)) ||
                (reply.StatusCode == HttpStatusCode.Gone && string.Equals(ack.State, "finalized", StringComparison.Ordinal)))
                return NodeFinalizationAttemptResult.Completed;
        }
        return NodeFinalizationAttemptResult.Retry;
    }

    private static bool HasErrorCode(byte[] body, string expected)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("error", out var error) &&
                   error.ValueKind == JsonValueKind.String && string.Equals(error.GetString(), expected, StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }
    private static void ValidateIdentity(NodeIdentity actual, NodeIdentity expected)
    {
        if (actual != expected) throw new InvalidOperationException("Node response identity did not match the exact dispatch generation.");
    }

    private static async Task<AuthenticatedReply> SendAuthenticatedAsync(HttpClient client, Uri uri, string secret, Guid nonce, byte[] body,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var nonceText = nonce.ToString("D");
        request.Headers.TryAddWithoutValidation("x-timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation("x-request-nonce", nonceText);
        request.Headers.TryAddWithoutValidation("x-tickerq-signature", SignRequest(secret, uri.PathAndQuery, timestamp, nonceText, body));
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TransportAttemptTimeout);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(response, attempt.Token).ConfigureAwait(false);
        VerifyResponse(secret, response, uri.PathAndQuery, nonceText, bytes);
        return new AuthenticatedReply(response.StatusCode, bytes);
    }

    private static string SignRequest(string secret, string path, long timestamp, string nonce, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes($"POST\n{path}\n{timestamp}\n{nonce}\n");
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), prefix.Concat(body).ToArray()));
    }
    private static void VerifyResponse(string secret, HttpResponseMessage response, string path, string nonce, byte[] body)
    {
        if (!response.Headers.TryGetValues("x-response-timestamp", out var timestamps) || !long.TryParse(timestamps.SingleOrDefault(), out var timestamp)) throw new InvalidOperationException("Node response timestamp was missing or invalid.");
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 300) throw new InvalidOperationException("Node response timestamp was stale.");
        if (!response.Headers.TryGetValues("x-request-nonce", out var nonces) || !string.Equals(nonces.SingleOrDefault(), nonce, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Node response nonce did not match.");
        if (!response.Headers.TryGetValues("x-tickerq-signature", out var signatures)) throw new InvalidOperationException("Node response signature was missing.");
        byte[] actual; try { actual = Convert.FromBase64String(signatures.Single()); } catch (FormatException) { throw new InvalidOperationException("Node response signature was malformed."); }
        var prefix = Encoding.UTF8.GetBytes($"{(int)response.StatusCode}\n{path}\n{timestamp}\n{nonce}\n");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), prefix.Concat(body).ToArray());
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)) throw new InvalidOperationException("Node response signature did not match exact response bytes.");
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBodyBytes) throw new InvalidOperationException("Node callback response exceeded the 2 MiB limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false); using var output = new MemoryStream();
        var buffer = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) { if (output.Length + read > MaxResponseBodyBytes) throw new InvalidOperationException("Node callback response exceeded the 2 MiB limit."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }

    internal static SocketsHttpHandler CreateHandler(bool allowPrivateAddressesForLocalDevelopment) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
            if (addresses.Length == 0 || addresses.Any(a => IsForbidden(a, allowPrivateAddressesForLocalDevelopment))) throw new HttpRequestException("Node callback DNS resolution returned a forbidden network address.");
            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct).ConfigureAwait(false); return new NetworkStream(socket, true); }
                catch (Exception ex) { socket.Dispose(); last = ex; }
            }
            throw new HttpRequestException("Unable to connect to the validated Node callback address.", last);
        }
    };
    private static HttpClient CreateClient(bool allowPrivate) => new(CreateHandler(allowPrivate)) { Timeout = Timeout.InfiniteTimeSpan };
    private static string ToBareFunctionName(string? name) { if (string.IsNullOrEmpty(name)) return string.Empty; var i = name.LastIndexOf('@'); return i > 0 ? name[..i] : name; }
    private static bool IsForbidden(IPAddress address, bool allowPrivate)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None) || address.IsIPv6Multicast) return true;
        if (allowPrivate) return false; if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return (address.GetAddressBytes()[0] & 0xfe) == 0xfc;
        if (address.AddressFamily != AddressFamily.InterNetwork) return true; var b = address.GetAddressBytes();
        return b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] >= 224;
    }

    private sealed record AuthenticatedReply(HttpStatusCode StatusCode, byte[] Body);
    private sealed record NodeIdentity(TickerType TickerType, Guid TickerId, Guid AcquisitionToken, Guid DispatchId, Guid NodeEpoch);
    private sealed record NodeExecuteRequest(TickerType TickerType, Guid TickerId, Guid AcquisitionToken, Guid DispatchId, Guid NodeEpoch,
        Guid Id, Guid ExecutionId, string FunctionName, TickerType Type, int RetryCount, bool IsDue, DateTime ScheduledFor,
        Guid? ParentId, bool HasRequest, byte[]? RequestPayload, NodeResultEnvelope? ParentResult);
    private sealed record NodeControlRequest(TickerType TickerType, Guid TickerId, Guid AcquisitionToken, Guid DispatchId, Guid NodeEpoch, Guid ControlNonce);
    private sealed record NodeCancelAcknowledgement(string State, Guid ControlNonce, NodeIdentity Identity, NodeExecutionOutcome? Outcome);
    private sealed record NodeFinalizeAcknowledgement(string State, Guid ControlNonce, NodeIdentity Identity);
    private sealed record NodeFinalizeConflictAcknowledgement(string Error, Guid ControlNonce, NodeIdentity Identity);
    private sealed record NodeRejectedAcknowledgement(string Error, string State, NodeIdentity Identity);
    private sealed record NodeExecutionOutcome(NodeIdentity Identity, Guid TickerId, Guid? AcquisitionToken, TickerStatus Status, string? ExceptionDetails, NodeResultEnvelope? ResultEnvelope);
    private enum NotStartedProof { None, Transient, Permanent }
    private sealed record NodeResultEnvelope(byte[] Payload, int EnvelopeVersion, string MediaType, string? ContractId, string? ContractType)
    {
        public static NodeResultEnvelope From(TickerResultEnvelope value) => new(value.ToPayloadArray(), value.Version, value.MediaType, value.ContractId, value.ContractType);
        public TickerResultEnvelope ToCore() => new(Payload, EnvelopeVersion, MediaType, ContractId, ContractType);
    }
}
