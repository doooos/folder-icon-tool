$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
$project = Get-Content -LiteralPath (Join-Path $toolRoot 'project.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Cannot find the built-in .NET Framework C# compiler.'
}
$releasePath = Join-Path $toolRoot 'release'
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $toolRoot 'src') -Filter '*.cs' -File |
    Sort-Object Name | ForEach-Object FullName)
$outputExe = Join-Path $releasePath ($project.name + '.exe')
$frameworkPath = Split-Path -Parent $compiler
$presentationCore = Join-Path $frameworkPath 'WPF\PresentationCore.dll'
$windowsBase = Join-Path $frameworkPath 'WPF\WindowsBase.dll'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll /reference:System.Xaml.dll `
    "/reference:$presentationCore" "/reference:$windowsBase" `
    "/win32manifest:$(Join-Path $toolRoot 'app.manifest')" `
    "/out:$outputExe" @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-ChildItem -LiteralPath $toolRoot -Filter '*.txt' -File |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $releasePath -Force }
Get-Item -LiteralPath $outputExe | Select-Object Name, Length, LastWriteTime
