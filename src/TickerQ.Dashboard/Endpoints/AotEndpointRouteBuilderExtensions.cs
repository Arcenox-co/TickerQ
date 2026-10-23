using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace TickerQ.Dashboard.Endpoints;

/// <summary>
/// Maps dashboard handlers through the <see cref="RequestDelegate"/> overloads so ASP.NET Core
/// does not need runtime parameter discovery. Dashboard handlers read services, route values and
/// request bodies from <see cref="HttpContext"/>, which keeps the endpoint surface Native AOT safe.
/// </summary>
internal static class AotEndpointRouteBuilderExtensions
{
    internal static IEndpointConventionBuilder MapGetAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        RequestDelegate handler)
        => MapAot(endpoints, pattern, HttpMethods.Get, handler);

    internal static IEndpointConventionBuilder MapPostAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        RequestDelegate handler)
        => MapAot(endpoints, pattern, HttpMethods.Post, handler);

    internal static IEndpointConventionBuilder MapPutAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        RequestDelegate handler)
        => MapAot(endpoints, pattern, HttpMethods.Put, handler);

    internal static IEndpointConventionBuilder MapPatchAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        RequestDelegate handler)
        => MapAot(endpoints, pattern, HttpMethods.Patch, handler);

    internal static IEndpointConventionBuilder MapDeleteAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        RequestDelegate handler)
        => MapAot(endpoints, pattern, HttpMethods.Delete, handler);

    internal static IEndpointConventionBuilder MapGetResultAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, Task<IResult>> handler)
        => endpoints.MapGetAot(pattern, context => ExecuteResultAsync(context, handler));

    internal static IEndpointConventionBuilder MapPostResultAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, Task<IResult>> handler)
        => endpoints.MapPostAot(pattern, context => ExecuteResultAsync(context, handler));

    internal static IEndpointConventionBuilder MapPatchResultAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, Task<IResult>> handler)
        => endpoints.MapPatchAot(pattern, context => ExecuteResultAsync(context, handler));

    internal static IEndpointConventionBuilder MapDeleteResultAot(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, Task<IResult>> handler)
        => endpoints.MapDeleteAot(pattern, context => ExecuteResultAsync(context, handler));

    internal static Guid RequiredRouteGuid(this HttpContext context, string name)
    {
        var value = Convert.ToString(context.Request.RouteValues[name], CultureInfo.InvariantCulture);
        if (Guid.TryParse(value, out var parsed))
            return parsed;

        throw new BadHttpRequestException($"Route value '{name}' must be a GUID.");
    }

    internal static string RequiredRouteString(this HttpContext context, string name)
    {
        var value = Convert.ToString(context.Request.RouteValues[name], CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        throw new BadHttpRequestException($"Route value '{name}' is required.");
    }

    private static async Task ExecuteResultAsync(
        HttpContext context,
        Func<HttpContext, Task<IResult>> handler)
    {
        var result = await handler(context).ConfigureAwait(false);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    private static IEndpointConventionBuilder MapAot(
        IEndpointRouteBuilder endpoints,
        string pattern,
        string method,
        RequestDelegate handler)
        => endpoints.Map(pattern, handler)
            .WithMetadata(new HttpMethodMetadata([method]));
}
