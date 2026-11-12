namespace ToolManager;

public sealed class RunRequest
{
    public const int MaximumInputCharacters = 4096;

    public string Root { get; set; } = string.Empty;
    public string Shell { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;

    public int Timeout
    { get; set; }

    public bool Interactive
    { get; set; }
}
