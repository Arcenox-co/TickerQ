using System.Text.Json;
using Json.Schema;

namespace TickerQ.SourceGenerator.Tests;

public class GeneratedExampleDraft202012Tests
{
    public static TheoryData<string, string> Contracts => new()
    {
        {
            """
using System;
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class Request
{
    [Range(10, 20)] public required int Count { get; init; }
    [StringLength(8, MinimumLength = 3)] public required string Code { get; init; }
    [EmailAddress] public required string Email { get; init; }
}
public static class Jobs
{
    [TickerFunction("ValidatedExample")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<Request> context) => System.Threading.Tasks.Task.CompletedTask;
}
""",
            "global::Request"
        },
        {
            """
using System;
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class Node
{
    public Node Next { get; init; }
    [Required] public Node? NullableNext { get; init; }
}
public static class Jobs
{
    [TickerFunction("RecursiveExample")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<Node> context) => System.Threading.Tasks.Task.CompletedTask;
}
""",
            "global::Node"
        },
        {
            """
using System;
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class TemporalRequest
{
    [Required] public TimeOnly Time { get; init; }
    [Required] public TimeSpan Duration { get; init; }
    [Required] public DateTime LocalDateTime { get; init; }
    [Required] public Uri RelativeUri { get; init; }
}
public static class Jobs
{
    [TickerFunction("TemporalExample")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<TemporalRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""",
            "global::TemporalRequest"
        }
    };

    [Theory]
    [MemberData(nameof(Contracts))]
    public void GeneratedExample_ValidatesAgainstGeneratedDraft202012Schema(string source, string requestType)
    {
        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);

        var schemaJson = result.SchemaFor(requestType)
            ?? throw new Xunit.Sdk.XunitException($"No schema was generated for {requestType}.");
        var schema = JsonSchema.FromText(schemaJson);
        var exampleJson = result.ExampleForDefault()!;
        using var document = JsonDocument.Parse(exampleJson);
        var evaluation = schema.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        Assert.True(evaluation.IsValid, evaluation.ToString());
        if (requestType == "global::TemporalRequest")
            Assert.NotNull(JsonSerializer.Deserialize<TemporalProbe>(exampleJson));
    }

    private sealed class TemporalProbe
    {
        public TimeOnly Time { get; init; }
        public TimeSpan Duration { get; init; }
        public DateTime LocalDateTime { get; init; }
        public Uri? RelativeUri { get; init; }
    }
}
