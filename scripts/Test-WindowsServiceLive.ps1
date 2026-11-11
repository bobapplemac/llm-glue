[CmdletBinding()]
param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\artifacts\final-qualification\publish\win-x64\llm-glue.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\live-service-qualification'),
    [switch]$AllRoles
)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this qualification script from an elevated PowerShell session.' }
if (Get-Service -Name LLMGlue -ErrorAction SilentlyContinue) { throw 'Refusing to modify an existing LLMGlue service.' }
$Executable = (Resolve-Path $Executable).Path
$testRoot = [IO.Path]::GetFullPath((Join-Path $OutputDirectory ([guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$config = Join-Path $testRoot 'llm-glue.conf'
$movedDirectory = Join-Path $testRoot 'moved'
$movedExecutable = Join-Path $movedDirectory 'llm-glue.exe'
$createdService = $false
$events = Join-Path $testRoot 'events.txt'

function Quote-Toml([string]$Value) { '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"' }
function Invoke-Glue([string]$Path, [string[]]$Arguments) {
    "$(Get-Date -Format o) start $Path $($Arguments -join ' ')" | Add-Content -LiteralPath $events -Encoding utf8
    & $Path @Arguments 2>&1 | Tee-Object -FilePath $events -Append
    $exitCode = $LASTEXITCODE
    "$(Get-Date -Format o) exit=$exitCode $Path $($Arguments -join ' ')" | Add-Content -LiteralPath $events -Encoding utf8
    if ($exitCode -ne 0) { throw "llm-glue $($Arguments -join ' ') failed with exit code $exitCode." }
}
function Wait-Healthy {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        try {
            $response = Invoke-RestMethod -Uri http://127.0.0.1:8765/api/v1/health -TimeoutSec 2
            if ($response.status -eq 'healthy') { return }
        } catch { Start-Sleep -Milliseconds 500 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'LLMGlue service did not become healthy within 45 seconds.'
}
function Wait-OwnedOpenCodeTarget {
    if (-not $AllRoles) { return }
    $target = Join-Path $testRoot 'opencode.json'
    $ownership = $target + '.llm-glue-ownership.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if ((Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $ownership)) {
            try {
                $catalog = Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -Depth 100
                $state = Get-Content -LiteralPath $ownership -Raw | ConvertFrom-Json
                if (@($catalog.disabled_providers) -contains 'ollama' -and @($state.disabledProviders) -contains 'ollama') { return }
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'LocalService did not create the managed OpenCode target and ownership sidecar within 45 seconds.'
}

if ($AllRoles) {
    $content = @(
        '[roles]','agent = true','server = true','client = true','',
        '[[agent.providers]]','type = "ollama"','name = "qualification-ollama"','url = "http://127.0.0.1:11434"','',
        '[server]','listen = "http://127.0.0.1:8765"',('state_directory = ' + (Quote-Toml (Join-Path $testRoot 'state'))),'',
        '[client]','source = "http://127.0.0.1:8765"','','[[client.targets]]','type = "opencode"',('path = ' + (Quote-Toml (Join-Path $testRoot 'opencode.json'))),'',
        '[logging]',('file = ' + (Quote-Toml (Join-Path $testRoot 'logs\llm-glue.log')))
    )
} else {
    $content = @(
        '[roles]','agent = false','server = true','client = false','',
        '[server]','listen = "http://127.0.0.1:8765"',('state_directory = ' + (Quote-Toml (Join-Path $testRoot 'state'))),'',
        '[logging]',('file = ' + (Quote-Toml (Join-Path $testRoot 'logs\llm-glue.log')))
    )
}
$content | Set-Content -LiteralPath $config -Encoding utf8NoBOM

try {
    Invoke-Glue $Executable @('config','validate','--config',$config)
    Invoke-Glue $Executable @('service','install','--config',$config)
    $createdService = $true
    Invoke-Glue $Executable @('service','start','--config',$config)
    Wait-Healthy
    Wait-OwnedOpenCodeTarget
    $service = Get-CimInstance Win32_Service -Filter "Name='LLMGlue'"
    if ($service.StartName -notin 'NT AUTHORITY\LocalService','NT AUTHORITY\LOCAL SERVICE') { throw "Unexpected service identity: $($service.StartName)" }

    Invoke-Glue $Executable @('service','stop','--config',$config)
    New-Item -ItemType Directory -Path $movedDirectory | Out-Null
    Copy-Item -LiteralPath $Executable -Destination $movedExecutable
    Invoke-Glue $movedExecutable @('service','repair','--config',$config)
    $service = Get-CimInstance Win32_Service -Filter "Name='LLMGlue'"
    if ($service.PathName -notlike "*$movedExecutable*") { throw "Service repair did not record the relocated executable: $($service.PathName)" }
    Invoke-Glue $movedExecutable @('service','start','--config',$config)
    Wait-Healthy
    Wait-OwnedOpenCodeTarget

    Copy-Item -LiteralPath $movedExecutable -Destination "$movedExecutable.rollback"
    Invoke-Glue $movedExecutable @('update','rollback','--service','--config',$config)
    $result = Join-Path $movedDirectory '.llm-glue-update\rollback\rollback-result.log'
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (-not (Test-Path $result) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path $result)) { throw 'Timed out waiting for the Windows rollback helper result.' }
    $resultText = Get-Content -LiteralPath $result -Raw
    if ($resultText -notmatch '^success') { throw "Rollback helper failed: $resultText" }
    Wait-Healthy
    Wait-OwnedOpenCodeTarget
    "passed $(Get-Date -Format o) roles=$(if ($AllRoles) { 'all' } else { 'server' }) identity=$($service.StartName) executable=$movedExecutable rollback=$($resultText.Trim()) ownershipSidecar=$(if ($AllRoles) { 'verified' } else { 'not-applicable' })" | Set-Content -LiteralPath (Join-Path $testRoot 'result.txt') -Encoding utf8NoBOM
    Write-Host "Windows service qualification passed. Evidence: $(Join-Path $testRoot 'result.txt')"
} catch {
    ($_ | Format-List * -Force | Out-String) | Set-Content -LiteralPath (Join-Path $testRoot 'failure.txt') -Encoding utf8NoBOM
    throw
} finally {
    if ($createdService -and (Get-Service -Name LLMGlue -ErrorAction SilentlyContinue)) {
        & sc.exe stop LLMGlue *> $null
        & sc.exe delete LLMGlue *> $null
    }
}
