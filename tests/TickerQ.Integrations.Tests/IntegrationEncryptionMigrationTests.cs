using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Tests;

public class IntegrationEncryptionMigrationTests
{
    [Fact]
    public async Task MigrationConflict_ReloadsWinningEdit_WithoutOverwritingIt()
    {
        var protection = new EphemeralDataProtectionProvider();
        var old = new IntegrationSettingsRecord(Guid.NewGuid(), protection
            .CreateProtector("TickerQ.Notifications.Configuration.v1").Protect("{\"destinations\":[],\"rules\":[]}"));
        var winner = new IntegrationSettingsRecord(Guid.NewGuid(), protection
            .CreateProtector("TickerQ.Integrations.Configuration.v1").Protect("{\"destinations\":[{\"name\":\"Winning edit\"}],\"rules\":[]}"));
        var store = new ConflictingStore(old, winner);
        var service = new IntegrationConfigurationService(store, protection, new(), [new SlackFormatter()]);

        var loaded = await service.LoadAsync(default);

        Assert.Equal(winner.Version, loaded.Version);
        Assert.Equal("Winning edit", Assert.Single(loaded.Destinations).Name);
        Assert.Equal(winner, store.Record);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task UnreadablePayload_DoesNotResetOrOverwriteSavedSettings()
    {
        var original = new IntegrationSettingsRecord(Guid.NewGuid(), "unreadable-payload");
        var store = new ConflictingStore(original, original);
        var service = new IntegrationConfigurationService(store, new EphemeralDataProtectionProvider(), new(), [new SlackFormatter()]);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => service.LoadAsync(default));

        Assert.Equal(original, store.Record);
        Assert.Equal(0, store.Writes);
    }

    private sealed class ConflictingStore(IntegrationSettingsRecord record, IntegrationSettingsRecord winner) : ITickerIntegrationSettingsStore
    {
        public IntegrationSettingsRecord Record = record;
        public int Writes;
        public Task<IntegrationSettingsRecord?> ReadIntegrationSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IntegrationSettingsRecord?>(Record);
        public Task<bool> SaveIntegrationSettingsAsync(IntegrationSettingsRecord candidate, Guid expectedVersion, CancellationToken cancellationToken = default)
        {
            Assert.Equal(record.Version, expectedVersion);
            Writes++;
            Record = winner;
            return Task.FromResult(false);
        }
    }
}
