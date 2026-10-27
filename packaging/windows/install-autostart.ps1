#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Starts ttlcd at boot as a hidden background task (runs as SYSTEM, so sensors work).
.EXAMPLE
    .\install-autostart.ps1 -ExePath C:\Tools\ttlcd\ttlcd.exe
#>
param(
    [string]$ExePath = (Join-Path $PSScriptRoot 'ttlcd.exe'),
    [string]$TaskName = 'ttlcd'
)

$ExePath = (Resolve-Path $ExePath).Path
$config = Join-Path $env:ProgramData 'ttlcd\config.json'

$action = New-ScheduledTaskAction -Execute $ExePath -Argument "run --config `"$config`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Task '$TaskName' installed and started."
Write-Host "Config file: $config  (create it with: `"$ExePath`" init-config --config `"$config`")"
