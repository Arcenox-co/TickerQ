using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Customizer;

// Give each TickerQ model configuration its own EF service-provider/model cache.
// The application's IModelCacheKeyFactory is left intact, including tenant keys
// and any dependencies supplied by the application or database provider.
internal sealed class TickerModelOptionsExtension(string schema, bool logs, bool assistant) : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
    public void ApplyServices(IServiceCollection services) { }
    public void Validate(IDbContextOptions options) { }

    internal static void Configure<TTimeTicker, TCronTicker>(DbContextOptionsBuilder options,
        TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> ticker)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new() where TCronTicker : CronTickerEntity, new()
        => ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
            new TickerModelOptionsExtension(ticker?.Schema ?? Constants.DefaultSchema,
                ticker?.ExecutionLogs?.PersistToDatabase == true,
                (ticker?.AssistantHistory ?? AssistantHistoryOptions.Current) != null));

    private sealed class ExtensionInfo(TickerModelOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "TickerQModel ";
        public override int GetServiceProviderHashCode() => HashCode.Combine(extension.Schema, extension.Logs, extension.Assistant);
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo info && extension.Schema == info.ExtensionValue.Schema
                && extension.Logs == info.ExtensionValue.Logs && extension.Assistant == info.ExtensionValue.Assistant;
        private TickerModelOptionsExtension ExtensionValue => extension;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["TickerQ:Schema"] = extension.Schema;
            debugInfo["TickerQ:ExecutionLogs"] = extension.Logs.ToString();
            debugInfo["TickerQ:AssistantHistory"] = extension.Assistant.ToString();
        }
    }

    private string Schema => schema;
    private bool Logs => logs;
    private bool Assistant => assistant;
}
