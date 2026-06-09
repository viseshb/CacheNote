param(
    [Parameter(Mandatory=$true)][string]$InPath,
    [Parameter(Mandatory=$true)][string]$OutPath,
    [int]$Master = 512,
    [double]$Tol = 70.0,
    [int]$RefR = 254, [int]$RefG = 252, [int]$RefB = 250
)
Add-Type -AssemblyName System.Drawing
$M = [int]$Master

# 1) Resize source to a square ARGB master.
$img = [System.Drawing.Image]::FromFile($InPath)
$canvas = New-Object System.Drawing.Bitmap($M, $M, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gfx = [System.Drawing.Graphics]::FromImage($canvas)
$gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gfx.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$destRect = New-Object System.Drawing.Rectangle 0,0,$M,$M
$gfx.DrawImage($img, $destRect)
$gfx.Dispose()
$img.Dispose()

# 2) LockBits -> raw BGRA bytes.
$lockRect = New-Object System.Drawing.Rectangle 0,0,$M,$M
$data = $canvas.LockBits($lockRect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = [int]$data.Stride
$total = $stride * $M
$bytes = New-Object byte[] $total
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $total)

$tol2 = [double]($Tol * $Tol)

# 3) BFS flood from every border pixel that matches the light background.
$visited = New-Object 'bool[]' ($M * $M)
$stack = New-Object System.Collections.Generic.Stack[int]
for ($x = 0; $x -lt $M; $x++) {
    $stack.Push($x)                 # top row
    $stack.Push((($M - 1) * $M) + $x)  # bottom row
}
for ($y = 0; $y -lt $M; $y++) {
    $stack.Push($y * $M)            # left col
    $stack.Push(($y * $M) + ($M - 1))  # right col
}
$cleared = 0
while ($stack.Count -gt 0) {
    $p = $stack.Pop()
    if ($visited[$p]) { continue }
    $visited[$p] = $true
    $px = $p % $M
    $py = [int](($p - $px) / $M)
    $idx = ($py * $stride) + ($px * 4)
    if ($bytes[$idx + 3] -eq 0) { continue }
    $bb = [int]$bytes[$idx]; $gg = [int]$bytes[$idx + 1]; $rr = [int]$bytes[$idx + 2]
    $dr = $rr - $RefR; $dg = $gg - $RefG; $db = $bb - $RefB
    if ((($dr * $dr) + ($dg * $dg) + ($db * $db)) -gt $tol2) { continue }
    $bytes[$idx + 3] = 0
    $cleared++
    if ($px -gt 0)       { $n = $p - 1;  if (-not $visited[$n]) { $stack.Push($n) } }
    if ($px -lt $M - 1)  { $n = $p + 1;  if (-not $visited[$n]) { $stack.Push($n) } }
    if ($py -gt 0)       { $n = $p - $M; if (-not $visited[$n]) { $stack.Push($n) } }
    if ($py -lt $M - 1)  { $n = $p + $M; if (-not $visited[$n]) { $stack.Push($n) } }
}

[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $total)
$canvas.UnlockBits($data)
$canvas.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Write-Output "transparent master: $OutPath ($M x $M, cleared $cleared px, tol=$Tol)"
