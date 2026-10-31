param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]*$')][string]$ServerAddress,
    [int]$LocalPort = 5443
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskKey = Join-Path $taskRoot '.local/oci/trobot-oracle'
$taskKnownHosts = Join-Path $taskRoot '.local/oci/known_hosts'
if (!(Test-Path -LiteralPath $taskKey) -or !(Test-Path -LiteralPath $taskKnownHosts)) {
    throw 'Chiave SSH o known_hosts assenti: usa il PC con il collegamento Oracle gia verificato.'
}
if ($LocalPort -lt 1024 -or $LocalPort -gt 65535) { throw 'Porta locale non valida.' }
$taskPidFile = Join-Path $taskRoot '.local/oci/tunnel.pid'
if (Test-Path -LiteralPath $taskPidFile) {
    $taskExistingPid = 0
    if ([int]::TryParse((Get-Content -LiteralPath $taskPidFile -Raw).Trim(), [ref]$taskExistingPid)) {
        $taskExisting = Get-CimInstance Win32_Process -Filter "ProcessId = $taskExistingPid"
        if ($taskExisting) {
            if ($taskExisting.Name -eq 'ssh.exe' -and $taskExisting.CommandLine.Contains("opc@$ServerAddress") `
                -and $taskExisting.CommandLine.Contains("127.0.0.1:${LocalPort}:127.0.0.1:5443")) {
                Write-Output "Tunnel SSH gia attivo (PID $taskExistingPid). Indirizzo: https://localhost:$LocalPort"
                return
            }
            throw 'Il PID registrato appartiene a un altro processo: nessun processo modificato.'
        }
    }
}
$taskArguments = @(
    '-N', '-T', '-i', ('"' + $taskKey + '"'), '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes',
    '-o', 'StrictHostKeyChecking=yes', '-o', ('"UserKnownHostsFile=' + $taskKnownHosts + '"'),
    '-o', 'ExitOnForwardFailure=yes', '-o', 'ConnectTimeout=15',
    '-o', 'ServerAliveInterval=30', '-o', 'ServerAliveCountMax=3',
    '-L', "127.0.0.1:${LocalPort}:127.0.0.1:5443", "opc@$ServerAddress"
)
$taskProcess = Start-Process -FilePath (Get-Command ssh).Source -ArgumentList $taskArguments `
    -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $taskRoot '.local/oci/tunnel.stdout.log') `
    -RedirectStandardError (Join-Path $taskRoot '.local/oci/tunnel.stderr.log')
if ($taskProcess.WaitForExit(1500)) {
    throw ('Tunnel SSH non avviato. ' + (Get-Content -LiteralPath (Join-Path $taskRoot '.local/oci/tunnel.stderr.log') -Raw))
}
$taskProcess.Id | Set-Content -LiteralPath $taskPidFile
Write-Output "Tunnel SSH avviato (PID $($taskProcess.Id)). Indirizzo: https://localhost:$LocalPort"
