param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $toolRoot 'build.ps1') }
$project = Get-Content -LiteralPath (Join-Path $toolRoot 'project.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$toolExe = Join-Path $toolRoot ('release\' + $project.name + '.exe')
if (-not (Test-Path -LiteralPath $toolExe)) { throw 'Build the application first.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$testOutput = Join-Path $toolRoot ('tests\artifacts\' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $testOutput | Out-Null
$references = @(
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Xaml.dll',
    ('/reference:' + (Join-Path $framework 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\WindowsBase.dll'))
)
$contracts = Join-Path $toolRoot 'src\Contracts.cs'
$engine = Join-Path $toolRoot 'src\IconEngine.cs'
$mainForm = Join-Path $toolRoot 'src\MainForm.cs'
$cases = @(
    @{ Name = 'EngineTests'; Entry = 'FolderIconTool.Tests.EngineTests'; Sources = @($contracts, $engine, (Join-Path $toolRoot 'tests\EngineTests.cs')); Arguments = @((Join-Path $testOutput 'engine')) },
    @{ Name = 'GuiSmoke'; Entry = 'GuiSmoke'; Sources = @($contracts, $engine, $mainForm, (Join-Path $toolRoot 'tests\GuiSmoke.cs')); Arguments = @((Join-Path $testOutput 'gui')) },
    @{ Name = 'CliSmoke'; Entry = 'CliSmoke'; Sources = @((Join-Path $toolRoot 'tests\CliSmoke.cs')); Arguments = @($toolExe, (Join-Path $testOutput 'cli')) }
)
foreach ($case in $cases) {
    $testExe = Join-Path $testOutput ($case.Name + '.exe')
    $sourceFiles = $case.Sources
    & $compiler /nologo /target:exe /langversion:5 /codepage:65001 /utf8output `
        ("/main:" + $case.Entry) ("/out:" + $testExe) @references @sourceFiles
    if ($LASTEXITCODE -ne 0) { throw ('Compilation failed: ' + $case.Name) }
    $stdout = Join-Path $testOutput ($case.Name + '.stdout.log')
    $stderr = Join-Path $testOutput ($case.Name + '.stderr.log')
    $arguments = @($case.Arguments | ForEach-Object {
        if ($_ -match '"') { throw 'Test paths must not contain quotes.' }
        '"' + $_ + '"'
    })
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $testExe
    $start.Arguments = $arguments -join ' '
    $start.WorkingDirectory = $toolRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $process = [System.Diagnostics.Process]::Start($start)
    $outputTask = $process.StandardOutput.ReadToEndAsync()
    $errorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw ('Test timed out: ' + $case.Name)
    }
    $process.WaitForExit()
    [System.IO.File]::WriteAllText($stdout, $outputTask.Result, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($stderr, $errorTask.Result, [System.Text.Encoding]::UTF8)
    Get-Content -LiteralPath $stdout -Encoding UTF8
    Get-Content -LiteralPath $stderr -Encoding UTF8
    $exitCode = $process.ExitCode
    $process.Dispose()
    if ($exitCode -ne 0) { throw ('Test failed: ' + $case.Name + ', exit code ' + $exitCode) }
}
Write-Output ('All checks passed. Artifacts: ' + $testOutput)
