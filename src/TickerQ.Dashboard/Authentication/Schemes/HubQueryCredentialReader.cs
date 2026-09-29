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

        if (!context.WebSockets.IsWebSocketRequest ||
            !context.Request.Path.Equals(
                ServiceCollectionExtensions.NotificationHubPath,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var queryToken = context.Request.Query["access_token"].FirstOrDefault();
        return string.IsNullOrEmpty(queryToken) ? null : queryToken;
    }
}
