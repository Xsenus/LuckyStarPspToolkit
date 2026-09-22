<#
.SYNOPSIS
Compiles C#13 sources with Roslyn carried by an explicitly installed PowerShell distribution.
.DESCRIPTION
Diagnostic fallback ONLY. Reference assemblies belong to that PowerShell runtime,
not necessarily .NET 9. This does not run MSBuild, SDK analyzers, restore or publish.
No compiler/runtime is downloaded or redistributed. Release builds still require .NET 9 SDK.
.PARAMETER Root
Repository root containing VERSION and src/tests/tools.
.PARAMETER LicenseTrustFile
Optional throwaway/public profile embedded only into the diagnostic CLI. Never pass authority secrets.
.PARAMETER Out
An existing or new directory under Root/artifacts reserved for diagnostic assemblies.
#>
param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$Out,[string]$LicenseTrustFile="")
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$Out=[IO.Path]::GetFullPath($Out)
$artifacts=[IO.Path]::Combine($Root,'artifacts')+[IO.Path]::DirectorySeparatorChar
if(-not $Out.StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase)){throw 'Diagnostic output must be inside repository artifacts.'}
[void][System.Reflection.Assembly]::LoadFrom("$PSHOME/Microsoft.CodeAnalysis.dll")
[void][System.Reflection.Assembly]::LoadFrom("$PSHOME/Microsoft.CodeAnalysis.CSharp.dll")
[void][IO.Directory]::CreateDirectory($Out)
$parse=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp13,[Microsoft.CodeAnalysis.DocumentationMode]::Diagnose)
$metadata=[Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
Get-ChildItem "$PSHOME/ref/*.dll" | ForEach-Object {$metadata.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_.FullName))}
$units=@(
 @('LuckyStarPspToolkit.Licensing','src/LuckyStarPspToolkit.Licensing','lib',@()),
 @('LuckyStarPspToolkit.Licensing.Authority','src/LuckyStarPspToolkit.Licensing.Authority','lib',@('LuckyStarPspToolkit.Licensing')),
 @('lsp-license-server','src/LuckyStarPspToolkit.LicenseServer','exe',@('LuckyStarPspToolkit.Licensing','LuckyStarPspToolkit.Licensing.Authority')),
 @('lsp-license-admin','src/LuckyStarPspToolkit.LicenseAdmin','exe',@('LuckyStarPspToolkit.Licensing','LuckyStarPspToolkit.Licensing.Authority')),
 @('LuckyStarPspToolkit.Core','src/LuckyStarPspToolkit.Core','lib',@()),
 @('LuckyStarPspToolkit.Formats','src/LuckyStarPspToolkit.Formats','lib',@()),
 @('lsptool','src/LuckyStarPspToolkit.Cli','exe',@('LuckyStarPspToolkit.Core','LuckyStarPspToolkit.Formats','LuckyStarPspToolkit.Licensing')),
 @('LuckyStarPspToolkit.SelfTests','tests/LuckyStarPspToolkit.SelfTests','exe',@('LuckyStarPspToolkit.Core')),
 @('LuckyStarPspToolkit.Formats.SelfTests','tests/LuckyStarPspToolkit.Formats.SelfTests','exe',@('LuckyStarPspToolkit.Core','LuckyStarPspToolkit.Formats','lsptool')),
 @('LuckyStarPspToolkit.Licensing.SelfTests','tests/LuckyStarPspToolkit.Licensing.SelfTests','exe',@('LuckyStarPspToolkit.Licensing','LuckyStarPspToolkit.Licensing.Authority','lsptool')),
 @('LuckyStarPspToolkit.Documentation','tools/LuckyStarPspToolkit.Documentation','exe',@('Microsoft.CodeAnalysis','Microsoft.CodeAnalysis.CSharp'))
)
$version=(Get-Content "$Root/VERSION" -Raw).Trim()
$hashes=[ordered]@{}
$checks=[Collections.Generic.List[object]]::new()
$referenceVersion=[Reflection.AssemblyName]::GetAssemblyName("$PSHOME/ref/System.Runtime.dll").Version.ToString()
$report=[ordered]@{schema='lsptool.roslyn-fallback-compilation.v1';version=$version;success=$false;
 standardDotnetBuild=$false;standardNet9Validation=$false;language='CSharp13';
 framework=[Runtime.InteropServices.RuntimeInformation]::FrameworkDescription;
 referenceAssemblyVersion=$referenceVersion;powerShellVersion=$PSVersionTable.PSVersion.ToString();
 compiler=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation].Assembly.FullName;assemblies=$checks;sourceHashes=$hashes}
foreach($path in @('VERSION','Directory.Build.props')){$hashes[$path]=(Get-FileHash (Join-Path $Root $path) -Algorithm SHA256).Hash.ToLowerInvariant()}
try {
foreach($u in $units){
 $name=$u[0];Write-Output "=== Compiling $name (Roslyn / C#13 / host reference fallback; NOT dotnet build) ==="
 $trees=[Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
 $global="global using System;global using System.Collections.Generic;global using System.IO;global using System.Linq;global using System.Net.Http;global using System.Threading;global using System.Threading.Tasks;[assembly:System.Reflection.AssemblyVersion(`"$version.0`")][assembly:System.Reflection.AssemblyInformationalVersion(`"$version`")]"
 $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($global,$parse,'GeneratedGlobalUsings.cs',[Text.Encoding]::UTF8))
 Get-ChildItem "$Root/$($u[1])" -Recurse -Filter *.cs | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'} | Sort-Object FullName | ForEach-Object {
  $relative=[IO.Path]::GetRelativePath($Root,$_.FullName).Replace('\','/')
  $hashes[$relative]=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName),$parse,$relative,[Text.Encoding]::UTF8))
 }
 $refs=[Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new($metadata)
 foreach($dep in $u[3]){ $path=if($dep.StartsWith('Microsoft.')){"$PSHOME/$dep.dll"}else{"$Out/$dep.dll"};$refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path)) }
 $kind=if($u[2] -eq 'exe'){[Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication}else{[Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary}
 $options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new($kind).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable).WithDeterministic($true)
 $comp=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($name,$trees,$refs,$options)
 $dll=[IO.File]::Create("$Out/$name.dll");$xml=[IO.File]::Create("$Out/$name.xml")
 try{
  if($name -eq 'lsptool' -and $LicenseTrustFile){
   $absoluteTrust=[IO.Path]::GetFullPath($LicenseTrustFile)
   $script:TrustPayload=[IO.File]::ReadAllBytes($absoluteTrust)
   $streamFactory=[Func[IO.Stream]]{[IO.MemoryStream]::new($script:TrustPayload,$false)}
   $resource=[Microsoft.CodeAnalysis.ResourceDescription]::new('LuckyStarPspToolkit.license-trust.json',$streamFactory,$true)
   $result=$comp.Emit($dll,$null,$xml,$null,[Microsoft.CodeAnalysis.ResourceDescription[]]@($resource))
  }else{$result=$comp.Emit($dll,$null,$xml)}
 }finally{$dll.Dispose();$xml.Dispose()}
 $warnings=@($result.Diagnostics | Where-Object {$_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning -and $_.Id -ne 'CS1587'})
 $errors=@($result.Diagnostics | Where-Object {$_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error})
 foreach($d in $result.Diagnostics){if($d.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden -and $d.Id -ne 'CS1587'){Write-Output $d.ToString()}}
 $checks.Add([ordered]@{name=$name;success=$result.Success;warnings=@($warnings | ForEach-Object {$_.ToString()});errors=@($errors | ForEach-Object {$_.ToString()});sha256=(Get-FileHash "$Out/$name.dll" -Algorithm SHA256).Hash.ToLowerInvariant()})
 if(-not $result.Success){throw "Compilation failed: $name"}
 $fatalWarnings=@($warnings | Where-Object {$_.Id -ne 'CS1701' -or $name -ne 'LuckyStarPspToolkit.Documentation'})
 if($fatalWarnings.Count -gt 0){throw "Source warnings found in $name"}
 Write-Output "COMPILED $name"
}

$report.success=$true
} finally {
 $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$Out/compilation-report.json" -Encoding utf8NoBOM
}
