using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class CronTickerConfigurations<TCronTicker> : IEntityTypeConfiguration<TCronTicker>
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly string _schema;

        public CronTickerConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<TCronTicker> builder)
        {
            builder.Property(x => x.Chain)
                .HasConversion(x => TickerQ.Utilities.Infrastructure.CronChainMaterializer.Serialize(x),
                    x => TickerQ.Utilities.Infrastructure.CronChainMaterializer.Deserialize(x))
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<TickerQ.Utilities.Models.CronChainTemplate>(
                    (a, b) => TickerQ.Utilities.Infrastructure.CronChainMaterializer.Serialize(a) == TickerQ.Utilities.Infrastructure.CronChainMaterializer.Serialize(b),
                    a => a == null ? 0 : TickerQ.Utilities.Infrastructure.CronChainMaterializer.Serialize(a).GetHashCode(),
                    a => TickerQ.Utilities.Infrastructure.CronChainMaterializer.Deserialize(TickerQ.Utilities.Infrastructure.CronChainMaterializer.Serialize(a))));
            builder.HasKey("ApplicationNamespaceKey", "Id");
            builder.Property(e => e.ApplicationNamespaceKey).IsRequired().HasMaxLength(80);
                
            builder.Property(e => e.Id)
                .ValueGeneratedNever();

            builder.HasIndex("ApplicationNamespaceKey", "Expression")
                .HasDatabaseName("IX_CronTickers_Expression");
            builder.Property(e => e.TimeZoneId).HasMaxLength(128).IsRequired(false);
            builder.Property(e => e.SchedulePolicyName).HasMaxLength(200).IsRequired(false);
            builder.Property(e => e.MissedExecutionAction)
                .IsRequired().HasDefaultValue(TickerQ.Utilities.Enums.MissedExecutionAction.RunOnce);
            builder.Property(e => e.MaximumLateness)
                .HasConversion(
                    value => value.HasValue ? value.Value.Ticks : (long?)null,
                    value => value.HasValue ? TimeSpan.FromTicks(value.Value) : (TimeSpan?)null)
                .IsRequired(false);
            builder.Property(e => e.CatchUpMode)
                .IsRequired().HasDefaultValue(TickerQ.Utilities.Enums.CronCatchUpMode.Skip);
            builder.HasIndex("ApplicationNamespaceKey", "SchedulePolicyName")
                .HasDatabaseName("IX_CronTickers_SchedulePolicy");

            // Index for common lookups by function + expression
            builder.HasIndex("ApplicationNamespaceKey", "Function", "Expression")
                .HasDatabaseName("IX_Function_Expression");

            builder.Property(e => e.IsEnabled)
                .IsRequired();

            builder.Property(e => e.RequestContractVersion)
                .IsRequired(false);

            builder.Property(e => e.RequestContractFingerprint)
                .HasMaxLength(128)
                .IsRequired(false);

            // Auto-pause flag set by the worker-stream connect/disconnect
            // monitor. Defaults false so existing rows from older databases
            // act as "not paused" without needing data backfill.
            builder.Property(e => e.IsSystemPaused)
                .IsRequired()
                .HasDefaultValue(false);

            // Stable code-ownership key for seeded crons (Slice 2). Nullable so user/dashboard rows and
            // legacy rows written before this release stay valid; a legacy seeded row adopts its SeedKey
            // in place at reconcile time without a data backfill migration.
            builder.Property(e => e.SeedKey)
                .HasMaxLength(256)
                .IsRequired(false);

            builder.Property(e => e.SeedOwnerNamespace)
                .HasMaxLength(128)
                .IsRequired(false)
                .IsConcurrencyToken();
            builder.Property(e => e.SeedManifestEpoch).IsRequired(false);
            builder.Property(e => e.DefinitionRevision)
                .IsRequired()
                .HasDefaultValue(0L)
                .IsConcurrencyToken();

            builder.Property(e => e.SeedLastSeenAt).IsRequired(false);
            builder.Property(e => e.RetirementRequestedAt).IsRequired(false);
            builder.Property(e => e.RetiredAt).IsRequired(false);

            // Preserves the seed's enabled/disabled state at the moment framework retirement disabled it,
            // so a later reappearance restores only a framework-disabled seed and never re-enables one a
            // user had deliberately disabled (Slice 3). Additive/nullable — null when no framework
            // retirement is in effect.
            builder.Property(e => e.SeedWasEnabledBeforeRetirement).IsRequired(false);

            // Enforce exactly one active code-owned row per seed key via a UNIQUE index on the nullable
            // SeedKey. Crucially we do NOT pin a hardcoded partial-index predicate here: the old
            // `"SeedKey" IS NOT NULL` filter used double-quote identifier quoting that is invalid on SQL
            // Server, so it was not actually provider-portable. Instead we rely on each EF provider's own
            // nullable-unique-index convention — the SqlServer provider auto-adds a
            // `WHERE [SeedKey] IS NOT NULL` filter for a unique index over a nullable column, while
            // SQLite and PostgreSQL treat NULLs as distinct natively and allow any number of null-SeedKey
            // rows. That distinctness is what makes legacy adoption safe: pre-existing duplicate seeded
            // rows keep a null SeedKey (only the canonical row adopts one), so the index is never violated
            // on startup.
            builder.HasIndex(e => new { e.ApplicationNamespaceKey, e.SeedKey })
                .IsUnique()
                .HasDatabaseName("UX_CronTickers_SeedKey");

            builder.HasIndex(e => new { e.ApplicationNamespaceKey, e.SeedOwnerNamespace, e.SeedManifestEpoch })
                .HasDatabaseName("IX_CronTickers_SeedOwner_Epoch");

            builder.ToTable("CronTickers", _schema);
        }
    }
}
