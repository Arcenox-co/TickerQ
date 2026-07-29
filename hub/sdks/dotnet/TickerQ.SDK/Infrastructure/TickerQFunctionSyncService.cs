using Grpc.Core;
using Grpc.Net.Client;
using TickerQ.SDK.Hub;
using TickerQ.Utilities;
using TickerQ.Utilities.Models;

namespace TickerQ.SDK.Infrastructure;

/// <summary>
/// Boot-time function-sync. Calls Hub's <c>HubService.SyncNodesFunctions</c> over gRPC
/// to register this SDK's node + function manifest with the env identified by the
/// API token, then captures the env's ApplicationUrl + WebhookSignature into options
/// so the worker stream knows where to dial the Scheduler.
/// </summary>
internal sealed class TickerQFunctionSyncService
{
    private readonly TickerSdkOptions _options;
    private GrpcChannel? _channel;

    public TickerQFunctionSyncService(TickerSdkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<SyncNodesFunctionsResponse?> SyncAsync(CancellationToken cancellationToken)
    {
        if (TickerFunctionProvider.TickerFunctions == null ||
            TickerFunctionProvider.TickerFunctions.Count == 0)
        {
            return null;
        }

        // Build (and validate) the entire manifest BEFORE opening the channel so an incomplete typed
        // contract fails fast, locally, without a half-formed request ever reaching the Hub.
        var request = BuildSyncRequest(
            _options.NodeName,
            TickerSdkOptions.SdkType,
            TickerFunctionProvider.TickerFunctionDescriptors.Values);

        var client = new HubService.HubServiceClient(GetChannel());
        var headers = new Metadata { { "x-api-key", _options.ApiKey ?? string.Empty } };

        var response = await client
            .SyncNodesFunctionsAsync(request, headers, cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        if (response != null)
        {
            if (!string.IsNullOrWhiteSpace(response.ApplicationUrl))
            {
                _options.ApiUri = new Uri(response.ApplicationUrl.TrimEnd('/') + "/");
            }

            if (!string.IsNullOrWhiteSpace(response.WebhookSignature))
            {
                _options.WebhookSignature = response.WebhookSignature;
            }
        }

        return response;
    }

    /// <summary>
    /// Maps the canonical descriptors to the wire request, validating each typed contract's
    /// completeness. Extracted for unit testing and so validation happens before any RPC.
    /// </summary>
    internal static SyncNodesFunctionsRequest BuildSyncRequest(
        string nodeName,
        string sdkType,
        IEnumerable<TickerFunctionDescriptor> descriptors)
    {
        var request = new SyncNodesFunctionsRequest
        {
            NodeName = nodeName ?? string.Empty,
            // Pure-client SDK — no callback URL. Field kept on the wire for compat
            // with existing Hub deployments; will be ignored server-side.
            CallbackUrl = string.Empty,
            SdkType = sdkType
        };

        foreach (var descriptor in descriptors)
            request.Functions.Add(BuildFunctionDescriptor(descriptor));

        return request;
    }

    /// <summary>
    /// Maps one canonical descriptor to its wire form. Request-less functions omit the
    /// <see cref="RequestContract"/> entirely; a request-bearing function must carry a complete
    /// schema, dialect, and fingerprint or this throws before the manifest is sent.
    /// </summary>
    internal static SyncFunctionDescriptor BuildFunctionDescriptor(TickerFunctionDescriptor descriptor)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

        var canonicalRequest = descriptor.Request;
        var legacyExample = canonicalRequest?.Examples.FirstOrDefault()?.Value.GetRawText() ?? string.Empty;
        var function = new SyncFunctionDescriptor
        {
            FunctionName = descriptor.FunctionName,
            ContractVersion = descriptor.ContractVersion,
            RequestType = canonicalRequest?.TypeName ?? string.Empty,
            RequestExampleJson = legacyExample,
            TaskPriority = (HubTaskPriority)(int)descriptor.Priority,
            Expression = descriptor.CronExpression ?? string.Empty
        };

        // Request-less: leave RequestContract unset (absence is never encoded as empty metadata).
        if (canonicalRequest == null)
            return function;

        // A request-bearing contract MUST carry a complete schema, dialect, and fingerprint. Refuse to
        // sync a half-formed typed contract — the Hub would otherwise either reject it or, worse, accept
        // it as schemaless and silently drop server-side payload validation for this function.
        if (!canonicalRequest.Schema.HasValue
            || string.IsNullOrEmpty(canonicalRequest.Fingerprint)
            || string.IsNullOrWhiteSpace(canonicalRequest.SchemaDialect))
        {
            throw new InvalidOperationException(
                $"Function '{descriptor.FunctionName}' declares a request contract without a complete " +
                "schema, dialect, and fingerprint. Refusing to sync an incomplete typed contract to the Hub.");
        }

        function.RequestContract = new RequestContract
        {
            TypeName = canonicalRequest.TypeName,
            MediaType = canonicalRequest.MediaType,
            Required = canonicalRequest.Required,
            SchemaDialect = canonicalRequest.SchemaDialect,
            SchemaJson = canonicalRequest.Schema.Value.GetRawText(),
            Fingerprint = canonicalRequest.Fingerprint
        };
        function.RequestContract.Examples.Add(canonicalRequest.Examples.Select(example =>
        {
            var mapped = new RequestExample
            {
                Key = example.Key,
                ValueJson = example.Value.GetRawText()
            };
            if (example.Summary != null) mapped.Summary = example.Summary;
            return mapped;
        }));

        return function;
    }

    private GrpcChannel GetChannel()
    {
        if (_channel != null) return _channel;
        _channel = GrpcChannel.ForAddress(_options.HubControlUri, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 16 * 1024 * 1024
        });
        return _channel;
    }
}
