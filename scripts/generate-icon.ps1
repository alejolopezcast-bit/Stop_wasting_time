<#
.SYNOPSIS
    Draws the Stop Wasting Time application icon: Assets/app.ico for Windows, Assets/logo.png for the UI.

.DESCRIPTION
    The icon is generated instead of checked in as an opaque binary, so the shape and the palette stay
    editable and reviewable. It draws a dark rounded tile, an accent coloured timer ring with a gap, and
    a white stop square in the middle, then packs 16/32/48/64/96/128/256 px PNG frames into a single .ico.

    The window and the tray need an .ico, while WPF renders a PNG far more sharply inside the app, so
    both come out of the same drawing code.

    Run it after changing the palette:  powershell -ExecutionPolicy Bypass -File scripts/generate-icon.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $OutputPath) {
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
    $OutputPath = Join-Path $repositoryRoot 'src\StopWastingTime.App\Assets\app.ico'
}

Add-Type -AssemblyName System.Drawing

$background = [System.Drawing.ColorTranslator]::FromHtml('#151823')
$accent     = [System.Drawing.ColorTranslator]::FromHtml('#5B8CFF')
$foreground = [System.Drawing.ColorTranslator]::FromHtml('#F2F4F8')

function New-IconFrame {
    param([int] $Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        # Rounded dark tile.
        $radius = [Math]::Max(2, [int]($Size * 0.22))
        $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
        $tile.AddArc(0, 0, $radius * 2, $radius * 2, 180, 90)
        $tile.AddArc($Size - $radius * 2 - 1, 0, $radius * 2, $radius * 2, 270, 90)
        $tile.AddArc($Size - $radius * 2 - 1, $Size - $radius * 2 - 1, $radius * 2, $radius * 2, 0, 90)
        $tile.AddArc(0, $Size - $radius * 2 - 1, $radius * 2, $radius * 2, 90, 90)
        $tile.CloseFigure()

        $tileBrush = New-Object System.Drawing.SolidBrush($background)
        $graphics.FillPath($tileBrush, $tile)
        $tileBrush.Dispose()
        $tile.Dispose()

        # Timer ring, open at the top so it reads as "time running".
        $thickness = [Math]::Max(1.5, $Size * 0.09)
        $inset = $Size * 0.2
        # Sizes are computed first: inline arithmetic inside a New-Object argument list would be
        # parsed as an array expression.
        $ringSize = $Size - ($inset * 2)
        $ringRect = New-Object System.Drawing.RectangleF -ArgumentList $inset, $inset, $ringSize, $ringSize
        $ringPen = New-Object System.Drawing.Pen($accent, $thickness)
        $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($ringPen, $ringRect, -60, 300)
        $ringPen.Dispose()

        # Stop square in the middle.
        $squareSize = $Size * 0.3
        $offset = ($Size - $squareSize) / 2
        $squareRect = New-Object System.Drawing.RectangleF -ArgumentList $offset, $offset, $squareSize, $squareSize
        $squareBrush = New-Object System.Drawing.SolidBrush($foreground)
        $graphics.FillRectangle($squareBrush, $squareRect)
        $squareBrush.Dispose()
    }
    finally {
        $graphics.Dispose()
    }

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    # The leading comma keeps PowerShell from unrolling the byte array into the pipeline.
    return ,$stream.ToArray()
}

$sizes = @(16, 32, 48, 64, 96, 128, 256)
$frames = @{}
foreach ($size in $sizes) {
    $frames[$size] = New-IconFrame -Size $size
}

# ICO container: 6 byte header, one 16 byte directory entry per frame, then the PNG payloads.
$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($output)
try {
    $writer.Write([UInt16] 0)               # reserved
    $writer.Write([UInt16] 1)               # type: icon
    $writer.Write([UInt16] $sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    foreach ($size in $sizes) {
        $bytes = [byte[]] $frames[$size]
        $writer.Write([Byte] ($(if ($size -ge 256) { 0 } else { $size })))  # width, 0 means 256
        $writer.Write([Byte] ($(if ($size -ge 256) { 0 } else { $size })))  # height
        $writer.Write([Byte] 0)             # palette size
        $writer.Write([Byte] 0)             # reserved
        $writer.Write([UInt16] 1)           # colour planes
        $writer.Write([UInt16] 32)          # bits per pixel
        $writer.Write([UInt32] $bytes.Length)
        $writer.Write([UInt32] $offset)
        $offset += $bytes.Length
    }

    foreach ($size in $sizes) {
        $writer.Write([byte[]] $frames[$size])
    }

    $writer.Flush()

    $directory = Split-Path -Parent $OutputPath
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    [System.IO.File]::WriteAllBytes($OutputPath, $output.ToArray())
}
finally {
    $writer.Dispose()
    $output.Dispose()
}

$resolved = (Resolve-Path $OutputPath).Path
Write-Host "Icon written to $resolved ($((Get-Item $resolved).Length) bytes)"

# The same artwork as a PNG: WPF scales an .ico frame poorly, so the in app logo uses this instead.
$pngPath = Join-Path (Split-Path -Parent $resolved) 'logo.png'
[System.IO.File]::WriteAllBytes($pngPath, [byte[]] $frames[256])
Write-Host "Logo written to $pngPath ($((Get-Item $pngPath).Length) bytes)"
