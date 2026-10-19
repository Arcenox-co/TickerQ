using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Caching.StackExchangeRedis;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

[JsonSerializable(typeof(TimeTickerEntity))]
internal sealed partial class ContractIdentityTestJsonContext : JsonSerializerContext;

public sealed class TrimSafeSerializationTests
{
    [Fact]
    public void DashboardContext_HasTypeInfo_For_String()
    {
        Assert.NotNull(DashboardJsonSerializerContext.Default.String);
    }

    [Fact]
    public void DashboardContext_HasTypeInfo_For_NullableDateTime()
    {
        Assert.NotNull(DashboardJsonSerializerContext.Default.NullableDateTime);
    }

    [Fact]
    public void DashboardContext_HasTypeInfo_For_Boolean()
    {
        Assert.NotNull(DashboardJsonSerializerContext.Default.Boolean);
    }

    [Fact]
    public void DashboardContext_HasTypeInfo_For_CronTickerEntity()
    {
        Assert.NotNull(DashboardJsonSerializerContext.Default.CronTickerEntity);
    }

    [Fact]
    public void DashboardContext_HasTypeInfo_For_TimeTickerEntity()
    {
        Assert.NotNull(DashboardJsonSerializerContext.Default.TimeTickerEntity);
    }

    [Fact]
    public void DashboardContext_HasTypeInfo_For_CronTickerOccurrenceEntityCronTickerEntity()
    {
        // Property name for CronTickerOccurrenceEntity<CronTickerEntity> as emitted by the source generator.
        Assert.NotNull(DashboardJsonSerializerContext.Default.CronTickerOccurrenceEntityCronTickerEntity);
    }

    [Fact]
    public void DashboardContext_HasTypeInfo_For_Guid()
    {
        // Cron occurrence updates are now id-only: clients receive just the
        // occurrence Guid and refetch the authoritative shape over REST. The id
        // payload must stay trim-safe serializable via the source-gen context.
        Assert.NotNull(DashboardJsonSerializerContext.Default.Guid);
    }

    [Fact]
    public void SerializeToElement_String_ProducesCorrectJson()
    {
        var element = JsonSerializer.SerializeToElement("hello", DashboardJsonSerializerContext.Default.String);

        Assert.Equal(JsonValueKind.String, element.ValueKind);
        Assert.Equal("hello", element.GetString());
    }

    [Fact]
    public void SerializeToElement_NullableDateTime_WithValue_ProducesCorrectJson()
    {
        DateTime? value = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var element = JsonSerializer.SerializeToElement(value, DashboardJsonSerializerContext.Default.NullableDateTime);

        Assert.Equal(JsonValueKind.String, element.ValueKind);
        Assert.False(string.IsNullOrEmpty(element.GetString()));
    }

    [Fact]
    public void SerializeToElement_NullableDateTime_Null_ProducesJsonNull()
    {
        DateTime? value = null;

        var element = JsonSerializer.SerializeToElement(value, DashboardJsonSerializerContext.Default.NullableDateTime);

        Assert.Equal(JsonValueKind.Null, element.ValueKind);
    }

    [Fact]
    public void SerializeToElement_CronTickerEntity_ProducesExpectedProperties()
    {
        var id = Guid.NewGuid();
        var entity = new CronTickerEntity
        {
            Id = id,
            Expression = "0 * * * *",
            IsEnabled = true,
            Retries = 3
        };

        var element = JsonSerializer.SerializeToElement(entity, DashboardJsonSerializerContext.Default.CronTickerEntity);

        Assert.Equal(JsonValueKind.Object, element.ValueKind);

        Assert.True(element.TryGetProperty("id", out var idProp));
        Assert.Equal(id.ToString(), idProp.GetString());

        Assert.True(element.TryGetProperty("expression", out var exprProp));
        Assert.Equal("0 * * * *", exprProp.GetString());

        Assert.True(element.TryGetProperty("isEnabled", out var enabledProp));
        Assert.True(enabledProp.GetBoolean());

        Assert.True(element.TryGetProperty("retries", out var retriesProp));
        Assert.Equal(3, retriesProp.GetInt32());
    }

    [Fact]
    public void SerializeToElement_CronOccurrenceId_ProducesGuidString()
    {
        // The full-entity CronOccurrenceUpdateNotification DTO was removed in favor
        // of the ID-only invalidation contract: only the occurrence id is broadcast.
        var id = Guid.NewGuid();

        var element = JsonSerializer.SerializeToElement(id, DashboardJsonSerializerContext.Default.Guid);

        Assert.Equal(JsonValueKind.String, element.ValueKind);
        Assert.Equal(id.ToString(), element.GetString());
    }

    [Fact]
    public void UpdateCronOccurrenceAsync_IsIdOnlyContract()
    {
        // Preserved ID-only contract: cron occurrence updates carry only the
        // group id and occurrence id (both Guid) — never a full entity payload.
        var method = typeof(ITickerQNotificationHubSender).GetMethod("UpdateCronOccurrenceAsync");

        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.All(parameters, p => Assert.Equal(typeof(Guid), p.ParameterType));
    }

    [Fact]
    public void AddCronOccurrenceAsync_IsIdOnlyContract()
    {
        // Adds are id-only too: (groupId, occurrenceId) Guids, no entity bytes.
        var method = typeof(ITickerQNotificationHubSender).GetMethod("AddCronOccurrenceAsync");

        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.All(parameters, p => Assert.Equal(typeof(Guid), p.ParameterType));
    }

    [Fact]
    public void UpdateNodeHeartBeatAsync_AcceptsJsonElement()
    {
        var method = typeof(ITickerQNotificationHubSender).GetMethod("UpdateNodeHeartBeatAsync");

        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(JsonElement), parameters[0].ParameterType);
    }

    [Fact]
    public void UpdateHostStatus_AcceptsBool()
    {
        var method = typeof(ITickerQNotificationHubSender).GetMethod("UpdateHostStatus");

        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(bool), parameters[0].ParameterType);
    }

    [Fact]
    public void RedisContext_HasTypeInfo_For_ICollectionTimeTickerEntity()
    {
        var typeInfo = RedisContextJsonSerializerContext.Default.Options.GetTypeInfo(typeof(ICollection<TimeTickerEntity>));
        Assert.NotNull(typeInfo);
    }

    [Fact]
    public void TimeTickerEntity_WithChildren_RoundTripsViaRedisContext()
    {
        // Verifies that a TimeTickerEntity with a populated Children collection survives a full
        // serialize→deserialize round-trip using the RedisContextJsonSerializerContext.
        var parent = new TimeTickerEntity { Id = Guid.NewGuid() };
        var child = new TimeTickerEntity { Id = Guid.NewGuid() };
        parent.Children = new List<TimeTickerEntity> { child };

        var json = JsonSerializer.Serialize(parent, RedisContextJsonSerializerContext.Default.TimeTickerEntity);
        var deserialized = JsonSerializer.Deserialize(json, RedisContextJsonSerializerContext.Default.TimeTickerEntity);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Children);
        Assert.Equal(child.Id, deserialized.Children.First().Id);
    }

    [Fact]
    public void TimeTickerEntity_RequestContractIdentity_RoundTripsViaSourceGeneratedContext()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            RequestContractVersion = 7,
            RequestContractFingerprint = "sha256:test-fingerprint"
        };

        var json = JsonSerializer.Serialize(ticker, ContractIdentityTestJsonContext.Default.TimeTickerEntity);
        Assert.Contains("\"RequestContractVersion\":7", json, StringComparison.Ordinal);
        Assert.Contains("\"RequestContractFingerprint\":\"sha256:test-fingerprint\"", json, StringComparison.Ordinal);

        var deserialized = JsonSerializer.Deserialize(json, ContractIdentityTestJsonContext.Default.TimeTickerEntity);

        Assert.NotNull(deserialized);
        Assert.Equal(7, deserialized.RequestContractVersion);
        Assert.Equal("sha256:test-fingerprint", deserialized.RequestContractFingerprint);
    }
}
