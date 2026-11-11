[CmdletBinding()]
param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\artifacts\final-qualification\publish\win-x64\llm-glue.exe'),
    [string]$OllamaExecutable = (Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'),
    [string]$OpenCodeExecutable = (Join-Path $env:APPDATA 'npm\opencode.cmd'),
    [string]$FirstModel = 'qwen3.5:4b-q4_K_M',
    [string]$SecondModel = 'huihui_ai/qwen3-vl-abliterated:8b-instruct',
    [string]$Alias = 'llm-glue-lifecycle:latest',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\windows-ollama-lifecycle')
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$OllamaExecutable = (Resolve-Path -LiteralPath $OllamaExecutable).Path
$OpenCodeExecutable = (Resolve-Path -LiteralPath $OpenCodeExecutable).Path
$sourceModels = (Resolve-Path -LiteralPath (Join-Path $env:USERPROFILE '.ollama\models')).Path
$testRoot = [IO.Path]::GetFullPath((Join-Path $OutputDirectory ([guid]::NewGuid().ToString('N'))))
$modelStore = Join-Path $testRoot 'isolated-models'
$target = Join-Path $testRoot 'opencode.json'
$config = Join-Path $testRoot 'llm-glue.conf'
$resultPath = Join-Path $testRoot 'result.json'
$events = [Collections.Generic.List[string]]::new()
$ollamaRun = $null
$glueRun = $null
$runNumber = 0

function Quote-Toml([string]$Value) { '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"' }
function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port } finally { $listener.Stop() }
}
function Get-ProductionStoreFingerprint {
    $lines = foreach ($file in Get-ChildItem -LiteralPath $sourceModels -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($sourceModels.Length + 1)
        if ($relative.StartsWith('blobs\', [StringComparison]::OrdinalIgnoreCase)) {
            "$relative|$($file.Length)|$($file.LastWriteTimeUtc.Ticks)"
        } else {
            "$relative|$((Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash)"
        }
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '') } finally { $sha.Dispose() }
}
function Start-Captured([string]$File, [string[]]$Arguments, [Collections.IDictionary]$Environment, [string]$Prefix) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($entry in $Environment.GetEnumerator()) { $start.Environment[$entry.Key] = [string]$entry.Value }
    $process = [Diagnostics.Process]::Start($start)
    [pscustomobject]@{
        Process = $process
        Stdout = $process.StandardOutput.ReadToEndAsync()
        Stderr = $process.StandardError.ReadToEndAsync()
        Prefix = $Prefix
    }
}
function Stop-Captured($Run) {
    if (-not $Run) { return }
    if (-not $Run.Process.HasExited) {
        $Run.Process.Kill($true)
        $Run.Process.WaitForExit(10000) | Out-Null
    }
    $Run.Stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath ($Run.Prefix + '.stdout.log') -Encoding utf8NoBOM
    $Run.Stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath ($Run.Prefix + '.stderr.log') -Encoding utf8NoBOM
    $Run.Process.Dispose()
}
function Wait-Http([string]$Uri, $Run, [int]$Seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if ($Run.Process.HasExited) { throw "Process exited early with code $($Run.Process.ExitCode): $Uri" }
        try { return Invoke-RestMethod -Uri $Uri -TimeoutSec 2 } catch { Start-Sleep -Milliseconds 200 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Uri"
}
function Invoke-IsolatedOllama([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($OllamaExecutable)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['OLLAMA_HOST'] = "http://127.0.0.1:$ollamaPort"
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit(30000) | Out-Null
    $code = $process.ExitCode
    $process.Dispose()
    if ($code -ne 0) { throw "ollama $($Arguments -join ' ') failed: $stderr" }
    return $stdout
}
function Start-IsolatedOllama {
    $script:runNumber++
    $script:ollamaRun = Start-Captured $OllamaExecutable @('serve') @{
        OLLAMA_HOST = "127.0.0.1:$ollamaPort"
        OLLAMA_MODELS = $modelStore
        OLLAMA_NO_CLOUD = '1'
    } (Join-Path $testRoot "ollama-$runNumber")
    Wait-Http "http://127.0.0.1:$ollamaPort/api/version" $script:ollamaRun | Out-Null
}
function Start-Glue {
    $script:runNumber++
    $script:glueRun = Start-Captured $Executable @('run', '--config', $config) @{} (Join-Path $testRoot "llm-glue-$runNumber")
    Wait-Http "http://127.0.0.1:$gluePort/api/v1/health" $script:glueRun | Out-Null
}
function Read-Target {
    if (-not (Test-Path -LiteralPath $target)) { return $null }
    try { return Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -Depth 100 } catch { return $null }
}
function Get-ManagedModel($Catalog, [string]$Model) {
    if (-not $Catalog) { return $null }
    foreach ($provider in $Catalog.providers.PSObject.Properties) {
        $property = $provider.Value.models.PSObject.Properties | Where-Object Name -EQ $Model | Select-Object -First 1
        if ($property) { return [pscustomobject]@{ Provider = $provider.Name; Model = $property.Value } }
    }
    return $null
}
function Wait-Target([scriptblock]$Condition, [string]$Description, [int]$Seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if ($glueRun.Process.HasExited) { throw "LLM Glue exited early with code $($glueRun.Process.ExitCode)." }
        $catalog = Read-Target
        if (& $Condition $catalog) { return $catalog }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for target state: $Description"
}
function Get-OpenCodeModels {
    $old = @{
        config = $env:OPENCODE_CONFIG
        configHome = $env:XDG_CONFIG_HOME
        dataHome = $env:XDG_DATA_HOME
        cacheHome = $env:XDG_CACHE_HOME
    }
    try {
        $env:OPENCODE_CONFIG = $target
        $env:XDG_CONFIG_HOME = Join-Path $testRoot 'opencode-config'
        $env:XDG_DATA_HOME = Join-Path $testRoot 'opencode-data'
        $env:XDG_CACHE_HOME = Join-Path $testRoot 'opencode-cache'
        New-Item -ItemType Directory -Force -Path $env:XDG_CONFIG_HOME,$env:XDG_DATA_HOME,$env:XDG_CACHE_HOME | Out-Null
        $models = @(& $OpenCodeExecutable --standalone models)
        if ($LASTEXITCODE -ne 0) { throw 'OpenCode model listing failed.' }
        return $models
    } finally {
        $env:OPENCODE_CONFIG = $old.config
        $env:XDG_CONFIG_HOME = $old.configHome
        $env:XDG_DATA_HOME = $old.dataHome
        $env:XDG_CACHE_HOME = $old.cacheHome
    }
}

New-Item -ItemType Directory -Path $testRoot,$modelStore | Out-Null
$ollamaPort = Get-FreePort
$gluePort = Get-FreePort
$productionFingerprintBefore = Get-ProductionStoreFingerprint
$productionTagsBefore = @((Invoke-RestMethod http://127.0.0.1:11434/api/tags -TimeoutSec 5).models.name | Sort-Object)
$content = @(
    '[roles]','agent = true','server = true','client = true','',
    '[agent]','discovery_interval_seconds = 1','removal_grace_seconds = 4','',
    '[[agent.providers]]','type = "ollama"','name = "lifecycle"',("url = `"http://127.0.0.1:$ollamaPort`""),'probe_mode = "off"','',
    '[server]',("listen = `"http://127.0.0.1:$gluePort`""),('state_directory = ' + (Quote-Toml (Join-Path $testRoot 'state'))),'',
    '[client]',("source = `"http://127.0.0.1:$gluePort`""),'poll_interval_seconds = 1','','[[client.targets]]','type = "opencode"',('path = ' + (Quote-Toml $target)),'',
    '[logging]','level = "Debug"',('file = ' + (Quote-Toml (Join-Path $testRoot 'logs\llm-glue.log')))
)
$content | Set-Content -LiteralPath $config -Encoding utf8NoBOM

try {
    foreach ($directoryName in @('manifests', 'manifests-v2', 'metadata')) {
        $source = Join-Path $sourceModels $directoryName
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $modelStore -Recurse }
    }
    $blobDestination = Join-Path $modelStore 'blobs'
    New-Item -ItemType Directory -Path $blobDestination | Out-Null
    foreach ($blob in Get-ChildItem -LiteralPath (Join-Path $sourceModels 'blobs') -File) {
        New-Item -ItemType HardLink -Path (Join-Path $blobDestination $blob.Name) -Target $blob.FullName | Out-Null
    }

    Start-IsolatedOllama
    $initial = Invoke-RestMethod -Uri "http://127.0.0.1:$ollamaPort/api/tags" -TimeoutSec 5
    if ($initial.models.name -notcontains $FirstModel -or $initial.models.name -notcontains $SecondModel) { throw 'Isolated Ollama store is missing source models.' }
    & $Executable config validate --config $config
    if ($LASTEXITCODE -ne 0) { throw 'Lifecycle test configuration is invalid.' }
    Start-Glue
    Wait-Target { param($catalog) (Get-ManagedModel $catalog $FirstModel) } 'initial catalog' | Out-Null

    Invoke-IsolatedOllama @('cp', $FirstModel, $Alias) | Out-Null
    $added = Wait-Target { param($catalog) $entry = Get-ManagedModel $catalog $Alias; $entry -and @($entry.Model.variants.id) -contains 'thinking-on' } 'added alias with thinking variants'
    $addedEntry = Get-ManagedModel $added $Alias
    $models = Get-OpenCodeModels
    if ($models -notcontains "$($addedEntry.Provider)/$Alias" -or $models -contains "ollama/$Alias") { throw 'OpenCode add propagation or duplicate suppression failed.' }
    $events.Add('add: thinking alias became selectable without a native duplicate')

    Stop-Captured $glueRun
    $glueRun = $null
    Invoke-IsolatedOllama @('rm', $Alias) | Out-Null
    Invoke-IsolatedOllama @('cp', $SecondModel, $Alias) | Out-Null
    Start-Glue
    $changed = Wait-Target { param($catalog) $entry = Get-ManagedModel $catalog $Alias; $entry -and $entry.Model.family -eq 'qwen3vl' -and -not $entry.Model.variants } 'changed alias metadata after LLM Glue restart'
    $events.Add('change/restart: alias changed family and reasoning modes after application restart')

    $logFile = Join-Path $testRoot 'logs\llm-glue.log'
    Stop-Captured $ollamaRun
    $ollamaRun = $null
    $failureDeadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        if ((Test-Path -LiteralPath $logFile) -and (Select-String -LiteralPath $logFile -SimpleMatch 'discovery failed' -Quiet)) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $failureDeadline)
    if (-not ((Test-Path -LiteralPath $logFile) -and (Select-String -LiteralPath $logFile -SimpleMatch 'discovery failed' -Quiet))) { throw 'No provider outage was observed in the LLM Glue log.' }
    if (-not (Get-ManagedModel (Read-Target) $Alias)) { throw 'Transient provider outage removed the last-known-good alias before grace expiry.' }
    Start-IsolatedOllama
    Wait-Target { param($catalog) Get-ManagedModel $catalog $Alias } 'alias recovery after provider restart' | Out-Null
    $events.Add('outage/recovery: discovery failure retained the alias through grace and recovered')

    Invoke-IsolatedOllama @('rm', $Alias) | Out-Null
    Wait-Target { param($catalog) -not (Get-ManagedModel $catalog $Alias) } 'alias removal after grace' 20 | Out-Null
    if ((Get-OpenCodeModels) -match [regex]::Escape("/$Alias")) { throw 'Removed alias remained visible in OpenCode.' }
    $events.Add('remove: alias disappeared from the generated target and OpenCode')

    $remaining = (Invoke-RestMethod -Uri "http://127.0.0.1:$ollamaPort/api/tags" -TimeoutSec 5).models.name
    foreach ($model in $remaining) { Invoke-IsolatedOllama @('rm', [string]$model) | Out-Null }
    $empty = Wait-Target {
        param($catalog)
        $hasDisabled = $catalog -and $null -ne $catalog.PSObject.Properties['disabled_providers']
        $catalog -and @($catalog.providers.PSObject.Properties).Count -eq 0 -and (-not $hasDisabled -or @($catalog.disabled_providers).Count -eq 0)
    } 'empty catalog and native provider re-enabled' 20
    $models = Get-OpenCodeModels
    if ($models -notcontains "ollama/$FirstModel" -or $models -match '^llm-glue-ollama-lifecycle/') { throw 'Native Ollama provider was not restored after the managed catalog became empty.' }
    $ownership = Get-Content -LiteralPath ($target + '.llm-glue-ownership.json') -Raw | ConvertFrom-Json
    if (@($ownership.disabledProviders).Count -ne 0) { throw 'Owned native-provider disablement was not released.' }
    $events.Add('empty/re-enable: removing all isolated models restored the native OpenCode provider')

    $productionFingerprintAfter = Get-ProductionStoreFingerprint
    $productionTagsAfter = @((Invoke-RestMethod http://127.0.0.1:11434/api/tags -TimeoutSec 5).models.name | Sort-Object)
    if ($productionFingerprintAfter -ne $productionFingerprintBefore -or (Compare-Object $productionTagsBefore $productionTagsAfter)) {
        throw 'Production Ollama model store changed during the isolated lifecycle test.'
    }

    [ordered]@{
        passed = $true
        timestamp = (Get-Date -Format o)
        ollamaVersion = (Invoke-RestMethod -Uri "http://127.0.0.1:$ollamaPort/api/version").version
        openCodeVersion = (& $OpenCodeExecutable --version)
        llmGlueVersion = (& $Executable --version)
        sourceModels = @($FirstModel, $SecondModel)
        alias = $Alias
        events = $events
        productionModelStoreModified = $false
        productionStoreFingerprint = $productionFingerprintAfter
        productionConfigModified = $false
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
    Write-Host "PASS isolated native Ollama catalog lifecycle. Evidence: $resultPath"
} catch {
    [ordered]@{ passed = $false; timestamp = (Get-Date -Format o); error = $_.Exception.Message; events = $events } |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
    throw
} finally {
    Stop-Captured $glueRun
    Stop-Captured $ollamaRun
    $resolvedStore = [IO.Path]::GetFullPath($modelStore)
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStore.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing unsafe isolated model-store cleanup.' }
    if (Test-Path -LiteralPath $resolvedStore) { Remove-Item -LiteralPath $resolvedStore -Recurse -Force }
}
