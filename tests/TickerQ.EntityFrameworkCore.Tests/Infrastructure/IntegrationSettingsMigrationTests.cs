using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public class IntegrationSettingsMigrationTests
{
    [Fact]
    public async Task RenameMigration_PreservesRowsVersionsAndEncryptedPayloads_AndIsReversible()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SettingsContext(new DbContextOptionsBuilder<SettingsContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "NotificationSettings" (
              "ApplicationNamespaceKey" TEXT NOT NULL CONSTRAINT "PK_NotificationSettings" PRIMARY KEY,
              "Version" TEXT NOT NULL,
              "ProtectedConfiguration" TEXT NOT NULL);
            """);
        var version = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO NotificationSettings VALUES ({"partition-a"}, {version}, {"encrypted-a"})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO NotificationSettings VALUES ({"partition-b"}, {version}, {"encrypted-b"})");
        var migration = new TickerQ.Sample.Console.Migrations.RenameIntegrationSettings();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        var model = db.GetService<IDesignTimeModel>().Model;

        foreach (var operation in generator.Generate(migration.UpOperations, model))
            await db.Database.ExecuteSqlRawAsync(operation.CommandText);

        var rows = await db.Set<IntegrationSettingsEntity>().AsNoTracking().OrderBy(x => x.ApplicationNamespaceKey).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Equal(version, row.Version));
        Assert.Equal(new[] { "encrypted-a", "encrypted-b" }, rows.Select(x => x.ProtectedConfiguration));

        // Use the legacy target shape for the SQLite table rebuild during Down.
        var legacyModelBuilder = new ModelBuilder(db.GetService<Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure.IConventionSetBuilder>().CreateConventionSet());
        legacyModelBuilder.ApplyConfiguration(new IntegrationSettingsConfigurations("ticker"));
        legacyModelBuilder.Entity<IntegrationSettingsEntity>().ToTable("NotificationSettings", "ticker");
        var legacyModel = db.GetService<IModelRuntimeInitializer>().Initialize(legacyModelBuilder.FinalizeModel(), designTime: true);
        foreach (var operation in generator.Generate(migration.DownOperations, legacyModel))
            await db.Database.ExecuteSqlRawAsync(operation.CommandText);
        var count = await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM NotificationSettings").SingleAsync();
        Assert.Equal(2, count);
    }

    private sealed class SettingsContext(DbContextOptions<SettingsContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyConfiguration(new IntegrationSettingsConfigurations("ticker"));
    }
}
