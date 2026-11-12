namespace ToolManager;

internal static class PlacementTests
{
    public static List<string> Run()
    {
        var results = new List<string>();
        void Assert(bool ok, string message)
        {
            if (!ok)
            {
                throw new Exception(message);
            }

            results.Add("PASS " + message);
        }

        foreach (var monitor in new[] { new PixelRect(0, 0, 1920, 1080), new PixelRect(-1920, -200, 0, 880), new PixelRect(1920, -1440, 4480, 0) })
        {
            foreach (var edge in Enum.GetValues<TaskbarEdge>())
            {
                foreach (var dpi in new uint[] { 96, 144, 192 })
                {
                    int thickness = (int)(48 * dpi / 96);
                    var bar = edge switch
                    {
                        TaskbarEdge.Left => monitor with { Right = monitor.Left + thickness },
                        TaskbarEdge.Top => monitor with { Bottom = monitor.Top + thickness },
                        TaskbarEdge.Right => monitor with { Left = monitor.Right - thickness },
                        _ => monitor with { Top = monitor.Bottom - thickness },
                    };
                    var anchor = new PixelPoint((bar.Left + bar.Right) / 2, (bar.Top + bar.Bottom) / 2);

                    // Deliberately use the whole monitor as work area: mods and auto-hide can leave this unchanged.
                    var location = new PopupLocation(monitor, monitor, bar, edge, anchor, dpi);
                    var popup = PopupPlacement.Calculate(location, (int)(620 * dpi / 96), (int)(430 * dpi / 96), (int)(8 * dpi / 96));
                    Assert(PopupPlacement.Edge(monitor, bar) == edge, $"Detect {edge} from actual taskbar rectangle at {dpi} DPI, monitor X={monitor.Left}");
                    Assert(popup.Left >= monitor.Left && popup.Right <= monitor.Right && popup.Top >= monitor.Top && popup.Bottom <= monitor.Bottom, "Popup stays entirely on clicked monitor");
                    bool overlaps = popup.Left < bar.Right && popup.Right > bar.Left && popup.Top < bar.Bottom && popup.Bottom > bar.Top;
                    Assert(!overlaps, "Popup never covers the detected taskbar");
                    bool adjacent = edge switch
                    {
                        TaskbarEdge.Left => popup.Left == bar.Right + (8 * dpi / 96),
                        TaskbarEdge.Top => popup.Top == bar.Bottom + (8 * dpi / 96),
                        TaskbarEdge.Right => popup.Right == bar.Left - (8 * dpi / 96),
                        _ => popup.Bottom == bar.Top - (8 * dpi / 96),
                    };
                    Assert(adjacent, "Popup sits next to the actual taskbar edge");
                }
            }
        }

        var small = new PixelRect(-800, 0, 0, 600);
        var top = new PixelRect(-800, 0, 0, 60);
        var clipped = PopupPlacement.Calculate(new(small, small, top, TaskbarEdge.Top, new(-799, 30), 96), 1800, 1200, 8);
        Assert(clipped.Left >= -800 && clipped.Right <= 0 && clipped.Top >= 68 && clipped.Bottom <= 600, "Oversized popup and edge anchor fit available monitor space");
        var inset = new PixelRect(0, 0, 1920, 1040);
        var placed = PopupPlacement.Calculate(new(new(0, 0, 1920, 1080), inset, new(0, 0, 1920, 60), TaskbarEdge.Top, new(1900, 30), 96), 620, 430, 8);
        Assert(placed.Top == 68 && placed.Bottom <= 1040 && placed.Right <= 1912, "Moved top taskbar wins over stale bottom work-area inset");
        var actual = PopupPlacement.Detect(PopupPlacement.Cursor());
        Assert(actual.Monitor.Contains(actual.Anchor), "Live Windows monitor detection uses the cursor's monitor");
        results.Add($"OBSERVED Taskbar edge={actual.Edge}, bounds={actual.Taskbar}, monitor={actual.Monitor}, work={actual.Work}, DPI={actual.Dpi}");
        return results;
    }
}
