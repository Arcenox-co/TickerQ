namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class SampleMigrationRollbackContractTests
{
    [Fact]
    public void Sample_store_upgrade_migrations_reject_destructive_down()
    {
        var root = FindRepositoryRoot();
        var relativePaths = new[]
        {
            "samples/TickerQ.Sample.ApplicationDbContext/Migrations/20261024223434_UpgradeTickerQStore.cs",
            "samples/TickerQ.Sample.Console/Migrations/20261024223427_UpgradeTickerQStore.cs",
            "samples/TickerQ.Sample.WebApi/Migrations/20261024223431_UpgradeTickerQStore.cs"
        };

        foreach (var relativePath in relativePaths)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            var down = source[source.IndexOf("protected override void Down", StringComparison.Ordinal)..];
            Assert.Contains("throw new NotSupportedException", down, StringComparison.Ordinal);
            Assert.True(down.IndexOf("throw new NotSupportedException", StringComparison.Ordinal) <
                        down.IndexOf("DropTable", StringComparison.Ordinal));
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TickerQ.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
