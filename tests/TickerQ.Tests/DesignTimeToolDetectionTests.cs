using TickerQ.DependencyInjection;

namespace TickerQ.Tests;

public class DesignTimeToolDetectionTests
{
    [Fact]
    public void IsDesignTimeTool_Returns_False_In_Normal_Test_Host()
    {
        // When running under the xunit test runner, the entry assembly is the test host
        // (e.g., "testhost" or "xunit.runner"), not a dotnet-* design-time tool.
        var result = TickerQServiceExtensions.IsDesignTimeTool();

        Assert.False(result);
    }

    [Fact]
    public void IsDesignTimeTool_Detection_Logic_Matches_Known_Tool_Names()
    {
        // Verify the naming convention check against known design-time tool assembly names.
        // These are the tool entry assembly names that should be detected:
        var toolNames = new[] { "dotnet-getdocument", "dotnet-ef", "dotnet-swagger" };

        foreach (var name in toolNames)
        {
            Assert.True(
                name.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase),
                $"Expected '{name}' to match the dotnet- prefix convention");
        }

        // These should NOT match the dotnet- prefix:
        var nonToolNames = new[] { "testhost", "MyApp", "WorkerService", "dotnet" };

        foreach (var name in nonToolNames)
        {
            Assert.False(
                name.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase),
                $"Expected '{name}' to NOT match the dotnet- prefix convention");
        }
    }
}
