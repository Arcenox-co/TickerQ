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
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class NodeCallbackDispatchTests
{
    private static readonly Guid NodeEpoch = Guid.Parse("8fef33e7-826b-49e4-a36d-8eb0aa570ef1");
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
    public async Task SignedSchedulerCallback_ReturnsResultToCoreSink_WithoutRemoteFinalization()
    {
        const string secret = "node-callback-integration-secret";
        var port = ReservePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        var persistedRequest = Encoding.UTF8.GetBytes("{\"message\":\"persisted-not-registration-default\",\"count\":7}");
        builder.Services.AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(persistedRequest));
        var app = builder.Build();
        var callbackCount = 0;
        app.MapPost("/execute", async http =>
        {
            callbackCount++;
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body);
            var bytes = body.ToArray();
            var timestamp = http.Request.Headers["x-timestamp"].ToString();
            var requestNonce = http.Request.Headers["x-request-nonce"].ToString();
            var canonical = Encoding.UTF8.GetBytes($"POST\n/execute\n{timestamp}\n{requestNonce}\n");
            var signed = canonical.Concat(bytes).ToArray();
            var expected = Convert.ToBase64String(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed));
            Assert.Equal(expected, http.Request.Headers["x-tickerq-signature"].ToString());

            using var request = JsonDocument.Parse(bytes);
            var root = request.RootElement;
            Assert.True(root.GetProperty("hasRequest").GetBoolean());
            Assert.Equal("CallbackJob", root.GetProperty("functionName").GetString());
            Assert.Equal(NodeEpoch, root.GetProperty("nodeEpoch").GetGuid());
            Assert.Equal(persistedRequest, root.GetProperty("requestPayload").GetBytesFromBase64());
            var identity = new
            {
                tickerType = root.GetProperty("tickerType").GetInt32(),
                tickerId = root.GetProperty("tickerId").GetGuid(),
                acquisitionToken = root.GetProperty("acquisitionToken").GetGuid(),
                dispatchId = root.GetProperty("dispatchId").GetGuid(),
                nodeEpoch = root.GetProperty("nodeEpoch").GetGuid()
            };
            var outcome = new
            {
                identity,
                tickerId = root.GetProperty("id").GetGuid(),
                acquisitionToken = root.GetProperty("acquisitionToken").GetGuid(),
                status = (int)TickerStatus.Done,
                resultEnvelope = new
                {
                    payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"answer\":42}")),
                    envelopeVersion = 1,
                    mediaType = "application/json",
                    contractId = "sha256:test",
                    contractType = "CallbackResult"
                }
            };
            http.Response.ContentType = "application/json";
            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(outcome, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var responseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var responseCanonical = Encoding.UTF8.GetBytes($"200\n/execute\n{responseTimestamp}\n{requestNonce}\n");
            var responseSignature = Convert.ToBase64String(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret), responseCanonical.Concat(responseBytes).ToArray()));
            http.Response.Headers["x-response-timestamp"] = responseTimestamp.ToString();
            http.Response.Headers["x-request-nonce"] = requestNonce;
            http.Response.Headers["x-tickerq-signature"] = responseSignature;
            await http.Response.Body.WriteAsync(responseBytes);
        });

        await app.StartAsync();
        try
        {
            var tickerId = Guid.NewGuid();
            var token = Guid.NewGuid();
            var context = new TickerFunctionContext
            {
                Id = tickerId,
                AcquisitionToken = token,
                FunctionName = "CallbackJob@node",
                Type = TickerType.TimeTicker,
                ScheduledFor = DateTime.UtcNow,
                ResultSink = new TickerResultSink()
            };
            var callback = NodeCallbackExecutionDelegateFactory.Create(
                $"http://127.0.0.1:{port}", () => secret, NodeEpoch,
                allowPrivateCallbackAddressesForLocalDevelopment: true);

            await callback(CancellationToken.None, app.Services, context);

            Assert.Equal(1, callbackCount);
            Assert.True(context.ResultSink.HasResult);
            Assert.Equal("{\"answer\":42}", Encoding.UTF8.GetString(context.ResultSink.Envelope!.Payload.Span));
            Assert.Equal("sha256:test", context.ResultSink.Envelope.ContractId);
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
    public async Task Callback_DoesNotFollowRedirects()
    {
        var port = ReservePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton<IRemotePayloadLoader>(new StubPayloadLoader(null));
        var app = builder.Build();
        var redirectedEndpointCalls = 0;
        app.MapPost("/execute", http =>
        {
            http.Response.Redirect("/redirected");
            return Task.CompletedTask;
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
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                callback(CancellationToken.None, app.Services, NewContext()));
            Assert.Contains("response timestamp", exception.Message);
            Assert.Equal(0, redirectedEndpointCalls);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
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
}
