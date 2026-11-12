[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [ValidateSet('remote-client','standalone-ollama','central-llama','custom')][string]$Mode = 'remote-client',
    [ValidateSet('agent','server','client')][string[]]$Roles = @(),
    [ValidateSet('ollama','llama.cpp')][string[]]$AgentProviders = @(),
    [ValidateSet('opencode','litellm')][string[]]$ClientTargets = @(),
    [string]$ServerUrl = 'https://replace-with-your-server:8765',
    [string]$ServerToken,
    [string]$InferenceApiKey,
    [string]$OllamaUrl = 'http://127.0.0.1:11434',
    [string]$LlamaUrl = 'http://127.0.0.1:8081',
    [string]$ModelDirectory = 'D:\Models\GGUF',
    [string]$ListenUrl = 'https://0.0.0.0:8765',
    [string]$ReadToken,
    [string]$WriteToken,
    [switch]$GenerateApiKeys,
    [bool]$AllowInvalidServerCertificate = $true,
    [string]$TrustedServerPublicKeySha256,
    [string[]]$TlsServerNames = @(),
    [string]$OpenCodePath = 'C:\ProgramData\opencode\opencode.jsonc',
    [string]$LiteLlmGeneratedPath = 'C:\ProgramData\LiteLLM\llm-glue-models.yaml',
    [string]$LiteLlmPrimaryPath = 'C:\ProgramData\LiteLLM\config.yaml',
    [string]$LiteLlmService = 'LiteLLM',
    [string]$LiteLlmUrl = 'http://replace-with-your-litellm:4000',
    [string]$LiteLlmHealthUrl = 'http://127.0.0.1:4000/health/liveliness',
    [switch]$DoNotEmbedInferenceApiKey,
    [switch]$InstallService,
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
function New-RandomHex([int]$ByteCount = 24) {
    $bytes = New-Object byte[] $ByteCount
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}
$installDirectoryWasSpecified = $PSBoundParameters.ContainsKey('InstallDirectory')
function Test-Role([string]$Name) { return $Roles -contains $Name }
function Find-AgentProviders {
    $found = [Collections.Generic.List[string]]::new()
    if ((Get-Command ollama -ErrorAction SilentlyContinue) -or (Test-Path 'C:\Program Files\Ollama\ollama.exe')) { $found.Add('ollama') }
    if ((Test-Path 'C:\llama.cpp\models.ini') -or (Test-Path 'C:\ProgramData\llama.cpp\models.ini')) { $found.Add('llama.cpp') }
    return $found.ToArray()
}
function Find-ClientTargets {
    $found = [Collections.Generic.List[string]]::new()
    if ((Get-Command opencode -ErrorAction SilentlyContinue) -or (Test-Path 'C:\ProgramData\opencode')) { $found.Add('opencode') }
    if ((Test-Path $LiteLlmPrimaryPath) -or (Get-Service -Name '*litellm*' -ErrorAction SilentlyContinue)) { $found.Add('litellm') }
    return $found.ToArray()
}
if (-not $NonInteractive) {
    $detectedProviders = @(Find-AgentProviders)
    $detectedTargets = @(Find-ClientTargets)
    $defaultRoles = if ($detectedProviders.Count -and $detectedTargets.Count) { 'agent,server,client' } elseif ($detectedProviders.Count) { 'agent' } else { 'client' }
    $answer = Read-Host "Enabled roles, comma separated [agent/server/client] ($defaultRoles)"
    $Roles = @(($(if ($answer) { $answer } else { $defaultRoles }) -split ',') | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
    if (-not $AgentProviders.Count) { $AgentProviders = $detectedProviders }
    if (-not $ClientTargets.Count) { $ClientTargets = $detectedTargets }
    $Mode = 'custom'
    if ($Mode -eq 'remote-client') {
        $answer = Read-Host "LLM Glue server URL ($ServerUrl)"; if ($answer) { $ServerUrl = $answer }
        $secret = Read-Host 'Server read token (leave blank to use LLM_GLUE_READ_TOKEN at runtime)' -AsSecureString
        if ($secret.Length) { $ServerToken = [Net.NetworkCredential]::new('', $secret).Password }
        $answer = Read-Host 'Pinned Server public-key SHA-256 (blank accepts any self-signed certificate)'
        if ($answer) { $TrustedServerPublicKeySha256 = $answer; $AllowInvalidServerCertificate = $false }
        $answer = Read-Host "OpenCode system config path ($OpenCodePath)"; if ($answer) { $OpenCodePath = $answer }
    } elseif ($Mode -eq 'standalone-ollama') {
        $answer = Read-Host "Ollama URL ($OllamaUrl)"; if ($answer) { $OllamaUrl = $answer }
        $answer = Read-Host "OpenCode system config path ($OpenCodePath)"; if ($answer) { $OpenCodePath = $answer }
    } elseif ($Mode -eq 'central-llama') {
        $answer = Read-Host "llama.cpp URL ($LlamaUrl)"; if ($answer) { $LlamaUrl = $answer }
        $answer = Read-Host "GGUF model directory ($ModelDirectory)"; if ($answer) { $ModelDirectory = $answer }
        $answer = Read-Host "LLM Glue listen URL ($ListenUrl)"; if ($answer) { $ListenUrl = $answer }
        $defaultNames = if ($TlsServerNames.Count) { $TlsServerNames -join ',' } else { "localhost,$([Net.Dns]::GetHostName())" }
        $answer = Read-Host "TLS/SNI server names, comma separated ($defaultNames)"
        $selectedNames = if ($answer) { $answer } else { $defaultNames }
        $TlsServerNames = @($selectedNames -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if (-not $GenerateApiKeys) { $GenerateApiKeys = (Read-Host 'Auto-generate 24-byte read/write API keys? [Y/n]') -notmatch '^[Nn]$' }
        if (-not $GenerateApiKeys) {
            $secret = Read-Host 'Remote client read token' -AsSecureString; if ($secret.Length) { $ReadToken = [Net.NetworkCredential]::new('', $secret).Password }
            $secret = Read-Host 'Remote agent write token' -AsSecureString; if ($secret.Length) { $WriteToken = [Net.NetworkCredential]::new('', $secret).Password }
        }
        $answer = Read-Host "LiteLLM primary config path ($LiteLlmPrimaryPath)"; if ($answer) { $LiteLlmPrimaryPath = $answer }
        $answer = Read-Host "LLM Glue LiteLLM include path ($LiteLlmGeneratedPath)"; if ($answer) { $LiteLlmGeneratedPath = $answer }
        $answer = Read-Host "LiteLLM Windows service ($LiteLlmService)"; if ($answer) { $LiteLlmService = $answer }
        $answer = Read-Host "Externally reachable LiteLLM URL for OpenCode clients ($LiteLlmUrl)"; if ($answer) { $LiteLlmUrl = $answer }
        if (-not $DoNotEmbedInferenceApiKey) {
            $secret = Read-Host 'LiteLLM API key to embed in authenticated OpenCode artifacts' -AsSecureString
            if ($secret.Length) { $InferenceApiKey = [Net.NetworkCredential]::new('', $secret).Password }
        }
        $answer = Read-Host "LiteLLM health URL ($LiteLlmHealthUrl)"; if ($answer) { $LiteLlmHealthUrl = $answer }
    } else {
        if ($Roles.Count -eq 0 -or @($Roles | Where-Object { $_ -notin 'agent','server','client' }).Count) { throw 'Invalid role selection.' }
        if (($Roles | Sort-Object -Unique).Count -ne $Roles.Count) { throw 'Duplicate roles are not allowed.' }
        if (Test-Role agent) {
            if (-not $AgentProviders.Count) { $AgentProviders = @('ollama') }
            $defaultProviders = $AgentProviders -join ','
            $answer = Read-Host "Agent providers, comma separated [ollama/llama.cpp] ($defaultProviders)"
            if ($answer) { $AgentProviders = @($answer -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ }) }
            if (@($AgentProviders | Where-Object { $_ -notin 'ollama','llama.cpp' }).Count) { throw 'Invalid Agent provider selection.' }
            if ($AgentProviders -contains 'ollama') { $answer = Read-Host "Ollama URL ($OllamaUrl)"; if ($answer) { $OllamaUrl = $answer } }
            if ($AgentProviders -contains 'llama.cpp') {
                $answer = Read-Host "llama.cpp URL ($LlamaUrl)"; if ($answer) { $LlamaUrl = $answer }
                $answer = Read-Host "GGUF model directory ($ModelDirectory)"; if ($answer) { $ModelDirectory = $answer }
            }
        }
        if (-not (Test-Role server) -and ((Test-Role agent) -or (Test-Role client))) {
            $answer = Read-Host "Remote LLM Glue server URL ($ServerUrl)"; if ($answer) { $ServerUrl = $answer }
            if (Test-Role agent) { $secret = Read-Host 'Remote Server write token' -AsSecureString; if ($secret.Length) { $WriteToken = [Net.NetworkCredential]::new('', $secret).Password } }
            if (Test-Role client) { $secret = Read-Host 'Remote Server read token' -AsSecureString; if ($secret.Length) { $ServerToken = [Net.NetworkCredential]::new('', $secret).Password } }
            $answer = Read-Host 'Pinned Server public-key SHA-256 (blank accepts any self-signed certificate)'
            if ($answer) { $TrustedServerPublicKeySha256 = $answer; $AllowInvalidServerCertificate = $false }
        }
        if (Test-Role server) {
            $answer = Read-Host "LLM Glue listen URL ($ListenUrl)"; if ($answer) { $ListenUrl = $answer }
            $defaultNames = if ($TlsServerNames.Count) { $TlsServerNames -join ',' } else { "localhost,$([Net.Dns]::GetHostName())" }
            $answer = Read-Host "TLS/SNI server names, comma separated ($defaultNames)"
            $TlsServerNames = @(($(if ($answer) { $answer } else { $defaultNames }) -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            if (-not $GenerateApiKeys) { $GenerateApiKeys = (Read-Host 'Auto-generate 24-byte read/write API keys? [Y/n]') -notmatch '^[Nn]$' }
            if (-not $GenerateApiKeys) {
                $secret = Read-Host 'Remote client read token' -AsSecureString; if ($secret.Length) { $ReadToken = [Net.NetworkCredential]::new('', $secret).Password }
                $secret = Read-Host 'Remote Agent write token' -AsSecureString; if ($secret.Length) { $WriteToken = [Net.NetworkCredential]::new('', $secret).Password }
            }
            $answer = Read-Host "Externally reachable LiteLLM URL for generated OpenCode routes ($LiteLlmUrl)"; if ($answer) { $LiteLlmUrl = $answer }
            if (-not $DoNotEmbedInferenceApiKey) { $secret = Read-Host 'LiteLLM API key to embed in authenticated OpenCode artifacts' -AsSecureString; if ($secret.Length) { $InferenceApiKey = [Net.NetworkCredential]::new('', $secret).Password } }
        }
        if (Test-Role client) {
            if (-not $ClientTargets.Count) { $ClientTargets = @('opencode') }
            $defaultTargets = $ClientTargets -join ','
            $answer = Read-Host "Client targets, comma separated [opencode/litellm] ($defaultTargets)"
            if ($answer) { $ClientTargets = @($answer -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ }) }
            if (@($ClientTargets | Where-Object { $_ -notin 'opencode','litellm' }).Count) { throw 'Invalid Client target selection.' }
            if ($ClientTargets -contains 'opencode') { $answer = Read-Host "OpenCode system config path ($OpenCodePath)"; if ($answer) { $OpenCodePath = $answer } }
            if ($ClientTargets -contains 'litellm') {
                $answer = Read-Host "LiteLLM primary config path ($LiteLlmPrimaryPath)"; if ($answer) { $LiteLlmPrimaryPath = $answer }
                $answer = Read-Host "LLM Glue LiteLLM include path ($LiteLlmGeneratedPath)"; if ($answer) { $LiteLlmGeneratedPath = $answer }
                $answer = Read-Host "LiteLLM Windows service ($LiteLlmService)"; if ($answer) { $LiteLlmService = $answer }
                $answer = Read-Host "LiteLLM health URL ($LiteLlmHealthUrl)"; if ($answer) { $LiteLlmHealthUrl = $answer }
            }
        }
    }
    if (-not $InstallService) { $InstallService = (Read-Host 'Register the Windows service? [y/N]') -match '^[Yy]$' }
}
if ($Mode -eq 'remote-client') { $Roles = @('client') }
if ($Mode -in 'standalone-ollama','central-llama') { $Roles = @('agent','server','client') }
if (($Mode -eq 'central-llama' -or ($Mode -eq 'custom' -and (Test-Role server))) -and $TlsServerNames.Count -eq 0) { $TlsServerNames = @('localhost', [Net.Dns]::GetHostName()) }
if (($Mode -eq 'central-llama' -or ($Mode -eq 'custom' -and (Test-Role server))) -and $GenerateApiKeys) {
    if (-not $ReadToken) { $ReadToken = New-RandomHex }
    if (-not $WriteToken) { $WriteToken = New-RandomHex }
}
if ($installDirectoryWasSpecified -and [string]::IsNullOrWhiteSpace($InstallDirectory)) { throw '-InstallDirectory cannot be empty.' }
if (-not $installDirectoryWasSpecified) {
    $perUserInstallDirectory = Join-Path $env:LOCALAPPDATA 'LLMGlue'
    if ($InstallService) {
        $machineInstallDirectory = Join-Path $env:ProgramData 'LLMGlue'
        $legacyInstallationExists = (Test-Path -LiteralPath (Join-Path $perUserInstallDirectory 'llm-glue.exe')) -or
            (Test-Path -LiteralPath (Join-Path $perUserInstallDirectory 'llm-glue.conf')) -or
            (Test-Path -LiteralPath (Join-Path $perUserInstallDirectory 'llm-glue.toml'))
        $machineInstallationExists = (Test-Path -LiteralPath (Join-Path $machineInstallDirectory 'llm-glue.exe')) -or
            (Test-Path -LiteralPath (Join-Path $machineInstallDirectory 'llm-glue.conf')) -or
            (Test-Path -LiteralPath (Join-Path $machineInstallDirectory 'llm-glue.toml'))
        if (-not $machineInstallationExists -and $legacyInstallationExists) {
            $InstallDirectory = $perUserInstallDirectory
            Write-Warning "Preserving existing per-user service installation at $InstallDirectory. Use -InstallDirectory '$machineInstallDirectory' for an explicit migration."
        } else {
            $InstallDirectory = $machineInstallDirectory
        }
    } else {
        $InstallDirectory = $perUserInstallDirectory
    }
}
if ($InstallService -and $Mode -eq 'remote-client' -and -not $ServerToken) { throw 'Service-mode remote-client requires -ServerToken; Windows services do not inherit the installer environment.' }
if ($NonInteractive -and $Mode -eq 'remote-client' -and $ServerUrl -eq 'https://replace-with-your-server:8765') { throw 'Noninteractive remote-client installation requires -ServerUrl.' }
if ($Mode -eq 'central-llama' -and (-not $ReadToken -or -not $WriteToken)) { throw 'central-llama requires -ReadToken and -WriteToken.' }
if ($Mode -eq 'central-llama' -and $LiteLlmUrl -eq 'http://replace-with-your-litellm:4000') { throw 'central-llama requires -LiteLlmUrl reachable by remote OpenCode clients.' }
if ($Mode -eq 'central-llama' -and -not $DoNotEmbedInferenceApiKey -and -not $InferenceApiKey) { throw 'central-llama requires -InferenceApiKey by default. Use -DoNotEmbedInferenceApiKey only when every OpenCode process receives LLM_GLUE_INFERENCE_API_KEY separately.' }
if ($Mode -eq 'custom') {
    if ($Roles.Count -eq 0 -or @($Roles | Where-Object { $_ -notin 'agent','server','client' }).Count) { throw 'custom requires -Roles with agent, server, and/or client.' }
    if (($Roles | Sort-Object -Unique).Count -ne $Roles.Count) { throw 'Duplicate roles are not allowed.' }
    if ((Test-Role agent) -and ($AgentProviders.Count -eq 0 -or @($AgentProviders | Where-Object { $_ -notin 'ollama','llama.cpp' }).Count)) { throw 'custom Agent requires -AgentProviders.' }
    if ((Test-Role client) -and ($ClientTargets.Count -eq 0 -or @($ClientTargets | Where-Object { $_ -notin 'opencode','litellm' }).Count)) { throw 'custom Client requires -ClientTargets.' }
    if (($AgentProviders | Sort-Object -Unique).Count -ne $AgentProviders.Count) { throw 'Duplicate Agent providers are not allowed.' }
    if (($ClientTargets | Sort-Object -Unique).Count -ne $ClientTargets.Count) { throw 'Duplicate Client targets are not allowed.' }
    if (-not (Test-Role server) -and ((Test-Role agent) -or (Test-Role client)) -and $ServerUrl -eq 'https://replace-with-your-server:8765') { throw 'custom remote Agent/Client roles require -ServerUrl.' }
    if (-not (Test-Role server) -and (Test-Role agent) -and -not $WriteToken) { throw 'custom remote Agent requires -WriteToken.' }
    if (-not (Test-Role server) -and (Test-Role client) -and -not $ServerToken) { throw 'custom remote Client requires -ServerToken.' }
    if ((Test-Role server) -and (-not $ReadToken -or -not $WriteToken)) { throw 'custom Server requires read/write tokens or -GenerateApiKeys.' }
    if ((Test-Role server) -and $LiteLlmUrl -eq 'http://replace-with-your-litellm:4000') { throw 'custom Server requires -LiteLlmUrl.' }
    if ((Test-Role server) -and -not $DoNotEmbedInferenceApiKey -and -not $InferenceApiKey) { throw 'custom Server requires -InferenceApiKey by default.' }
}
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
    $confConfig = Join-Path $InstallDirectory 'llm-glue.conf'
    $tomlConfig = Join-Path $InstallDirectory 'llm-glue.toml'
    $config = if (Test-Path -LiteralPath $confConfig) { $confConfig } elseif (Test-Path -LiteralPath $tomlConfig) { $tomlConfig } else { $confConfig }
    $candidate = Join-Path $temporary 'llm-glue.conf'
    if ($Mode -eq 'remote-client') {
        $tokenLine = if ($ServerToken) { 'token = ' + (ConvertTo-TomlString $ServerToken) } else { 'token_environment = "LLM_GLUE_READ_TOKEN"' }
        $trustLine = if ($TrustedServerPublicKeySha256) { 'trusted_server_public_key_sha256 = ' + (ConvertTo-TomlString $TrustedServerPublicKeySha256) } else { 'allow_invalid_server_certificate = ' + $AllowInvalidServerCertificate.ToString().ToLowerInvariant() }
        $content = @('[roles]','agent = false','server = false','client = true','','[client]',('source = ' + (ConvertTo-TomlString $ServerUrl)),$tokenLine,$trustLine,'','[[client.targets]]','type = "opencode"',('path = ' + (ConvertTo-TomlString $OpenCodePath)))
    } elseif ($Mode -eq 'standalone-ollama') {
        $statePath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\state' } else { 'state' }
        $logPath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\logs\llm-glue.log' } else { 'logs\llm-glue.log' }
        $content = @('[roles]','agent = true','server = true','client = true','','[[agent.providers]]','name = "local-ollama"','type = "ollama"',('url = ' + (ConvertTo-TomlString $OllamaUrl)),'','[server]','listen = "https://127.0.0.1:8765"','tls_auto_generate = true','tls_server_names = ["localhost"]',('state_directory = ' + (ConvertTo-TomlString $statePath)),'','[client]','','[[client.targets]]','type = "opencode"',('path = ' + (ConvertTo-TomlString $OpenCodePath)),'','[logging]',('file = ' + (ConvertTo-TomlString $logPath)))
    } elseif ($Mode -eq 'central-llama') {
        $listen = [Uri]$ListenUrl
        $tlsNames = '[' + (($TlsServerNames | ForEach-Object { ConvertTo-TomlString $_ }) -join ', ') + ']'
        $statePath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\state' } else { 'state' }
        $logPath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\logs\llm-glue.log' } else { 'logs\llm-glue.log' }
        $credentialLines = if ($DoNotEmbedInferenceApiKey) { @('embed_opencode_inference_api_key = false') } else { @('embed_opencode_inference_api_key = true', ('opencode_inference_api_key = ' + (ConvertTo-TomlString $InferenceApiKey))) }
        $content = @('[roles]','agent = true','server = true','client = true','','[[agent.providers]]','name = "local-llama"','type = "llama.cpp"',('url = ' + (ConvertTo-TomlString $LlamaUrl)),('model_directories = [' + (ConvertTo-TomlString $ModelDirectory) + ']'),'inspect_gguf = true','hash_model_files = false','probe_mode = "loaded-only"','','[server]',('listen = ' + (ConvertTo-TomlString $ListenUrl)),'tls_auto_generate = true',('tls_server_names = ' + $tlsNames),('opencode_inference_url = ' + (ConvertTo-TomlString $LiteLlmUrl))) + $credentialLines + @(('read_token = ' + (ConvertTo-TomlString $ReadToken)),('write_token = ' + (ConvertTo-TomlString $WriteToken)),('state_directory = ' + (ConvertTo-TomlString $statePath)),'','[client]',('token = ' + (ConvertTo-TomlString $ReadToken)),'','[[client.targets]]','type = "litellm"',('path = ' + (ConvertTo-TomlString $LiteLlmGeneratedPath)),('primary_config_path = ' + (ConvertTo-TomlString $LiteLlmPrimaryPath)),('service_name = ' + (ConvertTo-TomlString $LiteLlmService)),('health_url = ' + (ConvertTo-TomlString $LiteLlmHealthUrl)),'','[logging]',('file = ' + (ConvertTo-TomlString $logPath)))
    } else {
        $statePath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\state' } else { 'state' }
        $logPath = if ($InstallService) { Join-Path $env:ProgramData 'LLMGlue\logs\llm-glue.log' } else { 'logs\llm-glue.log' }
        $content = [Collections.Generic.List[string]]::new()
        $content.Add('[roles]')
        $content.Add('agent = ' + (Test-Role agent).ToString().ToLowerInvariant())
        $content.Add('server = ' + (Test-Role server).ToString().ToLowerInvariant())
        $content.Add('client = ' + (Test-Role client).ToString().ToLowerInvariant())
        if (Test-Role agent) {
            if (-not (Test-Role server)) {
                $content.Add('')
                $content.Add('[agent]')
                $content.Add('submit_to = ' + (ConvertTo-TomlString $ServerUrl))
                $content.Add('write_token = ' + (ConvertTo-TomlString $WriteToken))
                if ($TrustedServerPublicKeySha256) { $content.Add('trusted_server_public_key_sha256 = ' + (ConvertTo-TomlString $TrustedServerPublicKeySha256)) }
                else { $content.Add('allow_invalid_server_certificate = ' + $AllowInvalidServerCertificate.ToString().ToLowerInvariant()) }
            }
            foreach ($provider in $AgentProviders) {
                $content.Add('')
                $content.Add('[[agent.providers]]')
                $content.Add('name = ' + (ConvertTo-TomlString $(if ($provider -eq 'ollama') { 'local-ollama' } else { 'local-llama' })))
                $content.Add('type = ' + (ConvertTo-TomlString $provider))
                if ($provider -eq 'ollama') {
                    $content.Add('url = ' + (ConvertTo-TomlString $OllamaUrl))
                } else {
                    $content.Add('url = ' + (ConvertTo-TomlString $LlamaUrl))
                    $content.Add('model_directories = [' + (ConvertTo-TomlString $ModelDirectory) + ']')
                    $content.Add('inspect_gguf = true')
                    $content.Add('hash_model_files = false')
                    $content.Add('probe_mode = "loaded-only"')
                }
            }
        }
        if (Test-Role server) {
            $tlsNames = '[' + (($TlsServerNames | ForEach-Object { ConvertTo-TomlString $_ }) -join ', ') + ']'
            $content.Add('')
            $content.Add('[server]')
            $content.Add('listen = ' + (ConvertTo-TomlString $ListenUrl))
            $content.Add('opencode_inference_url = ' + (ConvertTo-TomlString $LiteLlmUrl))
            if ($DoNotEmbedInferenceApiKey) { $content.Add('embed_opencode_inference_api_key = false') }
            else {
                $content.Add('embed_opencode_inference_api_key = true')
                $content.Add('opencode_inference_api_key = ' + (ConvertTo-TomlString $InferenceApiKey))
            }
            $content.Add('tls_auto_generate = true')
            $content.Add('tls_server_names = ' + $tlsNames)
            $content.Add('read_token = ' + (ConvertTo-TomlString $ReadToken))
            $content.Add('write_token = ' + (ConvertTo-TomlString $WriteToken))
            $content.Add('state_directory = ' + (ConvertTo-TomlString $statePath))
        }
        if (Test-Role client) {
            $content.Add('')
            $content.Add('[client]')
            if (Test-Role server) { $content.Add('token = ' + (ConvertTo-TomlString $ReadToken)) }
            else {
                $content.Add('source = ' + (ConvertTo-TomlString $ServerUrl))
                $content.Add('token = ' + (ConvertTo-TomlString $ServerToken))
                if ($TrustedServerPublicKeySha256) { $content.Add('trusted_server_public_key_sha256 = ' + (ConvertTo-TomlString $TrustedServerPublicKeySha256)) }
                else { $content.Add('allow_invalid_server_certificate = ' + $AllowInvalidServerCertificate.ToString().ToLowerInvariant()) }
            }
            foreach ($target in $ClientTargets) {
                $content.Add('')
                $content.Add('[[client.targets]]')
                $content.Add('type = ' + (ConvertTo-TomlString $target))
                if ($target -eq 'opencode') { $content.Add('path = ' + (ConvertTo-TomlString $OpenCodePath)) }
                else {
                    $content.Add('path = ' + (ConvertTo-TomlString $LiteLlmGeneratedPath))
                    $content.Add('primary_config_path = ' + (ConvertTo-TomlString $LiteLlmPrimaryPath))
                    $content.Add('service_name = ' + (ConvertTo-TomlString $LiteLlmService))
                    $content.Add('health_url = ' + (ConvertTo-TomlString $LiteLlmHealthUrl))
                }
            }
        }
        $content.Add('')
        $content.Add('[logging]')
        $content.Add('file = ' + (ConvertTo-TomlString $logPath))
    }
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllLines($candidate, [string[]]$content, $utf8NoBom)
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
    if ($InstallService) {
        & $executable service install --config $config
        if ($LASTEXITCODE -ne 0) { throw 'Service installation failed.' }
        & $executable service start --config $config
        if ($LASTEXITCODE -ne 0) { throw 'Service start failed.' }
    }
    if ($Mode -eq 'remote-client' -and $InferenceApiKey) {
        [Environment]::SetEnvironmentVariable('LLM_GLUE_INFERENCE_API_KEY', $InferenceApiKey, 'User')
        $env:LLM_GLUE_INFERENCE_API_KEY = $InferenceApiKey
        Write-Host 'Stored LLM_GLUE_INFERENCE_API_KEY for the current Windows user. Restart OpenCode/Paseo to inherit it.'
    }
    Write-Host "Installed $($release.tag_name) to $InstallDirectory with config $config"
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
