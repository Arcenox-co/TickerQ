using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations;

/// <summary>
/// Provider-specific adjustments to TickerQ's EF model. TickerQ applies them automatically through its
/// model customizer and <c>TickerQDbContext</c>. Call <see cref="Apply"/> yourself after the TickerQ
/// entity configurations only when you map TickerQ manually with <c>IgnoreModelCustomizer</c>.
/// </summary>
public static class TickerProviderModelConventions
{
    internal const string SeedKeyIndexName = "UX_CronTickers_SeedKey";
    internal const string SeedKeyScopeProperty = "SeedKeyScope";

    /// <param name="modelBuilder">The builder that already has TickerQ's entity configurations applied.</param>
    /// <param name="providerName">The EF provider name, usually <c>context.Database.ProviderName</c>.</param>
    public static void Apply(ModelBuilder modelBuilder, string providerName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (providerName?.Contains("Oracle", StringComparison.OrdinalIgnoreCase) == true)
        {
            ApplyOracleBooleanColumns(modelBuilder);
            ApplyOracleSeedKeyIndex(modelBuilder);
        }
        Compat.PrimitiveCollectionCompat.Apply(modelBuilder, providerName, IsTickerQEntity);
        if (providerName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true ||
            providerName?.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) == true)
            ApplyMySqlIndexKeyLimits(modelBuilder);
    }

    // InnoDB limits an index key to 3072 bytes and utf8mb4 reserves 4 bytes per character, so
    // (ApplicationNamespace 256, FunctionName 512, OccurredAt) cannot be created on MySQL/MariaDB.
    // Investigations are capped per namespace by retention, so a (namespace, OccurredAt) index
    // followed by a function filter serves the same queries without shortening function names.
    private static void ApplyMySqlIndexKeyLimits(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Model.FindEntityType(typeof(Entities.AssistantInvestigationEntity));
        var index = entity?.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "IX_AssistantInvestigation_Namespace_Function_OccurredAt");
        if (entity == null || index == null)
            return;
        entity.RemoveIndex(index);
        modelBuilder.Entity<Entities.AssistantInvestigationEntity>()
            .HasIndex(x => new { x.ApplicationNamespace, x.OccurredAt })
            .HasDatabaseName("IX_AssistantInvestigation_Namespace_OccurredAt");
    }

    // The Oracle provider maps bool to the BOOLEAN type, which only exists from Oracle 23ai; on 19c and
    // 21c the migration fails with ORA-00904. For a NUMBER(1) column the provider uses its pre-23ai bool
    // mapping (0/1 literals and parameters) whatever its SQL compatibility setting is, so TickerQ's bool
    // columns use NUMBER(1) and work on every version. The host application's own entities keep the
    // provider's mapping.
    private static void ApplyOracleBooleanColumns(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => IsTickerQEntity(e.ClrType)))
        foreach (var property in entity.GetDeclaredProperties()
                     .Where(p => p.ClrType == typeof(bool) || p.ClrType == typeof(bool?)))
            property.SetColumnType("NUMBER(1)");
    }

    private static bool IsTickerQEntity(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Assembly == typeof(CronTickerEntity).Assembly ||
                current.Assembly == typeof(TickerProviderModelConventions).Assembly)
                return true;
        }
        return false;
    }

    // A unique index over (ApplicationNamespaceKey, SeedKey) must allow any number of unseeded crons
    // (null SeedKey) in one namespace. Other providers treat NULL keys as distinct or filter them out,
    // but Oracle indexes a row unless every key column is NULL, so (namespace, NULL) rows would collide.
    // A virtual column that is NULL whenever SeedKey is NULL keeps those rows out of the index while
    // still enforcing one seeded row per namespace and SeedKey.
    private static void ApplyOracleSeedKeyIndex(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(CronTickerEntity).IsAssignableFrom(e.ClrType) && e.BaseType == null)
                     .ToArray())
        {
            var index = entity.GetIndexes().FirstOrDefault(i => i.GetDatabaseName() == SeedKeyIndexName);
            var seedKey = entity.FindProperty(nameof(CronTickerEntity.SeedKey));
            var applicationNamespace = entity.FindProperty(nameof(CronTickerEntity.ApplicationNamespaceKey));
            if (index == null || seedKey == null || applicationNamespace == null)
                continue;

            entity.RemoveIndex(index);
            var builder = modelBuilder.Entity(entity.ClrType);
            builder.Property<string>(SeedKeyScopeProperty)
                .HasMaxLength(applicationNamespace.GetMaxLength() ?? 80)
                .HasComputedColumnSql(
                    $"CASE WHEN \"{seedKey.GetColumnName()}\" IS NOT NULL THEN \"{applicationNamespace.GetColumnName()}\" END",
                    stored: false);
            builder.HasIndex(SeedKeyScopeProperty, nameof(CronTickerEntity.SeedKey))
                .IsUnique()
                .HasDatabaseName(SeedKeyIndexName);
        }
    }
}
