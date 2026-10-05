using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class NodeCompatibilityEndpointsTests
{
    private const string Secret = "node-compatibility-test-secret";
    private const string Prefix = "/tickerq/node";

    [Fact]
    public async Task SignedContext_MapsFenceAndExplicitNullResult_ToManager()
    {
        InternalFunctionContext? captured = null;
        var manager = Substitute.For<IInternalTickerManager>();
        manager.UpdateTickerFromRemoteAsync(Arg.Do<InternalFunctionContext>(x => captured = x), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        using var host = await StartHostAsync(manager);
        var id = Guid.NewGuid();
        var token = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            parametersToUpdate = new[] { "Status", "ResultEnvelope" },
            functionName = "Job@node",
            tickerId = id,
            acquisitionToken = token,
            type = (int)TickerType.TimeTicker,
            status = (int)TickerStatus.Done,
            resultEnvelope = (object?)null
        });

        var response = await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
            $"{Prefix}/time-tickers/context", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(token, captured.AcquisitionToken);
        Assert.Contains(nameof(InternalFunctionContext.ResultEnvelope), captured.ParametersToUpdate);
        Assert.Null(captured.ResultEnvelope);
    }

    [Fact]
    public async Task SignedContext_MapsEnvelopeBytesAndMetadataExactly()
    {
        InternalFunctionContext? captured = null;
        var manager = Substitute.For<IInternalTickerManager>();
        manager.UpdateTickerFromRemoteAsync(Arg.Do<InternalFunctionContext>(x => captured = x), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        using var host = await StartHostAsync(manager);
        var payload = Encoding.UTF8.GetBytes("null");
        var body = ContextJson(resultEnvelope: new
        {
            envelopeVersion = 1,
            mediaType = "application/json",
            contractId = "sha256:contract",
            contractType = "Example.Result",
            payload = Convert.ToBase64String(payload)
        }, type: TickerType.CronTickerOccurrence);

        var response = await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
            $"{Prefix}/cron-ticker-occurrences/context", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured?.ResultEnvelope);
        Assert.Equal(payload, captured.ResultEnvelope.ToPayloadArray());
        Assert.Equal(1, captured.ResultEnvelope.Version);
        Assert.Equal("application/json", captured.ResultEnvelope.MediaType);
        Assert.Equal("sha256:contract", captured.ResultEnvelope.ContractId);
        Assert.Equal("Example.Result", captured.ResultEnvelope.ContractType);
    }

    [Fact]
    public async Task Authentication_IsCheckedBeforeMalformedJson_AndRejectsTamperAndSkew()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        using var host = await StartHostAsync(manager);
        var client = host.GetTestClient();
        var path = $"{Prefix}/time-tickers/context";

        var unsignedMalformed = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent("{")
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(unsignedMalformed)).StatusCode);

        var valid = ContextJson(resultEnvelope: null);
        var tampered = CreateSignedRequest(HttpMethod.Put, path, valid);
        tampered.Content = new StringContent(valid + " ", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(tampered)).StatusCode);

        var stale = CreateSignedRequest(HttpMethod.Put, path, valid,
            DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(stale)).StatusCode);

        var signedMalformed = CreateSignedRequest(HttpMethod.Put, path, "{");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(signedMalformed)).StatusCode);
    }

    [Theory]
    [InlineData("not base64!", 1, 400)]
    [InlineData("bnVsbA==", 2, 400)]
    public async Task InvalidEnvelope_IsRejected(string payload, int version, int expectedStatus)
    {
        var manager = Substitute.For<IInternalTickerManager>();
        using var host = await StartHostAsync(manager);
        var body = ContextJson(new { envelopeVersion = version, mediaType = "application/json", payload });

        var response = await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
            $"{Prefix}/time-tickers/context", body);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerFromRemoteAsync(default!, default);
    }

    [Fact]
    public async Task OversizeAndNonSuccessResult_AreRejected()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        using var host = await StartHostAsync(manager);
        var oversized = Convert.ToBase64String(new byte[1024 * 1024 + 1]);
        var oversizeBody = ContextJson(new { envelopeVersion = 1, mediaType = "application/json", payload = oversized });
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
                $"{Prefix}/time-tickers/context", oversizeBody)).StatusCode);

        var failedBody = ContextJson(new { envelopeVersion = 1, mediaType = "application/json", payload = "bnVsbA==" },
            TickerStatus.Failed);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
                $"{Prefix}/time-tickers/context", failedBody)).StatusCode);
    }

    [Fact]
    public async Task StaleManagerAcknowledgement_ReturnsConflict()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.UpdateTickerFromRemoteAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TickerResultNotAcknowledgedException("stale acquisition"));
        using var host = await StartHostAsync(manager);

        var response = await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
            $"{Prefix}/time-tickers/context", ContextJson(resultEnvelope: null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MissingOrMalformedFence_AndMissingSuccessClear_AreRejected()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        using var host = await StartHostAsync(manager);
        var path = $"{Prefix}/time-tickers/context";
        var missing = JsonSerializer.Serialize(new
        {
            parametersToUpdate = new[] { "Status", "ResultEnvelope" },
            functionName = "Job@node",
            tickerId = Guid.NewGuid(),
            type = (int)TickerType.TimeTicker,
            status = (int)TickerStatus.Done,
            resultEnvelope = (object?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Put, path, missing)).StatusCode);

        var malformed = missing.Replace("\"type\"", "\"acquisitionToken\":\"not-a-uuid\",\"type\"");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Put, path, malformed)).StatusCode);

        var missingClear = JsonSerializer.Serialize(new
        {
            parametersToUpdate = new[] { "Status" },
            functionName = "Job@node",
            tickerId = Guid.NewGuid(),
            acquisitionToken = Guid.NewGuid(),
            type = (int)TickerType.TimeTicker,
            status = (int)TickerStatus.Done
        });
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Put, path, missingClear)).StatusCode);
    }

    [Fact]
    public async Task RequestBodyLimitAndManagerFailureMapping_AreExplicit()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        using var host = await StartHostAsync(manager);
        var huge = new string('x', (2 * 1024 * 1024) + 1);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Post,
                $"{Prefix}/time-tickers", huge)).StatusCode);

        manager.UpdateTickerFromRemoteAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new NotSupportedException("result provider"));
        Assert.Equal(HttpStatusCode.NotImplemented,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
                $"{Prefix}/time-tickers/context", ContextJson(resultEnvelope: null))).StatusCode);

        manager.UpdateTickerFromRemoteAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("unexpected"));
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await SendSignedAsync(host.GetTestClient(), HttpMethod.Put,
                $"{Prefix}/time-tickers/context", ContextJson(resultEnvelope: null))).StatusCode);
    }

    [Fact]
    public async Task Crud_ReturnsProviderAffectedCount()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        using var host = await StartHostAsync(manager, provider);

        var response = await SendSignedAsync(host.GetTestClient(), HttpMethod.Post,
            $"{Prefix}/time-tickers", "[{}]");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await response.Content.ReadAsStringAsync());
    }

    private static string ContextJson(
        object? resultEnvelope,
        TickerStatus status = TickerStatus.Done,
        TickerType type = TickerType.TimeTicker)
        => JsonSerializer.Serialize(new
        {
            parametersToUpdate = new[] { "Status", "ResultEnvelope" },
            functionName = "Job@node",
            tickerId = Guid.NewGuid(),
            acquisitionToken = Guid.NewGuid(),
            type = (int)type,
            status = (int)status,
            resultEnvelope
        });

    private static async Task<IHost> StartHostAsync(
        IInternalTickerManager manager,
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>? provider = null)
    {
        provider ??= Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var options = new TickerQRemoteExecutionOptions { WebHookSignature = Secret };
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(options);
                    services.AddSingleton(manager);
                    services.AddSingleton(provider);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapTickerQNodeCompatibilityEndpoints(Prefix));
                }))
            .StartAsync();
        return host;
    }

    private static async Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client, HttpMethod method, string path, string body)
        => await client.SendAsync(CreateSignedRequest(method, path, body));

    private static HttpRequestMessage CreateSignedRequest(
        HttpMethod method, string path, string body, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Encoding.UTF8.GetBytes($"{method.Method}\n{path}\n{ts}\n");
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var signed = new byte[header.Length + bodyBytes.Length];
        Buffer.BlockCopy(header, 0, signed, 0, header.Length);
        Buffer.BlockCopy(bodyBytes, 0, signed, header.Length, bodyBytes.Length);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), signed));
        return new HttpRequestMessage(method, path)
        {
            Headers =
            {
                { "X-Timestamp", ts.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "X-TickerQ-Signature", signature }
            },
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}