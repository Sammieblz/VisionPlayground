param([string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe', [switch]$PublicAssets)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$gameProject = Join-Path $projectRoot 'game'
$buildLog = Join-Path $projectRoot 'outputs\unity\build.log'
New-Item -ItemType Directory -Force (Split-Path $buildLog) | Out-Null
if (-not (Test-Path -LiteralPath $Unity)) { throw 'Unity 6000.3.10f1 is required.' }
$arguments = '-batchmode -force-d3d11 -quit -projectPath "{0}" -executeMethod CoDriver.Editor.GameBuild.Build -logFile "{1}"' -f $gameProject, $buildLog
if ($PublicAssets) { $arguments += ' -public-assets' }
$buildProcess = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) { throw "Unity build failed. See $buildLog" }
Write-Output (Join-Path $projectRoot 'outputs\game\CoDriverSim.exe')
