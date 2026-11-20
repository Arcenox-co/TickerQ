using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities;
using TickerQ.Utilities.Promises;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Stops the host when a promise cannot work: its function or request type is wrong, a required part is missing, its
/// name is taken, or its rows' service is not registered. Registered after the initializer, which builds the function
/// registry these checks read.
/// </summary>
internal sealed class TickerQPromiseStartupValidator(IEnumerable<TickerPromiseDefinition> definitions, IServiceProvider services)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Validate(definitions, services.GetService<IServiceProviderIsService>());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Throws an <see cref="InvalidOperationException"/> listing every problem found.</summary>
    internal static void Validate(IEnumerable<TickerPromiseDefinition> definitions, IServiceProviderIsService services)
    {
        var promises = definitions.ToList();
        var errors = new List<string>();
        foreach (var promise in promises)
        {
            var name = promise.Name;
            if (promise.Within == null)
                errors.Add($"Promise '{name}' needs .Within(...): how soon after which time each row's job must succeed.");
            if (!promise.HasJob)
                errors.Add($"Promise '{name}' needs .Job(...): the request each row's job runs with, and how jobs are matched to rows.");
            if (promise.ContextType != null && services?.IsService(promise.ContextType) == false)
                errors.Add($"Promise '{name}' reads its rows from {promise.ContextType.Name}, which is not registered as a service.");

            if (!promise.IsFunctionMapped)
                errors.Add($"Promise '{name}': {promise.FunctionType.Name} is not registered as a ticker function. " +
                           $"Register it with MapTicker<{promise.FunctionType.Name}, {promise.RequestType.Name}>().");
            // The registry is built when the host starts with UseTickerQ; without it there is nothing to compare.
            else if (TickerFunctionProvider.IsBuilt)
            {
                if (!TickerFunctionProvider.TickerFunctions.ContainsKey(promise.Function))
                    errors.Add($"Promise '{name}': no ticker function named '{promise.Function}' is registered.");
                else if (!TickerFunctionProvider.RuntimeRequests.TryGetValue(promise.Function, out var request))
                    errors.Add($"Promise '{name}': {promise.Function} takes no request, so it cannot take a {promise.RequestType.Name}.");
                else if (request.RequestType != promise.RequestType)
                    errors.Add($"Promise '{name}': {promise.Function} takes a {request.RequestType.Name}, not a {promise.RequestType.Name}.");
            }
        }

        foreach (var named in promises.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"{named.Count()} promises are named '{named.Key}'. Give each its own name with .Named(...).");
        foreach (var same in promises.GroupBy(p => (p.Function, p.RowType)).Where(g => g.Count() > 1))
            errors.Add($"Promises {string.Join(" and ", same.Select(p => $"'{p.Name}'"))} both watch {same.Key.RowType.Name} rows " +
                       $"for {same.Key.Function}. Combine them into one promise.");

        if (errors.Count > 0)
            throw new InvalidOperationException("TickerQ promises are not set up correctly:" +
                                                string.Concat(errors.Select(e => Environment.NewLine + "- " + e)));
    }
}
