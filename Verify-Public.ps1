[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Review the Git index: these are the bytes the next commit will contain.
# This catches common leaks, not every possible secret or personal detail.
Push-Location $PSScriptRoot
try {
    $files = @(git -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Stage the reviewed source files before running this check.' }
    $ignored = @(git ls-files --cached --ignored --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to check ignored files in the index.' }
    if ($ignored.Count) { throw ('Ignored files are staged: ' + ($ignored -join ', ')) }
    foreach ($file in $files) {
        if ($file -match '(?i)\.(exe|dll|pdb|zip|7z|pcap|pcapng|cap|dmp|etl|log|binlog|user|pfx|p12|pem|key)$') {
            throw "Private or generated file in index: $file"
        }
        $content = (git show ":$file") -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "Cannot read staged file: $file" }
        $patterns = @(
            '(?i)[a-z]:[\\/]Users[\\/][^\s\\/]+',
            '(?i)[a-z]:[\\/](?:Projects|Porjects)[\\/]',
            'NPF_\{[0-9A-Fa-f-]{36}\}',
            '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
            '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[0-9A-Z]{16})'
        )
        foreach ($pattern in $patterns) {
            if ($content -match $pattern) { throw "Potential private content in staged file: $file (matched content withheld)" }
        }
    }
    git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Staged whitespace/conflict-marker check failed.' }
    Write-Output "PASS: $($files.Count) staged source files; no ignored artifacts or common credential/machine identifiers detected."
    Write-Output 'Manually review git diff --cached before committing. Ignore rules do not sanitize files or existing history.'
} finally { Pop-Location }
