[CmdletBinding()]
param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\artifacts\final-qualification\publish\win-x64\llm-glue.exe'),
    [string]$OllamaExecutable = (Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'),
    [string]$OpenCodeExecutable = (Join-Path $env:APPDATA 'npm\opencode.cmd'),
    [string]$Model = 'qwen3.5:4b-q4_K_M',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\windows-ollama-opencode')
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$OllamaExecutable = (Resolve-Path -LiteralPath $OllamaExecutable).Path
$OpenCodeExecutable = (Resolve-Path -LiteralPath $OpenCodeExecutable).Path
$testRoot = [IO.Path]::GetFullPath((Join-Path $OutputDirectory ([guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$config = Join-Path $testRoot 'llm-glue.conf'
$target = Join-Path $testRoot 'opencode.json'
$stdout = Join-Path $testRoot 'llm-glue.stdout.log'
$stderr = Join-Path $testRoot 'llm-glue.stderr.log'
$openCodeOutput = Join-Path $testRoot 'opencode-output.jsonl'
$resultPath = Join-Path $testRoot 'result.json'
$process = $null
$wasInitiallyLoaded = $false

function Quote-Toml([string]$Value) { '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"' }
function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port } finally { $listener.Stop() }
}
function Wait-ForHealthy([int]$Port) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if ($process.HasExited) { throw "LLM Glue exited early with code $($process.ExitCode)." }
        try {
            $response = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/api/v1/health" -TimeoutSec 2
            if ($response.status -eq 'healthy') { return }
        } catch { Start-Sleep -Milliseconds 250 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'LLM Glue did not become healthy within 45 seconds.'
}
function Wait-ForCatalog {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        if ($process.HasExited) { throw "LLM Glue exited early with code $($process.ExitCode)." }
        if (Test-Path -LiteralPath $target) {
            try {
                $catalog = Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -Depth 50
                $provider = $catalog.providers.PSObject.Properties | Where-Object {
                    $_.Value.models.PSObject.Properties.Name -contains $Model
                } | Select-Object -First 1
                if ($provider) { return [pscustomobject]@{ Catalog = $catalog; Provider = $provider } }
            } catch { }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Generated OpenCode catalog did not contain $Model within 60 seconds."
}

$port = Get-FreePort
$content = @(
    '[roles]','agent = true','server = true','client = true','',
    '[[agent.providers]]','type = "ollama"','name = "windows-live"','url = "http://127.0.0.1:11434"','probe_mode = "off"','',
    '[server]',("listen = `"http://127.0.0.1:$port`""),('state_directory = ' + (Quote-Toml (Join-Path $testRoot 'state'))),'',
    '[client]',("source = `"http://127.0.0.1:$port`""),'poll_interval_seconds = 1','','[[client.targets]]','type = "opencode"',('path = ' + (Quote-Toml $target)),'',
    '[logging]','level = "Debug"',('file = ' + (Quote-Toml (Join-Path $testRoot 'logs\llm-glue.log')))
)
$content | Set-Content -LiteralPath $config -Encoding utf8NoBOM

try {
    & $OllamaExecutable --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Installed Ollama executable failed.' }
    & $OpenCodeExecutable --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Installed OpenCode executable failed.' }
    $available = Invoke-RestMethod -Uri http://127.0.0.1:11434/api/tags -TimeoutSec 5
    if ($available.models.name -notcontains $Model) { throw "Ollama model is not installed: $Model" }
    $initialState = Invoke-RestMethod -Uri http://127.0.0.1:11434/api/ps -TimeoutSec 5
    $wasInitiallyLoaded = $initialState.models.name -contains $Model
    & $Executable config validate --config $config
    if ($LASTEXITCODE -ne 0) { throw 'Generated live-test configuration is invalid.' }

    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.ArgumentList.Add('run')
    $start.ArgumentList.Add('--config')
    $start.ArgumentList.Add($config)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    Wait-ForHealthy $port
    $published = Wait-ForCatalog
    $providerId = $published.Provider.Name
    $modelConfig = $published.Provider.Value.models.PSObject.Properties[$Model].Value
    $offVariant = $modelConfig.variants | Where-Object id -EQ 'thinking-off' | Select-Object -First 1
    $onVariant = $modelConfig.variants | Where-Object id -EQ 'thinking-on' | Select-Object -First 1
    if (-not $offVariant -or -not $onVariant) { throw "Generated model did not contain both verified thinking variants." }
    $selection = "$providerId/$Model#thinking-on"

    $oldConfig = $env:OPENCODE_CONFIG
    $oldConfigHome = $env:XDG_CONFIG_HOME
    $oldDataHome = $env:XDG_DATA_HOME
    $oldCacheHome = $env:XDG_CACHE_HOME
    try {
        $env:OPENCODE_CONFIG = $target
        $env:XDG_CONFIG_HOME = Join-Path $testRoot 'opencode-config'
        $env:XDG_DATA_HOME = Join-Path $testRoot 'opencode-data'
        $env:XDG_CACHE_HOME = Join-Path $testRoot 'opencode-cache'
        New-Item -ItemType Directory -Force -Path $env:XDG_CONFIG_HOME,$env:XDG_DATA_HOME,$env:XDG_CACHE_HOME | Out-Null
        $models = & $OpenCodeExecutable --standalone models
        if ($LASTEXITCODE -ne 0 -or $models -notcontains "$providerId/$Model") {
            throw "Installed OpenCode did not expose generated model $providerId/$Model."
        }
        $prompt = 'Reply with exactly LLM_GLUE_WINDOWS_OK and nothing else.'
        $runOutput = & $OpenCodeExecutable --standalone run --model $selection --format json --archived $prompt 2>&1
        $runOutput | Set-Content -LiteralPath $openCodeOutput -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw "OpenCode inference failed: $($runOutput -join [Environment]::NewLine)" }
        $responseText = $runOutput -join [Environment]::NewLine
        if ($responseText -notmatch 'LLM_GLUE_WINDOWS_OK') { throw "OpenCode response did not contain the expected marker: $responseText" }
    } finally {
        $env:OPENCODE_CONFIG = $oldConfig
        $env:XDG_CONFIG_HOME = $oldConfigHome
        $env:XDG_DATA_HOME = $oldDataHome
        $env:XDG_CACHE_HOME = $oldCacheHome
    }

    $result = [ordered]@{
        passed = $true
        timestamp = (Get-Date -Format o)
        ollamaVersion = (Invoke-RestMethod -Uri http://127.0.0.1:11434/api/version).version
        openCodeVersion = (& $OpenCodeExecutable --version)
        model = $Model
        provider = $providerId
        selection = $selection
        variants = @('thinking-off', 'thinking-on')
        target = $target
        productionConfigModified = $false
        modelInitiallyLoaded = $wasInitiallyLoaded
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
    Write-Host "PASS native Windows Ollama/OpenCode inference. Evidence: $resultPath"
} catch {
    [ordered]@{ passed = $false; timestamp = (Get-Date -Format o); error = $_.Exception.Message } |
        ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
    throw
} finally {
    try {
        if (-not $wasInitiallyLoaded) {
            $unloadBody = @{ model = $Model; keep_alive = 0 } | ConvertTo-Json -Compress
            Invoke-RestMethod -Method Post -Uri http://127.0.0.1:11434/api/generate -ContentType application/json -Body $unloadBody -TimeoutSec 15 | Out-Null
        }
    } catch { }
    if ($process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(10000) | Out-Null
    }
    if ($process) {
        $stdoutTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stdout -Encoding utf8NoBOM
        $stderrTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stderr -Encoding utf8NoBOM
        $process.Dispose()
    }
}
