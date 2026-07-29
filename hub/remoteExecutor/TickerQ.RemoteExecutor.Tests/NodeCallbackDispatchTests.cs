using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class NodeCallbackDispatchTests
{
    private static readonly Guid NodeEpoch = Guid.Parse("8fef33e7-826b-49e4-a36d-8eb0aa570ef1");

    [Fact]
    public async Task RealFactoryNotStarted_SchedulerUsesAcknowledgedCommitWithoutOutboxOrChildRelease()
    {
        var root = FindRepositoryRoot();
        var sdkRoot = Path.Combine(root, "hub", "sdks", "node");
        var fixture = Path.Combine(root, "hub", "remoteExecutor", "TickerQ.RemoteExecutor.Tests", "Fixtures", "node-callback-server.mjs");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node", ArgumentList = { fixture, sdkRoot }, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start Node callback fixture.");
        try
        {
            var startup = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var port = int.Parse(startup!["PORT:".Length..]);
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => "dotnet-node-e2e-secret", NodeEpoch, true);
            var manager = Substitute.For<IInternalTickerManager>();
            var instrumentation = Substitute.For<ITickerQInstrumentation>();
            await using var services = new ServiceCollection()
                .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null))
                .AddSingleton(manager)
                .AddSingleton(instrumentation)
                .BuildServiceProvider();
            var handler = new global::TickerQ.TickerExecutionTaskHandler(
                services, Substitute.For<ITickerClock>(), instrumentation, manager,
                new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>());
            var childCalls = 0;
            var context = new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(), AcquisitionToken = Guid.NewGuid(),
                FunctionName = $"missing-{Guid.NewGuid():N}@node-e2e", Type = TickerType.TimeTicker,
                ChainGeneration = Guid.NewGuid(), ExecutionTime = DateTime.UtcNow, RetryIntervals = [],
                Retries = 0, RetryCount = 0, Status = TickerStatus.Idle, CachedDelegate = callback,
                TimeTickerChildren =
                [
                    new InternalFunctionContext
                    {
                        TickerId = Guid.NewGuid(), FunctionName = "should-not-run", Type = TickerType.TimeTicker,
                        ChainGeneration = Guid.NewGuid(), ExecutionTime = DateTime.UtcNow, RetryIntervals = [],
                        Status = TickerStatus.Idle, RunCondition = RunCondition.OnAnyCompletedStatus,
                        CachedDelegate = (_, _, _) => { childCalls++; return Task.CompletedTask; }, TimeTickerChildren = []
                    }
                ]
            };

            await handler.ExecuteTaskAsync(context, isDue: false).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(TickerStatus.Skipped, context.Status);
            Assert.Equal(0, childCalls);
            await manager.Received(1).UpdateTickerFromRemoteAsync(context, Arg.Any<CancellationToken>());
            await manager.DidNotReceiveWithAnyArgs()
                .UpdateTickerFromRemoteAsync(default!, default!, default);
            await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
            using var client = new HttpClient();
            using var stats = await JsonDocument.ParseAsync(await client.GetStreamAsync($"http://127.0.0.1:{port}/stats"));
            Assert.Equal(0, stats.RootElement.GetProperty("invocationCount").GetInt32());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task CancelFirst_ReplaysLostInitialExecute_AndNeverInvokesNodeUserCode()
    {
        var root = FindRepositoryRoot();
        var sdkRoot = Path.Combine(root, "hub", "sdks", "node");
        var fixture = Path.Combine(root, "hub", "remoteExecutor", "TickerQ.RemoteExecutor.Tests", "Fixtures", "node-callback-server.mjs");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node", ArgumentList = { fixture, sdkRoot }, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
            Environment = { ["DROP_FIRST_EXECUTE"] = "1" }
        }) ?? throw new InvalidOperationException("Could not start Node callback fixture.");
        try
        {
            var startup = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var port = int.Parse(startup!["PORT:".Length..]);
            await using var services = new ServiceCollection()
                .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null)).BuildServiceProvider();
            var context = NewContext(); context.FunctionName = "dotnet-cancel-first@node-e2e";
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => "dotnet-node-e2e-secret", NodeEpoch, true);
            using var cts = new CancellationTokenSource(); cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => callback(cts.Token, services, context))
                .WaitAsync(TimeSpan.FromSeconds(10));

            using var client = new HttpClient();
            using var stats = await JsonDocument.ParseAsync(await client.GetStreamAsync($"http://127.0.0.1:{port}/stats"));
            Assert.Equal(0, stats.RootElement.GetProperty("invocationCount").GetInt32());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task CancelFirst_MissingFunction_ReturnsExactNotStartedProofWithoutInvocationOrFinalize()
    {
        var root = FindRepositoryRoot();
        var sdkRoot = Path.Combine(root, "hub", "sdks", "node");
        var fixture = Path.Combine(root, "hub", "remoteExecutor", "TickerQ.RemoteExecutor.Tests", "Fixtures", "node-callback-server.mjs");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node", ArgumentList = { fixture, sdkRoot }, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start Node callback fixture.");
        try
        {
            var startup = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var port = int.Parse(startup!["PORT:".Length..]);
            await using var services = new ServiceCollection()
                .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null)).BuildServiceProvider();
            var context = NewContext(); context.FunctionName = $"missing-{Guid.NewGuid():N}@node-e2e";
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => "dotnet-node-e2e-secret", NodeEpoch, true);
            using var cts = new CancellationTokenSource(); cts.Cancel();

            var exception = await Assert.ThrowsAsync<RemoteExecutionNotStartedException>(() =>
                callback(cts.Token, services, context)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("function_not_found", exception.Message);
            Assert.True(context.IsRemoteCallbackExecution);
            Assert.Null(context.RemoteFinalizationIntent);
            using var client = new HttpClient();
            using var stats = await JsonDocument.ParseAsync(await client.GetStreamAsync($"http://127.0.0.1:{port}/stats"));
            Assert.Equal(0, stats.RootElement.GetProperty("invocationCount").GetInt32());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task CancelFirst_DelayedRegistrationAckAfterRejectedDeletion_StartsFreshCycleWithoutInvocationOrHang()
    {
        var root = FindRepositoryRoot();
        var sdkRoot = Path.Combine(root, "hub", "sdks", "node");
        var fixture = Path.Combine(root, "hub", "remoteExecutor", "TickerQ.RemoteExecutor.Tests", "Fixtures", "node-callback-server.mjs");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node", ArgumentList = { fixture, sdkRoot }, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
            Environment = { ["DELAY_CANCEL_AFTER_REJECTION"] = "1" }
        }) ?? throw new InvalidOperationException("Could not start Node callback fixture.");
        try
        {
            var startup = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var port = int.Parse(startup!["PORT:".Length..]);
            await using var services = new ServiceCollection()
                .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null)).BuildServiceProvider();
            var context = NewContext(); context.FunctionName = "dotnet-late-registration@node-e2e";
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => "dotnet-node-e2e-secret", NodeEpoch, true);
            using var cts = new CancellationTokenSource(); cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => callback(cts.Token, services, context))
                .WaitAsync(TimeSpan.FromSeconds(10));

            using var client = new HttpClient();
            using var stats = await JsonDocument.ParseAsync(await client.GetStreamAsync($"http://127.0.0.1:{port}/stats"));
            Assert.Equal(0, stats.RootElement.GetProperty("invocationCount").GetInt32());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task Cancellation_WithReachableCancelAndExecuteOutage_RemainsUnsettledAndRetriesExactControls()
    {
        const string secret = "cancel-outage-secret";
        var port = ReservePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null));
        var app = builder.Build();
        var executeBodies = new List<byte[]>();
        var executeNonces = new List<string>();
        var cancelBodies = new List<byte[]>();
        var cancelNonces = new List<string>();
        var recover = 0;

        async Task Reply(HttpContext http, int status, string path, byte[] responseBytes)
        {
            var requestNonce = http.Request.Headers["x-request-nonce"].ToString();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var canonical = Encoding.UTF8.GetBytes($"{status}\n{path}\n{timestamp}\n{requestNonce}\n");
            http.Response.StatusCode = status;
            http.Response.Headers["x-response-timestamp"] = timestamp.ToString();
            http.Response.Headers["x-request-nonce"] = requestNonce;
            http.Response.Headers["x-tickerq-signature"] = Convert.ToBase64String(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret), canonical.Concat(responseBytes).ToArray()));
            await http.Response.Body.WriteAsync(responseBytes);
        }

        app.MapPost("/execute", async http =>
        {
            using var body = new MemoryStream(); await http.Request.Body.CopyToAsync(body);
            executeBodies.Add(body.ToArray()); executeNonces.Add(http.Request.Headers["x-request-nonce"].ToString());
            var recovered = Volatile.Read(ref recover) == 1;
            var response = Encoding.UTF8.GetBytes(recovered
                ? "{\"error\":\"node_epoch_mismatch\"}"
                : "{\"error\":\"registry_full\"}");
            await Reply(http, recovered ? 409 : 503, "/execute", response);
        });
        app.MapPost("/cancel", async http =>
        {
            using var body = new MemoryStream(); await http.Request.Body.CopyToAsync(body);
            var requestBytes = body.ToArray(); cancelBodies.Add(requestBytes);
            cancelNonces.Add(http.Request.Headers["x-request-nonce"].ToString());
            using var request = JsonDocument.Parse(requestBytes); var root = request.RootElement;
            var response = JsonSerializer.SerializeToUtf8Bytes(new
            {
                state = "cancellation_registered", controlNonce = root.GetProperty("controlNonce").GetGuid(),
                identity = new
                {
                    tickerType = root.GetProperty("tickerType").GetInt32(), tickerId = root.GetProperty("tickerId").GetGuid(),
                    acquisitionToken = root.GetProperty("acquisitionToken").GetGuid(), dispatchId = root.GetProperty("dispatchId").GetGuid(),
                    nodeEpoch = root.GetProperty("nodeEpoch").GetGuid()
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await Reply(http, 202, "/cancel", response);
        });

        await app.StartAsync();
        try
        {
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => secret, NodeEpoch, true);
            var context = NewContext();
            using var cts = new CancellationTokenSource(); cts.Cancel();
            var dispatch = callback(cts.Token, app.Services, context);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((executeBodies.Count < 3 || cancelBodies.Count < 3) && DateTime.UtcNow < deadline)
                await Task.Delay(25);

            Assert.False(dispatch.IsCompleted, "reachable cancel plus permanent execute outage must remain unresolved");
            Assert.False(context.IsRemoteCallbackExecution, "an unresolved dispatch must not expose a terminal persistence path");
            Assert.True(executeBodies.Count >= 3); Assert.True(cancelBodies.Count >= 3);
            Assert.Single(executeNonces.Distinct()); Assert.Single(cancelNonces.Distinct());
            Assert.All(executeBodies, bytes => Assert.Equal(executeBodies[0], bytes));
            Assert.All(cancelBodies, bytes => Assert.Equal(cancelBodies[0], bytes));

            Volatile.Write(ref recover, 1);
            await Assert.ThrowsAsync<RemoteExecutionNotStartedException>(() => dispatch).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(context.IsRemoteCallbackExecution);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task DotNetCancellation_DoesNotReturnBeforeNonCooperativeNodeExecutionExits()
    {
        var root = FindRepositoryRoot();
        var sdkRoot = Path.Combine(root, "hub", "sdks", "node");
        var fixture = Path.Combine(root, "hub", "remoteExecutor", "TickerQ.RemoteExecutor.Tests", "Fixtures", "node-callback-server.mjs");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node", ArgumentList = { fixture, sdkRoot }, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start Node callback fixture.");
        try
        {
            var startup = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var port = int.Parse(startup!["PORT:".Length..]);
            await using var services = new ServiceCollection()
                .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(Encoding.UTF8.GetBytes("{\"delayMs\":600}")))
                .BuildServiceProvider();
            var context = NewContext();
            context.FunctionName = "dotnet-delayed-cancel@node-e2e";
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => "dotnet-node-e2e-secret", NodeEpoch, true);
            using var cts = new CancellationTokenSource();
            var stopwatch = Stopwatch.StartNew();
            var execution = callback(cts.Token, services, context);
            await Task.Delay(100);
            cts.Cancel();
            await Task.Delay(150);

            Assert.False(execution.IsCompleted, "Core-facing delegate must remain pending until Node acknowledges pipeline exit.");
            await Assert.ThrowsAsync<TaskCanceledException>(() => execution);
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task DotNetDispatcher_ExecutesBareFunctionOnActualNodeSdk_WithPersistedRequest()
    {
        var root = FindRepositoryRoot();
        var sdkRoot = Path.Combine(root, "hub", "sdks", "node");
        var fixture = Path.Combine(root, "hub", "remoteExecutor", "TickerQ.RemoteExecutor.Tests", "Fixtures", "node-callback-server.mjs");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node",
            ArgumentList = { fixture, sdkRoot },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start Node callback fixture.");
        try
        {
            var startup = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.StartsWith("PORT:", startup);
            var port = int.Parse(startup!["PORT:".Length..]);
            var persisted = Encoding.UTF8.GetBytes("{\"message\":\"persisted-not-registration-default\",\"count\":7}");
            await using var services = new ServiceCollection()
                .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(persisted))
                .BuildServiceProvider();
            var context = NewContext();
            context.FunctionName = "dotnet-e2e-job@node-e2e";
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => "dotnet-node-e2e-secret", NodeEpoch, true);

            await callback(CancellationToken.None, services, context);

            Assert.True(context.ResultSink.HasResult);
            using var result = JsonDocument.Parse(context.ResultSink.Envelope!.Payload);
            var received = result.RootElement.GetProperty("received");
            Assert.Equal("persisted-not-registration-default", received.GetProperty("message").GetString());
            Assert.Equal(7, received.GetProperty("count").GetInt32());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }


    [Fact]
    public async Task Execute_ReconcilesEveryInvalidResponseProofWithExactDispatch()
    {
        const string secret = "invalid-response-proof-secret";
        var port = ReservePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null));
        var app = builder.Build();
        var bodies = new List<byte[]>();
        var nonces = new List<string>();
        var remoteInvocations = 0;
        app.MapPost("/execute", async http =>
        {
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body);
            var requestBytes = body.ToArray();
            bodies.Add(requestBytes);
            var requestNonce = http.Request.Headers["x-request-nonce"].ToString();
            nonces.Add(requestNonce);
            if (bodies.Count == 1) remoteInvocations++;

            using var request = JsonDocument.Parse(requestBytes);
            var root = request.RootElement;
            var identity = new
            {
                tickerType = root.GetProperty("tickerType").GetInt32(), tickerId = root.GetProperty("tickerId").GetGuid(),
                acquisitionToken = root.GetProperty("acquisitionToken").GetGuid(), dispatchId = root.GetProperty("dispatchId").GetGuid(),
                nodeEpoch = root.GetProperty("nodeEpoch").GetGuid()
            };
            object outcome = new
            {
                identity, tickerId = identity.tickerId, acquisitionToken = (Guid?)identity.acquisitionToken,
                status = (int)TickerStatus.Done, exceptionDetails = (string?)null, resultEnvelope = (object?)null
            };
            if (bodies.Count == 5)
            {
                outcome = new
                {
                    identity = new { identity.tickerType, tickerId = Guid.NewGuid(), identity.acquisitionToken, identity.dispatchId, identity.nodeEpoch },
                    tickerId = identity.tickerId, acquisitionToken = (Guid?)identity.acquisitionToken,
                    status = (int)TickerStatus.Done, exceptionDetails = (string?)null, resultEnvelope = (object?)null
                };
            }

            var responseBytes = bodies.Count == 4
                ? "{"u8.ToArray()
                : JsonSerializer.SerializeToUtf8Bytes(outcome, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var responseTimestamp = bodies.Count == 2
                ? DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var responseNonce = bodies.Count == 3 ? Guid.NewGuid().ToString("D") : requestNonce;
            var canonical = Encoding.UTF8.GetBytes($"200\n/execute\n{responseTimestamp}\n{requestNonce}\n");
            var signingSecret = bodies.Count == 1 ? "wrong-secret" : secret;
            http.Response.Headers["x-response-timestamp"] = responseTimestamp.ToString();
            http.Response.Headers["x-request-nonce"] = responseNonce;
            http.Response.Headers["x-tickerq-signature"] = Convert.ToBase64String(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(signingSecret), canonical.Concat(responseBytes).ToArray()));
            await http.Response.Body.WriteAsync(responseBytes);
        });

        await app.StartAsync();
        try
        {
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => secret, NodeEpoch, true);
            await callback(CancellationToken.None, app.Services, NewContext()).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(6, bodies.Count);
            Assert.Equal(1, remoteInvocations);
            Assert.Single(nonces.Distinct());
            Assert.All(bodies, bytes => Assert.Equal(bodies[0], bytes));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }


    [Theory]
    [InlineData("file:///tmp/callback")]
    [InlineData("ftp://example.com/callback")]
    [InlineData("http://user:password@example.com/callback")]
    [InlineData("https://example.com/callback?secret=value")]
    [InlineData("https://example.com/callback#fragment")]
    public void CallbackUrl_RejectsUnsafeSchemeCredentialsOrFragment(string callbackUrl)
        => Assert.Throws<ArgumentException>(() =>
            NodeCallbackExecutionDelegateFactory.Create(callbackUrl, () => "secret", NodeEpoch));

    [Theory]
    [InlineData("http://127.0.0.1:9")]
    [InlineData("http://[::ffff:127.0.0.1]:9")]
    [InlineData("http://[fc00::1]:9")]
    public async Task CallbackUrl_RejectsPrivateAddressByDefault(string callbackUrl)
    {
        var services = new ServiceCollection()
            .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null))
            .BuildServiceProvider();
        var callback = NodeCallbackExecutionDelegateFactory.Create(callbackUrl, () => "secret", NodeEpoch);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => callback(
            CancellationToken.None, services, NewContext()));
        Assert.Contains("forbidden network address", exception.Message);
    }

    [Fact]
    public void CallbackTransport_DisablesConfiguredAndEnvironmentProxies()
    {
        using var handler = NodeCallbackExecutionDelegateFactory.CreateHandler(false);
        Assert.False(handler.UseProxy);
    }

    [Fact]
    public async Task Execute_UncertainResponseThenEpochMismatch_ReconcilesSameDispatchWithoutExposingNotStarted()
    {
        var port = ReservePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null));
        var app = builder.Build();
        var redirectedEndpointCalls = 0;
        var executeCalls = 0;
        var recover = 0;
        var bodies = new List<byte[]>();
        var nonces = new List<string>();
        var secondReplySent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapPost("/execute", async http =>
        {
            executeCalls++;
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body);
            var requestBytes = body.ToArray();
            bodies.Add(requestBytes);
            var nonce = http.Request.Headers["x-request-nonce"].ToString();
            nonces.Add(nonce);
            if (executeCalls == 1)
            {
                http.Response.Redirect("/redirected");
                return;
            }

            using var request = JsonDocument.Parse(requestBytes);
            var root = request.RootElement;
            var identity = new
            {
                tickerType = root.GetProperty("tickerType").GetInt32(),
                tickerId = root.GetProperty("tickerId").GetGuid(),
                acquisitionToken = root.GetProperty("acquisitionToken").GetGuid(),
                dispatchId = root.GetProperty("dispatchId").GetGuid(),
                nodeEpoch = root.GetProperty("nodeEpoch").GetGuid()
            };
            var isRecovered = Volatile.Read(ref recover) == 1;
            var status = isRecovered ? 200 : 409;
            var responseBytes = isRecovered
                ? JsonSerializer.SerializeToUtf8Bytes(new
                {
                    identity, tickerId = identity.tickerId, acquisitionToken = (Guid?)identity.acquisitionToken,
                    status = (int)TickerStatus.Done, exceptionDetails = (string?)null, resultEnvelope = (object?)null
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : Encoding.UTF8.GetBytes("{\"error\":\"node_epoch_mismatch\"}");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var canonical = Encoding.UTF8.GetBytes($"{status}\n/execute\n{timestamp}\n{nonce}\n");
            http.Response.StatusCode = status;
            http.Response.Headers["x-response-timestamp"] = timestamp.ToString();
            http.Response.Headers["x-request-nonce"] = nonce;
            http.Response.Headers["x-tickerq-signature"] = Convert.ToBase64String(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes("secret"), canonical.Concat(responseBytes).ToArray()));
            await http.Response.Body.WriteAsync(responseBytes);
            if (!isRecovered) secondReplySent.TrySetResult();
        });
        app.MapPost("/redirected", http =>
        {
            redirectedEndpointCalls++;
            http.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        try
        {
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => "secret", NodeEpoch, true);
            var context = NewContext();
            var dispatch = callback(CancellationToken.None, app.Services, context);
            await secondReplySent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.False(dispatch.IsCompleted, "a later epoch mismatch cannot prove an earlier uncertain attempt did not execute");
            Assert.False(context.IsRemoteCallbackExecution, "uncertainty must not expose a terminal remote commit path");
            Assert.Null(context.RemoteFinalizationIntent);
            Assert.Equal(0, redirectedEndpointCalls);
            Assert.Single(nonces.Distinct());
            Assert.All(bodies, bytes => Assert.Equal(bodies[0], bytes));

            Volatile.Write(ref recover, 1);
            await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(context.IsRemoteCallbackExecution);
            Assert.NotNull(context.RemoteFinalizationIntent);
            Assert.True(executeCalls >= 3);
            Assert.Single(nonces.Distinct());
            Assert.All(bodies, bytes => Assert.Equal(bodies[0], bytes));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Execute_SecretRotationAfterUncertainResponse_UsesCurrentSecretWithExactRequestIdentity()
    {
        const string oldSecret = "old-rotation-secret";
        const string newSecret = "new-rotation-secret";
        var currentSecret = oldSecret;
        var calls = 0;
        var bodies = new List<byte[]>();
        var nonces = new List<string>();
        using var client = new HttpClient(new DelegateHandler(async request =>
        {
            var body = await request.Content!.ReadAsByteArrayAsync();
            bodies.Add(body);
            var nonce = request.Headers.GetValues("x-request-nonce").Single();
            nonces.Add(nonce);
            var attempt = Interlocked.Increment(ref calls);
            AssertRequestSignedWith(request, body, attempt == 1 ? oldSecret : newSecret);
            if (attempt == 1)
            {
                currentSecret = newSecret;
                throw new HttpRequestException("response lost after remote admission");
            }

            using var parsed = JsonDocument.Parse(body); var root = parsed.RootElement;
            var identity = new
            {
                tickerType = root.GetProperty("tickerType").GetInt32(), tickerId = root.GetProperty("tickerId").GetGuid(),
                acquisitionToken = root.GetProperty("acquisitionToken").GetGuid(), dispatchId = root.GetProperty("dispatchId").GetGuid(),
                nodeEpoch = root.GetProperty("nodeEpoch").GetGuid()
            };
            var responseBody = JsonSerializer.SerializeToUtf8Bytes(new
            {
                identity, tickerId = identity.tickerId, acquisitionToken = (Guid?)identity.acquisitionToken,
                status = (int)TickerStatus.Done, exceptionDetails = (string?)null, resultEnvelope = (object?)null
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return SignedResponse(HttpStatusCode.OK, request.RequestUri!, nonce, responseBody, newSecret);
        }));
        await using var services = new ServiceCollection()
            .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null)).BuildServiceProvider();
        var callback = NodeCallbackExecutionDelegateFactory.Create(
            "https://callbacks.example.test", () => currentSecret, NodeEpoch, transportOverride: client);

        await callback(CancellationToken.None, services, NewContext()).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, calls);
        Assert.Single(nonces.Distinct());
        Assert.All(bodies, body => Assert.Equal(bodies[0], body));
    }

    [Fact]
    public async Task Execute_ForbiddenNetworkAfterUncertainty_RemainsQuarantinedWithoutReconnect()
    {
        var calls = 0;
        using var client = new HttpClient(new DelegateHandler(_ =>
        {
            var attempt = Interlocked.Increment(ref calls);
            throw new HttpRequestException(attempt == 1
                ? "response lost after remote admission"
                : "Node callback DNS resolution returned a forbidden network address.");
        }));
        await using var services = new ServiceCollection()
            .AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null)).BuildServiceProvider();
        var callback = NodeCallbackExecutionDelegateFactory.Create(
            "https://callbacks.example.test", () => "secret", NodeEpoch, transportOverride: client);
        var context = NewContext();

        var dispatch = callback(CancellationToken.None, services, context);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref calls) < 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
        await Task.Delay(400);

        Assert.Equal(2, calls);
        Assert.False(dispatch.IsCompleted);
        Assert.False(context.IsRemoteCallbackExecution);
        Assert.Null(context.RemoteFinalizationIntent);
    }

    [Fact]
    public void GeneratedHubNodeContract_CarriesAdditiveSdkTypeFieldEleven()
    {
        var node = new Hub.HubNode { SdkType = "nodejs" };
        var bytes = node.ToByteArray();
        var roundTrip = Hub.HubNode.Parser.ParseFrom(bytes);

        Assert.Equal("nodejs", roundTrip.SdkType);
        Assert.Contains((byte)((11 << 3) | 2), bytes);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void AssertRequestSignedWith(HttpRequestMessage request, byte[] body, string secret)
    {
        var timestamp = request.Headers.GetValues("x-timestamp").Single();
        var nonce = request.Headers.GetValues("x-request-nonce").Single();
        var canonical = Encoding.UTF8.GetBytes($"POST\n{request.RequestUri!.PathAndQuery}\n{timestamp}\n{nonce}\n");
        var expected = Convert.ToBase64String(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), canonical.Concat(body).ToArray()));
        Assert.Equal(expected, request.Headers.GetValues("x-tickerq-signature").Single());
    }

    private static HttpResponseMessage SignedResponse(HttpStatusCode status, Uri uri, string nonce, byte[] body, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var canonical = Encoding.UTF8.GetBytes($"{(int)status}\n{uri.PathAndQuery}\n{timestamp}\n{nonce}\n");
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Headers.TryAddWithoutValidation("x-response-timestamp", timestamp.ToString());
        response.Headers.TryAddWithoutValidation("x-request-nonce", nonce);
        response.Headers.TryAddWithoutValidation("x-tickerq-signature", Convert.ToBase64String(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), canonical.Concat(body).ToArray())));
        return response;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "hub", "sdks", "node", "package.json")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static TickerFunctionContext NewContext() => new()
    {
        Id = Guid.NewGuid(), AcquisitionToken = Guid.NewGuid(), FunctionName = "CallbackJob@node",
        Type = TickerType.TimeTicker, ScheduledFor = DateTime.UtcNow, ResultSink = new TickerResultSink()
    };

    private sealed class StubPayloadLoader(byte[]? payload) : IRemotePayloadLoader
    {
        public Task<byte[]?> LoadPayloadAsync(Guid tickerId, TickerType type, CancellationToken ct)
            => Task.FromResult(payload);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
