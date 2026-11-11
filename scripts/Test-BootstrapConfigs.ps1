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
    $remote = Join-Path $temporary 'remote'
    & $installer -NonInteractive -Mode remote-client -InstallDirectory $remote -ServerUrl https://catalog.example -ServerToken test-read
    & (Join-Path $remote 'llm-glue.exe') config validate --config (Join-Path $remote 'llm-glue.conf')
    $remoteConfigBytes = [IO.File]::ReadAllBytes((Join-Path $remote 'llm-glue.conf'))
    if ($remoteConfigBytes.Length -ge 3 -and $remoteConfigBytes[0] -eq 0xEF -and $remoteConfigBytes[1] -eq 0xBB -and $remoteConfigBytes[2] -eq 0xBF) { throw 'Generated configuration unexpectedly contains a UTF-8 BOM.' }
    if (-not (Select-String -Quiet -SimpleMatch 'token = "test-read"' (Join-Path $remote 'llm-glue.conf'))) { throw 'Remote token was not generated.' }
    if (-not (Select-String -Quiet -SimpleMatch 'C:\\ProgramData\\opencode\\opencode.jsonc' (Join-Path $remote 'llm-glue.conf'))) { throw 'Remote client did not use the system JSONC default.' }
    $remoteHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $remote 'llm-glue.conf')).Hash
    & $installer -NonInteractive -Mode remote-client -InstallDirectory $remote -ServerUrl https://changed.example -ServerToken changed -OpenCodePath (Join-Path $temporary 'changed.json')
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $remote 'llm-glue.conf')).Hash -ne $remoteHash) { throw 'Rerun replaced the existing sidecar.' }
    if (-not (Test-Path -LiteralPath (Join-Path $remote 'llm-glue.exe.rollback'))) { throw 'Rerun did not retain the previous executable.' }

    $standalone = Join-Path $temporary 'standalone'
    & $installer -NonInteractive -Mode standalone-ollama -InstallDirectory $standalone -OpenCodePath (Join-Path $temporary 'opencode.json')
    & (Join-Path $standalone 'llm-glue.exe') config validate --config (Join-Path $standalone 'llm-glue.conf')
    if (-not (Select-String -Quiet -SimpleMatch '[[agent.providers]]' (Join-Path $standalone 'llm-glue.conf'))) { throw 'Standalone provider was not generated.' }

    $central = Join-Path $temporary 'central'
    & $installer -NonInteractive -Mode central-llama -InstallDirectory $central -ReadToken test-read -WriteToken test-write -ListenUrl http://0.0.0.0:9876 -LiteLlmUrl http://litellm.test:4000 -ModelDirectory (Join-Path $temporary 'models') -LiteLlmGeneratedPath (Join-Path $temporary 'generated.yaml') -LiteLlmPrimaryPath (Join-Path $temporary 'litellm.yaml')
    & (Join-Path $central 'llm-glue.exe') config validate --config (Join-Path $central 'llm-glue.conf')
    if (-not (Select-String -Quiet -SimpleMatch 'source = "http://127.0.0.1:9876"' (Join-Path $central 'llm-glue.conf'))) { throw 'Central loopback source did not follow the listen port.' }
    if (-not (Select-String -Quiet -SimpleMatch 'type = "litellm"' (Join-Path $central 'llm-glue.conf'))) { throw 'Central LiteLLM target was not generated.' }
    if (-not (Select-String -Quiet -SimpleMatch 'opencode_inference_url = "http://litellm.test:4000"' (Join-Path $central 'llm-glue.conf'))) { throw 'Central OpenCode route does not use LiteLLM.' }
    Write-Host 'PowerShell bootstrap configuration matrix passed.'
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
