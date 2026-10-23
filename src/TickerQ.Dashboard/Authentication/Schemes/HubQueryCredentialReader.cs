using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using TickerQ.Dashboard.DependencyInjection;

namespace TickerQ.Dashboard.Authentication.Schemes;

internal static class HubQueryCredentialReader
{
    internal static string? ReadAuthorizationOrHubQuery(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(header))
            return header;

        // Browser WebSocket and Server-Sent Events APIs cannot attach an
        // Authorization header, so SignalR uses this query parameter for both
        // transports. Keep the fallback constrained to the exact hub route so
        // query-string credentials are never accepted by dashboard APIs.
        if (!context.Request.Path.Equals(
                ServiceCollectionExtensions.NotificationHubPath,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var queryToken = context.Request.Query["access_token"].FirstOrDefault();
        return string.IsNullOrEmpty(queryToken) ? null : queryToken;
    }
}
