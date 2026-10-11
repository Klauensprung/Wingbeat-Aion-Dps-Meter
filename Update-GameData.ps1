param(
    [Parameter(Mandatory = $true)][string]$LocalData
)
$ErrorActionPreference = 'Stop'
# Explicit, one-time download. The meter itself reads only these local files.
$revision = 'c668aad9832963d6bf45cd3f8062e7172f5ae95c'
$repo = 'SkeeveAN/Aion-DPS-Meter'
$raw = "https://raw.githubusercontent.com/$repo/$revision/"
$root = [IO.Path]::GetFullPath($LocalData)
$icons = Join-Path $root 'skill-icons'
[IO.Directory]::CreateDirectory($icons) | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Net.Http
$http = New-Object Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromSeconds(30)
$http.DefaultRequestHeaders.UserAgent.ParseAdd('Wingbeat-local-data/1.0')
function Get-Text([string]$Url) { $http.GetStringAsync($Url).GetAwaiter().GetResult() }
function Write-Json([string]$Name, $Value) {
    $destination = Join-Path $root $Name
    $temporary = $destination + '.download-' + [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8), $utf8)
    Move-Item -LiteralPath $temporary -Destination $destination -Force
}
function Get-BlobHash([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA1]::Create()
    try {
        $prefix = [Text.Encoding]::ASCII.GetBytes(('blob ' + $Bytes.Length + [char]0))
        [byte[]]$blob = $prefix + $Bytes
        return [BitConverter]::ToString($sha.ComputeHash($blob)).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}
try {
    $tree = (Get-Text "https://api.github.com/repos/$repo/git/trees/${revision}?recursive=1" | ConvertFrom-Json).tree
    $npcPath = 'Client/assets/aion2/npcs/npc_names.json'
    $tokenPath = 'Client/assets/aion2/skills/skill_icon_tokens.json'
    $npcData = Get-Text ($raw + $npcPath) | ConvertFrom-Json
    $tokens = Get-Text ($raw + $tokenPath) | ConvertFrom-Json
    $names = [ordered]@{}
    foreach ($property in $npcData.PSObject.Properties) {
        $value = [string]$property.Value.en
        if ($property.Name -match '^[1-9][0-9]{0,8}$' -and $value.Trim().Length -gt 0 -and $value.Length -le 240 -and $value -notmatch '[\x00-\x1f\x7f]') {
            $names[$property.Name] = $value.Trim()
        }
    }
    if ($names.Count -lt 1000) { throw 'NPC catalog is unexpectedly incomplete; existing catalog kept.' }
    $files = @($tree | Where-Object { $_.type -eq 'blob' -and $_.path -match '^Client/assets/aion2/skills/icons/[A-Za-z0-9_-]+\.png$' })
    if ($files.Count -lt 100 -or $files.Count -gt 2000) { throw 'Unexpected skill icon list; existing catalog kept.' }
    $byToken = @{}
    $downloaded = 0
    for ($offset = 0; $offset -lt $files.Count; $offset += 6) {
        $batch = @()
        foreach ($file in ($files | Select-Object -Skip $offset -First 6)) {
            $filename = [IO.Path]::GetFileName($file.path)
            $byToken[[IO.Path]::GetFileNameWithoutExtension($filename)] = $filename
            $path = Join-Path $icons $filename
            if ((Test-Path -LiteralPath $path) -and (Get-BlobHash ([IO.File]::ReadAllBytes($path))) -eq $file.sha) { continue }
            $batch += @{ File = $file; Path = $path; Task = $http.GetByteArrayAsync($raw + $file.path) }
        }
        foreach ($item in $batch) {
            [byte[]]$bytes = $item.Task.GetAwaiter().GetResult()
            if ($bytes.Length -gt 262144 -or $bytes.Length -lt 33 -or (Get-BlobHash $bytes) -ne $item.File.sha) { throw 'Downloaded icon failed verification.' }
            if ([BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Downloaded icon is not a PNG.' }
            $temporary = $item.Path + '.download-' + [Guid]::NewGuid().ToString('N')
            [IO.File]::WriteAllBytes($temporary, $bytes)
            Move-Item -LiteralPath $temporary -Destination $item.Path -Force
            $downloaded++
        }
    }
    $map = [ordered]@{}
    $missing = @()
    foreach ($property in $tokens.PSObject.Properties) {
        if ($property.Name -notmatch '^[1-9][0-9]{2,3}$') { throw 'Unexpected skill family ID.' }
        $token = ([string]$property.Value).Split('/')[-1]
        if ($byToken.ContainsKey($token)) { $map[([int]$property.Name * 10000).ToString()] = $byToken[$token] }
        else { $missing += [int]$property.Name * 10000 }
    }
    Write-Json 'npc-names.json' $names
    Write-Json 'skill-icons.json' $map
    Write-Json 'game-data-sources.json' ([ordered]@{
        DownloadedUtc = [DateTime]::UtcNow.ToString('o'); Revision = $revision
        NpcNamesSource = $raw + $npcPath; SkillIconsSource = $raw + $tokenPath
        IconFolderSource = "https://github.com/$repo/tree/$revision/Client/assets/aion2/skills/icons"
        NpcNames = $names.Count; Icons = $files.Count; SkillFamilies = $map.Count; FamiliesWithoutPng = $missing
        Notice = 'Game names and artwork belong to NCSOFT and their respective owners. Downloaded for local display; separate from the application source license. Source repository code is MIT licensed (SkeeveTV).'
    })
    Write-Output ("Local data ready: {0} NPC names, {1} icons, {2} skill families ({3} files downloaded). Restart the meter to reload." -f $names.Count, $files.Count, $map.Count, $downloaded)
} finally { $http.Dispose() }
