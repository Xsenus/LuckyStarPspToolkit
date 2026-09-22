[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IsoPath,
    [string]$OutputZip = '.\lucky-star-psp-assets.zip',
    [switch]$IncludeOptional,
    [switch]$NoListing,
    [switch]$SkipSourceHash,
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
$Output = [System.IO.Path]::GetFullPath($OutputZip)
$Arguments = @(
    'collect-assets', $Iso, $Output
)
if ($IncludeOptional) { $Arguments += '--include-optional' }
if ($NoListing) { $Arguments += '--no-listing' }
if ($SkipSourceHash) { $Arguments += '--skip-source-hash' }
$Arguments += @('--json', "$Output.json")

& $CliPath @Arguments
$ExitCode = $LASTEXITCODE
if ($ExitCode -notin @(0, 2)) {
    throw "Asset collection failed with exit code $ExitCode."
}
Write-Host "Created: $Output"
Write-Host "Report:  $Output.json"
if ($ExitCode -eq 2) {
    Write-Warning 'Bundle is incomplete; inspect the JSON report for missing required files.'
}
exit $ExitCode
