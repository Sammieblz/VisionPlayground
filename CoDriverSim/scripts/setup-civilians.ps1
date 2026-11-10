param([Parameter(Mandatory=$true)][string]$Blender, [string]$Cache = '')
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $Cache) { $Cache = Join-Path (Split-Path -Parent $project) '.cache/makehuman' }
$python = Join-Path $project '.venv/Scripts/python.exe'
& $python (Join-Path $PSScriptRoot 'fetch-makehuman.py') --cache $Cache
if ($LASTEXITCODE -ne 0) { throw 'Pinned MakeHuman source verification failed' }
$prepared = Join-Path $Cache 'prepared'
& $Blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python (Join-Path $PSScriptRoot 'prepare-makehuman.py') -- --tool (Join-Path $Cache 'mpfb2') --user-data (Join-Path $Cache 'mpfb-user') --manifest (Join-Path $project 'game/makehuman-core-manifest.json') --output $prepared
if ($LASTEXITCODE -ne 0) { throw 'MakeHuman character preparation failed' }
$target = Join-Path $project 'game/Assets/External/Resources/WorldArt/MakeHumanSource'
New-Item -ItemType Directory -Force $target | Out-Null
Get-ChildItem -LiteralPath $prepared -File | Where-Object { $_.Extension -in '.fbx','.png','.json' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
}
Write-Output 'CC0 civilian sources prepared; Unity will generate retargeted prefabs.'
