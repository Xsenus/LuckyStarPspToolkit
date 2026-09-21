[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExtractedGameRoot,
    [string]$OutputDirectory = '.\lucky-star-intake'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $ExtractedGameRoot).Path
$out = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $out | Out-Null

$files = Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName
$manifest = foreach ($file in $files) {
    [pscustomobject]@{
        Path = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        Size = $file.Length
        SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM -LiteralPath (Join-Path $out 'PSP_GAME-files.json')
$manifest | Format-Table -AutoSize | Out-String -Width 4096 | Set-Content -Encoding utf8NoBOM -LiteralPath (Join-Path $out 'PSP_GAME-files.txt')

$candidates = @(
    'PSP_GAME/SYSDIR/EBOOT.BIN',
    'PSP_GAME/PARAM.SFO',
    'PSP_GAME/USRDIR/DATA/sc.cpk',
    'PSP_GAME/USRDIR/DATA/lt.bin',
    'PSP_GAME/USRDIR/DATA/union.cpk',
    'PSP_GAME/USRDIR/DATA/pr.bin'
)
foreach ($relative in $candidates) {
    $source = Join-Path $root ($relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        $target = Join-Path $out $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
}

$zip = "$out.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Created: $zip"
