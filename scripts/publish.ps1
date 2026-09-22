param([ValidateSet('win-x64', 'linux-x64')][string]$Runtime = 'win-x64', [string]$LicenseTrust)
# Publication includes the same compilation and tests; arbitrary output deletion is not allowed.
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/build.ps1" -Rids $Runtime -LicenseTrust $LicenseTrust
