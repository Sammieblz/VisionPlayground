param([Parameter(Mandatory = $true)][string]$Package)
$ErrorActionPreference = 'Stop'

$packageRoot = (Resolve-Path -LiteralPath $Package).Path
$executable = Join-Path $packageRoot 'CoDriverSim.exe'
$identityPath = Join-Path $packageRoot 'BuildIdentity.json'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $identityPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $packageRoot 'package-manifest.json') -PathType Leaf)) {
    throw 'Use a complete packaged CoDriverSim folder.'
}
$identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$vehicle = if ($identity.asset_profile -eq 'local-prototype') { 'gls-simulation-v1' } else { 'civic-comparison-v1' }
$arguments = '-map davis-downtown -vehicle {0} -view 1 -screen-width 1280 -screen-height 720 -screen-fullscreen 0' -f $vehicle
$destinations = @(
    [Environment]::GetFolderPath('DesktopDirectory'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'CoDriverSim')
)
$shell = New-Object -ComObject WScript.Shell
try {
    foreach ($directory in $destinations) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $path = Join-Path $directory 'CoDriverSim (Development).lnk'
        $shortcut = $shell.CreateShortcut($path)
        if ((Test-Path -LiteralPath $path) -and
            [IO.Path]::GetFileName($shortcut.TargetPath) -ne 'CoDriverSim.exe') {
            throw "Preserving an unrelated shortcut: $path"
        }
        $shortcut.TargetPath = $executable
        $shortcut.WorkingDirectory = $packageRoot
        $shortcut.Arguments = $arguments
        $shortcut.Description = 'CoDriverSim development application. Manual driving; no session time limit.'
        $shortcut.IconLocation = "$executable,0"
        $shortcut.Save()
        $saved = $shell.CreateShortcut($path)
        if ($saved.TargetPath -ne $executable -or $saved.Arguments -ne $arguments) {
            throw "Shortcut verification failed: $path"
        }
        [PSCustomObject]@{ Shortcut = $path; Package = $packageRoot; Arguments = $saved.Arguments }
    }
} finally {
    [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}
