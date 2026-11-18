$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(36, 83, 166))
    $scale = $size / 256.0
    $graphics.ScaleTransform($scale, $scale)
    $paper = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(36, 83, 166), 9)
    $graphics.FillRectangle($paper, 43, 45, 170, 166)
    $graphics.DrawLine($line, 128, 56, 128, 200)
    foreach ($y in @(87, 118, 149)) {
        $graphics.DrawLine($line, 62, $y, 108, $y)
        $graphics.DrawLine($line, 148, $y, 194, $y)
    }
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size = $size; Bytes = $stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save("$PSScriptRoot\placeholder.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose()
    $line.Dispose()
    $paper.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
$file = [System.IO.File]::Create("$PSScriptRoot\placeholder.ico")
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
