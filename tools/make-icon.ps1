# app.ico 생성 — 프로그램 아이콘을 코드로 만든다 (외부 이미지 파일 의존 없음)
# 사용: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path (Split-Path $PSScriptRoot -Parent) "src\app.ico"
$sizes = @(256, 64, 48, 32, 24, 16)

function New-IconBitmap {
    param([int]$S)
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $u = $S / 32.0   # 32px 기준 좌표계

    # 둥근 사각형 배경
    $r = [int](6 * $u)
    $rect = New-Object System.Drawing.Rectangle(0, 0, ($S - 1), ($S - 1))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    # 어두운 가죽 바탕 + 금색 테두리 (와우 UI 톤)
    $bgRect = New-Object System.Drawing.Rectangle(0, 0, $S, $S)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 58, 44, 26),
        [System.Drawing.Color]::FromArgb(255, 26, 20, 13), 90)
    $g.FillPath($bg, $path)
    $goldPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 168, 139, 74)), ([float](1.5 * $u))
    $g.DrawPath($goldPen, $path)

    # 상자 (애드온 꾸러미)
    $gold = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 198, 94))
    $boxX = 7 * $u; $boxY = 15 * $u; $boxW = 18 * $u; $boxH = 11 * $u
    $g.FillRectangle($gold, $boxX, $boxY, $boxW, $boxH)

    # 상자 뚜껑 띠
    $band = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 36, 28, 17))
    $g.FillRectangle($band, ($boxX + 7.5 * $u), $boxY, (3 * $u), $boxH)
    $white = $gold

    # 위로 향하는 화살표 (옮긴다는 뜻)
    $pts = @(
        (New-Object System.Drawing.PointF(([float](16 * $u)), ([float](4 * $u)))),
        (New-Object System.Drawing.PointF(([float](23 * $u)), ([float](11 * $u)))),
        (New-Object System.Drawing.PointF(([float](18.6 * $u)), ([float](11 * $u)))),
        (New-Object System.Drawing.PointF(([float](18.6 * $u)), ([float](13.5 * $u)))),
        (New-Object System.Drawing.PointF(([float](13.4 * $u)), ([float](13.5 * $u)))),
        (New-Object System.Drawing.PointF(([float](13.4 * $u)), ([float](11 * $u)))),
        (New-Object System.Drawing.PointF(([float](9 * $u)), ([float](11 * $u))))
    )
    $g.FillPolygon($white, $pts)

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
