using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Source-generated (no reflection fallback) System.Text.Json context over the wire models.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TickerFunctionDescriptor))]
[JsonSerializable(typeof(TickerRequestContract))]
[JsonSerializable(typeof(TickerRequestExample))]
internal partial class WireContractJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Proves the wire models serialize schema/value as embedded JSON (not escaped strings) under
/// camelCase source-generated serialization, and round-trip losslessly with no reflection fallback.
/// </summary>
public class TickerWireContractSerializationTests
{
    private static TickerFunctionDescriptor SampleDescriptor() =>
        new("Orders.Process",
            priority: TickerTaskPriority.High,
            cronExpression: "0 0 * * *",
            request: new TickerRequestContract("Orders.Req",
                schemaJson: "{\"type\":\"object\"}",
                examples: new[] { new TickerRequestExample("default", "Example", "{\"id\":1}") }));

    [Fact]
    public void Descriptor_SerializesSchemaAndValueAsEmbeddedJson_CamelCase()
    {
        // Serialize THROUGH the generated JsonTypeInfo — this path uses source generation only.
        var json = JsonSerializer.Serialize(SampleDescriptor(), WireContractJsonContext.Default.TickerFunctionDescriptor);

        // camelCase wire naming.
        Assert.Contains("\"functionName\":\"Orders.Process\"", json);
        Assert.Contains("\"contractVersion\":1", json);
        Assert.Contains("\"cronExpression\":\"0 0 * * *\"", json);

        // Schema and Value are EMBEDDED JSON, not escaped strings.
        Assert.Contains("\"schema\":{\"type\":\"object\"}", json);
        Assert.Contains("\"value\":{\"id\":1}", json);
        Assert.DoesNotContain("\"schema\":\"{", json);
        Assert.DoesNotContain("\"value\":\"{", json);
    }

    [Fact]
    public void Descriptor_SerializesDerivedFingerprint_ThroughSourceGeneratedContext()
    {
        var descriptor = SampleDescriptor();
        var json = JsonSerializer.Serialize(descriptor, WireContractJsonContext.Default.TickerFunctionDescriptor);

        // The derived fingerprint is emitted as a plain sha256: string (not an escaped object).
        Assert.Contains("\"fingerprint\":\"sha256:", json);
        Assert.Equal(descriptor.Request!.Fingerprint, DeserializeFingerprint(json));
    }

    private static string DeserializeFingerprint(string json)
    {
        // Round-trip through the source-generated context; the fingerprint is re-derived on read.
        var back = JsonSerializer.Deserialize(json, WireContractJsonContext.Default.TickerFunctionDescriptor);
        return back!.Request!.Fingerprint;
    }

    [Fact]
    public void Descriptor_RoundTrips_ThroughSourceGeneratedContext()
    {
        var json = JsonSerializer.Serialize(SampleDescriptor(), WireContractJsonContext.Default.TickerFunctionDescriptor);
        var back = JsonSerializer.Deserialize(json, WireContractJsonContext.Default.TickerFunctionDescriptor);

        Assert.NotNull(back);
        Assert.Equal("Orders.Process", back!.FunctionName);
        Assert.Equal(TickerTaskPriority.High, back.Priority);
        Assert.NotNull(back.Request);
        Assert.Equal(JsonValueKind.Object, back.Request!.Schema!.Value.ValueKind);
        Assert.Equal("object", back.Request.Schema.Value.GetProperty("type").GetString());
        Assert.Single(back.Request.Examples);
        Assert.Equal(1, back.Request.Examples[0].Value.GetProperty("id").GetInt32());

        // Structural equality survives the round-trip.
        Assert.Equal(SampleDescriptor(), back);
    }
}
