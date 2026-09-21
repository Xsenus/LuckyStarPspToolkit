param([string]$CustomerArchive)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Arguments = @()
if ($CustomerArchive) { $Arguments += @('--customer-archive', (Resolve-Path $CustomerArchive).Path) }
Push-Location $Root
try {
    python scripts/reference_oracle.py | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Reference oracle generation failed with exit code $LASTEXITCODE." }
    python validation/validate_font_fixtures.py | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Independent font fixture validation failed with exit code $LASTEXITCODE." }
    python validation/validate_iso_fixture.py | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Independent ISO fixture validation failed with exit code $LASTEXITCODE." }
    & "$Root\scripts\build.ps1" -CompileOnly
    dotnet run --project .\tests\LuckyStarPspToolkit.SelfTests\LuckyStarPspToolkit.SelfTests.csproj -c Release --no-build -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Core self-tests failed with exit code $LASTEXITCODE." }
    dotnet run --project .\tests\LuckyStarPspToolkit.Formats.SelfTests\LuckyStarPspToolkit.Formats.SelfTests.csproj -c Release --no-build -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Formats self-tests failed with exit code $LASTEXITCODE." }
    dotnet run --project .\src\LuckyStarPspToolkit.Cli\LuckyStarPspToolkit.Cli.csproj -c Release --no-build -- self-test
    if ($LASTEXITCODE -ne 0) { throw "CLI self-test failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
