using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class LazyTimeTickerChildrenTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Children_navigation_keeps_a_discoverable_backing_field(bool explicitFieldAccess)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var context = CreateContext(connection, explicitFieldAccess);
        var navigation = context.Model.FindEntityType(typeof(TimeTickerEntity))!
            .FindNavigation(nameof(TimeTickerEntity.Children))!;

        Assert.NotNull(navigation.FieldInfo);
        Assert.Equal("_children", navigation.FieldInfo!.Name);
        Assert.Equal(explicitFieldAccess ? PropertyAccessMode.Field : PropertyAccessMode.PreferField,
            navigation.GetPropertyAccessMode());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Include_and_relationship_fixup_preserve_independent_mutable_collections(bool explicitFieldAccess)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection, explicitFieldAccess);
        await context.Database.EnsureCreatedAsync();
        var firstRoot = Row();
        var firstChild = Row(firstRoot.Id);
        var grandchild = Row(firstChild.Id);
        var secondRoot = Row();
        var secondChild = Row(secondRoot.Id);
        // Do not initialize Children through its getter before EF tracks these rows.
        context.AddRange(firstRoot, firstChild, grandchild, secondRoot, secondChild);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var roots = await context.Set<TimeTickerEntity>().Where(row => row.ParentId == null)
            .Include(row => row.Children).ThenInclude(row => row.Children).ToArrayAsync();
        var loadedFirst = roots.Single(row => row.Id == firstRoot.Id);
        var loadedSecond = roots.Single(row => row.Id == secondRoot.Id);
        var loadedChild = Assert.Single(loadedFirst.Children);
        var loadedGrandchild = Assert.Single(loadedChild.Children);
        var loadedSecondChild = Assert.Single(loadedSecond.Children);

        Assert.Equal(firstChild.Id, loadedChild.Id);
        Assert.Equal(grandchild.Id, loadedGrandchild.Id);
        Assert.Equal(secondChild.Id, loadedSecondChild.Id);
        Assert.Same(loadedFirst, loadedChild.Parent);
        Assert.Same(loadedChild, loadedGrandchild.Parent);
        Assert.Same(loadedSecond, loadedSecondChild.Parent);
        Assert.NotSame(loadedFirst.Children, loadedSecond.Children);
        Assert.NotSame(loadedFirst.Children, loadedChild.Children);
        Assert.NotSame(loadedGrandchild.Children, loadedSecondChild.Children);
        Assert.Empty(loadedGrandchild.Children);
        Assert.Empty(loadedSecondChild.Children);

        var additionalChild = Row(loadedFirst.Id);
        context.Add(additionalChild);
        Assert.Same(loadedFirst, additionalChild.Parent);
        Assert.Contains(additionalChild, loadedFirst.Children);
        Assert.Single(loadedSecond.Children);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reread = await context.Set<TimeTickerEntity>().AsNoTracking()
            .Include(row => row.Children).ThenInclude(row => row.Children)
            .SingleAsync(row => row.Id == firstRoot.Id);
        Assert.Equal(new[] { firstChild.Id, additionalChild.Id }.Order(),
            reread.Children.Select(row => row.Id).Order());
        Assert.Equal(grandchild.Id, Assert.Single(reread.Children.Single(row => row.Id == firstChild.Id).Children).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Field_fixup_can_initialize_an_explicitly_null_children_collection(bool explicitFieldAccess)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection, explicitFieldAccess);
        await context.Database.EnsureCreatedAsync();
        var root = Row();
        var child = Row(root.Id);
        context.AddRange(root, child);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loadedRoot = await context.Set<TimeTickerEntity>().SingleAsync(row => row.Id == root.Id);
        loadedRoot.Children = null!;
        var loadedChild = await context.Set<TimeTickerEntity>().SingleAsync(row => row.Id == child.Id);

        Assert.Same(loadedRoot, loadedChild.Parent);
        Assert.Same(loadedChild, Assert.Single(loadedRoot.Children));
        var collectionInitializedByEf = loadedRoot.Children;
        Assert.Same(collectionInitializedByEf, loadedRoot.Children);
    }

    private static DbContext CreateContext(SqliteConnection connection, bool explicitFieldAccess)
        => explicitFieldAccess
            ? new FieldChildrenContext(new DbContextOptionsBuilder<FieldChildrenContext>().UseSqlite(connection).Options)
            : new DefaultChildrenContext(new DbContextOptionsBuilder<DefaultChildrenContext>().UseSqlite(connection).Options);

    private static TimeTickerEntity Row(Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(),
        ParentId = parentId,
        Function = "lazy-children",
        Request = [],
        ExecutionTime = parentId.HasValue ? null : DateTime.UtcNow.AddDays(1)
    };

    private class DefaultChildrenContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TimeTickerEntity>("ticker"));
    }

    // A separate context type keeps EF's model cache independent from the convention case.
    private sealed class FieldChildrenContext(DbContextOptions options) : DefaultChildrenContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TimeTickerEntity>().Navigation(row => row.Children)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
