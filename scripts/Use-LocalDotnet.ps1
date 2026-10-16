# Eseguire dalla radice: . ./scripts/Use-LocalDotnet.ps1
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '../.local/dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '../.local/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
