using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities;

namespace TickerQ.Tests;

/// <summary>
/// Transport-level contract for a ticker <c>request</c> byte[] over the dashboard
/// wire. The browser edits payloads as raw JSON text and ships them UTF-8 Base64
/// encoded; the server must decode that Base64, store the exact request bytes, and
/// echo them back as the same Base64 so the round trip cannot drift. Legacy rows
/// that stored the JSON verbatim must still deserialize.
/// </summary>
public sealed class DashboardRequestTransportTests
{
    private static JsonSerializerOptions BuildOptions() => new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new StringToByteArrayConverter() },
    };

    [Fact]
    public void Base64UnicodePayload_DeserializesToRequest_AndSerializesBackToSameBase64()
    {
        var options = BuildOptions();
        const string json = "{\"greeting\":\"héllo 世界 🚀\",\"emoji\":\"✅\"}";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        // Wire carries the request as a JSON string containing UTF-8 Base64.
        var wire = JsonSerializer.Serialize(base64, options.GetTypeInfo(typeof(string)));

        var bytes = JsonSerializer.Deserialize<byte[]>(wire, options);

        Assert.NotNull(bytes);
        // The decoded payload is the exact original JSON, Unicode intact.
        Assert.Equal(json, TickerHelper.ReadTickerRequestAsString(bytes!));

        // Serializing the request back produces the identical Base64 string token.
        var reserialized = JsonSerializer.Serialize(bytes, options);
        var expected = JsonSerializer.Serialize(base64, options.GetTypeInfo(typeof(string)));
        Assert.Equal(expected, reserialized);
    }

    [Fact]
    public void LegacyRawJsonPayload_StillDeserializes()
    {
        var options = BuildOptions();
        const string json = "{\"legacy\":true,\"name\":\"alice\"}";

        // Older clients stored the JSON verbatim (not Base64). `{` is not valid
        // Base64, so the converter must fall back to treating it as raw JSON.
        var wire = JsonSerializer.Serialize(json, options.GetTypeInfo(typeof(string)));

        var bytes = JsonSerializer.Deserialize<byte[]>(wire, options);

        Assert.NotNull(bytes);
        Assert.Equal(json, TickerHelper.ReadTickerRequestAsString(bytes!));
    }

    [Fact]
    public void LegacyNumericByteArrayPayload_DecodesDirectly_WithoutRecursion()
    {
        var options = BuildOptions();
        const string json = "{\"legacy\":\"numeric\",\"value\":42}";

        // The oldest transport serialized the request byte[] as a JSON array of
        // raw byte values. The converter must decode it in place (not by
        // re-entering the serializer for byte[], which would recurse into
        // itself and overflow the stack).
        var wire = "[" + string.Join(",", Encoding.UTF8.GetBytes(json).Select(b => b.ToString())) + "]";

        var bytes = JsonSerializer.Deserialize<byte[]>(wire, options);

        Assert.NotNull(bytes);
        Assert.Equal(json, TickerHelper.ReadTickerRequestAsString(bytes!));
    }

    [Fact]
    public void MalformedBase64String_ThrowsControlledJsonException()
    {
        var options = BuildOptions();
        // A non-JSON string that is also not valid Base64 (padding/length is off).
        var wire = JsonSerializer.Serialize("not valid base64 %%%", options.GetTypeInfo(typeof(string)));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<byte[]>(wire, options));
    }

    [Fact]
    public void Base64OfInvalidJson_ThrowsControlledJsonException()
    {
        var options = BuildOptions();
        // Valid Base64 whose decoded bytes are NOT valid JSON ("hello world").
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello world"));
        var wire = JsonSerializer.Serialize(base64, options.GetTypeInfo(typeof(string)));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<byte[]>(wire, options));
    }

    [Fact]
    public void ObjectToken_ThrowsControlledJsonException()
    {
        var options = BuildOptions();
        // An object where a request payload string/array is expected.
        const string wire = "{\"unexpected\":true}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<byte[]>(wire, options));
    }

    [Fact]
    public void LegacyNumericByteArray_ValueOutOfByteRange_ThrowsControlledJsonException()
    {
        var options = BuildOptions();
        // 256 is outside the 0-255 byte range; TryGetByte fails and the converter
        // must surface a controlled JsonException rather than truncating silently.
        const string wire = "[104,256,105]";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<byte[]>(wire, options));
    }

    [Fact]
    public void LegacyNumericByteArray_NonNumericElement_ThrowsControlledJsonException()
    {
        var options = BuildOptions();
        // A string element inside the legacy byte array is an unexpected token and
        // must fail closed instead of being skipped.
        const string wire = "[1,\"x\",2]";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<byte[]>(wire, options));
    }

    [Fact]
    public void LegacyNumericByteArray_DecodesToInvalidJson_ThrowsControlledJsonException()
    {
        var options = BuildOptions();
        // A well-formed byte array whose decoded bytes are NOT valid JSON
        // ("hello world"). The array path must validate JSON just like the
        // Base64 path and throw rather than store garbage.
        var wire = "[" + string.Join(",", Encoding.UTF8.GetBytes("hello world").Select(b => b.ToString())) + "]";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<byte[]>(wire, options));
    }

    [Fact]
    public void NullToken_DeserializesToNull()
    {
        var options = BuildOptions();
        // A JSON null is a legitimate "no payload" and must map to null, not throw.
        var bytes = JsonSerializer.Deserialize<byte[]>("null", options);

        Assert.Null(bytes);
    }

    [Fact]
    public void EmptyString_DeserializesToNull()
    {
        var options = BuildOptions();
        // An empty string token is also "no payload" and must map to null.
        var wire = JsonSerializer.Serialize(string.Empty, options.GetTypeInfo(typeof(string)));

        var bytes = JsonSerializer.Deserialize<byte[]>(wire, options);

        Assert.Null(bytes);
    }

    [Fact]
    public void WriteFallback_EmitsNumericArray_WithoutRecursing()
    {
        var options = BuildOptions();
        // Force the primary Base64 path to fail: with compression enabled,
        // ReadTickerRequestAsString rejects bytes lacking the GZip signature,
        // exercising the Write fallback. It must emit a numeric array directly
        // instead of re-entering the serializer for byte[] (infinite recursion).
        var original = TickerHelper.UseGZipCompression;
        try
        {
            TickerHelper.UseGZipCompression = true;
            var value = new byte[] { 1, 2, 3 };

            var wire = JsonSerializer.Serialize(value, options);

            Assert.Equal("[1,2,3]", wire);
        }
        finally
        {
            TickerHelper.UseGZipCompression = original;
        }
    }

    [Fact]
    public async Task UnexpectedTickerError_ReturnsGeneric500WithTraceId_WithoutLeakingException()
    {
        const string sensitiveMessage = "database password was hunter2";
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-contract-test",
        };
        context.Response.Body = new MemoryStream();

        var dashboardOptions = new DashboardOptionsBuilder();
        typeof(DashboardOptionsBuilder)
            .GetProperty("DashboardJsonOptions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(dashboardOptions, BuildOptions());
        context.RequestServices = new ServiceCollection()
            .AddSingleton(dashboardOptions)
            .AddLogging()
            .BuildServiceProvider();

        var writeTickerError = typeof(DashboardEndpoints).GetMethod(
            "WriteTickerError",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        await (Task)writeTickerError.Invoke(
            null,
            new object?[] { context, new InvalidOperationException(sensitiveMessage) })!;

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("trace-contract-test", body);
        Assert.DoesNotContain(sensitiveMessage, body);
        Assert.Contains("internal server error", body, StringComparison.OrdinalIgnoreCase);
    }
}
