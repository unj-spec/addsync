<#
.SYNOPSIS
  이 PC의 WoW 애드온 + 설정(WTF)을 하나의 폴더로 내보낸다.

.DESCRIPTION
  flavor(_retail_, _classic_, _forever_ ...)별로
    <flavor>-AddOns.zip   : Interface\AddOns 전체
    <flavor>-WTF.zip      : WTF (애드온 설정/키바인드/프로필)
  를 만들고 manifest.json 에 애드온 목록과 버전을 기록한다.

  이 폴더를 USB/클라우드/네트워크로 옮긴 뒤, 다른 PC에서
  Install-Addons.ps1 을 실행하면 그대로 복원된다.

.EXAMPLE
  .\Export-Addons.ps1
  .\Export-Addons.ps1 -Output "G:\내드라이브\wow-addons" -IncludeGraphicsConfig
#>
[CmdletBinding()]
param(
    # 내보낼 위치
    [string]$Output = (Join-Path $PSScriptRoot "export"),

    # WoW 설치 경로 (생략하면 자동 탐지)
    [string]$WowPath,

    # 특정 flavor만 (예: _retail_). 생략하면 애드온이 있는 전부
    [string[]]$Flavor,

    # 설정(WTF) 제외하고 애드온만 내보내기
    [switch]$NoSettings,

    # 그래픽·해상도 설정까지 그대로 가져간다
    # (끄면 Config.wtf 에서 그래픽 줄만 빼고 나머지 개인 설정은 모두 담는다)
    [switch]$IncludeGraphicsConfig
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

. (Join-Path $PSScriptRoot "lib\WowCommon.ps1")

# ── WoW 경로 ─────────────────────────────────────────────
if (-not $WowPath) { $WowPath = Find-WowPath }
if (-not $WowPath) {
    throw "WoW 설치 경로를 찾지 못했습니다. -WowPath 로 직접 지정하세요. (예: -WowPath 'C:\Program Files (x86)\World of Warcraft')"
}
Write-Host "WoW 경로 : $WowPath" -ForegroundColor Cyan

$flavors = Get-WowFlavors -WowPath $WowPath
if ($Flavor) { $flavors = $flavors | Where-Object { $Flavor -contains $_.Name } }
$flavors = @($flavors | Where-Object { $_.AddOnCount -gt 0 })
if ($flavors.Count -eq 0) { throw "애드온이 설치된 flavor가 없습니다." }

New-Item -ItemType Directory -Force -Path $Output | Out-Null

$manifest = [ordered]@{
    schema      = 1
    exportedAt  = (Get-Date).ToString("o")
    sourcePC    = $env:COMPUTERNAME
    sourceWow   = $WowPath
    settings    = (-not $NoSettings)
    flavors     = [ordered]@{}
}

foreach ($f in $flavors) {
    Write-Host ""
    Write-Host "=== $($f.Name) — 애드온 $($f.AddOnCount)개" -ForegroundColor Yellow

    # ── 애드온 ───────────────────────────────────────────
    $addons = @(Get-InstalledAddons -AddOnsPath $f.AddOnsPath)
    $zipAddons = Join-Path $Output "$($f.Name)-AddOns.zip"
    $files = Get-ChildItem -LiteralPath $f.AddOnsPath -Recurse -File |
             Where-Object { -not (Test-Excluded $_.Name) }
    New-ZipFromFiles -Files $files -BasePath $f.AddOnsPath -Destination $zipAddons -Label "AddOns"

    $entry = [ordered]@{
        addOnsZip = [IO.Path]::GetFileName($zipAddons)
        folders   = $addons.Count
        addons    = $addons
    }

    # ── 설정(WTF) ────────────────────────────────────────
    if (-not $NoSettings) {
        $wtf = Join-Path $f.Path "WTF"
        if (Test-Path -LiteralPath $wtf) {
            $zipWtf = Join-Path $Output "$($f.Name)-WTF.zip"
            $wfiles = Get-ChildItem -LiteralPath $wtf -Recurse -File |
                      Where-Object { -not (Test-Excluded $_.Name) }
            # Config.wtf 는 담되, 그래픽 줄만 빼낸다 (사운드·이름표 등 개인 설정이 같이 들어 있다)
            $cfg = Join-Path $wtf "Config.wtf"
            $filter = if ($IncludeGraphicsConfig -or -not (Test-Path -LiteralPath $cfg)) { $null } else { $cfg }
            New-ZipFromFiles -Files $wfiles -BasePath $wtf -Destination $zipWtf -Label "WTF" -FilterConfigPath $filter
            $entry.wtfZip = [IO.Path]::GetFileName($zipWtf)
            $entry.graphicsConfigIncluded = [bool]$IncludeGraphicsConfig
        } else {
            Write-Host "  WTF 폴더가 없어 설정은 건너뜁니다." -ForegroundColor DarkGray
        }
    }

    $manifest.flavors[$f.Name] = $entry
}

$manifestPath = Join-Path $Output "manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Out-File -FilePath $manifestPath -Encoding utf8

Write-Host ""
Write-Host "완료: $Output" -ForegroundColor Green
Get-ChildItem -LiteralPath $Output | ForEach-Object {
    "{0,-28} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)
}
Write-Host ""
Write-Host "다른 PC에서:" -ForegroundColor Cyan
Write-Host "  .\Install-Addons.ps1 -Source `"<이 폴더 경로>`""
