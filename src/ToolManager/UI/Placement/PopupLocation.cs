namespace ToolManager.UI.Placement;

internal readonly record struct PopupLocation(PixelRect Monitor, PixelRect Work, PixelRect? Taskbar, TaskbarEdge Edge, PixelPoint Anchor, uint Dpi);
