[CmdletBinding()]
param(
    [ValidateSet("Export", "Import")]
    [string]$Action = "Export"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$project = Join-Path $repoRoot "Tools\SetupReview\SetupReviewWorkflow.csproj"

dotnet run --project $project --no-restore -- $Action
if ($LASTEXITCODE -ne 0) {
    throw "Setup Review workflow skončil s chybou ($LASTEXITCODE)."
}
