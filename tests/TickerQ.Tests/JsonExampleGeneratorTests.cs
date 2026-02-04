using System.Text.Json;
using Xunit;
using TickerQ.Dashboard.Infrastructure.Dashboard;

namespace TickerQ.Tests;

public class JsonExampleGeneratorTests
{
    [Fact]
    public void TryGenerateExampleJson_WithInt_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(int), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<int>(json);
        Assert.Equal(123, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithString_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(string), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<string>(json);
        Assert.Equal("string", value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithBoolean_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(bool), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<bool>(json);
        Assert.True(value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithDouble_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(double), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<double>(json);
        Assert.Equal(123.45, value, 2);
    }

    [Fact]
    public void TryGenerateExampleJson_WithDecimal_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(decimal), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<decimal>(json);
        Assert.Equal(decimal.Zero, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithDateTime_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(DateTime), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<DateTime>(json);
        Assert.Equal(DateTime.MinValue, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithChar_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(char), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<char>(json);
        Assert.Equal('a', value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithLong_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(long), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<long>(json);
        Assert.Equal(123L, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithFloat_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(float), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<float>(json);
        Assert.InRange(value, 123.45f - 0.01f, 123.45f + 0.01f);
    }

    [Fact]
    public void TryGenerateExampleJson_WithByte_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(byte), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<byte>(json);
        Assert.Equal((byte)1, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithShort_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(short), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<short>(json);
        Assert.Equal((short)1, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithUInt_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(uint), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<uint>(json);
        Assert.Equal(123u, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithULong_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(ulong), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<ulong>(json);
        Assert.Equal(123ul, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithNullableInt_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(int?), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<int?>(json);
        Assert.Equal(123, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithNullableBoolean_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(bool?), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<bool?>(json);
        Assert.True(value.GetValueOrDefault());
    }

    [Fact]
    public void TryGenerateExampleJson_WithNullableDateTime_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(DateTime?), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<DateTime?>(json);
        Assert.Equal(DateTime.MinValue, value);
    }

    [Fact]
    public void TryGenerateExampleJson_WithIntArray_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(int[]), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<int[]>(json);
        Assert.NotNull(value);
        Assert.Single(value);
        Assert.Equal(123, value![0]);
    }

    [Fact]
    public void TryGenerateExampleJson_WithStringArray_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(string[]), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<string[]>(json);
        Assert.NotNull(value);
        Assert.Single(value);
        Assert.Equal("string", value![0]);
    }

    [Fact]
    public void TryGenerateExampleJson_WithListOfInt_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(List<int>), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<List<int>>(json);
        Assert.NotNull(value);
        Assert.Single(value);
        Assert.Equal(123, value![0]);
    }

    [Fact]
    public void TryGenerateExampleJson_WithListOfString_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(List<string>), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<List<string>>(json);
        Assert.NotNull(value);
        Assert.Single(value);
        Assert.Equal("string", value![0]);
    }

    [Fact]
    public void TryGenerateExampleJson_WithIListOfInt_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(IList<int>), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<List<int>>(json);
        Assert.NotNull(value);
        Assert.Single(value);
        Assert.Equal(123, value![0]);
    }

    [Fact]
    public void TryGenerateExampleJson_WithSimpleClass_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(SimpleTestClass), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<SimpleTestClass>(json);
        Assert.NotNull(value);
        Assert.Equal(123, value!.Id);
        Assert.Equal("string", value.Name);
        Assert.True(value.IsActive);
    }

    [Fact]
    public void TryGenerateExampleJson_WithNestedClass_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(NestedTestClass), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<NestedTestClass>(json);
        Assert.NotNull(value);
        Assert.Equal(123, value!.Id);
        Assert.NotNull(value.Child);
        Assert.Equal(123, value.Child!.Id);
        Assert.Equal("string", value.Child.Name);
    }

    [Fact]
    public void TryGenerateExampleJson_WithClassContainingList_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(ClassWithList), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<ClassWithList>(json);
        Assert.NotNull(value);
        Assert.NotNull(value!.Items);
        Assert.Single(value.Items);
        Assert.Equal("string", value.Items![0]);
    }

    [Fact]
    public void TryGenerateExampleJson_WithClassContainingArray_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(ClassWithArray), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<ClassWithArray>(json);
        Assert.NotNull(value);
        Assert.NotNull(value!.Numbers);
        Assert.Single(value.Numbers);
        Assert.Equal(123, value.Numbers![0]);
    }

    [Fact]
    public void TryGenerateExampleJson_WithStruct_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(TestStruct), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<TestStruct>(json);
        Assert.Equal(123, value.X);
        Assert.Equal(123, value.Y);
    }

    [Fact]
    public void TryGenerateExampleJson_WithReadOnlyProperty_OnlyGeneratesWritableProperties()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(ClassWithReadOnlyProperty), out var json);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<ClassWithReadOnlyProperty>(json);
        Assert.NotNull(value);
        Assert.Equal(123, value!.WritableProperty);
    }

    [Fact]
    public void TryGenerateExampleJson_WithTypeWithoutParameterlessConstructor_ReturnsFalse()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(ClassWithoutParameterlessConstructor), out var json);

        Assert.False(result);
        Assert.True(string.IsNullOrEmpty(json));
    }

    [Fact]
    public void TryGenerateExampleJson_ReturnsIndentedJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(SimpleTestClass), out var json);

        Assert.True(result);
        Assert.Contains("\n", json);
        Assert.Contains("  ", json);
    }

    [Fact]
    public void TryGenerateExampleJson_WithClassHierarchy_ReturnsValidJson()
    {
        var result = JsonExampleGenerator.TryGenerateExampleJson(typeof(ClassWithHierarchy), out var json);
        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(json));
        var value = JsonSerializer.Deserialize<ClassWithHierarchy>(json);
        Assert.NotNull(value);
        Assert.NotNull(value!.Child);
        Assert.Null(value.Child!.Child); // Ensure it doesn't go infinitely deep
    }

    public class SimpleTestClass
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
    }

    public class NestedTestClass
    {
        public int Id { get; set; }
        public SimpleTestClass? Child { get; set; }
    }

    public class ClassWithList
    {
        public List<string>? Items { get; set; }
    }

    public class ClassWithArray
    {
        public int[]? Numbers { get; set; }
    }

    public struct TestStruct
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class ClassWithReadOnlyProperty
    {
        public int WritableProperty { get; set; }
        public int ReadOnlyProperty => 42;
    }

    public class ClassWithoutParameterlessConstructor
    {
        public ClassWithoutParameterlessConstructor(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    public class ClassWithHierarchy
    {
        public ClassWithHierarchy? Child { get; set; }
    }
}
