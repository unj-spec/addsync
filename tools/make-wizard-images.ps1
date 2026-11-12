# 설치 화면에 쓸 그림 두 장을 프로그램과 같은 톤으로 만든다.
#   installer\wizard-left.bmp  — 첫 화면 왼쪽 띠
#   installer\wizard-top.bmp   — 나머지 화면 오른쪽 위 작은 그림
# Inno Setup 은 BMP 만 받는다.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $root 'installer'
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }

# 프로그램 팔레트 (Theme.cs 와 같은 값)
$cTitle  = [Drawing.Color]::FromArgb(13, 15, 18)
$cPanel  = [Drawing.Color]::FromArgb(18, 20, 25)
$cAccent = [Drawing.Color]::FromArgb(27, 133, 232)
$cText   = [Drawing.Color]::FromArgb(230, 232, 236)
$cMuted  = [Drawing.Color]::FromArgb(108, 116, 131)

function New-Bmp([int]$w, [int]$h) {
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'ClearTypeGridFit'
    return @($bmp, $g)
}

function Font([single]$size, [string]$style) {
    try { New-Object Drawing.Font('맑은 고딕', $size, [Drawing.FontStyle]$style) }
    catch { New-Object Drawing.Font('Segoe UI', $size, [Drawing.FontStyle]$style) }
}

# ── 왼쪽 띠 ────────────────────────────────────────────────
$w = 192; $h = 386
$r = New-Bmp $w $h; $bmp = $r[0]; $g = $r[1]

$grad = New-Object Drawing.Drawing2D.LinearGradientBrush(
    (New-Object Drawing.Rectangle 0, 0, $w, $h), $cTitle, $cPanel, 60.0)
$g.FillRectangle($grad, 0, 0, $w, $h)

# 은은한 강조 원
$glow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(26, 27, 133, 232))
$g.FillEllipse($glow, -70, 170, 300, 300)

# 아이콘 자리 — 둥근 사각형에 머리글자
$box = New-Object Drawing.Rectangle 24, 36, 56, 56
$path = New-Object Drawing.Drawing2D.GraphicsPath
$d = 24
$path.AddArc($box.X, $box.Y, $d, $d, 180, 90)
$path.AddArc($box.Right - $d, $box.Y, $d, $d, 270, 90)
$path.AddArc($box.Right - $d, $box.Bottom - $d, $d, $d, 0, 90)
$path.AddArc($box.X, $box.Bottom - $d, $d, $d, 90, 90)
$path.CloseFigure()
$g.FillPath((New-Object Drawing.SolidBrush $cAccent), $path)

$fmt = New-Object Drawing.StringFormat
$fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
$boxF = New-Object Drawing.RectangleF 24, 36, 56, 56
$g.DrawString('W', (Font 24 'Bold'), [Drawing.Brushes]::White, $boxF, $fmt)

$g.DrawString("WoW`n애드온`n매니저", (Font 15 'Bold'),
    (New-Object Drawing.SolidBrush $cText), 24, 112)

$g.DrawString("애드온과 설정을`n그대로 옮깁니다", (Font 8.5 'Regular'),
    (New-Object Drawing.SolidBrush $cMuted), 24, 202)

$g.Dispose()
$bmp.Save((Join-Path $out 'wizard-left.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# ── 오른쪽 위 작은 그림 ────────────────────────────────────
$w = 110; $h = 110
$r = New-Bmp $w $h; $bmp = $r[0]; $g = $r[1]
$g.Clear($cTitle)      # 머리글도 어둡게 입힌다

$box = New-Object Drawing.Rectangle 27, 27, 56, 56
$path = New-Object Drawing.Drawing2D.GraphicsPath
$path.AddArc($box.X, $box.Y, $d, $d, 180, 90)
$path.AddArc($box.Right - $d, $box.Y, $d, $d, 270, 90)
$path.AddArc($box.Right - $d, $box.Bottom - $d, $d, $d, 0, 90)
$path.AddArc($box.X, $box.Bottom - $d, $d, $d, 90, 90)
$path.CloseFigure()
$g.FillPath((New-Object Drawing.SolidBrush $cAccent), $path)
$boxF = New-Object Drawing.RectangleF 27, 27, 56, 56
$g.DrawString('W', (Font 22 'Bold'), [Drawing.Brushes]::White, $boxF, $fmt)

$g.Dispose()
$bmp.Save((Join-Path $out 'wizard-top.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

Write-Host "만들었습니다: $out"
