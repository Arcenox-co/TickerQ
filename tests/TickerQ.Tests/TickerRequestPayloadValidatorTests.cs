using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Behavior tests for <see cref="TickerRequestPayloadValidator"/> — the server-authoritative
/// request payload validation/deserialization service (typed-request-contracts, next slice, first
/// policy-independent vertical tracer bullet).
///
/// The service takes a function identity plus the stored/raw byte[] payload, resolves the canonical
/// <see cref="TickerFunctionDescriptor"/> and the function-specific registered <c>JsonTypeInfo</c>,
/// enforces request-less vs required payload semantics, decompresses/parses through the existing
/// <see cref="TickerHelper"/> conventions, validates against the descriptor's Draft 2020-12 schema
/// when one exists AND is expressible by the reflection-free core validator, then deserializes with
/// the exact <c>JsonTypeInfo</c> so schema-valid-but-CLR-invalid payloads also fail. It returns a
/// structured immutable result with machine-readable errors and never throws for ordinary user
/// payload invalidity.
///
/// The provider is fully static, so each test resets provider + helper state.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class TickerRequestPayloadValidatorTests : IDisposable
{
    // Only structural keywords the reflection-free core validator supports.
    private const string EnforceableSchema =
        "{\"type\":\"object\",\"properties\":{\"OrderId\":{\"type\":\"string\"}}," +
        "\"required\":[\"OrderId\"],\"additionalProperties\":true}";

    // Exercises the complete family of constraints emitted by the source generator rather than a
    // hand-picked structural subset. Runtime validation must enforce these Draft 2020-12 keywords.
    private const string FullSchema =
        "{\"type\":\"object\",\"properties\":{" +
        "\"OrderId\":{\"type\":\"string\",\"minLength\":3,\"maxLength\":8,\"pattern\":\"^(?:[a-z]+)(?![\\\\s\\\\S])\"}," +
        "\"Quantity\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}," +
        "\"Tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"a\",\"b\"]},\"minItems\":1,\"maxItems\":2}," +
        "\"Token\":{\"type\":\"string\",\"format\":\"uuid\"}}," +
        "\"required\":[\"OrderId\",\"Quantity\",\"Tags\",\"Token\"],\"additionalProperties\":false}";

    private readonly bool _originalGZipEnabled;

    public TickerRequestPayloadValidatorTests()
    {
        // The validator only ever binds through function-specific JsonTypeInfo, so it never reads the
        // process-global TickerHelper.RequestJsonSerializerOptions — do not touch that global here, or
        // parallel collections (e.g. JsonExampleGeneratorTests) that configure it would be corrupted.
        // UseGZipCompression is genuinely consumed by the decompression path; bracket save/restore it
        // the same way TickerHelperTests does.
        _originalGZipEnabled = TickerHelper.UseGZipCompression;
        ResetProvider();
    }

    public void Dispose()
    {
        ResetProvider();
        TickerHelper.UseGZipCompression = _originalGZipEnabled;
    }

    private static void ResetProvider()
    {
        var type = typeof(TickerFunctionProvider);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        type.GetField("TickerFunctions", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetField("TickerFunctionRequestTypes", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, Type)>.Empty);
        type.GetField("TickerFunctionRequestInfos", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, string)>.Empty);
        type.GetField("_snapshot", flags)!.SetValue(null, TickerFunctionRegistrySnapshot.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeRequestRegistrations", flags)!.SetValue(null, null);
        ((System.Collections.IList)type.GetField("_pendingDescriptors", flags)!.GetValue(null)!).Clear();
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);

        TickerHelper.UseGZipCompression = false;
    }

    private static void RegisterTyped(string functionName, bool required, string schemaJson, bool withRuntime = true)
    {
        var contract = new TickerRequestContract(
            typeof(OrderRequest).FullName!,
            TickerRequestContractConstants.DefaultMediaType,
            required,
            TickerRequestContractConstants.SchemaDialect2020_12,
            schemaJson);
        var descriptor = new TickerFunctionDescriptor(functionName, TickerTaskPriority.Normal, null, 1, contract);
        TickerFunctionProvider.RegisterDescriptors(
            new Dictionary<string, TickerFunctionDescriptor> { [functionName] = descriptor });

        if (withRuntime)
            TickerFunctionProvider.RegisterRequestTypeInfo(
                new Dictionary<string, (Type, JsonTypeInfo)>
                {
                    [functionName] = (typeof(OrderRequest), ValidatorTestJsonContext.Default.OrderRequest)
                });

        TickerFunctionProvider.Build();
    }

    private static void RegisterRequestless(string functionName)
    {
        var descriptor = new TickerFunctionDescriptor(functionName);
        TickerFunctionProvider.RegisterDescriptors(
            new Dictionary<string, TickerFunctionDescriptor> { [functionName] = descriptor });
        TickerFunctionProvider.Build();
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    // ---------------------------------------------------------------
    // Request-less vs required payload semantics
    // ---------------------------------------------------------------

    [Fact]
    public void Requestless_EmptyPayload_IsValid_NoValue_NoSchema()
    {
        RegisterRequestless("Ping");

        var result = TickerRequestPayloadValidator.Validate("Ping", Array.Empty<byte>());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Null(result.Value);
        Assert.Equal(TickerRequestSchemaValidationState.NotApplicable, result.SchemaValidation);
    }

    [Fact]
    public void Requestless_WithPayload_IsInvalid_PayloadNotAllowed()
    {
        RegisterRequestless("Ping");

        var result = TickerRequestPayloadValidator.Validate("Ping", Utf8("{\"OrderId\":\"x\"}"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.PayloadNotAllowed);
    }

    [Fact]
    public void Required_EmptyPayload_IsInvalid_PayloadRequiredButMissing()
    {
        RegisterTyped("Order", required: true, EnforceableSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.PayloadRequiredButMissing);
    }

    [Fact]
    public void Optional_EmptyPayload_IsValid_NoValue()
    {
        RegisterTyped("Order", required: false, EnforceableSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", Array.Empty<byte>());

        Assert.True(result.IsValid);
        Assert.Null(result.Value);
        Assert.Equal(TickerRequestSchemaValidationState.NotApplicable, result.SchemaValidation);
    }

    // ---------------------------------------------------------------
    // Happy path: schema enforced, then deserialized
    // ---------------------------------------------------------------

    [Fact]
    public void ValidPayload_PassesSchema_AndDeserializes()
    {
        RegisterTyped("Order", required: true, EnforceableSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8("{\"OrderId\":\"abc\",\"Quantity\":5}"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, result.SchemaValidation);
        var order = Assert.IsType<OrderRequest>(result.Value);
        Assert.Equal("abc", order.OrderId);
        Assert.Equal(5, order.Quantity);
        Assert.Equal(1, result.AcceptedContractVersion);
        Assert.Equal(
            TickerFunctionProvider.TickerFunctionDescriptors["Order"].Request.Fingerprint,
            result.AcceptedContractFingerprint);
    }

    // ---------------------------------------------------------------
    // Schema violations short-circuit before deserialization
    // ---------------------------------------------------------------

    [Fact]
    public void MissingRequiredProperty_IsInvalid_SchemaViolation()
    {
        RegisterTyped("Order", required: true, EnforceableSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8("{\"Quantity\":5}"));

        Assert.False(result.IsValid);
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, result.SchemaValidation);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.SchemaViolation);
        Assert.DoesNotContain(result.Errors, e => e.Code == TickerRequestValidationErrorCode.DeserializationFailed);
    }

    [Fact]
    public void WrongPropertyType_IsInvalid_SchemaViolation()
    {
        RegisterTyped("Order", required: true, EnforceableSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8("{\"OrderId\":123}"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.SchemaViolation);
    }

    // ---------------------------------------------------------------
    // Schema-valid but CLR-invalid still fails at deserialization
    // ---------------------------------------------------------------

    [Fact]
    public void SchemaValidButClrInvalid_IsInvalid_DeserializationFailed()
    {
        RegisterTyped("Order", required: true, EnforceableSchema);

        // Quantity is not constrained by the schema (additionalProperties:true) so this passes schema
        // validation, but the exact JsonTypeInfo cannot bind "notanint" to System.Int32.
        var result = TickerRequestPayloadValidator.Validate("Order", Utf8("{\"OrderId\":\"abc\",\"Quantity\":\"notanint\"}"));

        Assert.False(result.IsValid);
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, result.SchemaValidation);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.DeserializationFailed);
    }

    // ---------------------------------------------------------------
    // Full Draft 2020-12 validation of generated keyword families
    // ---------------------------------------------------------------

    [Fact]
    public void FullGeneratedSchema_IsEnforced_AndDeserializesValidPayload()
    {
        RegisterTyped("Order", required: true, FullSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8(
            "{\"OrderId\":\"abc\",\"Quantity\":5,\"Tags\":[\"a\"],\"Token\":\"6ba7b810-9dad-11d1-80b4-00c04fd430c8\"}"));

        Assert.True(result.IsValid);
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, result.SchemaValidation);
        var order = Assert.IsType<OrderRequest>(result.Value);
        Assert.Equal("abc", order.OrderId);
    }

    [Fact]
    public void FullGeneratedSchema_RejectsEveryGeneratedConstraintFamily()
    {
        RegisterTyped("Order", required: true, FullSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8(
            "{\"OrderId\":\"A\",\"Quantity\":0,\"Tags\":[\"z\"],\"Token\":\"not-a-uuid\",\"Extra\":true}"));

        Assert.False(result.IsValid);
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, result.SchemaValidation);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.SchemaViolation);
    }

    // ---------------------------------------------------------------
    // Malformed payload bytes
    // ---------------------------------------------------------------

    [Fact]
    public void MalformedJson_IsInvalid_MalformedPayload()
    {
        RegisterTyped("Order", required: true, EnforceableSchema);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8("{ this is not json"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.MalformedPayload);
    }

    // ---------------------------------------------------------------
    // Decompression honors TickerHelper GZip conventions
    // ---------------------------------------------------------------

    [Fact]
    public void GZipCompressedPayload_IsDecompressed_AndValidated()
    {
        TickerHelper.UseGZipCompression = true;
        RegisterTyped("Order", required: true, EnforceableSchema);

        var payload = TickerHelper.CreateTickerRequest(
            new OrderRequest { OrderId = "abc", Quantity = 7 },
            ValidatorTestJsonContext.Default.OrderRequest);

        var result = TickerRequestPayloadValidator.Validate("Order", payload);

        Assert.True(result.IsValid);
        var order = Assert.IsType<OrderRequest>(result.Value);
        Assert.Equal(7, order.Quantity);
    }

    // ---------------------------------------------------------------
    // Configuration / identity errors are structured, not thrown
    // ---------------------------------------------------------------

    [Fact]
    public void UnknownFunction_IsInvalid_UnknownFunction_DoesNotThrow()
    {
        RegisterTyped("Order", required: true, EnforceableSchema);

        var result = TickerRequestPayloadValidator.Validate("DoesNotExist", Utf8("{\"OrderId\":\"abc\"}"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.UnknownFunction);
    }

    [Fact]
    public void RemoteSchemaWithoutLocalSerializerMetadata_IsValid_BindingReportedUnavailable()
    {
        // Descriptor with a schema but no locally-registered JsonTypeInfo (e.g. a remote/Hub contract).
        RegisterTyped("Order", required: true, EnforceableSchema, withRuntime: false);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8("{\"OrderId\":\"abc\"}"));

        Assert.True(result.IsValid);
        Assert.Null(result.Value);
        Assert.Equal(TickerRequestSchemaValidationState.Enforced, result.SchemaValidation);
        Assert.Equal(TickerRequestBindingValidationState.NotAvailable, result.BindingValidation);
    }

    [Fact]
    public void NoSchemaAndNoSerializerMetadata_IsInvalid_MissingSerializerMetadata()
    {
        RegisterTyped("Order", required: true, schemaJson: null, withRuntime: false);

        var result = TickerRequestPayloadValidator.Validate("Order", Utf8("{\"OrderId\":\"abc\"}"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == TickerRequestValidationErrorCode.MissingSerializerMetadata);
    }

    [Fact]
    public void NullFunctionName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TickerRequestPayloadValidator.Validate(null!, Array.Empty<byte>()));
    }
}

internal sealed class OrderRequest
{
    public string OrderId { get; set; }
    public int Quantity { get; set; }
}

[JsonSerializable(typeof(OrderRequest))]
internal sealed partial class ValidatorTestJsonContext : JsonSerializerContext
{
}
