[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Archive = (Resolve-Path $ArchivePath).Path
Push-Location $Root
try {
    & "$Root\scripts\build.ps1" -CompileOnly
    dotnet run --project .\tests\LuckyStarPspToolkit.SelfTests -c Release --no-build -- --customer-archive $Archive
    if ($LASTEXITCODE -ne 0) { throw "Core customer tests failed with exit code $LASTEXITCODE." }
    dotnet run --project .\tests\LuckyStarPspToolkit.Formats.SelfTests -c Release --no-build -- --customer-archive $Archive
    if ($LASTEXITCODE -ne 0) { throw "Formats customer tests failed with exit code $LASTEXITCODE." }
    dotnet run --project .\src\LuckyStarPspToolkit.Cli -c Release --no-build -- customer-audit $Archive --json .\validation\csharp-customer-audit.json
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 2) { throw "customer-audit failed with exit code $LASTEXITCODE." }
    dotnet run --project .\src\LuckyStarPspToolkit.Cli -c Release --no-build -- audit-rgo $Archive --json .\validation\csharp-rgo-audit.json
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 2) { throw "audit-rgo failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
