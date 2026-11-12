namespace ToolManager;

public sealed record RunnerMessage(string Kind, string Text = "", int Code = 0);
