using FluentAssertions;
using TickerQ.Utilities.Extensions;

namespace TickerQ.Tests.Utilities.Extensions;

public class EnumerableExtensionTests
{
    private record Person(string Name, int Age);

    private readonly int batchSize = 3;

    [Fact]
    public void Batch_WhenListIsEmpty_ShouldReturnEmptyEnumerable()
    {
        // Act
        var batched = Enumerable.Empty<int>().Batch(batchSize);

        // Assert
        batched.Should().BeEmpty();
    }

    [Fact]
    public void Batch_WhenSourceIsLargerThanBatchSize_ReturnsMultipleBatches()
    {
        // Arrange
        var oneToTen = Enumerable.Range(1, 10);

        // Act
        var batched = oneToTen.Batch(batchSize).ToArray();

        // Assert
        batched.Should().HaveCount(4);

        batched[0].Should().BeEquivalentTo([1, 2, 3]);
        batched[0].Should().HaveCount(3);

        batched[1].Should().BeEquivalentTo([4, 5, 6]);
        batched[1].Should().HaveCount(3);

        batched[2].Should().BeEquivalentTo([7, 8, 9]);
        batched[2].Should().HaveCount(3);

        batched[3].Should().BeEquivalentTo([10]);
        batched[3].Should().HaveCount(1);
    }

    [Fact]
    public void Batch_WhenSourceIsEqualToBatchSize_ReturnsSingleBatch()
    {
        // Arrange
        var oneToThree = Enumerable.Range(1, 3);

        // Act
        var batched = oneToThree.Batch(batchSize).ToArray();

        // Assert
        batched.Should().HaveCount(1);
        batched[0].Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void Batch_WhenSourceIsSmallerThanBatchSize_ReturnsSingleBatch()
    {
        // Arrange
        var oneToTen = Enumerable.Range(1, 2);

        // Act
        var batched = oneToTen.Batch(batchSize).ToArray();

        // Assert
        batched.Should().HaveCount(1);
        batched[0].Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void DistinctBy_WhenEmpty_ReturnsEmptyEnumerable()
    {
        // Arrange
        var empty = Enumerable.Empty<object>();

        // Act
        var result = EnumerableExtension.DistinctBy(empty, e => e);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DistinctBy_WhenNotEmpty_ReturnsCorrectEnumerable()
    {
        // Arrange
        var people = new[]
        {
            new { Id = 1, Name = "Alice" },
            new { Id = 2, Name = "Bob" },
            new { Id = 1, Name = "Aryan" },
            new { Id = 3, Name = "Charlie" }
        };

        // Act
        var result = EnumerableExtension.DistinctBy(people, p => p.Id).ToArray();

        // Assert
        result.Should().HaveCount(3);
        result.Where(p => p.Id == 1).Should().HaveCount(1);
        result.First(p => p.Id == 1).Should().BeEquivalentTo(people[0]);
        result.First(p => p.Id == 1).Should().NotBeEquivalentTo(people[3]);
    }

    [Fact]
    public void DistinctBy_WhenNotEmpty_ReturnsCorrectEnumerable2()
    {
        // Arrange
        var people = new[]
        {
            new { Id = 1, Name = "Alice", Age = 23 },
            new { Id = 2, Name = "Bob", Age = 27 },
            new { Id = 1, Name = "Aryan", Age = 22 },
            new { Id = 3, Name = "Charlie", Age = 45 }
        };

        // Act
        var result = EnumerableExtension.DistinctBy(people, p => new { p.Id, p.Age }).ToArray();

        // Assert
        result.Should().HaveCount(4);
        result.Where(p => p.Id == 1).Should().HaveCount(2);
    }
    
    [Fact]
    public void DistinctBy_EmptySource_ReturnsEmptyEnumerable()
    {
        // Arrange
        var source = Enumerable.Empty<Person>();

        // Act
        var result = EnumerableExtension.DistinctBy(source, p => p.Name).ToArray();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DistinctBy_AllDistinctElements_ReturnsAllElements()
    {
        // Arrange
        var source = new List<Person>
        {
            new("Alice", 25),
            new("Bob", 30),
            new("Aryan", 22)
        };

        // Act
        var result = EnumerableExtension.DistinctBy(source, p => p.Name).ToArray();

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(source);
    }

    [Fact]
    public void DistinctBy_WithDuplicates_ReturnsDistinctElements()
    {
        // Arrange
        var source = new List<Person>
        {
            new("Aryan", 22),
            new("Bob", 30),
            new("Aryan", 35),  // Duplicate Aryan
            new("Charlie", 40),
            new("Bob", 45)      // Duplicate Bob
        };
        
        // Act
        var result = EnumerableExtension.DistinctBy(source,  p => p.Name).ToArray();

        // Assert
        result.Should().HaveCount(3);
        result.Select(p => p.Name).Should().BeEquivalentTo("Aryan", "Bob", "Charlie");
    }

    [Fact]
    public void DistinctBy_WithCaseSensitiveStrings_RespectsCase()
    {
        // Arrange
        var source = new List<Person>
        {
            new("Aryan", 25),
            new("aryan", 30),  // Different case
            new("Bob", 35)
        };

        // Act
        var result = EnumerableExtension.DistinctBy(source, p => p.Name).ToArray();

        // Assert
        result.Should().HaveCount(3);
        result.Select(p => p.Name).Should().BeEquivalentTo("Aryan", "aryan", "Bob");
    }

    [Fact]
    public void DistinctBy_WithValueTypes_WorksCorrectly()
    {
        // Arrange
        var source = new List<Person>
        {
            new("Alice", 25),
            new("Bob", 25),     // Same age
            new("Charlie", 30),
            new("David", 25)    // Same age
        };

        // Act
        var result = EnumerableExtension.DistinctBy(source,  p => p.Age).ToArray();

        // Assert
        result.Should().HaveCount(2);
        result.Select(p => p.Age).Should().BeEquivalentTo([25, 30]);
    }

    [Fact]
    public void DistinctBy_LazyEvaluation_Preserved()
    {
        // Arrange
        var source = new List<Person>
        {
            new("Alice", 25),
            new("Bob", 30)
        };

        // Act
        var result = EnumerableExtension.DistinctBy(source, p => p.Name);

        // Assert
        result.Should().NotBeAssignableTo<List<Person>>();
        result.Should().BeAssignableTo<IEnumerable<Person>>();
    }

    [Fact]
    public void DistinctBy_WithCustomReferenceType_WorksCorrectly()
    {
        // Arrange
        var alice1 = new Person("Alice", 25);
        var alice2 = new Person("Alice", 25);  // Different instance but same values
        var bob = new Person("Bob", 30);
    
        var source = new List<Person> { alice1, alice2, bob };

        // Act
        var result = EnumerableExtension.DistinctBy(source, p => p).ToArray();

        // Assert
        // Records use value equality, so alice1 and alice2 are considered equal
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo([alice1, bob]);
    }
}