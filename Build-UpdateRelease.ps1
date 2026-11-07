param([string]$Version = '0.5.52')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a stable three-part release version.' }
$project = Join-Path $PSScriptRoot 'AionDpsMeter'
$output = Join-Path $PSScriptRoot "releases\$Version\update-packages"
# A fresh staging directory prevents old binaries or local user data entering a release.
$staging = Join-Path $project ('bin\update-publish-' + [Guid]::NewGuid().ToString('N'))
$tool = Join-Path $project 'bin\velopack-tools'
if (!(Test-Path (Join-Path $tool 'vpk.exe'))) {
    dotnet tool install vpk --version 1.2.161 --tool-path $tool --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
    if ($LASTEXITCODE) { throw 'Could not install the pinned packaging tool.' }
}
dotnet publish (Join-Path $project 'AionDpsMeter.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:Version=$Version" -o $staging --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --nologo -v quiet
if ($LASTEXITCODE) { throw 'Publish failed.' }
& (Join-Path $tool 'vpk.exe') pack --runtime win-x64 --packId Klauensprung.Wingbeat --packVersion $Version --packDir $staging --mainExe Wingbeat.exe --packTitle Wingbeat --icon (Join-Path $project 'Assets\aion-dps-meter.ico') --shortcuts StartMenuRoot --instLocation PerUser --outputDir $output
if ($LASTEXITCODE) { throw 'Packaging failed.' }
# Upload all generated assets together to the matching stable GitHub release.
# The original portable ZIP may also be included for users who prefer it.
Write-Output "Release assets prepared in $output (not uploaded)."
