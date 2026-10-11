[CmdletBinding()]
param([switch]$RequireParity)
$ErrorActionPreference = 'Stop'
$coverage = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'protocol/event-coverage.json') -Raw | ConvertFrom-Json
if ($coverage.Events.Count -ne $coverage.ReferenceEventCount -or @($coverage.Events.ReferenceEvent | Select-Object -Unique).Count -ne $coverage.ReferenceEventCount) {
    throw 'Event inventory is missing entries or contains duplicates.'
}
$experimental = @($coverage.Events | Where-Object Status -eq 'experimental').Count
$missing = @($coverage.Events | Where-Object Status -eq 'not-implemented').Count
$unverified = @($coverage.Events | Where-Object { -not $_.ReferenceParityVerified }).Count
Write-Output "$($coverage.ReferenceEventCount) declared reference events: $experimental experimental subsets, $missing not implemented, $unverified without verified reference parity."
if ($RequireParity -and ($unverified -gt 0 -or -not $coverage.FullParityVerified)) { throw 'Full network event compatibility is NOT verified. See protocol/event-coverage.json.' }
