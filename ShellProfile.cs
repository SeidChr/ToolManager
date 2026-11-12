namespace ToolManager;

public sealed class ShellProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "PowerShell 7";
    public string Executable { get; set; } = "pwsh.exe";
    public string Kind { get; set; } = "PowerShell";
    public bool LoadProfile { get; set; } = true;

    public override string ToString() => this.Name;
}
