$ErrorActionPreference = 'Stop'

$app = Join-Path $PSScriptRoot '..\PowerMateVolume\publish\PowerMateVolume.exe'
$config = Join-Path $PSScriptRoot '..\PowerMateVolume\publish\PowerMateVolume.json'
$logDir = Join-Path $env:LOCALAPPDATA 'PowerMateVolume'
$logFile = Join-Path $logDir 'startup.log'

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

if (-not (Test-Path -LiteralPath $app)) {
    "$(Get-Date -Format o) PowerMateVolume executable not found: $app" | Add-Content -LiteralPath $logFile
    exit 1
}

if (-not (Test-Path -LiteralPath $config)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\PowerMateVolume\PowerMateVolume.json') -Destination $config -Force
}

Start-Process -FilePath $app -WindowStyle Hidden -WorkingDirectory (Split-Path -Parent $app)
