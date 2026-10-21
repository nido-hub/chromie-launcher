param(
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$properties = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    $Version = [string]$properties.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version attendue : major.minor.patch.' }
$application = Join-Path $root 'dist\win-x64'
foreach ($file in @('ChromieLauncher.exe', 'av_libglesv2.dll', 'libHarfBuzzSharp.dll', 'libSkiaSharp.dll')) {
    if (!(Test-Path (Join-Path $application $file))) { throw "Fichier de publication absent : $file. Lancer publish.ps1 d'abord." }
}
$binary = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $application 'ChromieLauncher.exe'))
$binaryVersion = "$($binary.FileMajorPart).$($binary.FileMinorPart).$($binary.FileBuildPart)"
if ($binaryVersion -ne $Version) { throw "Le binaire est en version $binaryVersion, mais le package demande $Version. Recompiler d'abord." }
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$archive = Join-Path $artifacts "ChromieLauncher-$Version-win-x64.zip"
if (Test-Path $archive) { throw "L'archive existe déjà : $archive" }

# Include readable attribution and font licenses alongside the embedded assets.
Copy-Item (Join-Path $root 'README.md') (Join-Path $application 'README.md')
Copy-Item (Join-Path $root 'src\ChromieLauncher.Desktop\Assets\NOTICE.md') (Join-Path $application 'THIRD-PARTY-NOTICES.md')
$licenses = Join-Path $application 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item (Join-Path $root 'src\ChromieLauncher.Desktop\Assets\Fonts\*-OFL.txt') $licenses
Compress-Archive -Path (Join-Path $application '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($archive)) -Encoding ascii
Write-Host "Archive : $archive"
Write-Host "SHA-256 : $hash"
