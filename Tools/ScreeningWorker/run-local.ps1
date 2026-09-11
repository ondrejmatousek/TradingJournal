param(
    [switch]$Force,
    [string]$PublishDirectory = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$worker = Join-Path $PublishDirectory 'ScreeningWorker.dll'
if (-not (Test-Path -LiteralPath $worker)) {
    throw "Worker není publikovaný. Spusť: dotnet publish Tools/ScreeningWorker/ScreeningWorker.csproj -c Release -o `"$PublishDirectory`""
}
if ([string]::IsNullOrWhiteSpace($env:SCREENING__SECUSERAGENT)) {
    throw 'Nastav proměnnou SCREENING__SECUSERAGENT s kontaktním e-mailem pro SEC.'
}

$arguments = @($worker, '--run')
if ($Force) { $arguments += '--force' }
& dotnet @arguments
exit $LASTEXITCODE

