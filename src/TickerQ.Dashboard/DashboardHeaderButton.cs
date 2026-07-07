namespace TickerQ.Dashboard;

/// <summary>Defines a custom button rendered in the TickerQ dashboard header.</summary>
public sealed class DashboardHeaderButton
{
    public string? Label { get; set; }
    public string? Icon { get; set; }
    public string? Href { get; set; }
    public bool OpenInNewTab { get; set; }
    public string? Tooltip { get; set; }
}
