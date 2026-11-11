[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'LLMGlue'),
    [ValidateSet('remote-client','standalone-ollama')][string]$Mode = 'remote-client',
    [string]$ServerUrl = 'https://replace-with-your-server',
    [string]$OllamaUrl = 'http://127.0.0.1:11434',
    [string]$OpenCodePath = 'C:\ProgramData\opencode\opencode.json',
    [switch]$InstallService,
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
if (-not $NonInteractive) {
    $answer = Read-Host "Installation mode [remote-client/standalone-ollama] ($Mode)"
    if ($answer) { if ($answer -notin 'remote-client','standalone-ollama') { throw 'Invalid installation mode.' }; $Mode = $answer }
    if ($Mode -eq 'remote-client') { $answer = Read-Host "LLM Glue server URL ($ServerUrl)"; if ($answer) { $ServerUrl = $answer } }
    else { $answer = Read-Host "Ollama URL ($OllamaUrl)"; if ($answer) { $OllamaUrl = $answer } }
    $answer = Read-Host "OpenCode system config path ($OpenCodePath)"; if ($answer) { $OpenCodePath = $answer }
    if (-not $InstallService) { $InstallService = (Read-Host 'Register the Windows service? [y/N]') -match '^[Yy]$' }
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
    New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
    $executable = Join-Path $InstallDirectory 'llm-glue.exe'
    if (Test-Path $executable) { Copy-Item -LiteralPath $executable -Destination "$executable.rollback" -Force }
    Copy-Item -LiteralPath (Join-Path $stage 'llm-glue.exe') -Destination $executable -Force
    $config = Join-Path $InstallDirectory 'llm-glue.conf'
    if (-not (Test-Path $config)) {
        if ($Mode -eq 'remote-client') {
            @"
[roles]
agent = false
server = false
client = true

[client]
source = "$ServerUrl"
token_environment = "LLM_GLUE_READ_TOKEN"

[[client.targets]]
type = "opencode"
path = '$OpenCodePath'
"@ | Set-Content -LiteralPath $config -Encoding utf8NoBOM
        } else {
            @"
[roles]
agent = true
server = true
client = true

[[providers]]
name = "local-ollama"
type = "ollama"
url = "$OllamaUrl"

[client]
poll_interval_seconds = 30

[[client.targets]]
type = "opencode"
path = '$OpenCodePath'
"@ | Set-Content -LiteralPath $config -Encoding utf8NoBOM
        }
        Write-Warning "Created $config. Validate it before starting LLM Glue."
    }
    & $executable --version
    if ($LASTEXITCODE -ne 0) { throw 'Installed executable failed its version check.' }
    & $executable config validate --config $config
    if ($LASTEXITCODE -ne 0) { throw 'Generated configuration failed validation.' }
    if ($InstallService) { & $executable service install --config $config; if ($LASTEXITCODE -ne 0) { throw 'Service installation failed.' } }
    Write-Host "Installed $($release.tag_name) to $InstallDirectory"
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
