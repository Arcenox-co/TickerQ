namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class SampleMigrationRollbackContractTests
{
    [Fact]
    public void Professional_sample_migrations_reject_destructive_down()
    {
        var root = FindRepositoryRoot();
        var relativePaths = new[]
        {
            "samples/TickerQ.Sample.Console/Migrations/20260730141338_ProfessionalTickerStoreUpgrade.cs",
            "samples/TickerQ.Sample.WebApi/Migrations/20260730141346_ProfessionalTickerStoreUpgrade.cs",
            "samples/TickerQ.Sample.ApplicationDbContext/Migrations/20260730141353_ProfessionalTickerStoreUpgrade.cs"
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
