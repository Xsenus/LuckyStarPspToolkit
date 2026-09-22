[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IsoPath,
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [string]$OutputIso = '.\LuckyStar-patched.iso',
    [string]$ReportPath,
    [string]$CliPath
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $CliPath) {
    $Version = (Get-Content -Raw "$Root/VERSION").Trim()
    $CliPath = "$Root/artifacts/releases/$Version/win-x64/lsptool.exe"
}
if (-not (Test-Path -LiteralPath $CliPath -PathType Leaf)) { throw 'Build and activate the configured customer release first, or specify -CliPath.' }
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
    'iso-apply-manifest', $Iso, $Manifest, $Output,
    '--json', $Report
)

& $CliPath @Arguments
if ($LASTEXITCODE -ne 0) {
    throw "ISO rebuild failed with exit code $LASTEXITCODE."
}
Write-Host "Created: $Output"
Write-Host "Report:  $Report"
