param([string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'AionDpsMeter/AionDpsMeter.csproj'
& (Join-Path $PSScriptRoot 'Verify-Source.ps1')
dotnet restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $project --no-restore -c Release --self-contained false -o $OutputPath
exit $LASTEXITCODE
