[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IsoPath,
    [string]$OutputZip = '.\lucky-star-psp-assets.zip',
    [switch]$IncludeOptional,
    [switch]$NoListing,
    [switch]$SkipSourceHash
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Iso = (Resolve-Path -LiteralPath $IsoPath).Path
$Output = [System.IO.Path]::GetFullPath($OutputZip)
$Arguments = @(
    'run', '--project', "$Root/src/LuckyStarPspToolkit.Cli/LuckyStarPspToolkit.Cli.csproj",
    '-c', 'Release', '--', 'collect-assets', $Iso, $Output
)
if ($IncludeOptional) { $Arguments += '--include-optional' }
if ($NoListing) { $Arguments += '--no-listing' }
if ($SkipSourceHash) { $Arguments += '--skip-source-hash' }
$Arguments += @('--json', "$Output.json")

dotnet @Arguments
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
