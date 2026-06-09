param(
    [Parameter(Mandatory=$true)][string]$Src,
    [Parameter(Mandatory=$true)][string]$IcoOut
)
Add-Type -AssemblyName System.Drawing

$srcBmp = [System.Drawing.Bitmap]::FromFile($Src)
$sizes = @(16,24,32,48,64,128,256)

# Render each size to a PNG byte[] using high-quality bicubic.
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality= [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($srcBmp, (New-Object System.Drawing.Rectangle(0,0,$s,$s)))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
}
$srcBmp.Dispose()

# Assemble ICO container (PNG-compressed frames; supported Vista+).
$out = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter($out)
$count = $sizes.Count
$bw.Write([UInt16]0)        # reserved
$bw.Write([UInt16]1)        # type = icon
$bw.Write([UInt16]$count)   # image count

$offset = 6 + (16 * $count) # header + dir entries
for ($i=0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256
    $bw.Write([Byte]$dim)   # width
    $bw.Write([Byte]$dim)   # height
    $bw.Write([Byte]0)      # palette
    $bw.Write([Byte]0)      # reserved
    $bw.Write([UInt16]1)    # color planes
    $bw.Write([UInt16]32)   # bits per pixel
    $bw.Write([UInt32]$len) # data size
    $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($IcoOut, $out.ToArray())
$bw.Dispose(); $out.Dispose()
"ICO written: $IcoOut ($([System.IO.FileInfo]::new($IcoOut).Length) bytes, sizes: $($sizes -join ','))"
