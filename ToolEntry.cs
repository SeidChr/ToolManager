namespace ToolManager;

public sealed class ToolEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New tool";
    public string Mode { get; set; } = "Installed";
    public string Behavior { get; set; } = "Managed";
    public string ShellId { get; set; } = "pwsh";

    public bool Interactive
    { get; set; }

    public string Command { get; set; } = string.Empty;
    public string StopCommand { get; set; } = string.Empty;
    public string StatusCommand { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = ".";

    public int? StopTimeoutSeconds
    { get; set; }

    public bool RestrictToPcs
    { get; set; }

    public List<string> PcNames { get; set; } = [];
    public bool Visible => !this.RestrictToPcs || this.PcNames.Any(n => string.Equals(n.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => this.Name + (this.Visible ? string.Empty : " (hidden on this PC)");
}
