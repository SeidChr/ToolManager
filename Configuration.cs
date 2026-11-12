namespace ToolManager;

public sealed class Configuration
{
    public int Version { get; set; } = 1;
    public int StopTimeoutSeconds { get; set; } = 10;
    public int HistoryCount { get; set; } = 10;
    public int OutputLimitKiB { get; set; } = 2048;
    public int GlobalOutputLimitMiB { get; set; } = 64;
    public string DefaultShellId { get; set; } = "pwsh";

    public List<ShellProfile> Shells
    {
        get;
        set;
    } = [
        new() { Id = "pwsh" },
        new()
        {
            Id = "powershell",
            Name = "Windows PowerShell",
            Executable = "powershell.exe",
        },
        new()
        {
            Id = "cmd",
            Name = "Command Prompt",
            Executable = "cmd.exe",
            Kind = "CMD",
        },
    ];

    public List<ToolEntry> Tools { get; set; } = [];
}
