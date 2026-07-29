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
        Guid nodeEpoch, bool allowPrivateCallbackAddressesForLocalDevelopment = false)
    {
        if (nodeEpoch == Guid.Empty) throw new ArgumentException("A non-empty Node process epoch is required.", nameof(nodeEpoch));
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var baseUri)) throw new ArgumentException("A valid absolute callback URL is required.", nameof(callbackUrl));
        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("Node callback URLs must use http or https.", nameof(callbackUrl));
        if (!string.IsNullOrEmpty(baseUri.UserInfo)) throw new ArgumentException("Node callback URLs must not contain credentials.", nameof(callbackUrl));
        if (!string.IsNullOrEmpty(baseUri.Fragment)) throw new ArgumentException("Node callback URLs must not contain fragments.", nameof(callbackUrl));
        ArgumentNullException.ThrowIfNull(signatureResolver);
        var root = baseUri.ToString().TrimEnd('/');
        var executeUri = new Uri(root + "/execute"); var cancelUri = new Uri(root + "/cancel"); var finalizeUri = new Uri(root + "/finalize");
        var client = allowPrivateCallbackAddressesForLocalDevelopment ? LocalDevelopmentClient : PublicAddressClient;

        return async (ct, serviceProvider, context) =>
        {
            if (!context.AcquisitionToken.HasValue || context.AcquisitionToken == Guid.Empty) throw new InvalidOperationException("Node callback dispatch requires an acquisition token.");
            var secret = signatureResolver();
            if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Webhook signature is not configured for Node callback dispatch.");
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

            while (true)
            {
                var executeAttempt = SendAuthenticatedAsync(client, executeUri, secret, executeNonce, executeBody);
                var winner = ct.CanBeCanceled ? await Task.WhenAny(executeAttempt, cancellationSignal.Task).ConfigureAwait(false) : executeAttempt;
                if (winner == cancellationSignal.Task)
                {
                    outcome = await ReconcileCancellationAsync(client, cancelUri, secret, identity).ConfigureAwait(false);
                    break;
                }
                try
                {
                    var reply = await executeAttempt.ConfigureAwait(false);
                    if (reply.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout)
                    { await Task.Delay(250, CancellationToken.None).ConfigureAwait(false); continue; }
                    if (reply.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"Node callback returned authenticated HTTP {(int)reply.StatusCode}.", null, reply.StatusCode);
                    outcome = ParseOutcome(reply.Body, identity);
                    break;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                }
            }

            context.IsRemoteCallbackExecution = true;
            var finalizeNonce = Guid.NewGuid();
            var controlNonce = Guid.NewGuid();
            var finalizeBody = JsonSerializer.SerializeToUtf8Bytes(new NodeControlRequest(identity.TickerType, identity.TickerId,
                identity.AcquisitionToken, identity.DispatchId, identity.NodeEpoch, controlNonce), JsonOptions);
            context.ConfirmRemoteCommitAsync = async _ =>
            {
                try
                {
                    var reply = await SendAuthenticatedAsync(client, finalizeUri, secret, finalizeNonce, finalizeBody).ConfigureAwait(false);
                    if (reply.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"Node finalize returned authenticated HTTP {(int)reply.StatusCode}.");
                }
                catch
                {
                    // The durable exact-generation commit already succeeded. Failure to forget is safe:
                    // Node retains the authenticated settled outcome and its bounded tombstone fallback.
                }
            };

            ApplyOutcome(context, outcome);
        };
    }

    private static async Task<NodeExecutionOutcome> ReconcileCancellationAsync(HttpClient client, Uri uri, string secret, NodeIdentity identity)
    {
        var requestNonce = Guid.NewGuid(); var controlNonce = Guid.NewGuid();
        var body = JsonSerializer.SerializeToUtf8Bytes(new NodeControlRequest(identity.TickerType, identity.TickerId,
            identity.AcquisitionToken, identity.DispatchId, identity.NodeEpoch, controlNonce), JsonOptions);
        while (true)
        {
            try
            {
                var reply = await SendAuthenticatedAsync(client, uri, secret, requestNonce, body).ConfigureAwait(false);
                if (reply.StatusCode == HttpStatusCode.OK)
                {
                    var ack = JsonSerializer.Deserialize<NodeCancelAcknowledgement>(reply.Body, JsonOptions)
                        ?? throw new InvalidOperationException("Node cancellation returned an empty acknowledgement.");
                    ValidateIdentity(ack.Identity, identity);
                    if (ack.ControlNonce != controlNonce) throw new InvalidOperationException("Node cancellation control nonce did not match.");
                    return ack.Outcome ?? throw new InvalidOperationException("Node cancellation did not carry a settled outcome.");
                }
            }
            catch (Exception ex) when (IsTransient(ex)) { }
            await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
        }
    }

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
        ValidateIdentity(outcome.Identity, expected);
        if (outcome.TickerId != expected.TickerId || outcome.AcquisitionToken != expected.AcquisitionToken) throw new InvalidOperationException("Node outcome projection did not match its signed identity.");
        return outcome;
    }
    private static void ValidateIdentity(NodeIdentity actual, NodeIdentity expected)
    {
        if (actual != expected) throw new InvalidOperationException("Node response identity did not match the exact dispatch generation.");
    }

    private static async Task<AuthenticatedReply> SendAuthenticatedAsync(HttpClient client, Uri uri, string secret, Guid nonce, byte[] body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var nonceText = nonce.ToString("D");
        request.Headers.TryAddWithoutValidation("x-timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation("x-request-nonce", nonceText);
        request.Headers.TryAddWithoutValidation("x-tickerq-signature", SignRequest(secret, uri.PathAndQuery, timestamp, nonceText, body));
        using var attempt = new CancellationTokenSource(TransportAttemptTimeout);
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
    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException http => !http.Message.Contains("forbidden network address", StringComparison.OrdinalIgnoreCase),
        TaskCanceledException or IOException => true,
        _ => false
    };

    internal static SocketsHttpHandler CreateHandler(bool allowPrivateAddressesForLocalDevelopment) => new()
    {
        AllowAutoRedirect = false, UseProxy = false,
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
    private sealed record NodeExecutionOutcome(NodeIdentity Identity, Guid TickerId, Guid? AcquisitionToken, TickerStatus Status, string? ExceptionDetails, NodeResultEnvelope? ResultEnvelope);
    private sealed record NodeResultEnvelope(byte[] Payload, int EnvelopeVersion, string MediaType, string? ContractId, string? ContractType)
    {
        public static NodeResultEnvelope From(TickerResultEnvelope value) => new(value.ToPayloadArray(), value.Version, value.MediaType, value.ContractId, value.ContractType);
        public TickerResultEnvelope ToCore() => new(Payload, EnvelopeVersion, MediaType, ContractId, ContractType);
    }
}
