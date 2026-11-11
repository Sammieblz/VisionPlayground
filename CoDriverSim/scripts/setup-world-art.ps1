param([string]$Blender = '', [string]$SuppliedAssets = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $projectRoot '.venv/Scripts/python.exe'
$toolCache = Join-Path (Split-Path -Parent $projectRoot) '.cache/world-art-tools'
if (-not $Blender) {
    $Blender = Join-Path $toolCache 'blender-4.5.15-windows-x64/blender.exe'
    if (-not (Test-Path -LiteralPath $Blender)) {
        New-Item -ItemType Directory -Force $toolCache | Out-Null
        $archive = Join-Path $toolCache 'blender-4.5.15-windows-x64.zip'
        Invoke-WebRequest -UseBasicParsing 'https://download.blender.org/release/Blender4.5/blender-4.5.15-windows-x64.zip' -OutFile $archive
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '52dd02fd74877fa08e74b67d7854cf32468a232c02aaa19b6844c91285574f8d') {
            throw 'Blender checksum mismatch'
        }
        Expand-Archive -LiteralPath $archive -DestinationPath $toolCache
    }
}
if (-not (Test-Path -LiteralPath $Blender)) { throw 'Blender executable not found' }
$version = & $Blender --version
if ($version[0] -notmatch 'Blender 4.5.15') { throw 'Preparation is pinned to Blender 4.5.15 LTS' }
& $python (Join-Path $PSScriptRoot 'fetch-world-art.py')
if ($LASTEXITCODE -ne 0) { throw 'Facade download failed' }
$worldArt = Join-Path $projectRoot 'game/Assets/External/Resources/WorldArt'
$reports = Join-Path $projectRoot 'outputs/world-art'
New-Item -ItemType Directory -Force $reports | Out-Null
function Prepare-Art([string]$source, [string]$kind, [string]$output) {
    & $Blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python (Join-Path $PSScriptRoot 'blender-world-assets.py') -- --source $source --report (Join-Path $reports ($kind + '-audit.json')) --prepare $kind --output $output
    if ($LASTEXITCODE -ne 0) { throw "Asset preparation failed: $kind" }
}
Prepare-Art (Join-Path $worldArt 'FacadeSource/modular_urban_apartments_facade.gltf') 'facades' (Join-Path $worldArt 'FacadePrepared')
& (Join-Path $PSScriptRoot 'setup-civilians.ps1') -Blender $Blender
& $python (Join-Path $PSScriptRoot 'fetch-construction.py')
if ($LASTEXITCODE -ne 0) { throw 'Construction download failed' }
& $Blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python (Join-Path $PSScriptRoot 'blender-construction.py') -- --source (Join-Path $projectRoot 'outputs/asset-downloads/construction/compact-tracked-excavator.glb') --output (Join-Path $worldArt 'ConstructionSource')
if ($LASTEXITCODE -ne 0) { throw 'Construction preparation failed' }
$concept = Join-Path $projectRoot 'game/Assets/External/Resources/Cockpit/Source/CarConcept.glb'
if (Test-Path -LiteralPath $concept) {
    Prepare-Art $concept 'fleet' (Join-Path $worldArt 'FleetSource')
}
if ($SuppliedAssets) {
    $buildings = Join-Path $SuppliedAssets 'Prop Buildings.blend'
    if (Test-Path -LiteralPath $buildings) {
        Prepare-Art $buildings 'buildings' (Join-Path $worldArt 'BuildingSource')
    }
    $signs = Join-Path $SuppliedAssets 'Street Signs.blend'
    if (Test-Path -LiteralPath $signs) {
        Prepare-Art $signs 'signs' (Join-Path $worldArt 'SignsSource')
    }
    foreach ($asset in @(
        @{archive='32-mercedes-benz-gls-580-2020.zip'; kind='mercedes'; target='Local'},
        @{archive='Rigged Man With Walk Cycle.zip'; kind='person'; target='CivilianSource'},
        @{archive='Rigged and animated Cat.zip'; kind='cat'; target='CatSource'}
    )) {
        $archivePath = Join-Path $SuppliedAssets $asset.archive
        if (-not (Test-Path -LiteralPath $archivePath)) { continue }
        $extract = Join-Path $reports ($asset.kind + '-source-' + [Guid]::NewGuid().ToString('N'))
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            $safeRoot = [System.IO.Path]::GetFullPath($extract) + [System.IO.Path]::DirectorySeparatorChar
            foreach ($entry in $zip.Entries) {
                $destination = [System.IO.Path]::GetFullPath((Join-Path $extract $entry.FullName))
                if (-not $destination.StartsWith($safeRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escapes extraction directory' }
            }
        } finally { $zip.Dispose() }
        [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extract)
        $source = Get-ChildItem -LiteralPath $extract -Filter '*.blend' -Recurse | Select-Object -First 1
        if (-not $source) { throw 'Supplied archive has no Blender source' }
        Prepare-Art $source.FullName $asset.kind (Join-Path $worldArt $asset.target)
    }
}
Write-Output 'World art prepared. Local Mercedes assets are excluded by -PublicAssets builds.'
