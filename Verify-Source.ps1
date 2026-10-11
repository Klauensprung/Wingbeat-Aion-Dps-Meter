$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'AionDpsMeter/AionDpsMeter.csproj'
[xml]$project = [IO.File]::ReadAllText($projectPath)
if ($project.SelectNodes('//ProjectReference|//Reference|//Compile[@Include]').Count -gt 0) { throw 'Only declared NuGet dependencies and local source files are allowed.' }
$expected = @{ SharpPcap = '6.3.0'; PacketDotNet = '1.4.7' }
$packages = @($project.SelectNodes('//PackageReference'))
if ($packages.Count -ne $expected.Count) { throw 'Unexpected package dependency count.' }
foreach ($package in $packages) {
    if (-not $expected.ContainsKey($package.Include) -or $expected[$package.Include] -ne $package.Version) { throw "Unexpected package: $($package.Include) $($package.Version)" }
}
$files = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'AionDpsMeter') -File -Filter '*.cs'
foreach ($file in $files) {
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($text -match 'using AionDpsMeter\.(Core|Application|Infrastructure)|DpsPacketParser|Firebase|EntitlementService|LicenseService') { throw "Recovered dependency in $($file.Name)" }
}
Write-Output 'PASS: standalone source and pinned NuGet dependency checks.'
