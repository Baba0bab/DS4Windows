# Construit l'installeur DS4Windows : publie l'appli dans installer\publish,
# puis compile installer\DS4Windows.iss avec Inno Setup 6.
# Résultat dans installer\Output :
#   DS4Windows_<version>_x64_Setup.exe  installeur
#   DS4Windows_<version>_x64.zip        version portable (format attendu par ds4w.bat)
#   SHA256SUMS.txt                      empreintes à joindre à la release GitHub
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

$outputDir = Join-Path $PSScriptRoot 'Output'
$suffix = if ($SelfContained) { '_selfcontained' } else { '' }

Write-Host "== Création du zip portable ==" -ForegroundColor Cyan
# Le zip contient un dossier DS4Windows\ : ds4w.bat s'attend à ce format
$staging = Join-Path ([IO.Path]::GetTempPath()) "DS4Windows_zip_$version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory (Join-Path $staging 'DS4Windows') | Out-Null
Copy-Item (Join-Path $publishDir '*') (Join-Path $staging 'DS4Windows') -Recurse
Copy-Item (Join-Path $root 'LICENSE.txt'), (Join-Path $root 'NOTICE.txt') (Join-Path $staging 'DS4Windows')
$zip = Join-Path $outputDir "DS4Windows_${version}_x64${suffix}.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Entrées écrites une par une : sous Windows PowerShell, Compress-Archive et
# CreateFromDirectory mettent des « \ » dans les chemins, hors norme zip
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stagingFull = (Get-Item $staging).FullName
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    Get-ChildItem $stagingFull -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stagingFull.Length).TrimStart('\').Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, 'Optimal') | Out-Null
    }
}
finally {
    $archive.Dispose()
}
Remove-Item $staging -Recurse -Force

Write-Host "== Empreintes SHA-256 ==" -ForegroundColor Cyan
# Format sha256sum : "<empreinte>  <fichier>", une ligne par fichier de la version
$sums = Get-ChildItem $outputDir -File |
    Where-Object { $_.Name -like "DS4Windows_${version}_*" -and $_.Extension -in '.exe', '.zip' } |
    Sort-Object Name |
    ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
$sumsFile = Join-Path $outputDir 'SHA256SUMS.txt'
[IO.File]::WriteAllLines($sumsFile, [string[]]$sums)
$sums | ForEach-Object { Write-Host "  $_" }

Write-Host "Fichiers prêts dans $outputDir" -ForegroundColor Green
