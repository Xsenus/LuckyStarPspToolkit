<#
.SYNOPSIS
Compares the public CPK API of an already compiled revision using identical synthetic inputs.
.DESCRIPTION
Optional maintainer diagnostic only. Uses a locally trusted PowerShell/Roslyn runtime,
not MSBuild or a verified .NET 9 release. Downloads nothing. Never point it at an
untrusted assembly. The same CpkScaleFixture.cs is compiled for both revisions.
.PARAMETER Root
Repository containing the benchmark fixture and diagnostic host scripts.
.PARAMETER ReferenceDirectory
Directory containing the specific revision's LuckyStarPspToolkit.Formats.dll.
.PARAMETER Out
A new directory inside Root/artifacts; existing directories are rejected.
.PARAMETER Probe
Observe four historical failure cases instead of collecting performance samples.
#>
param(
    [Parameter(Mandatory=$true)][string]$Root,
    [Parameter(Mandatory=$true)][string]$ReferenceDirectory,
    [Parameter(Mandatory=$true)][string]$Out,
    [switch]$Probe
)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root)
$Out = [IO.Path]::GetFullPath($Out)
$prefix = [IO.Path]::Combine($Root, 'artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $Out.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Output must be inside repository artifacts.' }
if ([IO.Directory]::Exists($Out)) { throw 'Use a fresh output directory for each revision/run.' }
$inputAssembly = [IO.Path]::Combine([IO.Path]::GetFullPath($ReferenceDirectory), 'LuckyStarPspToolkit.Formats.dll')
if (-not [IO.File]::Exists($inputAssembly)) { throw 'The requested Formats assembly is missing.' }
[void][IO.Directory]::CreateDirectory($Out)
[IO.File]::Copy($inputAssembly, [IO.Path]::Combine($Out, 'LuckyStarPspToolkit.Formats.dll'))
[void][Reflection.Assembly]::LoadFrom("$PSHOME/Microsoft.CodeAnalysis.dll")
[void][Reflection.Assembly]::LoadFrom("$PSHOME/Microsoft.CodeAnalysis.CSharp.dll")
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
Get-ChildItem "$PSHOME/ref/*.dll" | ForEach-Object { $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_.FullName)) }
$refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($inputAssembly))
$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp13)
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$entry = 'global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic; return CpkScaleFixture.Run(args);'
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($entry, $parse))
$fixture = Join-Path $Root 'tests/LuckyStarPspToolkit.Formats.SelfTests/CpkScaleFixture.cs'
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($fixture), $parse))
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('CpkScaleProbe', $trees, $refs, $options)
$outputAssembly = Join-Path $Out 'CpkScaleProbe.dll'
$stream = [IO.File]::Create($outputAssembly)
try { $emitted = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (-not $emitted.Success) {
    foreach ($d in $emitted.Diagnostics) { [Console]::Error.WriteLine($d.ToString()) }
    throw 'Benchmark harness compilation failed.'
}
if ($Probe.IsPresent) {
    & "$PSHOME/pwsh" -NoProfile -NoLogo -File (Join-Path $Root 'scripts/run_managed.ps1') $outputAssembly '--cpk-probe'
} else {
    & "$PSHOME/pwsh" -NoProfile -NoLogo -File (Join-Path $Root 'scripts/run_managed.ps1') $outputAssembly
}
exit $LASTEXITCODE
