# Construit l'installeur DS4Windows : publie l'appli dans installer\publish,
# puis compile installer\DS4Windows.iss avec Inno Setup 6.
# Résultat : installer\Output\DS4Windows_<version>_x64_Setup.exe
#
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1 -SelfContained
param(
    # Embarque le runtime .NET (installeur plus gros, marche sur un PC sans .NET 10)
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'DS4Windows\DS4WinWPF.csproj'
$publishDir = Join-Path $PSScriptRoot 'publish'

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Version introuvable dans $csproj" }

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 introuvable. L'installer avec : winget install JRSoftware.InnoSetup"
}

Write-Host "== Publication de DS4Windows $version ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
$sc = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish $csproj -c Release -r win-x64 --self-contained $sc -p:Platform=x64 -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué ($LASTEXITCODE)" }

Write-Host "== Compilation de l'installeur ==" -ForegroundColor Cyan
& $iscc "/DPublishDir=$publishDir" "/DAppVersion=$version" "/DSelfContained=$(if ($SelfContained) { '1' } else { '0' })" (Join-Path $PSScriptRoot 'DS4Windows.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC a échoué ($LASTEXITCODE)" }

Write-Host "Installeur prêt dans $(Join-Path $PSScriptRoot 'Output')" -ForegroundColor Green
