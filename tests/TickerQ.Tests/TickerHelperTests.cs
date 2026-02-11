using System.Text;
using System.Text.Json;
using FluentAssertions;
using TickerQ.Utilities;

namespace TickerQ.Tests;

public class TickerHelperTests : IDisposable
{
    // Store original state so we can restore after each test
    private readonly bool _originalGZipEnabled;
    private readonly JsonSerializerOptions _originalOptions;

    public TickerHelperTests()
    {
        _originalGZipEnabled = TickerHelper.UseGZipCompression;
        _originalOptions = TickerHelper.RequestJsonSerializerOptions;
    }

    public void Dispose()
    {
        TickerHelper.UseGZipCompression = _originalGZipEnabled;
        TickerHelper.RequestJsonSerializerOptions = _originalOptions;
    }

    #region CreateTickerRequest - No Compression

    [Fact]
    public void CreateTickerRequest_WithoutCompression_SerializesObject()
    {
        TickerHelper.UseGZipCompression = false;
        var data = new TestPayload { Name = "test", Value = 42 };

        var bytes = TickerHelper.CreateTickerRequest(data);

        var json = Encoding.UTF8.GetString(bytes);
        json.Should().Contain("test");
        json.Should().Contain("42");
    }

    [Fact]
    public void CreateTickerRequest_WithoutCompression_ByteArray_ReturnsAsIs()
    {
        TickerHelper.UseGZipCompression = false;
        var original = new byte[] { 1, 2, 3, 4, 5 };

        var result = TickerHelper.CreateTickerRequest(original);

        result.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void CreateTickerRequest_WithoutCompression_String_SerializesToJson()
    {
        TickerHelper.UseGZipCompression = false;

        var bytes = TickerHelper.CreateTickerRequest("hello");

        var result = Encoding.UTF8.GetString(bytes);
        result.Should().Contain("hello");
    }

    #endregion

    #region CreateTickerRequest - With GZip Compression

    [Fact]
    public void CreateTickerRequest_WithCompression_ProducesGZipBytes()
    {
        TickerHelper.UseGZipCompression = true;
        var data = new TestPayload { Name = "compressed", Value = 99 };

        var bytes = TickerHelper.CreateTickerRequest(data);

        // GZip signature is appended at end: [0x1f, 0x8b, 0x08, 0x00]
        bytes.Length.Should().BeGreaterThan(4);
        bytes[^4].Should().Be(0x1f);
        bytes[^3].Should().Be(0x8b);
        bytes[^2].Should().Be(0x08);
        bytes[^1].Should().Be(0x00);
    }

    [Fact]
    public void CreateTickerRequest_WithCompression_AlreadyCompressed_ReturnsSameBytes()
    {
        TickerHelper.UseGZipCompression = true;
        // Create compressed data first
        var original = TickerHelper.CreateTickerRequest(new TestPayload { Name = "test", Value = 1 });

        // Pass the already-compressed bytes back in
        var result = TickerHelper.CreateTickerRequest(original);

        result.Should().BeEquivalentTo(original);
    }

    #endregion

    #region ReadTickerRequest - No Compression

    [Fact]
    public void ReadTickerRequest_WithoutCompression_DeserializesObject()
    {
        TickerHelper.UseGZipCompression = false;
        var data = new TestPayload { Name = "read_test", Value = 7 };
        var bytes = TickerHelper.CreateTickerRequest(data);

        var result = TickerHelper.ReadTickerRequest<TestPayload>(bytes);

        result.Name.Should().Be("read_test");
        result.Value.Should().Be(7);
    }

    [Fact]
    public void ReadTickerRequestAsString_WithoutCompression_ReturnsJsonString()
    {
        TickerHelper.UseGZipCompression = false;
        var json = """{"Name":"hello","Value":42}""";
        var bytes = Encoding.UTF8.GetBytes(json);

        var result = TickerHelper.ReadTickerRequestAsString(bytes);

        result.Should().Be(json);
    }

    #endregion

    #region ReadTickerRequest - With GZip Compression

    [Fact]
    public void ReadTickerRequest_WithCompression_DeserializesCompressedObject()
    {
        TickerHelper.UseGZipCompression = true;
        var data = new TestPayload { Name = "gzip_test", Value = 100 };
        var bytes = TickerHelper.CreateTickerRequest(data);

        var result = TickerHelper.ReadTickerRequest<TestPayload>(bytes);

        result.Name.Should().Be("gzip_test");
        result.Value.Should().Be(100);
    }

    [Fact]
    public void ReadTickerRequestAsString_WithCompression_ThrowsForNonGzipBytes()
    {
        TickerHelper.UseGZipCompression = true;
        var plainBytes = Encoding.UTF8.GetBytes("not compressed");

        var act = () => TickerHelper.ReadTickerRequestAsString(plainBytes);

        act.Should().Throw<Exception>().WithMessage("*not GZip compressed*");
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_WithoutCompression_PreservesData()
    {
        TickerHelper.UseGZipCompression = false;
        var original = new TestPayload { Name = "roundtrip", Value = 123 };

        var bytes = TickerHelper.CreateTickerRequest(original);
        var result = TickerHelper.ReadTickerRequest<TestPayload>(bytes);

        result.Name.Should().Be(original.Name);
        result.Value.Should().Be(original.Value);
    }

    [Fact]
    public void RoundTrip_WithCompression_PreservesData()
    {
        TickerHelper.UseGZipCompression = true;
        var original = new TestPayload { Name = "gzip_roundtrip", Value = 456 };

        var bytes = TickerHelper.CreateTickerRequest(original);
        var result = TickerHelper.ReadTickerRequest<TestPayload>(bytes);

        result.Name.Should().Be(original.Name);
        result.Value.Should().Be(original.Value);
    }

    [Fact]
    public void RoundTrip_WithCustomJsonOptions_UsesConfiguredOptions()
    {
        TickerHelper.UseGZipCompression = false;
        TickerHelper.RequestJsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var original = new TestPayload { Name = "camel", Value = 789 };
        var bytes = TickerHelper.CreateTickerRequest(original);
        var json = Encoding.UTF8.GetString(bytes);

        json.Should().Contain("\"name\"");
        json.Should().Contain("\"value\"");
    }

    [Fact]
    public void RoundTrip_ComplexObject_PreservesStructure()
    {
        TickerHelper.UseGZipCompression = false;
        var original = new ComplexPayload
        {
            Id = Guid.NewGuid(),
            Items = ["a", "b", "c"],
            Nested = new TestPayload { Name = "nested", Value = 10 }
        };

        var bytes = TickerHelper.CreateTickerRequest(original);
        var result = TickerHelper.ReadTickerRequest<ComplexPayload>(bytes);

        result.Id.Should().Be(original.Id);
        result.Items.Should().BeEquivalentTo(original.Items);
        result.Nested.Name.Should().Be("nested");
        result.Nested.Value.Should().Be(10);
    }

    #endregion

    #region Test Models

    private class TestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private class ComplexPayload
    {
        public Guid Id { get; set; }
        public List<string> Items { get; set; } = [];
        public TestPayload Nested { get; set; } = new();
    }

    #endregion
}
