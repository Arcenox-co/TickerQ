using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Json.Schema;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Serialization;
using Xunit;

namespace TickerQ.Tests;

// These tests temporarily customize JsonSchema.Net defaults. The existing nonparallel collection
// also serializes them with the public request-validator tests that consume those defaults.
[Collection("TickerFunctionProviderState")]
public sealed class TickerJsonSchemaValidatorTests
{
    [Fact]
    public void RepeatedExplicitId_RemainsValid_WithoutGlobalRegistration()
    {
        var id = UniqueUri();
        var schema = IntegerPropertySchema(id);

        for (var iteration = 0; iteration < 32; iteration++)
        {
            Assert.Empty(Validate(schema, """{"Value":1}"""));
            AssertViolationAt(Validate(schema, """{"Value":"wrong"}"""), "/Value");
        }

        // A global registration is a permanent root, even after every job and log is deleted.
        Assert.Null(SchemaRegistry.Global.Get(id));
    }

    [Fact]
    public void ParallelValidation_WithSameId_KeepsIndependentOutcomes()
    {
        var id = UniqueUri();
        var schema = IntegerPropertySchema(id);

        Parallel.For(0, 128, iteration =>
        {
            if (iteration % 2 == 0)
                Assert.Empty(Validate(schema, """{"Value":1}"""));
            else
                AssertViolationAt(Validate(schema, """{"Value":0}"""), "/Value");
        });

        Assert.Null(SchemaRegistry.Global.Get(id));
    }

    [Fact]
    public void DifferentSchemas_WithSameId_DoNotShareRegistrations()
    {
        var id = UniqueUri();
        var first = AtId(id, """{"$id":"$SCHEMA_ID","type":"object","properties":{"Value":{"const":1}}}""");
        var second = AtId(id, """{"$id":"$SCHEMA_ID","type":"object","properties":{"Value":{"const":2}}}""");

        for (var iteration = 0; iteration < 4; iteration++)
        {
            Assert.Empty(Validate(first, """{"Value":1}"""));
            AssertViolationAt(Validate(first, """{"Value":2}"""), "/Value");
            Assert.Empty(Validate(second, """{"Value":2}"""));
            AssertViolationAt(Validate(second, """{"Value":1}"""), "/Value");
        }

        Assert.Null(SchemaRegistry.Global.Get(id));
    }

    [Fact]
    public void RecursiveLocalDefinition_EnforcesNestedValues()
    {
        const string schema = """
            {
              "$schema":"https://json-schema.org/draft/2020-12/schema",
              "type":"object",
              "properties":{"Head":{"$ref":"#/$defs/node"}},
              "required":["Head"],
              "$defs":{
                "node":{
                  "type":"object",
                  "properties":{
                    "Value":{"type":"integer","minimum":1},
                    "Next":{"anyOf":[{"$ref":"#/$defs/node"},{"type":"null"}]}
                  },
                  "required":["Value"]
                }
              }
            }
            """;

        Assert.Empty(Validate(schema, """{"Head":{"Value":1,"Next":{"Value":2,"Next":null}}}"""));
        AssertViolationAt(
            Validate(schema, """{"Head":{"Value":1,"Next":{"Value":"wrong"}}}"""),
            "/Head/Next/Value");
    }

    [Fact]
    public void NestedIdAndAnchor_ResolveLocally_WithoutGlobalRegistration()
    {
        var rootId = UniqueUri();
        var childId = new Uri(rootId, "child");
        var schema = AtId(rootId, """
            {
              "$schema":"https://json-schema.org/draft/2020-12/schema",
              "$id":"$SCHEMA_ID",
              "$defs":{"value":{"$id":"child","$anchor":"number","type":"integer","minimum":1}},
              "type":"object",
              "properties":{"Value":{"$ref":"child#number"}},
              "required":["Value"]
            }
            """);

        Assert.Empty(Validate(schema, """{"Value":1}"""));
        AssertViolationAt(Validate(schema, """{"Value":0}"""), "/Value");
        Assert.Null(SchemaRegistry.Global.Get(rootId));
        Assert.Null(SchemaRegistry.Global.Get(childId));
    }

    [Fact]
    public void GlobalExternalSchema_StillResolves()
    {
        var externalId = UniqueUri();
        var external = BuildExternalIntegerSchema(externalId);
        SchemaRegistry.Global.Register(external);
        var rootId = UniqueUri();
        var schema = ExternalReferenceSchema(rootId, externalId);

        Assert.Empty(Validate(schema, """{"Value":1}"""));
        AssertViolationAt(Validate(schema, """{"Value":"wrong"}"""), "/Value");
        Assert.Same(external, SchemaRegistry.Global.Get(externalId));
        Assert.Null(SchemaRegistry.Global.Get(rootId));
    }

    [Fact]
    public void GlobalFetch_ReceivesLocalRegistry_WithoutRetainingFetchedSchemaGlobally()
    {
        var originalFetch = SchemaRegistry.Global.Fetch;
        var externalId = UniqueUri();
        var rootId = UniqueUri();
        var external = BuildExternalIntegerSchema(externalId);
        var fetches = 0;
        try
        {
            SchemaRegistry.Global.Fetch = (uri, registry) =>
            {
                if (uri != externalId) return originalFetch(uri, registry);
                Assert.NotSame(SchemaRegistry.Global, registry);
                fetches++;
                return external;
            };

            var schema = ExternalReferenceSchema(rootId, externalId);
            Assert.Empty(Validate(schema, """{"Value":1}"""));
            AssertViolationAt(Validate(schema, """{"Value":0}"""), "/Value");
            Assert.True(fetches >= 2); // each validation owns its fetched registration
        }
        finally
        {
            SchemaRegistry.Global.Fetch = originalFetch;
        }

        Assert.Null(SchemaRegistry.Global.Get(externalId));
        Assert.Null(SchemaRegistry.Global.Get(rootId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomDefaultRegistry_ResolvesExternalSchemas_WithoutRetainingRequest(bool useFetcher)
    {
        var originalRegistry = BuildOptions.Default.SchemaRegistry;
        var customRegistry = new SchemaRegistry();
        var externalId = UniqueUri();
        var rootId = UniqueUri();
        var external = BuildExternalIntegerSchema(externalId);
        if (useFetcher)
        {
            customRegistry.Fetch = (uri, registry) =>
            {
                Assert.Same(customRegistry, registry);
                return uri == externalId ? external : null;
            };
        }
        else
        {
            customRegistry.Register(external);
        }

        try
        {
            BuildOptions.Default.SchemaRegistry = customRegistry;
            var schema = ExternalReferenceSchema(rootId, externalId);

            Assert.Empty(Validate(schema, """{"Value":1}"""));
            AssertViolationAt(Validate(schema, """{"Value":0}"""), "/Value");
            Assert.Null(customRegistry.Get(rootId));
            Assert.Same(external, customRegistry.Get(externalId));
            Assert.Null(SchemaRegistry.Global.Get(rootId));
            Assert.Null(SchemaRegistry.Global.Get(externalId));
        }
        finally
        {
            BuildOptions.Default.SchemaRegistry = originalRegistry;
        }
    }

    [Fact]
    public void ConfiguredDefaultDialect_IsPreserved()
    {
        var originalDialect = BuildOptions.Default.Dialect;
        try
        {
            BuildOptions.Default.Dialect = Dialect.Draft07;
            // Draft 7 ignores the sibling type; replacing the configured default with Draft 2020-12
            // would incorrectly reject this value. No embedded $schema overrides the default.
            const string schema = """
                {"definitions":{"value":{"type":"integer"}},"$ref":"#/definitions/value","type":"string"}
                """;
            Assert.Empty(Validate(schema, "1"));
        }
        finally
        {
            BuildOptions.Default.Dialect = originalDialect;
        }
    }

    [Fact]
    public void FormatValidation_RemainsEnforced()
    {
        const string schema = """
            {"type":"object","properties":{"Value":{"type":"string","format":"uuid"}},"required":["Value"]}
            """;

        Assert.Empty(Validate(schema, """{"Value":"6ba7b810-9dad-11d1-80b4-00c04fd430c8"}"""));
        AssertViolationAt(Validate(schema, """{"Value":"not-a-uuid"}"""), "/Value");
    }

    private static IReadOnlyList<TickerRequestValidationError> Validate(string schema, string instance)
    {
        using var schemaDocument = JsonDocument.Parse(schema);
        using var instanceDocument = JsonDocument.Parse(instance);
        return TickerJsonSchemaValidator.Validate(schemaDocument.RootElement, instanceDocument.RootElement);
    }

    private static JsonSchema BuildExternalIntegerSchema(Uri id)
        => JsonSchema.FromText(
            AtId(id, """{"$id":"$SCHEMA_ID","type":"integer","minimum":1}"""),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    private static string IntegerPropertySchema(Uri id)
        => AtId(id, """{"$id":"$SCHEMA_ID","type":"object","properties":{"Value":{"type":"integer","minimum":1}},"required":["Value"]}""");

    private static string ExternalReferenceSchema(Uri id, Uri externalId)
        => AtId(id, """{"$id":"$SCHEMA_ID","type":"object","properties":{"Value":{"$ref":"$EXTERNAL_ID"}},"required":["Value"]}""")
            .Replace("$EXTERNAL_ID", externalId.AbsoluteUri, StringComparison.Ordinal);

    private static string AtId(Uri id, string schema)
        => schema.Replace("$SCHEMA_ID", id.AbsoluteUri, StringComparison.Ordinal);

    private static Uri UniqueUri() => new($"https://tickerq.test/schema/{Guid.NewGuid():N}/root");

    private static void AssertViolationAt(IReadOnlyList<TickerRequestValidationError> errors, string location)
    {
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Equal(TickerRequestValidationErrorCode.SchemaViolation, error.Code));
        Assert.Contains(errors, error => error.Location == location);
    }
}
