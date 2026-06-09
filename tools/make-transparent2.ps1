param(
    [Parameter(Mandatory=$true)][string]$InPath,
    [Parameter(Mandatory=$true)][string]$OutPath,
    [int]$Master = 512,
    [double]$Tol = 95.0,        # color distance from light bg
    [double]$Edge = 16.0,       # luminance-gradient barrier; flood cannot cross stronger edges
    [int]$RefR = 254, [int]$RefG = 252, [int]$RefB = 250
)
Add-Type -AssemblyName System.Drawing
$M = [int]$Master

$img = [System.Drawing.Image]::FromFile($InPath)
$canvas = New-Object System.Drawing.Bitmap($M, $M, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gfx = [System.Drawing.Graphics]::FromImage($canvas)
$gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gfx.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$gfx.DrawImage($img, (New-Object System.Drawing.Rectangle 0,0,$M,$M))
$gfx.Dispose(); $img.Dispose()

$lockRect = New-Object System.Drawing.Rectangle 0,0,$M,$M
$data = $canvas.LockBits($lockRect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = [int]$data.Stride
$total = $stride * $M
$bytes = New-Object byte[] $total
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $total)

$N = $M * $M
# luminance per pixel
$lum = New-Object 'single[]' $N
for ($p = 0; $p -lt $N; $p++) {
    $px = $p % $M; $py = [int](($p - $px) / $M)
    $idx = ($py * $stride) + ($px * 4)
    $lum[$p] = (0.114 * $bytes[$idx]) + (0.587 * $bytes[$idx+1]) + (0.299 * $bytes[$idx+2])
}
# Sobel gradient magnitude
$grad = New-Object 'single[]' $N
for ($y = 1; $y -lt $M - 1; $y++) {
    for ($x = 1; $x -lt $M - 1; $x++) {
        $p = $y * $M + $x
        $tl=$lum[$p-$M-1]; $tc=$lum[$p-$M]; $tr=$lum[$p-$M+1]
        $ml=$lum[$p-1];                     $mr=$lum[$p+1]
        $bl=$lum[$p+$M-1]; $bc=$lum[$p+$M]; $br=$lum[$p+$M+1]
        $gx = ($tr + 2*$mr + $br) - ($tl + 2*$ml + $bl)
        $gy = ($bl + 2*$bc + $br) - ($tl + 2*$tc + $tr)
        $grad[$p] = [math]::Sqrt(($gx*$gx) + ($gy*$gy))
    }
}

$tol2 = [double]($Tol * $Tol)
$visited = New-Object 'bool[]' $N
$stack = New-Object System.Collections.Generic.Stack[int]
for ($x = 0; $x -lt $M; $x++) { $stack.Push($x); $stack.Push((($M-1)*$M)+$x) }
for ($y = 0; $y -lt $M; $y++) { $stack.Push($y*$M); $stack.Push(($y*$M)+($M-1)) }

$cleared = 0
while ($stack.Count -gt 0) {
    $p = $stack.Pop()
    if ($visited[$p]) { continue }
    $visited[$p] = $true
    $px = $p % $M; $py = [int](($p - $px) / $M)
    $idx = ($py * $stride) + ($px * 4)
    if ($bytes[$idx+3] -eq 0) { continue }
    # color test
    $dr = [int]$bytes[$idx+2] - $RefR; $dg = [int]$bytes[$idx+1] - $RefG; $db = [int]$bytes[$idx] - $RefB
    if ((($dr*$dr)+($dg*$dg)+($db*$db)) -gt $tol2) { continue }
    # edge barrier: do not cross strong luminance edges
    if ($grad[$p] -gt $Edge) { continue }
    $bytes[$idx+3] = 0
    $cleared++
    if ($px -gt 0)      { $n=$p-1;  if (-not $visited[$n]) { $stack.Push($n) } }
    if ($px -lt $M-1)   { $n=$p+1;  if (-not $visited[$n]) { $stack.Push($n) } }
    if ($py -gt 0)      { $n=$p-$M; if (-not $visited[$n]) { $stack.Push($n) } }
    if ($py -lt $M-1)   { $n=$p+$M; if (-not $visited[$n]) { $stack.Push($n) } }
}

[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $total)
$canvas.UnlockBits($data)
$canvas.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Write-Output "edge-barrier transparent: $OutPath (tol=$Tol edge=$Edge cleared=$cleared)"
