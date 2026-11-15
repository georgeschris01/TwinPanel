# TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
# Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
# Fabrique le .zip de la version portable : un dossier « TwinPanel » à la racine, chemins avec « / »
# (la norme des .zip : ils s'ouvrent partout, pas seulement dans l'Explorateur).
param([Parameter(Mandatory)] [string] $Source, [Parameter(Mandatory)] [string] $Zip)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$Source = (Resolve-Path $Source).Path.TrimEnd('\')
$top = Split-Path $Source -Leaf
if (Test-Path $Zip) { Remove-Item $Zip -Force }
$archive = [IO.Compression.ZipFile]::Open($Zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $Source -Recurse -File | ForEach-Object {
        $name = $top + '/' + $_.FullName.Substring($Source.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $archive.Dispose() }
