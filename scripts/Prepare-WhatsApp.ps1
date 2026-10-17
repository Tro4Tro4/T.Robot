[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$nodeVersion = 'v24.22.0'
$archive = "node-$nodeVersion-win-x64.zip"
$downloadDirectory = Join-Path $workspace '.local/downloads'
$nodeDirectory = Join-Path $workspace ".local/node/node-$nodeVersion-win-x64"
$configurationDirectory = Join-Path $workspace '.local/whatsapp'
New-Item -ItemType Directory -Force $downloadDirectory, $configurationDirectory | Out-Null

if (!(Test-Path -LiteralPath (Join-Path $nodeDirectory 'node.exe'))) {
    $archivePath = Join-Path $downloadDirectory $archive
    if (!(Test-Path -LiteralPath $archivePath)) {
        Invoke-WebRequest "https://nodejs.org/dist/$nodeVersion/$archive" -OutFile $archivePath
    }
    $checksums = (Invoke-WebRequest "https://nodejs.org/dist/$nodeVersion/SHASUMS256.txt").Content
    $checksumLine = @($checksums -split "`n" | Where-Object { $_ -match ([regex]::Escape($archive) + '\s*$') })
    if ($checksumLine.Count -ne 1) { throw 'Checksum ufficiale Node non disponibile.' }
    $expectedHash = ($checksumLine[0].Trim() -split '\s+')[0]
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Checksum Node non corrispondente: installazione interrotta.'
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath (Split-Path -Parent $nodeDirectory)
}

$configurationPath = Join-Path $configurationDirectory 'gateway.json'
if (!(Test-Path -LiteralPath $configurationPath)) {
    $randomBytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    $token = ([System.BitConverter]::ToString($randomBytes)).Replace('-', '').ToLowerInvariant()
    @{
        WhatsAppGateway = @{
            Enabled = $true
            BaseUrl = 'http://127.0.0.1:5240'
            Token = $token
            CallbackUrl = 'http://127.0.0.1:5238/internal/whatsapp/incoming'
            StateDirectory = Join-Path $configurationDirectory 'state'
        }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configurationPath -Encoding utf8
}

$previousPath = $env:PATH
$previousLocation = Get-Location
try {
    $env:PATH = "$nodeDirectory;$previousPath"
    Set-Location (Join-Path $workspace 'gateway/TRobot.WhatsApp')
    & (Join-Path $nodeDirectory 'npm.cmd') ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Installazione dipendenze gateway non riuscita.' }
} finally {
    $env:PATH = $previousPath
    Set-Location $previousLocation
}
Write-Output 'Gateway preparato. Configurazione e sessione restano in .local/whatsapp. Nessun collegamento WhatsApp avviato.'
