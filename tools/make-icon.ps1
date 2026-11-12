# app.ico 생성 — assets\addsync.png 과 assets\addsync-mark.png 에서 만든다.
# 큰 크기는 글자가 든 전체 로고를, 작은 크기는 글자를 뺀 A 마크를 쓴다.
# 사용: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root "src\app.ico"
$full = Join-Path $root "assets\addsync.png"
$mark = Join-Path $root "assets\addsync-mark.png"

foreach ($p in @($full, $mark)) {
    if (-not (Test-Path $p)) { throw "그림을 찾을 수 없습니다: $p" }
}

$srcFull = [System.Drawing.Image]::FromFile($full)
$srcMark = [System.Drawing.Image]::FromFile($mark)

# 작은 크기에서는 'AddSync' 글자가 뭉개지므로 마크만 쓴다
$sizes = @(256, 128, 64, 48, 32, 24, 16)

function New-IconBitmap {
    param([int]$S)
    $src = if ($S -ge 128) { $srcFull } else { $srcMark }

    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'AntiAlias'
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $S, $S)))
    $g.Dispose()
    return $bmp
}

# 256은 PNG, 나머지는 DIB(BMP) 페이로드.
# GDI+ 의 Icon 클래스는 작은 크기의 PNG 페이로드를 읽지 못하므로 DIB 로 넣어야
# WinForms 창 아이콘과 작업 표시줄에서 정상적으로 보인다.
function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bmp)
    $S = $Bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    $maskRow = [int][Math]::Floor((($S + 31) / 32)) * 4
    $maskLen = $maskRow * $S

    $bw.Write([uint32]40)            # biSize
    $bw.Write([int32]$S)             # biWidth
    $bw.Write([int32]($S * 2))       # biHeight (XOR + AND)
    $bw.Write([uint16]1)             # biPlanes
    $bw.Write([uint16]32)            # biBitCount
    $bw.Write([uint32]0)             # biCompression = BI_RGB
    $bw.Write([uint32]($S * $S * 4 + $maskLen))
    $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)

    $data = New-Object 'byte[]' ($S * $S * 4)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $S, $S)
    $bd = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($y = 0; $y -lt $S; $y++) {
            $src = [IntPtr]::Add($bd.Scan0, $bd.Stride * $y)
            [System.Runtime.InteropServices.Marshal]::Copy($src, $data, ($y * $S * 4), ($S * 4))
        }
    } finally { $Bmp.UnlockBits($bd) }

    for ($y = $S - 1; $y -ge 0; $y--) { $bw.Write($data, ($y * $S * 4), ($S * 4)) }
    $bw.Write((New-Object 'byte[]' $maskLen))

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return , $bytes      # 쉼표: PowerShell 이 배열을 풀어버리지 않게
}

$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap -S $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , @{ Size = $s; Bytes = $ms.ToArray() }
        $ms.Dispose()
    } else {
        $pngs += , @{ Size = $s; Bytes = (Get-DibBytes -Bmp $bmp) }
    }
    $bmp.Dispose()
}

$srcFull.Dispose(); $srcMark.Dispose()

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: icon
$bw.Write([uint16]$pngs.Count)

$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # palette
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # color planes
    $bw.Write([uint16]32)            # bits per pixel
    $bw.Write([uint32]$p.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Host ("생성: {0} ({1:N0} bytes, {2}개 크기)" -f $out, (Get-Item $out).Length, $pngs.Count)
