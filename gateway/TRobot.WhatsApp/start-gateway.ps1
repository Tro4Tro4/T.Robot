param([string]$NodePath)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $NodePath) {
    $portableNodes = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.local/node') -Filter node.exe -File -Recurse | Sort-Object FullName)
    if ($portableNodes.Count -ne 1) { throw 'Indicare -NodePath: runtime Node portabile non univoco.' }
    $NodePath = $portableNodes[0].FullName
}
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw 'Runtime Node portabile assente.' }
& $NodePath (Join-Path $PSScriptRoot 'src/index.js')
exit $LASTEXITCODE
