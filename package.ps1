param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $toolRoot 'build.ps1') }
$project = Get-Content -LiteralPath (Join-Path $toolRoot 'project.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$dist = Join-Path $toolRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$baseName = $project.name + '-' + $project.version
$portableZip = Join-Path $dist ($baseName + '-portable.zip')
$sourceZip = Join-Path $dist ($baseName + '-source.zip')
$readmeFiles = @(Get-ChildItem -LiteralPath $toolRoot -Filter '*.txt' -File)
if ($readmeFiles.Count -ne 1) { throw 'Expected one portable user guide.' }
$executable = Join-Path $toolRoot ('release\' + $project.name + '.exe')
$portableFiles = @(
    @{ Source = $executable; Entry = ($project.name + '.exe') },
    @{ Source = $readmeFiles[0].FullName; Entry = $readmeFiles[0].Name }
)
$sourceFiles = @()
foreach ($relative in @('README.md', 'project.json', 'app.manifest', 'build.ps1', 'test.ps1', 'package.ps1', '.gitignore', 'tests\README.md')) {
    $sourceFiles += @{ Source = (Join-Path $toolRoot $relative); Entry = $relative.Replace('\', '/') }
}
$sourceFiles += @{ Source = $readmeFiles[0].FullName; Entry = $readmeFiles[0].Name }
foreach ($directory in @('src', 'tests')) {
    Get-ChildItem -LiteralPath (Join-Path $toolRoot $directory) -Filter '*.cs' -File |
        Sort-Object Name | ForEach-Object {
            $sourceFiles += @{ Source = $_.FullName; Entry = ($directory + '/' + $_.Name) }
        }
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-ReleaseZip([string]$Destination, [object[]]$Files) {
    if (Test-Path -LiteralPath $Destination) {
        throw ('Archive already exists; preserve it and select a new version: ' + $Destination)
    }
    foreach ($item in $Files) {
        if (-not (Test-Path -LiteralPath $item.Source -PathType Leaf)) {
            throw ('Missing package input: ' + $item.Source)
        }
    }
    $stream = [System.IO.File]::Open($Destination, [System.IO.FileMode]::CreateNew)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream, [System.IO.Compression.ZipArchiveMode]::Create, $false, [System.Text.Encoding]::UTF8)
        try {
            foreach ($item in $Files) {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $item.Source, $item.Entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}
Write-ReleaseZip $portableZip $portableFiles
Write-ReleaseZip $sourceZip $sourceFiles
Get-Item -LiteralPath $portableZip, $sourceZip | Select-Object Name, Length
Get-FileHash -LiteralPath $portableZip, $sourceZip -Algorithm SHA256 | Select-Object Path, Hash
