[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param(
    [Parameter(Mandatory)][ValidateRange(1,[int]::MaxValue)][int]$Revision,
    [string]$Branch = 'main',
    [switch]$DryRun,
    [switch]$CheckOnly,
    [switch]$NonInteractive,
    [string]$OutputDirectory = 'artifacts'
)
$ErrorActionPreference = 'Stop'
foreach ($tool in 'git','gh','dotnet','tar') { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Required tool is missing: $tool" } }

function Invoke-Git {
    [CmdletBinding()]
    param([Parameter(Position=0, ValueFromRemainingArguments=$true)][string[]]$GitArguments)

    $text = & git @GitArguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArguments -join ' ') failed: $text" }
    return ($text | Out-String).Trim()
}
function Require-CleanReleaseState {
    $root = Invoke-Git rev-parse --show-toplevel
    Set-Location -LiteralPath $root
    $current = Invoke-Git symbolic-ref --short -q HEAD
    if ($current -ne $Branch) { throw "Release requires branch '$Branch'; detached or other branches fail closed." }
    if ((Invoke-Git status --porcelain=v1 --untracked-files=all)) { throw 'Worktree must be completely clean.' }
    $upstream = Invoke-Git rev-parse --abbrev-ref '@{upstream}'
    $remotes = @((Invoke-Git remote) -split "`r?`n" | Where-Object { $_ })
    if ($remotes.Count -ne 1 -or $remotes[0] -ne 'origin') { throw 'Release requires exactly one remote named origin.' }
    if ($upstream -ne "origin/$Branch") { throw "Upstream must be origin/$Branch." }
    $originUrl = Invoke-Git config --get remote.origin.url
    if ($originUrl -notmatch '^(https://github\.com/|git@github\.com:)bobapplemac/llm-glue(\.git)?$') { throw 'Origin is not the pinned bobapplemac/llm-glue repository.' }
    Invoke-Git fetch --prune | Out-Null
    $script:ReleaseHead = Invoke-Git rev-parse HEAD
    if ($script:ReleaseHead -ne (Invoke-Git rev-parse '@{upstream}')) { throw "Local HEAD must equal $upstream; ahead, behind, and divergent states fail closed." }
    $tag = "r$Revision"
    & git rev-parse -q --verify "refs/tags/$tag" *> $null
    if ($LASTEXITCODE -eq 0) { throw "Tag $tag already exists." }
    & gh release view $tag *> $null
    if ($LASTEXITCODE -eq 0) { throw "GitHub release $tag already exists." }
    $revisionTags = (Invoke-Git tag -l 'r[1-9]*') -split "`r?`n"
    $revisions = @($revisionTags | Where-Object { $_ -match '^r\d+$' } | ForEach-Object { [int]$_.Substring(1) } | Sort-Object)
    $latest = if ($revisions.Count -eq 0) { 0 } else { $revisions[-1] }
    if ($Revision -ne ($latest + 1)) { throw "Revision must increment exactly; expected r$($latest + 1)." }
    return $root
}

$root = Require-CleanReleaseState
Write-Host "Preflight passed for r$Revision at $ReleaseHead"
if ($CheckOnly) { return }

& dotnet test (Join-Path $root 'LlmGlue.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
& dotnet (Join-Path $root 'src/LlmGlue/bin/Release/net10.0/llm-glue.dll') docs generate --output (Join-Path $root 'docs') --check
if ($LASTEXITCODE -ne 0) { throw 'Generated reference documentation is stale.' }
if ((Invoke-Git status --porcelain=v1 --untracked-files=all)) { throw 'Build or generated reference documentation changed the worktree.' }
foreach ($rid in 'linux-x64','win-x64') {
    $publish = Join-Path $root "$OutputDirectory/publish/$rid"
    & dotnet publish (Join-Path $root 'src/LlmGlue/LlmGlue.csproj') -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:AssemblyVersion=1.0.0.0 "-p:FileVersion=1.$Revision.0.0" "-p:InformationalVersion=r$Revision+$ReleaseHead" "-p:Version=1.$Revision.0" -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
}
$windowsBinary = Join-Path $root "$OutputDirectory/publish/win-x64/llm-glue.exe"
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($windowsBinary)
if ($versionInfo.FileVersion -ne "1.$Revision.0.0") { throw "Published FileVersion mismatch: $($versionInfo.FileVersion)" }
$package = Join-Path $root "$OutputDirectory/packages"
New-Item -ItemType Directory -Force -Path $package | Out-Null
Compress-Archive -Path (Join-Path $root "$OutputDirectory/publish/win-x64/*") -DestinationPath (Join-Path $package "llm-glue-r$Revision-win-x64.zip") -Force
tar -C (Join-Path $root "$OutputDirectory/publish/linux-x64") -czf (Join-Path $package "llm-glue-r$Revision-linux-x64.tar.gz") .
$assets = Get-ChildItem -LiteralPath $package -File | Where-Object Name -Like "llm-glue-r$Revision-*"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$checksumText = (($assets | ForEach-Object { "{0}  {1}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.Name }) -join "`n") + "`n"
$manifestText = (@{ product='LLM Glue'; revision="r$Revision"; assemblyVersion='1.0.0.0'; fileVersion="1.$Revision.0.0"; commit=$ReleaseHead; assets=@('linux-x64','win-x64') } | ConvertTo-Json -Compress) + "`n"
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS'), $checksumText, $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $package 'manifest.json'), $manifestText, $utf8NoBom)

Invoke-Git fetch --prune | Out-Null
if ((Invoke-Git rev-parse '@{upstream}') -ne $ReleaseHead) { throw 'Remote changed after build; aborting.' }
if ($DryRun -or $WhatIfPreference) { Write-Host "Dry run: would create tag and GitHub release r$Revision"; return }
if (-not $NonInteractive) {
    $confirmation = Read-Host "Publish immutable GitHub release r$Revision? Type r$Revision"
    if ($confirmation -ne "r$Revision") { throw 'Confirmation did not match.' }
}
if ($PSCmdlet.ShouldProcess("origin and GitHub", "Publish r$Revision")) {
    Invoke-Git tag -a "r$Revision" -m "LLM Glue r$Revision" | Out-Null
    Invoke-Git push origin "r$Revision" | Out-Null
    & gh release create "r$Revision" @($assets.FullName) (Join-Path $package 'SHA256SUMS') (Join-Path $package 'manifest.json') --verify-tag --title "LLM Glue r$Revision" --generate-notes
    if ($LASTEXITCODE -ne 0) { throw "Partial publication may exist. Inspect tag r$Revision and the GitHub release; no automatic deletion was attempted." }
}
