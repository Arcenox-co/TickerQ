using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class NodeFinalizationReconcilerTests
{
    private const string Secret = "durable-finalization-secret";
    private static readonly Guid NodeEpoch = Guid.Parse("8fef33e7-826b-49e4-a36d-8eb0aa570ef1");

    [Fact]
    public async Task StartupImmediatelyClaimsPersistedWorkSimulatingRestart()
    {
        var store = new DurableOutboxFake();
        var intent = Intent("https://node.example/finalize");
        store.Seed(intent);
        using var client = Client((request, _) => Task.FromResult(Finalized(request, intent, Secret)));
        var worker = Reconciler(store, client, workers: 1);

        await worker.StartAsync(CancellationToken.None);
        await store.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(store.Snapshot());
        Assert.Equal(1, store.MaxRequestedCount);
    }

    [Fact]
    public async Task ClaimsOnlyFreeSlotsAndSlowEndpointDoesNotHeadOfLineBlockHealthyWork()
    {
        var store = new DurableOutboxFake();
        var slow = Intent("https://node.example/slow/finalize");
        var healthy = Intent("https://node.example/healthy/finalize");
        var next = Intent("https://node.example/next/finalize");
        store.Seed(slow); store.Seed(healthy); store.Seed(next);
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var maxActive = 0;
        using var client = Client(async (request, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref active);
            SetMax(ref maxActive, current);
            try
            {
                if (request.RequestUri!.AbsolutePath.Contains("/slow/", StringComparison.Ordinal))
                {
                    slowStarted.TrySetResult();
                    await releaseSlow.Task.WaitAsync(cancellationToken);
                    return Finalized(request, slow, Secret);
                }
                var matching = request.RequestUri.AbsolutePath.Contains("/healthy/", StringComparison.Ordinal) ? healthy : next;
                return Finalized(request, matching, Secret);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        var worker = Reconciler(store, client, workers: 2);

        await worker.StartAsync(CancellationToken.None);
        await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !store.Contains(healthy.OutboxId) && !store.Contains(next.OutboxId));

        Assert.True(store.ClaimRequestCounts.Count >= 2);
        Assert.Equal(2, store.ClaimRequestCounts[0]);
        Assert.All(store.ClaimRequestCounts.Skip(1), count => Assert.InRange(count, 1, 2));
        Assert.InRange(maxActive, 1, 2);
        Assert.True(store.Contains(slow.OutboxId));
        releaseSlow.TrySetResult();
        await WaitUntilAsync(() => !store.Contains(slow.OutboxId));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExpiredLeaseReclaimsAndStaleTokenCannotCompleteOrRescheduleNewerClaim()
    {
        var store = new DurableOutboxFake();
        var intent = Intent("https://node.example/finalize");
        store.Seed(intent);
        var provider = store.Provider;
        var now = DateTime.UtcNow;
        var oldClaim = Assert.Single(await provider.ClaimDueNodeFinalizationsAsync("old", 1, now, now.AddMilliseconds(10)));
        var none = await provider.ClaimDueNodeFinalizationsAsync("new", 1, now, now.AddMinutes(1));
        Assert.Empty(none);
        var newClaim = Assert.Single(await provider.ClaimDueNodeFinalizationsAsync(
            "new", 1, now.AddSeconds(1), now.AddMinutes(2)));

        Assert.NotEqual(oldClaim.ClaimToken, newClaim.ClaimToken);
        Assert.False(await provider.CompleteNodeFinalizationAsync(oldClaim));
        Assert.False(await provider.RescheduleNodeFinalizationAsync(oldClaim, now.AddMinutes(3), "stale"));
        Assert.True(await provider.CompleteNodeFinalizationAsync(newClaim));
    }

    [Theory]
    [InlineData(200, "finalized")]
    [InlineData(404, "unknown")]
    [InlineData(410, "finalized")]
    [InlineData(409, "node_epoch_mismatch")]
    public async Task ExactSignedTerminalAcknowledgementMatrixCompletes(int status, string stateOrError)
    {
        var store = new DurableOutboxFake();
        var intent = Intent("https://node.example/finalize");
        store.Seed(intent);
        using var client = Client((request, _) => Task.FromResult(
            ExactAck(request, intent, Secret, (HttpStatusCode)status, stateOrError)));
        var worker = Reconciler(store, client, workers: 1);

        await worker.StartAsync(CancellationToken.None);
        await store.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(store.Snapshot());
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("wrong_identity")]
    [InlineData("wrong_nonce")]
    [InlineData("unsigned")]
    [InlineData("replay")]
    [InlineData("unauthorized")]
    [InlineData("server_error")]
    public async Task UntrustedOrTransientAcknowledgementsReschedule(string scenario)
    {
        var store = new DurableOutboxFake();
        var intent = Intent("https://node.example/finalize");
        store.Seed(intent);
        using var client = Client((request, _) => Task.FromResult(NonTerminalAck(request, intent, scenario)));
        var worker = Reconciler(store, client, workers: 1);

        await worker.StartAsync(CancellationToken.None);
        await store.Rescheduled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        var record = Assert.Single(store.Snapshot());
        Assert.Equal(1, record.AttemptCount);
        Assert.Null(record.ClaimToken);
    }

    [Fact]
    public async Task SecretRotationIsResolvedPerAttemptAndResponseLossThenGoneCompletes()
    {
        var currentSecret = "old-secret";
        var store = new DurableOutboxFake();
        var intent = Intent("https://node.example/finalize");
        store.Seed(intent);
        var calls = 0;
        using var client = Client((request, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new HttpRequestException("response lost after Node finalized");
            return Task.FromResult(ExactAck(request, intent, currentSecret, HttpStatusCode.Gone, "finalized"));
        });
        var options = new TickerQRemoteExecutionOptions { WebHookSignature = "old-secret" };
        var worker = Reconciler(store, client, 1, options);

        await worker.StartAsync(CancellationToken.None);
        await store.Rescheduled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        currentSecret = "rotated-secret";
        options.WebHookSignature = currentSecret;
        store.MakeDue(intent.OutboxId);
        worker.Wake();
        await store.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task ShutdownStopsClaimingAndLeavesClaimRecoverableByNewService()
    {
        var store = new DurableOutboxFake();
        var intent = Intent("https://node.example/finalize");
        store.Seed(intent);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blocked = Client(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        });
        var first = Reconciler(store, blocked, workers: 1);
        await first.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var claimsBeforeStop = store.ClaimCalls;

        await first.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);
        Assert.Equal(claimsBeforeStop, store.ClaimCalls);
        Assert.NotNull(Assert.Single(store.Snapshot()).ClaimToken);

        store.ExpireClaims();
        using var healthy = Client((request, _) => Task.FromResult(Finalized(request, intent, Secret)));
        var second = Reconciler(store, healthy, workers: 1);
        await second.StartAsync(CancellationToken.None);
        await store.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await second.StopAsync(CancellationToken.None);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task WakeSignalCoalescesWithoutCreatingAnUnboundedQueue()
    {
        var store = new DurableOutboxFake();
        using var client = Client((_, _) => throw new InvalidOperationException("no work should be sent"));
        var worker = Reconciler(store, client, workers: 1);
        for (var i = 0; i < 10_000; i++) worker.Wake();
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.InRange(store.ClaimCalls, 1, 3);
        Assert.Empty(store.Snapshot());
    }

    private static NodeFinalizationReconciler<TimeTickerEntity, CronTickerEntity> Reconciler(
        DurableOutboxFake store, HttpClient client, int workers,
        TickerQRemoteExecutionOptions? options = null) => new(
            store.Provider,
            options ?? new TickerQRemoteExecutionOptions { WebHookSignature = Secret },
            NullLogger<NodeFinalizationReconciler<TimeTickerEntity, CronTickerEntity>>.Instance,
            client, workers);

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new DelegateHandler(send));

    private static NodeFinalizationIntent Intent(string uri)
    {
        var tickerId = Guid.NewGuid(); var token = Guid.NewGuid(); var dispatch = Guid.NewGuid();
        var control = Guid.NewGuid();
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tickerType = (int)TickerType.TimeTicker, tickerId, acquisitionToken = token,
            dispatchId = dispatch, nodeEpoch = NodeEpoch, controlNonce = control
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var parsed = new Uri(uri);
        return new NodeFinalizationIntent(1, dispatch, TickerType.TimeTicker, tickerId, token, dispatch,
            NodeEpoch, uri, parsed.PathAndQuery, false, Guid.NewGuid(), control, body, DateTime.UtcNow);
    }

    private static HttpResponseMessage Finalized(HttpRequestMessage request, NodeFinalizationIntent intent, string secret)
        => ExactAck(request, intent, secret, HttpStatusCode.OK, "finalized");

    private static HttpResponseMessage ExactAck(HttpRequestMessage request, NodeFinalizationIntent intent,
        string secret, HttpStatusCode status, string stateOrError)
    {
        object body = status == HttpStatusCode.Conflict
            ? new { error = stateOrError, controlNonce = intent.ControlNonce, identity = Identity(intent) }
            : new { state = stateOrError, controlNonce = intent.ControlNonce, identity = Identity(intent) };
        return Signed(request, status, JsonSerializer.SerializeToUtf8Bytes(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), secret);
    }

    private static HttpResponseMessage NonTerminalAck(HttpRequestMessage request, NodeFinalizationIntent intent, string scenario)
    {
        if (scenario == "unsigned") return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent("{}"u8.ToArray()) };
        if (scenario == "unauthorized") return Signed(request, HttpStatusCode.Unauthorized, "{}"u8.ToArray(), Secret);
        if (scenario == "server_error") return Signed(request, HttpStatusCode.InternalServerError, "{}"u8.ToArray(), Secret);
        if (scenario == "replay") return Signed(request, HttpStatusCode.Conflict,
            JsonSerializer.SerializeToUtf8Bytes(new { error = "replay_conflict" }), Secret);
        if (scenario == "malformed") return Signed(request, HttpStatusCode.OK, "{"u8.ToArray(), Secret);
        var identity = Identity(intent);
        object body = scenario == "wrong_nonce"
            ? new { state = "finalized", controlNonce = Guid.NewGuid(), identity }
            : new { state = "finalized", controlNonce = intent.ControlNonce,
                identity = new { identity.tickerType, tickerId = Guid.NewGuid(), identity.acquisitionToken,
                    identity.dispatchId, identity.nodeEpoch } };
        return Signed(request, HttpStatusCode.OK,
            JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Secret);
    }

    private static dynamic Identity(NodeFinalizationIntent intent) => new
    {
        tickerType = (int)intent.TickerType, tickerId = intent.TickerId,
        acquisitionToken = intent.AcquisitionToken, dispatchId = intent.DispatchId, nodeEpoch = intent.NodeEpoch
    };

    private static HttpResponseMessage Signed(HttpRequestMessage request, HttpStatusCode status, byte[] body, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = request.Headers.GetValues("x-request-nonce").Single();
        var path = request.RequestUri!.PathAndQuery;
        var canonical = Encoding.UTF8.GetBytes($"{(int)status}\n{path}\n{timestamp}\n{nonce}\n");
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Headers.TryAddWithoutValidation("x-response-timestamp", timestamp.ToString());
        response.Headers.TryAddWithoutValidation("x-request-nonce", nonce);
        response.Headers.TryAddWithoutValidation("x-tickerq-signature", Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), canonical.Concat(body).ToArray())));
        return response;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition());
    }

    private static void SetMax(ref int location, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref location)) &&
               Interlocked.CompareExchange(ref location, value, current) != current) { }
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class DurableOutboxFake
    {
        internal sealed record SnapshotRecord(NodeFinalizationIntent Intent, DateTime AvailableAtUtc,
            Guid? ClaimToken, string? ClaimedBy, int AttemptCount);
        private sealed class Record(NodeFinalizationIntent intent)
        {
            public NodeFinalizationIntent Intent { get; } = intent;
            public DateTime AvailableAtUtc = DateTime.UtcNow.AddSeconds(-1);
            public Guid? ClaimToken;
            public string? ClaimedBy;
            public int AttemptCount;
        }

        private readonly object _gate = new();
        private readonly Dictionary<Guid, Record> _records = [];
        public ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider { get; }
        public List<int> ClaimRequestCounts { get; } = [];
        public int ClaimCalls { get; private set; }
        public int MaxRequestedCount { get; private set; }
        public TaskCompletionSource Completed { get; private set; } = NewSignal();
        public TaskCompletionSource Rescheduled { get; private set; } = NewSignal();

        public DurableOutboxFake()
        {
            Provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
            Provider.SupportsDurableNodeFinalizationOutbox.Returns(true);
            Provider.ClaimDueNodeFinalizationsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTime>(),
                    Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IReadOnlyList<NodeFinalizationClaim>>(Claim(
                    call.ArgAt<string>(0), call.ArgAt<int>(1), call.ArgAt<DateTime>(2), call.ArgAt<DateTime>(3))));
            Provider.CompleteNodeFinalizationAsync(Arg.Any<NodeFinalizationClaim>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(Complete(call.ArgAt<NodeFinalizationClaim>(0))));
            Provider.RescheduleNodeFinalizationAsync(Arg.Any<NodeFinalizationClaim>(), Arg.Any<DateTime>(),
                    Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(Reschedule(call.ArgAt<NodeFinalizationClaim>(0), call.ArgAt<DateTime>(1))));
        }

        public void Seed(NodeFinalizationIntent intent)
        {
            lock (_gate) _records.Add(intent.OutboxId, new Record(intent));
        }

        public bool Contains(Guid id) { lock (_gate) return _records.ContainsKey(id); }

        public SnapshotRecord[] Snapshot()
        {
            lock (_gate) return _records.Values.Select(x => new SnapshotRecord(
                x.Intent, x.AvailableAtUtc, x.ClaimToken, x.ClaimedBy, x.AttemptCount)).ToArray();
        }

        public void MakeDue(Guid id)
        {
            lock (_gate) _records[id].AvailableAtUtc = DateTime.UtcNow.AddSeconds(-1);
            Rescheduled = NewSignal();
        }

        public void ExpireClaims()
        {
            lock (_gate) foreach (var record in _records.Values)
                record.AvailableAtUtc = DateTime.UtcNow.AddSeconds(-1);
            Completed = NewSignal();
        }

        private IReadOnlyList<NodeFinalizationClaim> Claim(string worker, int count, DateTime now, DateTime lease)
        {
            lock (_gate)
            {
                ClaimCalls++;
                ClaimRequestCounts.Add(count);
                MaxRequestedCount = Math.Max(MaxRequestedCount, count);
                var due = _records.Values.Where(x => x.AvailableAtUtc <= now)
                    .OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.Intent.OutboxId).Take(count).ToArray();
                return due.Select(record =>
                {
                    record.ClaimToken = Guid.NewGuid(); record.ClaimedBy = worker;
                    record.AvailableAtUtc = lease; record.AttemptCount++;
                    return new NodeFinalizationClaim(record.Intent, record.ClaimToken.Value, worker, lease, record.AttemptCount);
                }).ToArray();
            }
        }

        private bool Complete(NodeFinalizationClaim claim)
        {
            lock (_gate)
            {
                if (!_records.TryGetValue(claim.Intent.OutboxId, out var record) || !Matches(record, claim)) return false;
                _records.Remove(claim.Intent.OutboxId); Completed.TrySetResult(); return true;
            }
        }

        private bool Reschedule(NodeFinalizationClaim claim, DateTime available)
        {
            lock (_gate)
            {
                if (!_records.TryGetValue(claim.Intent.OutboxId, out var record) || !Matches(record, claim)) return false;
                record.AvailableAtUtc = available; record.ClaimToken = null; record.ClaimedBy = null;
                Rescheduled.TrySetResult(); return true;
            }
        }

        private static bool Matches(Record record, NodeFinalizationClaim claim)
            => record.ClaimToken == claim.ClaimToken && record.ClaimedBy == claim.ClaimedBy &&
               record.Intent.OutboxId == claim.Intent.OutboxId &&
               record.Intent.TickerType == claim.Intent.TickerType &&
               record.Intent.TickerId == claim.Intent.TickerId &&
               record.Intent.AcquisitionToken == claim.Intent.AcquisitionToken &&
               record.Intent.DispatchId == claim.Intent.DispatchId &&
               record.Intent.NodeEpoch == claim.Intent.NodeEpoch;

        private static TaskCompletionSource NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
