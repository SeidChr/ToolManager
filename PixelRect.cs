namespace ToolManager;

internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => this.Right - this.Left;
    public int Height => this.Bottom - this.Top;

    public bool Contains(PixelPoint p) => p.X >= this.Left && p.X < this.Right && p.Y >= this.Top && p.Y < this.Bottom;
}
