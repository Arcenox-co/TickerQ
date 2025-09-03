using System.Text.Json;
using FluentAssertions;
using TickerQ.Utilities;

namespace TickerQ.Tests.Utilities;

public class TickerHelperTests
{

    [Fact]
    public void Serialize_And_Compress_An_Object_Then_Decompress_And_Deserialize_It_To_Check_If_Same_Object_Is_Being_Returned()
    {
        // Arrange
        var person = new Project("TickerQ", true);
        
        // Act
        var tickerRequest = TickerHelper.CreateTickerRequest(person);
        var deserializedPerson = TickerHelper.ReadTickerRequest<Project>(tickerRequest);
        
        // Assert
        deserializedPerson.Should().BeEquivalentTo(person);
    }

    [Fact]
    public void ReadTickerRequestAsString_ShouldThrow_WhenSignatureIsMissing()
    {
        // Arrange
        var invalidBytes = new byte[] { 1, 2, 3, 4, 5, 6 };

        // Act
        Action act = () => TickerHelper.ReadTickerRequestAsString(invalidBytes);
        
        // Assert
        act.Should().Throw<Exception>().WithMessage("The bytes are not GZip compressed.");
    }

    [Fact]
    public void ReadTickerRequestAsString_ShouldReturnOriginalString()
    {
        // Arrange
        const string initial = "Hey! It is TickerQ!";
        
        // Act
        var bytes = TickerHelper.CreateTickerRequest(initial);
        var converted = TickerHelper.ReadTickerRequestAsString(bytes);
        var deserialized = JsonSerializer.Deserialize<string>(converted);
        
        // Assert
        deserialized.Should().Be(initial);
    }
    
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(int.MaxValue / 2)]
    [InlineData(double.MaxValue / 2)]
    [InlineData("Some Random String")]
    public void Serialize_And_Compress_An_Object_Then_Decompress_And_Deserialize_It_Should_Return_The_Same_Object(object data)
    {
        // Arrange
        var objectType = data.GetType();

        // Act
        var tickerRequest = TickerHelper.CreateTickerRequest(data);
        var converted = TickerHelper.ReadTickerRequestAsString(tickerRequest);
        var deserialized = JsonSerializer.Deserialize(converted, objectType);

        // Assert
        deserialized.Should().BeEquivalentTo(data);
    }
}

public class Project(string name, bool isAwesome)
{
    public string Name { get; set; } = name;
    public bool IsAwesome { get; set; } = isAwesome;
}