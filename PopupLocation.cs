namespace ToolManager;

internal readonly record struct PopupLocation(PixelRect Monitor, PixelRect Work, PixelRect? Taskbar, TaskbarEdge Edge, PixelPoint Anchor, uint Dpi);
