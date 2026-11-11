[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'LLMGlue'),
    [ValidateSet('remote-client','standalone-ollama','central-llama')][string]$Mode = 'remote-client',
    [string]$ServerUrl = 'https://replace-with-your-server',
    [string]$ServerToken,
    [string]$OllamaUrl = 'http://127.0.0.1:11434',
    [string]$LlamaUrl = 'http://127.0.0.1:8081',
    [string]$ModelDirectory = 'D:\Models\GGUF',
    [string]$ListenUrl = 'http://0.0.0.0:8765',
    [string]$ReadToken,
    [string]$WriteToken,
    [string]$OpenCodePath = 'C:\ProgramData\opencode\opencode.jsonc',
    [string]$LiteLlmGeneratedPath = 'C:\ProgramData\LiteLLM\llm-glue-models.yaml',
    [string]$LiteLlmPrimaryPath = 'C:\ProgramData\LiteLLM\config.yaml',
    [string]$LiteLlmService = 'LiteLLM',
    [string]$LiteLlmUrl = 'http://replace-with-your-litellm:4000',
    [string]$LiteLlmHealthUrl = 'http://127.0.0.1:4000/health/liveliness',
    [switch]$InstallService,
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
if (-not $NonInteractive) {
    $answer = Read-Host "Installation mode [remote-client/standalone-ollama/central-llama] ($Mode)"
    if ($answer) { if ($answer -notin 'remote-client','standalone-ollama','central-llama') { throw 'Invalid installation mode.' }; $Mode = $answer }
    if ($Mode -eq 'remote-client') {
        $answer = Read-Host "LLM Glue server URL ($ServerUrl)"; if ($answer) { $ServerUrl = $answer }
        $secret = Read-Host 'Server read token (leave blank to use LLM_GLUE_READ_TOKEN at runtime)' -AsSecureString
        if ($secret.Length) { $ServerToken = [Net.NetworkCredential]::new('', $secret).Password }
        $answer = Read-Host "OpenCode system config path ($OpenCodePath)"; if ($answer) { $OpenCodePath = $answer }
    } elseif ($Mode -eq 'standalone-ollama') {
        $answer = Read-Host "Ollama URL ($OllamaUrl)"; if ($answer) { $OllamaUrl = $answer }
        $answer = Read-Host "OpenCode system config path ($OpenCodePath)"; if ($answer) { $OpenCodePath = $answer }
    } else {
        $answer = Read-Host "llama.cpp URL ($LlamaUrl)"; if ($answer) { $LlamaUrl = $answer }
        $answer = Read-Host "GGUF model directory ($ModelDirectory)"; if ($answer) { $ModelDirectory = $answer }
        $answer = Read-Host "LLM Glue listen URL ($ListenUrl)"; if ($answer) { $ListenUrl = $answer }
        $secret = Read-Host 'Remote client read token' -AsSecureString; if ($secret.Length) { $ReadToken = [Net.NetworkCredential]::new('', $secret).Password }
        $secret = Read-Host 'Remote agent write token' -AsSecureString; if ($secret.Length) { $WriteToken = [Net.NetworkCredential]::new('', $secret).Password }
        $answer = Read-Host "LiteLLM primary config path ($LiteLlmPrimaryPath)"; if ($answer) { $LiteLlmPrimaryPath = $answer }
        $answer = Read-Host "LLM Glue LiteLLM include path ($LiteLlmGeneratedPath)"; if ($answer) { $LiteLlmGeneratedPath = $answer }
        $answer = Read-Host "LiteLLM Windows service ($LiteLlmService)"; if ($answer) { $LiteLlmService = $answer }
        $answer = Read-Host "Externally reachable LiteLLM URL for OpenCode clients ($LiteLlmUrl)"; if ($answer) { $LiteLlmUrl = $answer }
        $answer = Read-Host "LiteLLM health URL ($LiteLlmHealthUrl)"; if ($answer) { $LiteLlmHealthUrl = $answer }
    }
    if (-not $InstallService) { $InstallService = (Read-Host 'Register the Windows service? [y/N]') -match '^[Yy]$' }
}
if ($InstallService -and $Mode -eq 'remote-client' -and -not $ServerToken) { throw 'Service-mode remote-client requires -ServerToken; Windows services do not inherit the installer environment.' }
if ($NonInteractive -and $Mode -eq 'remote-client' -and $ServerUrl -eq 'https://replace-with-your-server') { throw 'Noninteractive remote-client installation requires -ServerUrl.' }
if ($Mode -eq 'central-llama' -and (-not $ReadToken -or -not $WriteToken)) { throw 'central-llama requires -ReadToken and -WriteToken.' }
if ($Mode -eq 'central-llama' -and $LiteLlmUrl -eq 'http://replace-with-your-litellm:4000') { throw 'central-llama requires -LiteLlmUrl reachable by remote OpenCode clients.' }
if ($InstallService) {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Service installation requires an elevated PowerShell session. Re-run the inspected script with Run as administrator.' }
}
function ConvertTo-TomlString([string]$Value) {
    if ($Value -match "[\r\n]") { throw 'Configuration values cannot contain line breaks.' }
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}
$Repository = 'bobapplemac/llm-glue'
$api = "https://api.github.com/repos/$Repository/releases/latest"
$headers = @{ 'User-Agent' = 'llm-glue-bootstrap' }
$release = Invoke-RestMethod -Uri $api -Headers $headers
if ($release.prerelease -or $release.draft -or $release.tag_name -notmatch '^r[1-9]\d*$') { throw 'Latest release is not an official stable rN release.' }
$zip = $release.assets | Where-Object name -EQ "llm-glue-$($release.tag_name)-win-x64.zip" | Select-Object -First 1
$checksums = $release.assets | Where-Object name -EQ 'SHA256SUMS' | Select-Object -First 1
if (-not $zip -or -not $checksums) { throw 'Release is missing the Windows archive or SHA256SUMS.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) "llm-glue-install-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = Join-Path $temporary $zip.name
    $manifest = Join-Path $temporary 'SHA256SUMS'
    Invoke-WebRequest -Uri $zip.browser_download_url -Headers $headers -OutFile $archive
    Invoke-WebRequest -Uri $checksums.browser_download_url -Headers $headers -OutFile $manifest
    $line = Get-Content -LiteralPath $manifest | Where-Object { $_ -match "\s+$([regex]::Escape($zip.name))$" } | Select-Object -First 1
    if (-not $line) { throw 'Checksum manifest does not name the Windows archive.' }
    $expected = ($line -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw 'Release archive checksum verification failed.' }
    $stage = Join-Path $temporary 'stage'
    Expand-Archive -LiteralPath $archive -DestinationPath $stage
    $stagedExecutable = Join-Path $stage 'llm-glue.exe'
    & $stagedExecutable --version
    if ($LASTEXITCODE -ne 0) { throw 'Checksum-verified executable failed its version check.' }
    $config = Join-Path $InstallDirectory 'llm-glue.conf'
    $candidate = Join-Path $temporary 'llm-glue.conf'
    if ($Mode -eq 'remote-client') {
        $tokenLine = if ($ServerToken) { 'token = ' + (ConvertTo-TomlString $ServerToken) } else { 'token_environment = "LLM_GLUE_READ_TOKEN"' }
        $content = @('[roles]','agent = false','server = false','client = true','','[client]',('source = ' + (ConvertTo-TomlString $ServerUrl)),$tokenLine,'','[[client.targets]]','type = "opencode"',('path = ' + (ConvertTo-TomlString $OpenCodePath)))
    } elseif ($Mode -eq 'standalone-ollama') {
        $statePath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\state' } else { 'state' }
        $logPath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\logs\llm-glue.log' } else { 'logs\llm-glue.log' }
        $content = @('[roles]','agent = true','server = true','client = true','','[[agent.providers]]','name = "local-ollama"','type = "ollama"',('url = ' + (ConvertTo-TomlString $OllamaUrl)),'','[server]','listen = "http://127.0.0.1:8765"',('state_directory = ' + (ConvertTo-TomlString $statePath)),'','[client]','source = "http://127.0.0.1:8765"','','[[client.targets]]','type = "opencode"',('path = ' + (ConvertTo-TomlString $OpenCodePath)),'','[logging]',('file = ' + (ConvertTo-TomlString $logPath)))
    } else {
        $listen = [Uri]$ListenUrl
        $localSource = "http://127.0.0.1:$($listen.Port)"
        $statePath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\state' } else { 'state' }
        $logPath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\logs\llm-glue.log' } else { 'logs\llm-glue.log' }
        $content = @('[roles]','agent = true','server = true','client = true','','[[agent.providers]]','name = "local-llama"','type = "llama.cpp"',('url = ' + (ConvertTo-TomlString $LlamaUrl)),('model_directories = [' + (ConvertTo-TomlString $ModelDirectory) + ']'),'inspect_gguf = true','hash_model_files = false','probe_mode = "loaded-only"','','[server]',('listen = ' + (ConvertTo-TomlString $ListenUrl)),('opencode_inference_url = ' + (ConvertTo-TomlString $LiteLlmUrl)),('read_token = ' + (ConvertTo-TomlString $ReadToken)),('write_token = ' + (ConvertTo-TomlString $WriteToken)),'acknowledge_insecure_remote_http = true',('state_directory = ' + (ConvertTo-TomlString $statePath)),'','[client]',('source = ' + (ConvertTo-TomlString $localSource)),('token = ' + (ConvertTo-TomlString $ReadToken)),'','[[client.targets]]','type = "litellm"',('path = ' + (ConvertTo-TomlString $LiteLlmGeneratedPath)),('primary_config_path = ' + (ConvertTo-TomlString $LiteLlmPrimaryPath)),('service_name = ' + (ConvertTo-TomlString $LiteLlmService)),('health_url = ' + (ConvertTo-TomlString $LiteLlmHealthUrl)),'','[logging]',('file = ' + (ConvertTo-TomlString $logPath)))
    }
    $content | Set-Content -LiteralPath $candidate -Encoding utf8NoBOM
    & $stagedExecutable config validate --config $candidate
    if ($LASTEXITCODE -ne 0) { throw 'Generated configuration failed validation before installation.' }
    if (Test-Path $config) {
        & $stagedExecutable config validate --config $config
        if ($LASTEXITCODE -ne 0) { throw 'Existing configuration is invalid; it was not modified.' }
        Write-Warning "Preserving existing configuration $config"
    }
    New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
    $executable = Join-Path $InstallDirectory 'llm-glue.exe'
    if (Test-Path $executable) { Copy-Item -LiteralPath $executable -Destination "$executable.rollback" -Force }
    Copy-Item -LiteralPath $stagedExecutable -Destination $executable -Force
    if (-not (Test-Path $config)) {
        Copy-Item -LiteralPath $candidate -Destination $config
        $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        & icacls.exe $config '/inheritance:r' '/grant:r' "*$currentSid`:(F)" '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not protect the generated sidecar ACL.' }
    }
    if ($InstallService) { & $executable service install --config $config; if ($LASTEXITCODE -ne 0) { throw 'Service installation failed.' } }
    Write-Host "Installed $($release.tag_name) to $InstallDirectory with config $config"
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
