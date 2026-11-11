param(
    [ValidatePattern('^[A-Za-z0-9_.-]+$')]
    [string]$Distribution = 'Ubuntu-22.04',
    [ValidateSet('onnx', 'latest')]
    [string]$Profile = 'onnx'
)
$ErrorActionPreference = 'Stop'
$terminal = Get-Command wt.exe -ErrorAction Stop
$scriptWindows = Join-Path $PSScriptRoot 'comma-sim.sh'
$scriptLinux = & wsl.exe -d $Distribution --exec wslpath -a $scriptWindows
if ($LASTEXITCODE -ne 0) { throw 'Could not locate the Comma launcher inside WSL.' }
$scriptLinux = $scriptLinux.Trim()
$terminalArguments = @(
    'new-tab', '--title', 'Comma-controls',
    'wsl.exe', '-d', $Distribution, '--exec', 'bash', $scriptLinux, 'bridge', $Profile,
    ';', 'new-tab', '--title', 'Comma-manager',
    'wsl.exe', '-d', $Distribution, '--exec', 'bash', $scriptLinux, 'manager', $Profile
)
# Start-Process joins arguments into one native command line. Quote each value
# so checkout paths with spaces remain a single argument.
$nativeArguments = ($terminalArguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
Start-Process -FilePath $terminal.Source -ArgumentList $nativeArguments -WindowStyle Normal
Write-Host "Comma $Profile manager and controls opened in Windows Terminal."
Write-Host 'Allow up to a minute for the driving camera to appear. Both terminal tabs must stay open.'
Write-Host "WSL logs: ~/VisionPlayground-experiments/logs/$Profile/"
Write-Host 'Focus Comma-controls for keyboard input. Stop both tabs with Ctrl+C when finished.'
