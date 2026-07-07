using System;
using TickerQ.Dashboard;
using Xunit;

namespace TickerQ.Tests;

public class DashboardOptionsBuilderTests
{
    [Fact]
    public void AddHeaderButton_NullConfigure_ThrowsArgumentNullException()
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.AddHeaderButton(null!));
    }

    [Fact]
    public void AddHeaderButton_EmptyLabel_ThrowsArgumentException()
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddHeaderButton(b =>
        {
            b.Label = "";
            b.Href = "/dashboard";
        }));
    }

    [Fact]
    public void AddHeaderButton_WhitespaceLabel_ThrowsArgumentException()
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddHeaderButton(b =>
        {
            b.Label = "   ";
            b.Href = "/dashboard";
        }));
    }

    [Fact]
    public void AddHeaderButton_EmptyHref_ThrowsArgumentException()
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddHeaderButton(b =>
        {
            b.Label = "My Link";
            b.Href = "";
        }));
    }

    [Fact]
    public void AddHeaderButton_ProtocolRelativeUrl_ThrowsArgumentException()
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddHeaderButton(b =>
        {
            b.Label = "My Link";
            b.Href = "//example.com";
        }));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("data:text/html,<h1>xss</h1>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("\njavascript:alert(1)")]
    [InlineData("\tjavascript:alert(1)")]
    [InlineData("javascript:alert(1) ")]
    public void AddHeaderButton_UnsafeScheme_ThrowsArgumentException(string href)
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddHeaderButton(b =>
        {
            b.Label = "My Link";
            b.Href = href;
        }));
    }

    [Theory]
    [InlineData("no-leading-slash")]
    [InlineData("path/to/page")]
    [InlineData("%6Aavascript:alert(1)")]
    [InlineData(" /valid-path")]
    public void AddHeaderButton_InvalidRelativePath_ThrowsArgumentException(string href)
    {
        var builder = new DashboardOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddHeaderButton(b =>
        {
            b.Label = "My Link";
            b.Href = href;
        }));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("mailto:support@example.com")]
    [InlineData("tel:+15551234567")]
    [InlineData("/my-dashboard")]
    [InlineData("./relative-path")]
    public void AddHeaderButton_ValidHref_AddsButton(string href)
    {
        var builder = new DashboardOptionsBuilder();

        builder.AddHeaderButton(b =>
        {
            b.Label = "My Link";
            b.Href = href;
        });

        var button = Assert.Single(builder.HeaderButtons);
        Assert.Equal("My Link", button.Label);
        Assert.Equal(href, button.Href);
    }

    [Fact]
    public void AddHeaderButton_SetsButtonProperties()
    {
        var builder = new DashboardOptionsBuilder();

        builder.AddHeaderButton(b =>
        {
            b.Label = "Visit Website";
            b.Href = "https://example.com";
            b.Icon = "mdi-web";
            b.OpenInNewTab = true;
            b.Tooltip = "Open Website";
        });

        var button = Assert.Single(builder.HeaderButtons);
        Assert.Equal("Visit Website", button.Label);
        Assert.Equal("https://example.com", button.Href);
        Assert.Equal("mdi-web", button.Icon);
        Assert.True(button.OpenInNewTab);
        Assert.Equal("Open Website", button.Tooltip);
    }

    [Fact]
    public void AddHeaderButton_MultipleButtons_AllAdded()
    {
        var builder = new DashboardOptionsBuilder();

        builder
            .AddHeaderButton(b => { b.Label = "One"; b.Href = "/one"; })
            .AddHeaderButton(b => { b.Label = "Two"; b.Href = "/two"; })
            .AddHeaderButton(b => { b.Label = "Three"; b.Href = "/three"; });

        Assert.Equal(3, builder.HeaderButtons.Count);
    }

    [Fact]
    public void AddHeaderButton_ReturnsBuilderInstance_ForChaining()
    {
        var builder = new DashboardOptionsBuilder();

        var result = builder.AddHeaderButton(b => { b.Label = "Link"; b.Href = "/link"; });

        Assert.Same(builder, result);
    }
}
