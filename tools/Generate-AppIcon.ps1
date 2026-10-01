param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\MonitorCenter\Assets\MonitorCenter.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 48, 64, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 32.0
    $frameBounds = [System.Drawing.RectangleF]::new(2.5 * $scale, 4 * $scale, 27 * $scale, 18.5 * $scale)
    $outerPath = New-RoundedRectanglePath -Bounds $frameBounds -Radius (3.8 * $scale)
    $frameBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 32, 151, 216))
    $graphics.FillPath($frameBrush, $outerPath)

    $inset = 2.2 * $scale
    $screenBounds = [System.Drawing.RectangleF]::new(
        $frameBounds.Left + $inset,
        $frameBounds.Top + $inset,
        $frameBounds.Width - ($inset * 2),
        $frameBounds.Height - ($inset * 2))
    $screenPath = New-RoundedRectanglePath -Bounds $screenBounds -Radius (2.1 * $scale)
    $screenBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 12, 34, 48))
    $graphics.FillPath($screenBrush, $screenPath)

    $sunBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 203, 82))
    $sunSize = [Math]::Max(3.4 * $scale, 2.4)
    $sunX = 18.2 * $scale
    $sunY = 8.0 * $scale
    $graphics.FillEllipse($sunBrush, $sunX, $sunY, $sunSize, $sunSize)

    $standPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 32, 151, 216), [Math]::Max(2.1 * $scale, 1.1))
    $standPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $standPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($standPen, 16 * $scale, 22.2 * $scale, 16 * $scale, 27 * $scale)
    $graphics.DrawLine($standPen, 11.5 * $scale, 27.3 * $scale, 20.5 * $scale, 27.3 * $scale)

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($stream.ToArray())

    $stream.Dispose()
    $standPen.Dispose()
    $sunBrush.Dispose()
    $screenBrush.Dispose()
    $screenPath.Dispose()
    $frameBrush.Dispose()
    $outerPath.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$fileStream = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($fileStream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
for ($index = 0; $index -lt $frames.Count; $index++) {
    $size = $sizes[$index]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$index].Length
}

foreach ($frame in $frames) {
    $writer.Write($frame)
}

$writer.Dispose()
$fileStream.Dispose()

$icon = [System.Drawing.Icon]::new($resolvedOutput)
try {
    if ($icon.Width -le 0 -or $icon.Height -le 0) {
        throw "Generated icon could not be decoded."
    }
}
finally {
    $icon.Dispose()
}

Write-Output "Generated $resolvedOutput with $($frames.Count) sizes."
