using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.BackgroundServices;

namespace TickerQ
{
    /// <summary>
    /// Detects build-time tools that run the application's entry point only to inspect it. The .NET 10
    /// <c>dotnet-getdocument</c> tool used by Microsoft.Extensions.ApiDescription.Server also starts the
    /// host, which would run TickerQ's license check, store bootstrap, and reconciliation during
    /// <c>dotnet build</c>, and fail the build when the store is unreachable or not migrated.
    /// </summary>
    internal static class DesignTimeHost
    {
        internal const string GetDocumentInsider = "GetDocument.Insider";

        /// <summary>The entry assembly name of the design-time tool hosting this process, or null.</summary>
        internal static string ToolName { get; } = Detect(Assembly.GetEntryAssembly()?.GetName().Name);

        internal static string Detect(string entryAssemblyName)
            => entryAssemblyName == GetDocumentInsider ? entryAssemblyName : null;

        /// <summary>Hosted services registered before AddTickerQ runs, or null when no tool is detected.</summary>
        internal static HashSet<ServiceDescriptor> SnapshotHostedServices(IServiceCollection services, string toolName)
        {
            if (toolName == null)
                return null;

            var snapshot = new HashSet<ServiceDescriptor>(ReferenceEqualityComparer.Instance);
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType == typeof(IHostedService))
                    snapshot.Add(descriptor);
            }
            return snapshot;
        }

        /// <summary>
        /// Removes every hosted service that AddTickerQ registered, including those added by provider and
        /// dashboard callbacks or inserted at the front of the collection, and adds one that logs why the
        /// scheduler did not start. Hosted services registered before AddTickerQ stay untouched.
        /// </summary>
        internal static void SuppressHostedServices(IServiceCollection services,
            HashSet<ServiceDescriptor> hostedServicesBefore, string toolName)
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                var descriptor = services[i];
                if (descriptor.ServiceType == typeof(IHostedService) && !hostedServicesBefore.Contains(descriptor))
                    services.RemoveAt(i);
            }

            services.AddSingleton<IHostedService>(sp => new TickerQDesignTimeNoticeService(
                toolName, sp.GetRequiredService<ILogger<TickerQDesignTimeNoticeService>>()));
        }
    }
}
