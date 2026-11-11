<#
.SYNOPSIS
  Export-Addons.ps1 로 만든 폴더를 이 PC의 WoW에 설치한다.

.DESCRIPTION
  - WoW 설치 경로를 자동 탐지 (못 찾으면 -WowPath 로 지정)
  - 덮어쓰기 전에 기존 AddOns / WTF 를 backup 폴더에 zip 으로 보관
  - 애드온과 설정(WTF)을 그대로 전개

.EXAMPLE
  .\Install-Addons.ps1 -Source "G:\내드라이브\wow-addons"
  .\Install-Addons.ps1 -Source .\export -WhatIf
  .\Install-Addons.ps1 -Source .\export -NoSettings -Clean
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # Export-Addons.ps1 의 결과 폴더 (manifest.json 이 있는 곳)
    [Parameter(Mandatory)][string]$Source,

    # WoW 설치 경로 (생략하면 자동 탐지)
    [string]$WowPath,

    # 특정 flavor만 설치
    [string[]]$Flavor,

    # 설정(WTF)은 건드리지 않고 애드온만 설치
    [switch]$NoSettings,

    # 기존 AddOns 폴더를 완전히 비우고 설치 (기본은 덮어쓰기 + 기존 유지)
    [switch]$Clean,

    # 백업 생략 (권장하지 않음)
    [switch]$NoBackup
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot "lib\WowCommon.ps1")

# ── 입력 확인 ────────────────────────────────────────────
$Source = (Resolve-Path -LiteralPath $Source).Path
$manifestPath = Join-Path $Source 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "manifest.json 이 없습니다: $Source  (Export-Addons.ps1 의 결과 폴더를 지정하세요)"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

# ── WoW 실행 중이면 중단 ─────────────────────────────────
$running = Get-Process -Name 'Wow', 'WowClassic', 'WowB', 'WowT' -ErrorAction SilentlyContinue
if ($running) {
    throw "WoW가 실행 중입니다 ($($running.Name -join ', ')). 게임을 완전히 종료한 뒤 다시 실행하세요."
}

# ── WoW 경로 ─────────────────────────────────────────────
if (-not $WowPath) { $WowPath = Find-WowPath }
if (-not $WowPath) {
    throw "WoW 설치 경로를 찾지 못했습니다. -WowPath 로 직접 지정하세요."
}
Write-Host "내보낸 PC : $($manifest.sourcePC)  ($($manifest.exportedAt))" -ForegroundColor DarkGray
Write-Host "이 PC WoW : $WowPath" -ForegroundColor Cyan

$backupDir = Join-Path $PSScriptRoot ("backup\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))

$names = @($manifest.flavors.PSObject.Properties.Name)
if ($Flavor) { $names = @($names | Where-Object { $Flavor -contains $_ }) }
if ($names.Count -eq 0) { throw "설치할 flavor가 없습니다." }

foreach ($name in $names) {
    $info = $manifest.flavors.$name
    $flavorPath = Join-Path $WowPath $name

    Write-Host ""
    Write-Host "=== $name" -ForegroundColor Yellow

    if (-not (Test-Path -LiteralPath $flavorPath)) {
        Write-Host "  이 PC에 $name 이(가) 없어 건너뜁니다." -ForegroundColor DarkYellow
        continue
    }

    $addOnsPath = Join-Path $flavorPath 'Interface\AddOns'
    $wtfPath    = Join-Path $flavorPath 'WTF'

    # ── 백업 ────────────────────────────────────────────
    if (-not $NoBackup) {
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        foreach ($pair in @(@{P = $addOnsPath; N = "$name-AddOns" }, @{P = $wtfPath; N = "$name-WTF" })) {
            if (-not (Test-Path -LiteralPath $pair.P)) { continue }
            $files = @(Get-ChildItem -LiteralPath $pair.P -Recurse -File -ErrorAction SilentlyContinue)
            if ($files.Count -eq 0) { continue }
            if ($PSCmdlet.ShouldProcess($pair.P, "백업")) {
                New-ZipFromFiles -Files $files -BasePath $pair.P `
                    -Destination (Join-Path $backupDir "$($pair.N).zip") -Label 'backup'
            }
        }
    }

    # ── 애드온 ──────────────────────────────────────────
    if ($info.addOnsZip) {
        $zip = Join-Path $Source $info.addOnsZip
        if (Test-Path -LiteralPath $zip) {
            if ($Clean -and (Test-Path -LiteralPath $addOnsPath)) {
                if ($PSCmdlet.ShouldProcess($addOnsPath, "기존 애드온 삭제(-Clean)")) {
                    Get-ChildItem -LiteralPath $addOnsPath -Force | Remove-Item -Recurse -Force
                }
            }
            if ($PSCmdlet.ShouldProcess($addOnsPath, "애드온 설치")) {
                Expand-ZipOverwrite -ZipPath $zip -Destination $addOnsPath -Label 'AddOns'
            }
        } else {
            Write-Warning "  $($info.addOnsZip) 파일이 없습니다."
        }
    }

    # ── 설정 ────────────────────────────────────────────
    if (-not $NoSettings -and $info.wtfZip) {
        $zip = Join-Path $Source $info.wtfZip
        if (Test-Path -LiteralPath $zip) {
            if ($PSCmdlet.ShouldProcess($wtfPath, "설정(WTF) 설치")) {
                Expand-ZipOverwrite -ZipPath $zip -Destination $wtfPath -Label 'WTF'
            }
        } else {
            Write-Warning "  $($info.wtfZip) 파일이 없습니다."
        }
    }
}

Write-Host ""
Write-Host "완료." -ForegroundColor Green
if (-not $NoBackup -and (Test-Path -LiteralPath $backupDir)) {
    Write-Host "기존 상태 백업: $backupDir" -ForegroundColor DarkGray
}
Write-Host "게임 안에서 애드온 목록을 확인하세요. 설정이 안 보이면 캐릭터 선택 화면의 'AddOns'에서 활성화 여부를 확인하면 됩니다." -ForegroundColor DarkGray
