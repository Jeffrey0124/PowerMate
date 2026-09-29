$ErrorActionPreference = 'Continue'

Write-Host 'Stopping PowerMateVolume while the driver is being changed...'
Get-Process -Name PowerMateVolume -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name PowerMate -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host 'Removing stale PowerMate device instance...'
pnputil /remove-device 'USB\VID_077D&PID_0410\5&2CF64626&0&9'

Write-Host 'Removing active PowerMate device instance so Windows can redetect it...'
pnputil /remove-device 'USB\VID_077D&PID_0410\6&1D73F1DC&0&4'

Write-Host 'Deleting libwdi Griffin PowerMate driver packages...'
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

Write-Host ''
Write-Host 'Please unplug the Griffin PowerMate, wait 3 seconds, then plug it back in.'
Write-Host 'After Windows redetects it, press Enter here to start PowerMateVolume.'
Read-Host

$startupScript = Join-Path $PSScriptRoot 'start-powermate-volume.ps1'
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $startupScript

Write-Host 'Driver repair finished. You can close this window.'
Read-Host
