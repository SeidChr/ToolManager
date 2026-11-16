[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
function New-RoundedPath([single]$x,[single]$y,[single]$width,[single]$height,[single]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = 2 * $radius
    $path.AddArc($x,$y,$diameter,$diameter,180,90)
    $path.AddArc(($x+$width-$diameter),$y,$diameter,$diameter,270,90)
    $path.AddArc(($x+$width-$diameter),($y+$height-$diameter),$diameter,$diameter,0,90)
    $path.AddArc($x,($y+$height-$diameter),$diameter,$diameter,90,90)
    $path.CloseFigure()
    return $path
}
$images = [Collections.Generic.List[byte[]]]::new()
$iconSizes = @(16,20,24,32,40,48,64,128,256)
foreach ($iconSize in $iconSizes) {
    $bitmap = [Drawing.Bitmap]::new($iconSize,$iconSize,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($iconSize/64.0),($iconSize/64.0))
    $base = New-RoundedPath 0 0 64 64 14
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0),[Drawing.Point]::new(64,64),[Drawing.ColorTranslator]::FromHtml('#172554'),[Drawing.ColorTranslator]::FromHtml('#0f172a'))
    $graphics.FillPath($gradient,$base)
    foreach ($item in @(@(10,10,44,44,9,'#2563eb'),@(14,15,36,34,5,'#eff6ff'))) {
        $path = New-RoundedPath $item[0] $item[1] $item[2] $item[3] $item[4]
        $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($item[5]))
        $graphics.FillPath($brush,$path); $brush.Dispose(); $path.Dispose()
    }
    $blue = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#3b82f6'))
    foreach ($row in @(24,34,43)) { $graphics.FillEllipse($blue,[single]18,[single]($row-3),[single]6,[single]6) }
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#64748b'),[single]3)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($pen,[single]29,[single]24,[single]42,[single]24)
    $graphics.DrawLine($pen,[single]29,[single]34,[single]42,[single]34)
    $graphics.DrawLine($pen,[single]29,[single]43,[single]35,[single]43)
    $navy = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#172554'))
    $green = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#22c55e'))
    $graphics.FillEllipse($navy,[single]35,[single]34,[single]26,[single]26)
    $graphics.FillEllipse($green,[single]38,[single]37,[single]20,[single]20)
    $graphics.FillPolygon([Drawing.Brushes]::White,[Drawing.PointF[]]@([Drawing.PointF]::new(45,41),[Drawing.PointF]::new(54,47),[Drawing.PointF]::new(45,53)))
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    if ($iconSize -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'ToolManager.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $base.Dispose(); $gradient.Dispose(); $blue.Dispose(); $pen.Dispose(); $navy.Dispose(); $green.Dispose()
}
$target = [IO.File]::Create((Join-Path $PSScriptRoot 'ToolManager.ico'))
$writer = [IO.BinaryWriter]::new($target)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    for ($index=0; $index -lt $images.Count; $index++) {
        $dimension = if ($iconSizes[$index] -eq 256) { 0 } else { $iconSizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($bytes in $images) { $writer.Write($bytes) }
} finally { $writer.Dispose() }
