[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publisher = Join-Path $PSScriptRoot 'Publish-Release.ps1'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "llm-glue-release-tests-$([guid]::NewGuid().ToString('N'))"
$tools = Join-Path $scratch 'tools'
New-Item -ItemType Directory -Path $tools | Out-Null
@'
@echo off
if /I "%1 %2"=="release view" if "%FAKE_GH_RELEASE_EXISTS%"=="1" exit /b 0
if /I "%1 %2"=="release view" exit /b 1
exit /b 0
'@ | Set-Content -LiteralPath (Join-Path $tools 'gh.cmd') -Encoding ascii
$oldPath = $env:PATH
$env:PATH = "$tools;$oldPath"
$oldPrompt = $env:GIT_TERMINAL_PROMPT
$env:GIT_TERMINAL_PROMPT = '0'

function Invoke-TestGit {
    [CmdletBinding()]
    param([Parameter(Position=0, ValueFromRemainingArguments=$true)][string[]]$GitArguments)

    & git @GitArguments *> $null
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArguments -join ' ') failed" }
}
function New-Case([string]$Name) {
    $script:caseRoot = Join-Path $scratch $Name
    $script:caseBare = Join-Path $caseRoot 'origin.git'
    $script:caseRepo = Join-Path $caseRoot 'work'
    New-Item -ItemType Directory -Path $caseRoot | Out-Null
    Invoke-TestGit init --bare --initial-branch=main $caseBare
    Invoke-TestGit clone $caseBare $caseRepo
    Invoke-TestGit -C $caseRepo config user.name 'LLM Glue Test'
    Invoke-TestGit -C $caseRepo config user.email 'test@example.invalid'
    Set-Content -LiteralPath (Join-Path $caseRepo 'fixture.txt') -Value 'fixture' -Encoding utf8NoBOM
    Invoke-TestGit -C $caseRepo add fixture.txt
    Invoke-TestGit -C $caseRepo commit -m initial
    Invoke-TestGit -C $caseRepo push -u origin main
    $bareUri = ([uri]$caseBare).AbsoluteUri
    Invoke-TestGit -C $caseRepo config "url.$bareUri.insteadOf" 'https://github.com/bobapplemac/llm-glue.git'
    Invoke-TestGit -C $caseRepo remote set-url origin 'https://github.com/bobapplemac/llm-glue.git'
    $resolved = (& git -C $caseRepo remote get-url origin | Out-String).Trim()
    if ($resolved -notlike 'file://*') { throw "Local URL rewrite did not apply: $resolved" }
}
function Invoke-Check {
    Push-Location $caseRepo
    try { & $publisher -Revision 1 -DryRun -CheckOnly -Branch main -NonInteractive }
    finally { Pop-Location }
}
function Expect-Pass([string]$Name) {
    Write-Host "Testing $Name (expect pass)"
    try { Invoke-Check *> $null } catch { throw "Expected pass '$Name': $_" }
}
function Expect-Fail([string]$Name) {
    Write-Host "Testing $Name (expect fail)"
    $failed = $false
    try { Invoke-Check *> $null } catch { $failed = $true }
    if (-not $failed) { throw "Expected failure '$Name'." }
}

try {
    New-Case clean; Expect-Pass clean
    New-Case dirty; Set-Content -LiteralPath (Join-Path $caseRepo 'untracked.txt') -Value dirty; Expect-Fail dirty
    New-Case ahead; Add-Content -LiteralPath (Join-Path $caseRepo 'fixture.txt') -Value ahead; Invoke-TestGit -C $caseRepo commit -am ahead; Expect-Fail ahead
    New-Case behind
    $other = Join-Path $caseRoot 'other'
    Invoke-TestGit clone $caseBare $other
    Invoke-TestGit -C $other config user.name 'LLM Glue Test'
    Invoke-TestGit -C $other config user.email 'test@example.invalid'
    Add-Content -LiteralPath (Join-Path $other 'fixture.txt') -Value behind
    Invoke-TestGit -C $other commit -am behind
    Invoke-TestGit -C $other push origin main
    Expect-Fail behind
    New-Case detached; Invoke-TestGit -C $caseRepo checkout --detach; Expect-Fail detached
    New-Case tag_collision; Invoke-TestGit -C $caseRepo tag r1; Expect-Fail tag_collision
    New-Case release_collision; $env:FAKE_GH_RELEASE_EXISTS = '1'; Expect-Fail release_collision; Remove-Item Env:FAKE_GH_RELEASE_EXISTS
    New-Case ambiguous_remote; Invoke-TestGit -C $caseRepo remote add backup $caseBare; Expect-Fail ambiguous_remote
    New-Case wrong_remote; Invoke-TestGit -C $caseRepo remote set-url origin 'https://github.com/someone-else/llm-glue.git'; Expect-Fail wrong_remote
    New-Case unavailable_remote
    $bareUri = ([uri]$caseBare).AbsoluteUri
    Invoke-TestGit -C $caseRepo config --unset-all "url.$bareUri.insteadOf"
    Invoke-TestGit -C $caseRepo config 'url.file:///definitely-missing-llm-glue-origin.insteadOf' 'https://github.com/bobapplemac/llm-glue.git'
    Expect-Fail unavailable_remote
    Write-Host 'PowerShell release preflight matrix: PASS'
} finally {
    $env:PATH = $oldPath
    if ($null -eq $oldPrompt) { Remove-Item Env:GIT_TERMINAL_PROMPT -ErrorAction SilentlyContinue } else { $env:GIT_TERMINAL_PROMPT = $oldPrompt }
    Remove-Item Env:FAKE_GH_RELEASE_EXISTS -ErrorAction SilentlyContinue
    if (Test-Path $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
