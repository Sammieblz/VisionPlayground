$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $projectRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) { & py -3.11 -m venv (Join-Path $projectRoot '.venv') }
& $python -m pip install -r (Join-Path $projectRoot 'requirements-game-lock.txt')
if ($LASTEXITCODE -ne 0) { throw 'Game dependencies failed to install' }
& $python -m pip install --no-deps --no-build-isolation -e $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'CoDriverSim package installation failed' }
& $python (Join-Path $PSScriptRoot 'fetch-game-assets.py')
if ($LASTEXITCODE -ne 0) { throw 'Asset download failed' }
& $python (Join-Path $PSScriptRoot 'fetch-actors.py')
if ($LASTEXITCODE -ne 0) { throw 'Character asset download failed' }
& $python (Join-Path $PSScriptRoot 'fetch-overhaul.py')
if ($LASTEXITCODE -ne 0) { throw 'Pinned cockpit or physics source preparation failed' }
& (Join-Path $PSScriptRoot 'setup-world-art.ps1')
& (Join-Path $PSScriptRoot 'build-game.ps1')
