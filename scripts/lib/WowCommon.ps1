# Export/Install 공용 함수
# PowerShell 5.1 호환 (Windows 기본 탑재)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# 복사하지 않을 파일 (WoW가 다시 만드는 캐시/백업)
$script:ExcludeNames = @('cache.md5', 'Thumbs.db', 'desktop.ini')
$script:ExcludePatterns = @('*.old', '*.bak')

function Test-Excluded {
    param([Parameter(Mandatory)][string]$Name)
    if ($script:ExcludeNames -contains $Name) { return $true }
    foreach ($p in $script:ExcludePatterns) { if ($Name -like $p) { return $true } }
    return $false
}

function Find-WowPath {
    <# 레지스트리 → Battle.net product.db → 흔한 경로 순으로 탐지 #>
    $candidates = New-Object System.Collections.Generic.List[string]

    foreach ($key in @(
        'HKLM:\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft',
        'HKLM:\SOFTWARE\Blizzard Entertainment\World of Warcraft'
    )) {
        try {
            $v = (Get-ItemProperty -Path $key -ErrorAction Stop).InstallPath
            if ($v) { $candidates.Add($v) }
        } catch {}
    }

    $pdb = Join-Path $env:ProgramData 'Battle.net\Agent\product.db'
    if (Test-Path -LiteralPath $pdb) {
        try {
            # product.db는 바이너리(protobuf)지만 경로 문자열은 평문으로 들어있다
            $raw = [IO.File]::ReadAllText($pdb, [Text.Encoding]::ASCII)
            foreach ($m in [regex]::Matches($raw, '[A-Za-z]:\\[^\x00-\x1F"]*World of Warcraft')) {
                $candidates.Add($m.Value)
            }
        } catch {}
    }

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem | Select-Object -ExpandProperty Root)) {
        foreach ($sub in @('Program Files (x86)\World of Warcraft',
                           'Program Files\World of Warcraft',
                           'World of Warcraft',
                           'Games\World of Warcraft')) {
            $candidates.Add((Join-Path $drive $sub))
        }
    }

    foreach ($c in $candidates) {
        if ([string]::IsNullOrWhiteSpace($c)) { continue }
        $p = $c.TrimEnd('\')
        # flavor 폴더(_retail_ 등)를 가리키고 있으면 한 단계 올린다
        if ((Split-Path $p -Leaf) -like '_*_') { $p = Split-Path $p -Parent }
        if (-not (Test-Path -LiteralPath $p)) { continue }
        if (Get-ChildItem -LiteralPath $p -Directory -Filter '_*_' -ErrorAction SilentlyContinue) {
            return (Resolve-Path -LiteralPath $p).Path
        }
    }
    return $null
}

function Get-WowFlavors {
    param([Parameter(Mandatory)][string]$WowPath)
    Get-ChildItem -LiteralPath $WowPath -Directory -Filter '_*_' | ForEach-Object {
        $addons = Join-Path $_.FullName 'Interface\AddOns'
        $count = 0
        if (Test-Path -LiteralPath $addons) {
            $count = @(Get-ChildItem -LiteralPath $addons -Directory -ErrorAction SilentlyContinue).Count
        }
        [pscustomobject]@{
            Name       = $_.Name
            Path       = $_.FullName
            AddOnsPath = $addons
            AddOnCount = $count
        }
    }
}

function Get-InstalledAddons {
    <# .toc 를 읽어 폴더별 이름/버전/소스ID 를 뽑는다 (기록용) #>
    param([Parameter(Mandatory)][string]$AddOnsPath)
    if (-not (Test-Path -LiteralPath $AddOnsPath)) { return @() }

    Get-ChildItem -LiteralPath $AddOnsPath -Directory | ForEach-Object {
        $dir = $_
        $toc = Get-ChildItem -LiteralPath $dir.FullName -Filter '*.toc' -File -ErrorAction SilentlyContinue |
               Sort-Object { $_.BaseName -eq $dir.Name } -Descending |
               Select-Object -First 1

        $o = [ordered]@{ folder = $dir.Name }
        if ($toc) {
            foreach ($line in (Get-Content -LiteralPath $toc.FullName -ErrorAction SilentlyContinue)) {
                if ($line -notmatch '^\s*##\s*([^:]+):\s*(.*)$') { continue }
                $k = $Matches[1].Trim(); $v = $Matches[2].Trim()
                switch -Regex ($k) {
                    '^Title$'               { if (-not $o.title)   { $o.title   = ($v -replace '\|c[0-9a-fA-F]{8}|\|r', '') } }
                    '^Version$'             { if (-not $o.version) { $o.version = $v } }
                    '^X-Curse-Project-ID$'  { $o.curse = $v }
                    '^X-Wago-ID$'           { $o.wago  = $v }
                    '^X-WoWI-ID$'           { $o.wowi  = $v }
                }
            }
        }
        [pscustomobject]$o
    }
}

# Config.wtf 에서 그래픽·하드웨어 설정만 걸러낸다 (앱의 WowCore 와 같은 규칙)
$script:GfxPrefixes = @('gx', 'graphics', 'ffx')
$script:GfxNames = @(
    'brightness','contrast','lowlatencymode','msaaquality','outlineenginemode',
    'renderscale','resamplequality','resamplealwayssharpen','ssao','hwdetect',
    'farclip','horizonclip','horizonstart','groundeffectdensity','groundeffectdist',
    'doodadlodscale','lodobjectcullsize','lodobjectfadescale','lodobjectminsize',
    'physicslevel','projectedtextures','refraction','reflectionmode','rippledetail',
    'shadowmode','shadowrt','shadowsoft','shadowtexturesize','shadowblendcascades',
    'shadownumcascades','entityshadowfadescale','sunshafts','terrainloddist',
    'terrainloddiv','volumefog','volumefoglevel','vsync','waterdetail',
    'weatherdensity','wmoloddist','texturefilteringmode','videooptionsversion',
    'usetargetfps','targetfps','maxfps','maxfpsbk','uiscale','useuiscale',
    'componentmodellevel','particledensity','spelldensity','sound_outputdrivername'
)

function Test-GraphicsCVar {
    param([string]$Name)
    if ([string]::IsNullOrEmpty($Name)) { return $false }
    $n = $Name.ToLowerInvariant()
    if ($n.StartsWith('raid')) { $n = $n.Substring(4) }
    foreach ($p in $script:GfxPrefixes) { if ($n.StartsWith($p)) { return $true } }
    return ($script:GfxNames -contains $n)
}

function Get-FilteredConfigWtf {
    param([string]$Path)
    $out = New-Object System.Text.StringBuilder
    $kept = 0; $dropped = 0
    foreach ($line in (Get-Content -LiteralPath $Path)) {
        $m = [regex]::Match($line, '^\s*SET\s+([A-Za-z0-9_\-]+)\s')
        if ($m.Success -and (Test-GraphicsCVar $m.Groups[1].Value)) { $dropped++; continue }
        [void]$out.AppendLine($line)
        if ($m.Success) { $kept++ }
    }
    return @{ Text = $out.ToString(); Kept = $kept; Dropped = $dropped }
}

function New-ZipFromFiles {
    <# 파일 목록을 BasePath 기준 상대경로로 zip 에 담는다 #>
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Files,
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$Destination,
        [string]$Label = 'files',
        # 이 경로의 파일은 그래픽 줄을 빼고 담는다 (Config.wtf)
        [string]$FilterConfigPath
    )
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    $base = (Resolve-Path -LiteralPath $BasePath).Path.TrimEnd('\') + '\'

    $zip = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $i = 0
        foreach ($f in $Files) {
            $rel = $f.FullName.Substring($base.Length).Replace('\', '/')
            try {
                if ($FilterConfigPath -and
                    ($f.FullName -ieq (Resolve-Path -LiteralPath $FilterConfigPath).Path)) {
                    $res = Get-FilteredConfigWtf -Path $f.FullName
                    $entry = $zip.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
                    $sw = New-Object System.IO.StreamWriter($entry.Open(),
                              (New-Object System.Text.UTF8Encoding($false)))
                    $sw.Write($res.Text); $sw.Dispose()
                    Write-Host ("  Config.wtf - 개인 설정 {0}개 포함, 그래픽 {1}개 제외" -f $res.Kept, $res.Dropped)
                } else {
                    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $zip, $f.FullName, $rel, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
            } catch {
                Write-Warning "  건너뜀: $rel ($($_.Exception.Message))"
                continue
            }
            $i++
            if ($i % 200 -eq 0) {
                Write-Progress -Activity "$Label 압축" -Status "$i / $($Files.Count)" `
                               -PercentComplete ([math]::Min(100, $i * 100 / [math]::Max(1, $Files.Count)))
            }
        }
    } finally {
        $zip.Dispose()
        Write-Progress -Activity "$Label 압축" -Completed
    }
    $mb = (Get-Item -LiteralPath $Destination).Length / 1MB
    Write-Host ("  {0,-8} {1,5} files  ->  {2}  ({3:N1} MB)" -f $Label, $Files.Count, [IO.Path]::GetFileName($Destination), $mb)
}

function Expand-ZipOverwrite {
    <# zip 을 대상 폴더에 덮어쓰며 전개. zip-slip 방지 포함 #>
    param(
        [Parameter(Mandatory)][string]$ZipPath,
        [Parameter(Mandatory)][string]$Destination,
        [string]$Label = 'extract'
    )
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    # GetFullPath 는 8.3 단축경로(ADMINI~1)를 펼치므로, 비교 기준도 같은 방식으로 정규화한다
    $root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Destination).Path).TrimEnd('\') + '\'

    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $total = $zip.Entries.Count; $i = 0
        foreach ($e in $zip.Entries) {
            $i++
            if ([string]::IsNullOrEmpty($e.Name)) { continue }   # 디렉터리 엔트리
            $target = [IO.Path]::GetFullPath((Join-Path $root $e.FullName))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
                Write-Warning "  수상한 경로 무시: $($e.FullName)"
                continue
            }
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
            if ($i % 200 -eq 0) {
                Write-Progress -Activity "$Label 전개" -Status "$i / $total" `
                               -PercentComplete ([math]::Min(100, $i * 100 / [math]::Max(1, $total)))
            }
        }
        Write-Host ("  {0,-8} {1,5} files  <-  {2}" -f $Label, $total, [IO.Path]::GetFileName($ZipPath))
    } finally {
        $zip.Dispose()
        Write-Progress -Activity "$Label 전개" -Completed
    }
}
