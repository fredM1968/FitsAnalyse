# Prépare une version de FitsAnalyse à diffuser :
#   1. compile l'installateur Inno Setup (si Inno Setup 6 est installé) ;
#   2. écrit manifest.json pour le dépôt des plugins NINA (somme SHA256 de la DLL comprise).
#
# Utilisation, après une compilation en Release (PowerShell, dans le dossier Installer) :
#   powershell -ExecutionPolicy Bypass -File .\Publier.ps1
#
# Ensuite, sur GitHub (fredM1968/FitsAnalyse) : créer une « release » nommée v<version> et y joindre
# FitsAnalyse.dll et FitsAnalyse-<version>-setup.exe (dossier Installer\Sortie). Le manifeste pointe vers
# la DLL de cette release : ne pas la renommer.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dll = Resolve-Path '..\bin\Release\net8.0-windows\FitsAnalyse.dll'
$version = [Version](Get-Item $dll).VersionInfo.FileVersion
$tag = "v$version"
$sortie = Join-Path $PSScriptRoot 'Sortie'
New-Item -ItemType Directory -Force -Path $sortie | Out-Null

Write-Host "FitsAnalyse $version ($dll)"

# 1. Installateur
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc /Q 'FitsAnalyse.iss'
    if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation de l'installateur" }
    Write-Host "Installateur : $sortie\FitsAnalyse-$version-setup.exe"
} else {
    Write-Warning "Inno Setup 6 introuvable : installateur non généré (ouvre FitsAnalyse.iss dans Inno Setup)."
}

# 2. Copie de la DLL à joindre à la release, et manifeste pour le dépôt des plugins NINA
Copy-Item $dll (Join-Path $sortie 'FitsAnalyse.dll') -Force
$sha = (Get-FileHash -Algorithm SHA256 $dll).Hash

$manifest = [ordered]@{
    Name                      = 'FitsAnalyse'
    Identifier                = 'f068a0d7-32a8-481c-864e-6b21455f2394'   # = Guid de AssemblyInfo.cs
    Version                   = [ordered]@{ Major = "$($version.Major)"; Minor = "$($version.Minor)"; Patch = "$($version.Build)"; Build = "$($version.Revision)" }
    Author                    = 'Fred'
    Homepage                  = 'https://github.com/fredM1968/FitsAnalyse'
    Repository                = 'https://github.com/fredM1968/FitsAnalyse'
    License                   = 'MPL-2.0'
    LicenseURL                = 'https://www.mozilla.org/en-US/MPL/2.0/'
    ChangelogURL              = 'https://github.com/fredM1968/FitsAnalyse/releases'
    Tags                      = @('Histogramme', 'Statistiques', 'Temps de pose', 'FITS')
    MinimumApplicationVersion = [ordered]@{ Major = '3'; Minor = '2'; Patch = '0'; Build = '0' }
    Descriptions              = [ordered]@{
        ShortDescription = "Statistiques, histogramme interactif et temps de pose conseillé pour la dernière image."
        LongDescription  = "FitsAnalyse remplace le panneau Statistiques de l'onglet Imagerie (mêmes valeurs, par canal pour les caméras couleur) et ajoute une suggestion de temps de pose calculée sur la dernière image (bruit du fond comparé au bruit de lecture mesuré sur un bias, arrondi aux paliers de la bibliothèque de darks), un histogramme interactif (mono ou RVB, linéaire ou logarithmique, zoom, valeurs au clic, cadrage automatique, histogramme de référence en gris) et l'ouverture d'une image déjà acquise."
        FeaturedImageURL = ''
        ScreenshotURL    = ''
        AltScreenshotURL = ''
    }
    Installer                 = [ordered]@{
        URL          = "https://github.com/fredM1968/FitsAnalyse/releases/download/$tag/FitsAnalyse.dll"
        Type         = 'DLL'
        Checksum     = $sha
        ChecksumType = 'SHA256'
    }
}

$json = $manifest | ConvertTo-Json -Depth 5
# UTF-8 sans BOM (attendu par le dépôt des manifestes)
[System.IO.File]::WriteAllText((Join-Path $sortie 'manifest.json'), $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Manifeste : $sortie\manifest.json (SHA256 $sha)"
Write-Host "Release GitHub à créer : $tag, avec FitsAnalyse.dll et l'installateur."
