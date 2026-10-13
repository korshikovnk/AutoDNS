param(
    [Parameter(Mandatory = $true)][string]$PngPath,
    [Parameter(Mandatory = $true)][string]$IcoPath
)

Add-Type -AssemblyName System.Drawing

$pngPath = (Resolve-Path $PngPath).Path
$src = [System.Drawing.Bitmap]::FromFile($pngPath)
$sizes = @(16, 32, 48, 256)
$blobs = New-Object System.Collections.Generic.List[byte[]]

try {
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.DrawImage($src, 0, 0, $size, $size)
        } finally {
            $g.Dispose()
        }

        $ms = New-Object System.IO.MemoryStream
        try {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $blobs.Add($ms.ToArray())
        } finally {
            $ms.Dispose()
            $bmp.Dispose()
        }
    }
} finally {
    $src.Dispose()
}

$fs = [System.IO.File]::Create($IcoPath)
$bw = New-Object System.IO.BinaryWriter $fs
try {
    $count = $blobs.Count
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)

    $offset = 6 + (16 * $count)
    for ($i = 0; $i -lt $count; $i++) {
        $size = $sizes[$i]
        $blob = $blobs[$i]
        $bw.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $bw.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$blob.Length)
        $bw.Write([uint32]$offset)
        $offset += $blob.Length
    }

    foreach ($blob in $blobs) {
        $bw.Write($blob)
    }
} finally {
    $bw.Dispose()
    $fs.Dispose()
}
