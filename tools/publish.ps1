param(
    [switch]$Native,
    [string]$Version = '0.1.0',
    [string]$DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\ChromieLauncher.Desktop\ChromieLauncher.Desktop.csproj'
$output = Join-Path $root 'dist\win-x64'
$nativeValue = $Native.IsPresent.ToString().ToLowerInvariant()
& $DotNet publish $project -c Release -r win-x64 --self-contained true "-p:NativeRelease=$nativeValue" "-p:Version=$Version" -o $output
if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }
# Debug symbols remain in bin/ for diagnostics, but are not required by the user.
foreach ($symbol in @('ChromieLauncher.pdb', 'ChromieLauncher.Core.pdb')) {
    $path = Join-Path $output $symbol
    if (Test-Path $path) { Remove-Item -LiteralPath $path }
}
Write-Host "Application : $output\ChromieLauncher.exe"
Write-Host 'Distribuer tout le contenu du dossier, y compris les bibliothèques natives Avalonia.'
