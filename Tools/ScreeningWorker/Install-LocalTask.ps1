param(
    [string]$TaskName = 'TradingJournal daily investment data',
    [string]$StartTime = '06:00',
    [string]$PublishDirectory = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'run-local.ps1'
if (-not (Test-Path -LiteralPath $runner)) { throw "Nenalezen $runner" }
$action = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$runner`" -PublishDirectory `"$PublishDirectory`""
$trigger = New-ScheduledTaskTrigger -Daily -At ([datetime]::ParseExact($StartTime, 'HH:mm', $null))
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Aktualizace TradingJournal do lokální SQL Server databáze.' -Force | Out-Null
Write-Host "Naplánováno: $TaskName každý den v $StartTime."
