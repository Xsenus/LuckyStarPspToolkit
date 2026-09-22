param(
    [string]$Rids = 'win-x64,linux-x64',
    [switch]$CompileOnly,
    [string]$LicenseTrust
)
# A nonzero native exit is an error even in Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$BuildArguments = @("$Root/scripts/build_release.py", '--rids', $Rids)
if ($CompileOnly) { $BuildArguments += '--compile-only' }
if ($LicenseTrust) { $BuildArguments += @('--license-trust', $LicenseTrust) }
if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 @BuildArguments
} else {
    & python @BuildArguments
}
if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE. No validated release was promoted." }
