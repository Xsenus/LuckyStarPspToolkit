[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IsoPath,
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [string]$OutputIso = '.\LuckyStar-patched.iso',
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Iso = (Resolve-Path -LiteralPath $IsoPath).Path
$Manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$Output = [System.IO.Path]::GetFullPath($OutputIso)
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $Report = "$Output.json"
}
else {
    $Report = [System.IO.Path]::GetFullPath($ReportPath)
}

$Arguments = @(
    'run', '--project', "$Root/src/LuckyStarPspToolkit.Cli/LuckyStarPspToolkit.Cli.csproj",
    '-c', 'Release', '--', 'iso-apply-manifest', $Iso, $Manifest, $Output,
    '--json', $Report
)

dotnet @Arguments
if ($LASTEXITCODE -ne 0) {
    throw "ISO rebuild failed with exit code $LASTEXITCODE."
}
Write-Host "Created: $Output"
Write-Host "Report:  $Report"
