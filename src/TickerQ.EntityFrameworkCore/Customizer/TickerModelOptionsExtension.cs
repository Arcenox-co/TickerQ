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
internal sealed class TickerModelOptionsExtension(string schema) : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
    public void ApplyServices(IServiceCollection services) { }
    public void Validate(IDbContextOptions options) { }

    internal static void Configure<TTimeTicker, TCronTicker>(DbContextOptionsBuilder options,
        TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> ticker)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new() where TCronTicker : CronTickerEntity, new()
        => ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
            new TickerModelOptionsExtension(ticker?.Schema ?? Constants.DefaultSchema));

    private sealed class ExtensionInfo(TickerModelOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "TickerQModel ";
        public override int GetServiceProviderHashCode() => extension.Schema.GetHashCode(StringComparison.Ordinal);
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo info && extension.Schema == info.ExtensionValue.Schema;
        private TickerModelOptionsExtension ExtensionValue => extension;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["TickerQ:Schema"] = extension.Schema;
        }
    }

    private string Schema => schema;
}
