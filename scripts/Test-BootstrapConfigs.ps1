[CmdletBinding()]
param([string]$Binary)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Binary) { $Binary = Join-Path $root 'artifacts\service-check\win-x64\llm-glue.exe' }
$Binary = (Resolve-Path $Binary).Path
$temporary = Join-Path ([IO.Path]::GetTempPath()) "llm-glue-bootstrap-test-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $stage = Join-Path $temporary 'stage'
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $Binary -Destination (Join-Path $stage 'llm-glue.exe')
    $archive = Join-Path $temporary 'llm-glue-r1-win-x64.zip'
    Compress-Archive -Path (Join-Path $stage 'llm-glue.exe') -DestinationPath $archive
    $manifest = Join-Path $temporary 'SHA256SUMS'
    "$(Get-FileHash -Algorithm SHA256 -LiteralPath $archive | Select-Object -ExpandProperty Hash)  llm-glue-r1-win-x64.zip" | Set-Content -LiteralPath $manifest -Encoding ascii
    $global:LlmGlueMockArchive = $archive
    $global:LlmGlueMockManifest = $manifest

    function Invoke-RestMethod {
        param([string]$Uri, $Headers)
        [pscustomobject]@{ prerelease = $false; draft = $false; tag_name = 'r1'; assets = @([pscustomobject]@{ name = 'llm-glue-r1-win-x64.zip'; browser_download_url = 'mock:archive' },[pscustomobject]@{ name = 'SHA256SUMS'; browser_download_url = 'mock:manifest' }) }
    }
    function Invoke-WebRequest {
        param([string]$Uri, $Headers, [string]$OutFile)
        Copy-Item -LiteralPath $(if ($Uri -eq 'mock:archive') { $global:LlmGlueMockArchive } else { $global:LlmGlueMockManifest }) -Destination $OutFile
    }

    $installer = Join-Path $root 'install.ps1'
    $installerSource = Get-Content -LiteralPath $installer -Raw
    if ($installerSource -notmatch '(?s)service install --config \$config.*service start --config \$config') { throw 'Windows service bootstrap does not install and start in sequence.' }
    if ($installerSource -match "LiteLLM inference API key for OpenCode/Paseo") { throw 'Windows remote-client bootstrap still prompts for a separately provisioned inference key.' }
    if ($installerSource -match "\[string\]\`$InstallDirectory\s*=\s*\(Join-Path\s+\`$env:LOCALAPPDATA") { throw 'Windows bootstrap still hard-codes the per-user install directory as its parameter default.' }
    if ($installerSource -notmatch "(?s)if \(\`$InstallService\).*?Join-Path \`$env:ProgramData 'LLMGlue'") { throw 'Windows service bootstrap does not default new installations to ProgramData.' }
    if ($installerSource -notmatch "(?s)legacyInstallationExists.*?Preserving existing per-user service installation") { throw 'Windows bootstrap does not preserve an existing legacy per-user service installation.' }
    if ($installerSource -notmatch "(?s)machineInstallationExists.*?llm-glue\.toml.*?if \(-not \`$machineInstallationExists -and \`$legacyInstallationExists\)") { throw 'Windows bootstrap mistakes an existing ProgramData state/log directory for a machine installation.' }
    $remote = Join-Path $temporary 'remote'
    & $installer -NonInteractive -Mode remote-client -InstallDirectory $remote -ServerUrl https://catalog.example:8765 -ServerToken test-read
    & (Join-Path $remote 'llm-glue.exe') config validate --config (Join-Path $remote 'llm-glue.conf')
    $remoteConfigBytes = [IO.File]::ReadAllBytes((Join-Path $remote 'llm-glue.conf'))
    if ($remoteConfigBytes.Length -ge 3 -and $remoteConfigBytes[0] -eq 0xEF -and $remoteConfigBytes[1] -eq 0xBB -and $remoteConfigBytes[2] -eq 0xBF) { throw 'Generated configuration unexpectedly contains a UTF-8 BOM.' }
    if (-not (Select-String -Quiet -SimpleMatch 'token = "test-read"' (Join-Path $remote 'llm-glue.conf'))) { throw 'Remote token was not generated.' }
    if (-not (Select-String -Quiet -SimpleMatch 'allow_invalid_server_certificate = true' (Join-Path $remote 'llm-glue.conf'))) { throw 'Remote self-signed TLS trust mode was not generated.' }
    if (-not (Select-String -Quiet -SimpleMatch 'C:\\ProgramData\\opencode\\opencode.jsonc' (Join-Path $remote 'llm-glue.conf'))) { throw 'Remote client did not use the system JSONC default.' }
    $remoteHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $remote 'llm-glue.conf')).Hash
    & $installer -NonInteractive -Mode remote-client -InstallDirectory $remote -ServerUrl https://changed.example -ServerToken changed -OpenCodePath (Join-Path $temporary 'changed.json')
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $remote 'llm-glue.conf')).Hash -ne $remoteHash) { throw 'Rerun replaced the existing sidecar.' }
    if (-not (Test-Path -LiteralPath (Join-Path $remote 'llm-glue.exe.rollback'))) { throw 'Rerun did not retain the previous executable.' }

    $tomlInstall = Join-Path $temporary 'existing-toml'
    New-Item -ItemType Directory -Path $tomlInstall | Out-Null
    Copy-Item -LiteralPath (Join-Path $remote 'llm-glue.conf') -Destination (Join-Path $tomlInstall 'llm-glue.toml')
    $tomlHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $tomlInstall 'llm-glue.toml')).Hash
    & $installer -NonInteractive -Mode remote-client -InstallDirectory $tomlInstall -ServerUrl https://changed.example -ServerToken changed
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $tomlInstall 'llm-glue.toml')).Hash -ne $tomlHash) { throw 'Rerun replaced the existing TOML sidecar.' }
    if (Test-Path -LiteralPath (Join-Path $tomlInstall 'llm-glue.conf')) { throw 'Rerun shadowed the existing TOML sidecar with a new CONF file.' }

    $standalone = Join-Path $temporary 'standalone'
    & $installer -NonInteractive -Mode standalone-ollama -InstallDirectory $standalone -OpenCodePath (Join-Path $temporary 'opencode.json')
    & (Join-Path $standalone 'llm-glue.exe') config validate --config (Join-Path $standalone 'llm-glue.conf')
    if (-not (Select-String -Quiet -SimpleMatch '[[agent.providers]]' (Join-Path $standalone 'llm-glue.conf'))) { throw 'Standalone provider was not generated.' }

    $central = Join-Path $temporary 'central'
    & $installer -NonInteractive -Mode central-llama -InstallDirectory $central -GenerateApiKeys -InferenceApiKey test-inference -ListenUrl https://0.0.0.0:9876 -TlsServerNames localhost,catalog.example -LiteLlmUrl http://litellm.test:4000 -ModelDirectory (Join-Path $temporary 'models') -LiteLlmGeneratedPath (Join-Path $temporary 'generated.yaml') -LiteLlmPrimaryPath (Join-Path $temporary 'litellm.yaml')
    & (Join-Path $central 'llm-glue.exe') config validate --config (Join-Path $central 'llm-glue.conf')
    if (-not (Select-String -Quiet -SimpleMatch 'listen = "https://0.0.0.0:9876"' (Join-Path $central 'llm-glue.conf'))) { throw 'Central HTTPS listener was not generated.' }
    if (-not (Select-String -Quiet -SimpleMatch 'tls_server_names = ["localhost", "catalog.example"]' (Join-Path $central 'llm-glue.conf'))) { throw 'Central SNI names were not generated.' }
    $centralText = Get-Content -LiteralPath (Join-Path $central 'llm-glue.conf') -Raw
    if ($centralText -notmatch 'read_token = "[0-9a-f]{48}"' -or $centralText -notmatch 'write_token = "[0-9a-f]{48}"') { throw 'Central 24-byte API keys were not generated.' }
    if (-not (Select-String -Quiet -SimpleMatch 'type = "litellm"' (Join-Path $central 'llm-glue.conf'))) { throw 'Central LiteLLM target was not generated.' }
    if (-not (Select-String -Quiet -SimpleMatch 'opencode_inference_url = "http://litellm.test:4000"' (Join-Path $central 'llm-glue.conf'))) { throw 'Central OpenCode route does not use LiteLLM.' }
    if (-not (Select-String -Quiet -SimpleMatch 'opencode_inference_api_key = "test-inference"' (Join-Path $central 'llm-glue.conf'))) { throw 'Central server did not receive the inference key for authenticated artifact embedding.' }

    foreach ($roleSet in @('agent','server','client','agent,server','agent,client','server,client','agent,server,client')) {
        $selectedRoles = @($roleSet -split ',')
        $custom = Join-Path $temporary ("custom-" + ($selectedRoles -join '-'))
        $arguments = @{
            NonInteractive = $true
            Mode = 'custom'
            Roles = $selectedRoles
            InstallDirectory = $custom
            ServerUrl = 'https://catalog.example:8765'
            ServerToken = 'test-read'
            WriteToken = 'test-write'
            ReadToken = 'test-read'
            InferenceApiKey = 'test-inference'
            LiteLlmUrl = 'http://litellm.test:4000'
            AgentProviders = @('ollama')
            ClientTargets = @('opencode')
            OpenCodePath = (Join-Path $temporary 'custom-opencode.jsonc')
        }
        & $installer @arguments
        & (Join-Path $custom 'llm-glue.exe') config validate --config (Join-Path $custom 'llm-glue.conf')
        $customText = Get-Content -LiteralPath (Join-Path $custom 'llm-glue.conf') -Raw
        foreach ($role in 'agent','server','client') {
            $expected = if ($selectedRoles -contains $role) { 'true' } else { 'false' }
            if ($customText -notmatch "(?m)^$role = $expected\r?$") { throw "Custom role matrix generated the wrong $role value for $roleSet." }
        }
    }

    $multi = Join-Path $temporary 'custom-multi'
    & $installer -NonInteractive -Mode custom -Roles agent,server,client -AgentProviders ollama,llama.cpp -ClientTargets opencode,litellm -InstallDirectory $multi -GenerateApiKeys -InferenceApiKey test-inference -LiteLlmUrl http://litellm.test:4000 -ModelDirectory (Join-Path $temporary 'models') -OpenCodePath (Join-Path $temporary 'multi-opencode.jsonc') -LiteLlmGeneratedPath (Join-Path $temporary 'multi-generated.yaml') -LiteLlmPrimaryPath (Join-Path $temporary 'multi-litellm.yaml')
    & (Join-Path $multi 'llm-glue.exe') config validate --config (Join-Path $multi 'llm-glue.conf')
    $multiText = Get-Content -LiteralPath (Join-Path $multi 'llm-glue.conf') -Raw
    if (($multiText | Select-String -AllMatches -Pattern '\[\[agent\.providers\]\]').Matches.Count -ne 2) { throw 'Custom bootstrap did not generate both selected Agent providers.' }
    if (($multiText | Select-String -AllMatches -Pattern '\[\[client\.targets\]\]').Matches.Count -ne 2) { throw 'Custom bootstrap did not generate both selected Client targets.' }
    Write-Host 'PowerShell bootstrap configuration matrix passed.'
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
