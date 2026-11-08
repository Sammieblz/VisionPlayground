$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

& py -3.11 --version
if ($LASTEXITCODE -ne 0) {
    throw 'Install Python 3.11 first: winget install --exact --id Python.Python.3.11 --source winget --architecture x64 --scope user'
}
if (-not (Test-Path '.venv\Scripts\python.exe')) {
    & py -3.11 -m venv .venv
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the virtual environment.' }
}
$Python = Join-Path (Get-Location) '.venv\Scripts\python.exe'
& $Python -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'pip upgrade failed.' }
& $Python -m pip install -r requirements-lock.txt
if ($LASTEXITCODE -ne 0) { throw 'Installation failed. Keep the error output for diagnosis.' }
& $Python -m pip install --no-deps --no-build-isolation -e .
if ($LASTEXITCODE -ne 0) { throw 'Project installation failed.' }
& $Python -m codriversim demo
if ($LASTEXITCODE -ne 0) { throw 'Warning-logic demo failed.' }
Write-Host 'Ready. Run: .\.venv\Scripts\python.exe -m codriversim drive'
