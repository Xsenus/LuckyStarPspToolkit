<#
.SYNOPSIS
Invokes the entry point of a trusted, locally compiled diagnostic assembly under PowerShell's runtime.
.DESCRIPTION
Not a production CLI host. Used only by verify_roslyn.py when the .NET SDK is unavailable.
Never invoke this script on an assembly from an untrusted source.
.PARAMETER Assembly
Absolute path of the local assembly emitted by compile_with_roslyn.ps1.
.PARAMETER CliArgs
Arguments passed unchanged to the managed entry point.
#>
param([string]$Assembly,[Parameter(ValueFromRemainingArguments=$true)][string[]]$CliArgs)
$ErrorActionPreference='Stop'
try {
 $directory=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Assembly))
 [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY',$directory+[IO.Path]::DirectorySeparatorChar)
 foreach($name in @('LuckyStarPspToolkit.Core','LuckyStarPspToolkit.Formats','lsptool')){
  $p=Join-Path $directory ($name+'.dll')
  if([IO.File]::Exists($p)){[void][Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($p)}
 }
 $a=[Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath([IO.Path]::GetFullPath($Assembly))
 $entry=$a.EntryPoint
 if($null -eq $CliArgs){$CliArgs=[string[]]@()}
 $callArgs=[object[]]::new(1)
 $callArgs[0]=[string[]]$CliArgs
 $code=$entry.Invoke($null,$callArgs)
 if($code -is [Threading.Tasks.Task]){$code=$code.GetAwaiter().GetResult()}
 exit [int]$code
}catch{[Console]::Error.WriteLine($_.Exception.ToString());exit 70}
