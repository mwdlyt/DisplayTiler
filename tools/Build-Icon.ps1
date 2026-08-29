<#
.SYNOPSIS
    Builds the multi-resolution DisplayTiler.ico from the source PNG.

.DESCRIPTION
    Windows picks a different image out of an .ico for the taskbar, the notification area, Alt+Tab,
    Explorer's tile views and the file properties dialog. Shipping a single 256px image forces the
    shell to downscale on the fly, which is what makes an otherwise crisp icon look muddy at 16px in
    the notification area - exactly where this app lives. So every size the shell asks for is encoded
    explicitly, each resampled from the full-resolution original rather than from a smaller step.

    Entries are stored as PNG. That has been supported since Windows Vista, keeps the file small, and
    preserves the straight alpha the source has.

.NOTES
    Re-run only when the artwork changes; the generated .ico is committed.
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\assets\DisplayTiler.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\native\DisplayTiler.Host\DisplayTiler.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Source = (Resolve-Path $Source).Path
$Destination = [System.IO.Path]::GetFullPath($Destination)
Write-Host "source      : $Source"
Write-Host "destination : $Destination"

# 16-48 are shell/tray sizes, 64-256 are Explorer's larger tile views and the properties dialog.
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

$original = [System.Drawing.Image]::FromFile($Source)
try {
    if ($original.Width -ne $original.Height) {
        Write-Warning "source is $($original.Width)x$($original.Height); a square image gives better results"
    }

    $images = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        # Always resample from the original, never from the previous (already softened) step.
        $g.DrawImage($original, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $images += , @{ Size = $size; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }
}
finally {
    $original.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
# ICONDIR
$w.Write([uint16]0)               # reserved
$w.Write([uint16]1)               # type: 1 = icon
$w.Write([uint16]$images.Count)
# ICONDIRENTRY table. Image data begins after the header plus one 16-byte entry per image.
$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }   # 0 encodes 256 in this field
    $w.Write([byte]$dim)          # width
    $w.Write([byte]$dim)          # height
    $w.Write([byte]0)             # palette entries: 0 for truecolour
    $w.Write([byte]0)             # reserved
    $w.Write([uint16]1)           # colour planes
    $w.Write([uint16]32)          # bits per pixel
    $w.Write([uint32]$image.Bytes.Length)
    $w.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $w.Write($image.Bytes) }
$w.Flush()

$directory = Split-Path $Destination -Parent
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
[System.IO.File]::WriteAllBytes($Destination, $out.ToArray())
$w.Dispose(); $out.Dispose()

$written = Get-Item $Destination
Write-Host ("wrote {0} bytes, {1} images: {2}" -f $written.Length, $images.Count, ($sizes -join ', '))
