param(
    [string]$Binary = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\service-check\win-x64\llm-glue.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Binary -PathType Leaf)) { throw "Publish a Windows test binary first: $Binary" }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('llm-glue-https-live-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$server = Join-Path $temporary 'server.conf'
$client = Join-Path $temporary 'client.conf'
$stdout = Join-Path $temporary 'server.log'
$stderr = Join-Path $temporary 'server.err.log'
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($server, "[roles]`nagent=false`nserver=true`nclient=false`n[server]`nlisten=`"https://localhost:$port`"`ntls_server_names=[`"localhost`"]`nstate_directory=`"state`"`n", $utf8)
$process = $null
try {
    $doctor = & $Binary doctor --config $server
    if ($LASTEXITCODE -ne 0) { throw 'Initial TLS doctor failed.' }
    $pin = (($doctor | Select-String '^Server TLS public-key SHA-256: ([0-9a-f]{64})$').Matches.Groups[1].Value)
    if (-not $pin) { throw 'Initial public-key pin was not reported.' }
    $process = Start-Process -FilePath $Binary -ArgumentList @('run', '--config', $server) -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    [IO.File]::WriteAllText($client, "[roles]`nagent=false`nserver=false`nclient=true`n[client]`nsource=`"https://localhost:$port`"`ntrusted_server_public_key_sha256=`"$pin`"`n", $utf8)
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        & $Binary status --config $client *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw "HTTPS Server did not become ready.`n$(Get-Content $stderr -Raw)" }
    & $Binary tls validate --config $server *> $null
    if ($LASTEXITCODE -ne 0) { throw 'TLS validation failed.' }
    & $Binary tls reload --config $server *> $null
    if ($LASTEXITCODE -ne 0) { throw 'TLS reload failed.' }
    & $Binary tls regenerate --config $server *> $null
    if ($LASTEXITCODE -ne 0) { throw 'TLS regeneration failed.' }
    $doctor = & $Binary doctor --config $server
    $replacement = (($doctor | Select-String '^Server TLS public-key SHA-256: ([0-9a-f]{64})$').Matches.Groups[1].Value)
    if (-not $replacement -or $replacement -eq $pin) { throw 'TLS regeneration did not rotate the public key.' }
    [IO.File]::WriteAllText($client, "[roles]`nagent=false`nserver=false`nclient=true`n[client]`nsource=`"https://localhost:$port`"`ntrusted_server_public_key_sha256=`"$replacement`"`n", $utf8)
    & $Binary status --config $client *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Pinned reconnect after TLS regeneration failed.' }
    Write-Host "Windows HTTPS live test passed; rotated public-key SHA-256 $pin -> $replacement"
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
