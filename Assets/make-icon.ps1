# Generates Assets/app.ico (16/32/48/256 PNG-compressed entries) from Assets/logo.png:
# white background keyed to transparent, autocropped square with padding.
# Usage: powershell -ExecutionPolicy Bypass -File Assets/make-icon.ps1
param(
    [string]$Src = (Join-Path $PSScriptRoot "logo.png"),
    [string]$Dst = (Join-Path $PSScriptRoot "app.ico")
)
Add-Type -AssemblyName System.Drawing

function Get-KeyedSquare([System.Drawing.Bitmap]$src) {
    $w = $src.Width; $h = $src.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($data.Stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $minX = $w; $minY = $h; $maxX = -1; $maxY = -1
        for ($y = 0; $y -lt $h; $y++) {
            for ($x = 0; $x -lt $w; $x++) {
                $i = $y * $data.Stride + $x * 4
                $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
                if ([Math]::Min($r, [Math]::Min($g, $b)) -lt 200) {
                    # key to transparent later; track dark bounds
                    if ($x -lt $minX) { $minX = $x }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($y -gt $maxY) { $maxY = $y }
                }
                else { $bytes[$i + 3] = 0 }
            }
        }
        [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    }
    finally { $src.UnlockBits($data) }
    if ($maxX -lt 0) { throw "No dark pixels found in $Src" }
    $pad = [int]([Math]::Max($maxX - $minX, $maxY - $minY) * 0.06) + 2
    $side = [Math]::Max($maxX - $minX, $maxY - $minY) + $pad * 2
    $cx = ($minX + $maxX) / 2; $cy = ($minY + $maxY) / 2
    $sx = [int][Math]::Max(0, $cx - $side / 2); $sy = [int][Math]::Max(0, $cy - $side / 2)
    if ($sx + $side -gt $w) { $sx = $w - $side }
    if ($sy + $side -gt $h) { $sy = $h - $side }
    if ($sx -lt 0 -or $sy -lt 0 -or $side -gt $w -or $side -gt $h) {
        # source smaller than padded square: center on transparent canvas
        $side = [int][Math]::Max($w, $h)
        $canvas = New-Object System.Drawing.Bitmap($side, $side)
        $gg = [System.Drawing.Graphics]::FromImage($canvas)
        $gg.Clear([System.Drawing.Color]::Transparent)
        $gg.DrawImage($src, [int](($side - $w) / 2), [int](($side - $h) / 2), $w, $h)
        $gg.Dispose()
        return $canvas
    }
    return $src.Clone((New-Object System.Drawing.Rectangle($sx, $sy, [int]$side, [int]$side)),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

$srcBmp = [System.Drawing.Bitmap]::FromFile($Src)
try {
    $square = Get-KeyedSquare $srcBmp
    try {
        $payloads = @()
        foreach ($size in @(256, 48, 32, 16)) {
            $thumb = New-Object System.Drawing.Bitmap($size, $size)
            $gg = [System.Drawing.Graphics]::FromImage($thumb)
            $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $gg.Clear([System.Drawing.Color]::Transparent)
            $gg.DrawImage($square, 0, 0, $size, $size)
            $gg.Dispose()
            $ms = New-Object System.IO.MemoryStream
            $thumb.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $thumb.Dispose()
            $payloads += @(, $ms.ToArray())
            $ms.Dispose()
        }
        $fs = [System.IO.File]::Create($Dst)
        try {
            $bw = New-Object System.IO.BinaryWriter($fs)
            $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$payloads.Count)
            $offset = 6 + 16 * $payloads.Count
            foreach ($p in $payloads) {
                $dim = if ($p.Length -gt 0 -and $true) { 0 } else { 0 }
                $bw.Write([byte]0); $bw.Write([byte]0) # placeholder, fixed below
                $bw.Write([byte]0); $bw.Write([byte]0)
                $bw.Write([uint16]1); $bw.Write([uint16]32)
                $bw.Write([uint32]$p.Length); $bw.Write([uint32]$offset)
                $offset += $p.Length
            }
            # fix width/height bytes (0 == 256)
            $sizes = @(256, 48, 32, 16)
            for ($i = 0; $i -lt $payloads.Count; $i++) {
                $fs.Position = 6 + 16 * $i
                $v = if ($sizes[$i] -ge 256) { 0 } else { [byte]$sizes[$i] }
                $fs.WriteByte($v); $fs.WriteByte($v)
            }
            $fs.Position = 6 + 16 * $payloads.Count
            foreach ($p in $payloads) { $fs.Write($p, 0, $p.Length) }
            $bw.Close()
        }
        finally { $fs.Close() }
        Write-Output "Wrote $Dst"
    }
    finally { $square.Dispose() }
}
finally { $srcBmp.Dispose() }
