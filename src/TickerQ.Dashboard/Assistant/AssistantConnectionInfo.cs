using System;
using System.Net;
using Microsoft.Extensions.AI;

namespace TickerQ.Dashboard.Assistant;

/// <summary>
/// What the dashboard shows about the configured chat client: the provider, the endpoint host,
/// and whether that endpoint runs on this machine. Only the scheme, host, and port are exposed;
/// paths, query strings, and credentials never leave the server.
/// </summary>
internal sealed record AssistantConnectionInfo(string Provider, string Endpoint, string Location, string Model)
{
    internal const string Local = "local";
    internal const string Remote = "remote";
    internal const string Unknown = "unknown";

    internal static AssistantConnectionInfo Describe(IChatClient client, string configuredModel)
    {
        ChatClientMetadata metadata = null;
        try
        {
            metadata = client?.GetService<ChatClientMetadata>();
        }
        catch (Exception)
        {
            // Metadata is optional; a provider that fails to describe itself still works.
        }

        var model = string.IsNullOrWhiteSpace(configuredModel) ? metadata?.DefaultModelId ?? "" : configuredModel;
        var uri = metadata?.ProviderUri;
        var location = uri == null || !uri.IsAbsoluteUri ? Unknown : IsLocalHost(uri.Host) ? Local : Remote;
        var endpoint = uri == null || !uri.IsAbsoluteUri ? "" : uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return new AssistantConnectionInfo(metadata?.ProviderName ?? "", endpoint, location, model);
    }

    internal static bool IsLocalHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var trimmed = host.Trim('[', ']');
        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(trimmed, out var address) && IPAddress.IsLoopback(address);
    }
}
