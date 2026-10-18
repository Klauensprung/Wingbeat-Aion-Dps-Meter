param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'releases'),
    [string]$GameDataPath,
    [switch]$CheckNpcap
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$project = Join-Path $PSScriptRoot 'AionDpsMeter/AionDpsMeter.csproj'
[xml]$projectXml = [IO.File]::ReadAllText($project)
$version = [string]$projectXml.Project.PropertyGroup.Version
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
$name = "Wingbeat-Aion2-DpsMeter-$version-win-x64"
if ((Test-Path -LiteralPath (Join-Path $releaseRoot $name)) -or (Test-Path -LiteralPath (Join-Path $releaseRoot ($name + '.zip')))) {
    $name += '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
}
$packageFolder = 'Winbeat ' + [char]0x2665 + ' Aion Dps Meter'
$packageRoot = Join-Path $releaseRoot "$name/$packageFolder"
$checkRoot = Join-Path $releaseRoot "build-checks/$name"
$testApp = Join-Path $checkRoot $packageFolder
New-Item -ItemType Directory -Path $packageRoot, $testApp -Force | Out-Null

& (Join-Path $PSScriptRoot 'Verify-Source.ps1')
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $packageRoot --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed ($LASTEXITCODE)." }

$runtime = [IO.File]::ReadAllText((Join-Path $packageRoot 'Wingbeat.runtimeconfig.json')) | ConvertFrom-Json
$frameworks = @($runtime.runtimeOptions.includedFrameworks)
if ($frameworks.Count -lt 2 -or $frameworks.name -notcontains 'Microsoft.WindowsDesktop.App') { throw 'Expected bundled .NET and Windows Desktop runtimes.' }
foreach ($file in @('Wingbeat.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'wpfgfx_cor3.dll', 'SharpPcap.dll', 'PacketDotNet.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $file))) { throw "Missing standalone dependency: $file" }
}

# Preserve the exact runtime pack's notices alongside the application notices.
$assets = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AionDpsMeter/obj/project.assets.json')) | ConvertFrom-Json
foreach ($framework in $frameworks) {
    $id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $pack = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder "$id/$($framework.version)"
        if (Test-Path -LiteralPath $candidate) { $pack = $candidate; break }
    }
    if (-not $pack) { throw "Cannot find notices for $id $($framework.version)." }
    $licenses = @(Get-ChildItem -LiteralPath $pack -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
    if ($licenses.Count -eq 0) { throw "No runtime license found in $pack." }
    foreach ($file in $licenses) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $packageRoot "licenses/$($framework.name)-$($framework.version)-$($file.Name).txt")
    }
}

# Opt-in local display metadata only. Never copy a complete local-data folder:
# fight history, player identities, PCAPs, logs and adapter preferences stay private.
$copiedData = @()
if ($GameDataPath) {
    $dataSource = (Resolve-Path -LiteralPath $GameDataPath).Path
    $dataTarget = Join-Path $packageRoot 'local-data'
    New-Item -ItemType Directory -Path $dataTarget -Force | Out-Null
    foreach ($file in @('skill-names.json', 'npc-names.json', 'skill-icons.json', 'game-data-sources.json', 'skill-catalog-source.txt')) {
        $source = Join-Path $dataSource $file
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $dataTarget $file)
            $copiedData += $file
        }
    }
    $icons = Join-Path $dataSource 'skill-icons'
    if (Test-Path -LiteralPath $icons -PathType Container) {
        $targetIcons = Join-Path $dataTarget 'skill-icons'
        New-Item -ItemType Directory -Path $targetIcons -Force | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $icons -File -Filter '*.png') {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $targetIcons $file.Name)
        }
        $copiedData += 'skill-icons/*.png'
    }
}
[IO.File]::WriteAllText((Join-Path $packageRoot 'Install Npcap.url'), "[InternetShortcut]`r`nURL=https://npcap.com/#download`r`n")
$buildInfo = [ordered]@{
    Application = ('Wingbeat ' + [char]0x2665 + ' Aion 2 DPS Meter'); Version = $version; RuntimeIdentifier = 'win-x64'
    SelfContained = $true; Frameworks = $frameworks; BuiltUtc = [DateTime]::UtcNow.ToString('O')
    Libraries = @('SharpPcap 6.3.0', 'PacketDotNet 1.4.7'); LocalDisplayData = $copiedData
    Npcap = 'Separate system driver installation; official download shortcut included.'
}
[IO.File]::WriteAllText((Join-Path $packageRoot 'build-info.json'), ($buildInfo | ConvertTo-Json -Depth 5))

# Test a separate copy so test output and synthetic fights cannot enter the ZIP.
foreach ($child in Get-ChildItem -LiteralPath $packageRoot -Force) { Copy-Item -LiteralPath $child.FullName -Destination $testApp -Recurse }
function Test-Executable([string]$argument, [string]$label) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $testApp 'Wingbeat.exe'
    $info.Arguments = $argument
    $info.WorkingDirectory = $testApp
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $checkRoot 'no-installed-dotnet'
    $info.EnvironmentVariables['DOTNET_ROOT_X64'] = $info.EnvironmentVariables['DOTNET_ROOT']
    $info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $info.EnvironmentVariables['PATH'] = "$env:WINDIR\System32;$env:WINDIR"
    $info.EnvironmentVariables['DOTNET_HOST_TRACE'] = '1'
    $info.EnvironmentVariables['DOTNET_HOST_TRACEFILE'] = Join-Path $checkRoot "$label-host.txt"
    $process = [Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "$label timed out; ZIP not created." }
    $code = $process.ExitCode
    $process.Dispose()
    if ($code -ne 0) { throw "$label failed ($code). Inspect $testApp; ZIP not created." }
    Write-Output "PASS: $label through the packaged EXE with isolated .NET environment."
}
Test-Executable '--self-test' 'self-test'
Test-Executable '--smoke-test' 'wpf-smoke'
if ($CheckNpcap) { Test-Executable '--check-npcap' 'npcap' }

$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName)
$checksums = foreach ($file in $files) {
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
}
[IO.File]::WriteAllLines((Join-Path $packageRoot 'CHECKSUMS.sha256'), [string[]]$checksums)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = Join-Path $releaseRoot ($name + '.zip')
$zipArchive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    # Windows PowerShell's .NET Framework can write backslash ZIP entry names.
    # Explicit forward slashes work with Explorer and other archive readers.
    foreach ($file in Get-ChildItem -LiteralPath $packageRoot -Recurse -File) {
        $entryName = $packageFolder + '/' + $file.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipArchive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $zipArchive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), $zipHash + '  ' + [IO.Path]::GetFileName($zip) + "`r`n")
Write-Output "Windows x64 package: $zip"
Write-Output "Executable: $(Join-Path $packageRoot 'Wingbeat.exe')"
Write-Output "Validation output: $checkRoot"
