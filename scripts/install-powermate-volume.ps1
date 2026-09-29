$ErrorActionPreference = 'Continue'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'PowerMateVolume\PowerMateVolume.csproj'
$publishDir = Join-Path $root 'PowerMateVolume\publish'
$startupScript = Join-Path $root 'scripts\start-powermate-volume.ps1'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Write-Host 'Stopping legacy Griffin PowerMate process and startup entry...'
Get-Process -Name PowerMate -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name 'PowerMate' -ErrorAction SilentlyContinue

Write-Host 'Publishing PowerMateVolume...'
dotnet publish $project -c Release -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

Copy-Item -LiteralPath (Join-Path $root 'PowerMateVolume\PowerMateVolume.json') -Destination (Join-Path $publishDir 'PowerMateVolume.json') -Force

Write-Host 'Registering current-user startup entry...'
$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$startupScript`""
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name 'PowerMateVolume' -Value $command

Write-Host 'Attempting to remove stale PowerMate device instance...'
pnputil /remove-device 'USB\VID_077D&PID_0410\5&2CF64626&0&9'

Write-Host 'Attempting to remove duplicate libwdi Griffin PowerMate driver packages from the driver store...'
$driverText = pnputil /enum-drivers
$current = @{}
$blocks = @()
foreach ($line in $driverText) {
    if ($line -match '^\s*$') {
        if ($current.Count -gt 0) {
            $blocks += [pscustomobject]$current
            $current = @{}
        }
        continue
    }

    if ($line -match '^\s*([^:]+):\s*(.*)$') {
        $current[$matches[1].Trim()] = $matches[2].Trim()
    }
}
if ($current.Count -gt 0) {
    $blocks += [pscustomobject]$current
}

$powermateDrivers = $blocks | Where-Object {
    $_.'Original Name' -eq 'griffin_powermate.inf' -and
    $_.'Provider Name' -eq 'libwdi'
}

foreach ($driver in $powermateDrivers) {
    $publishedName = $driver.'Published Name'
    if ($publishedName) {
        Write-Host "Deleting $publishedName..."
        pnputil /delete-driver $publishedName /uninstall /force
    }
}

Write-Host 'Starting PowerMateVolume...'
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $startupScript

Write-Host ''
Write-Host 'Done. If the device still shows as WinUSB/libwdi, unplug and replug the PowerMate, then rerun this script from an elevated PowerShell.'
