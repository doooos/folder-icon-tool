# Engine integration tests

For the complete hidden suite, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1` from the project root. This builds the portable application,
runs the engine suite, renders and checks the GUI without showing it, and tests
the actual executable's CLI. CLI coverage includes Windows Shell resolving the
custom icon, Unicode paths with spaces, undo, source preservation, crop errors,
and result-file protections. All generated files stay under `tests/artifacts`.

`EngineTests.cs` is a standalone C# 5 console test harness. It does not start the
GUI. Compile from the workspace root with the .NET Framework compiler:

```powershell
New-Item -ItemType Directory -Force .\.runtime\folder-icon-tests | Out-Null
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
& "$framework\csc.exe" /nologo /target:exe /langversion:5 /main:FolderIconTool.Tests.EngineTests /out:.runtime\folder-icon-tests\EngineTests.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Xaml.dll "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\WindowsBase.dll" .\things\folder-icon-tool\src\Contracts.cs .\things\folder-icon-tool\src\IconEngine.cs .\things\folder-icon-tool\tests\EngineTests.cs
```

Run from the workspace root with one fixture-output directory inside that root:

```powershell
& .\.runtime\folder-icon-tests\EngineTests.exe .\things\folder-icon-tool\tests\artifacts
```

The example exercises a realistic directory depth. .NET Framework still applies
the Windows MAX_PATH limit, so avoid unusually long output paths.
Each run creates a unique subdirectory. All tests use generated images and
isolated folders there. The harness never accesses the user's media folders and
never removes fixtures, including on failure. Exit codes: `0` means all tests
pass; `1` means a test fails; `2` means harness setup fails.

Coverage includes center/explicit crops, EXIF orientation 6, independent ICO
directory parsing and frame decoding at 16/24/32/48/64/128/256 pixels, source
SHA-256 preservation, UTF-16 INI settings, exact preexisting bytes and file/folder
attributes after restore, two-step undo history, unrelated-file preservation,
sources inside target folders, external-change conflicts, and nonmutating
rejection of invalid inputs.
The self-source regression uses a valid ICO at the target icon path. Both crop
and fit modes must reject it without changing its bytes or attributes, INI
contents, folder attributes, or backup state.

Auto-fit tests use wide, tall, and tiny images. They check edge markers, centered
content bounds, preserved aspect ratios, transparent padding, enlargement, and
the legacy center-crop mode. Fit Apply tests decode every ICO size, preserve the
source SHA-256, and restore exact target bytes and attributes. A fit request with
an explicit crop must fail without changing files or creating backup state.

Format tests cover PNG, JPEG, BMP, static GIF, TIFF, and a generated two-frame
ICO. The ICO test requires the larger frame, not the first frame. Each format
test checks decoded/cropped pixels, an exclusive source reopen, source SHA-256,
generated ICO frames, and exact undo restoration.

Small WebP, AVIF, and HEIC fixtures are embedded as Base64. They were generated
locally from the same red/blue test image with Pillow and pillow-heif. Running
the harness does not require Python, downloads, or real photos. These formats
use installed Windows codecs: a working decoder must pass the full format test;
an unavailable decoder must give a JPG/PNG alternative and reject Apply without
changing source or target files. `INFO` lines distinguish successful decoding
from safe rejection, so a passing rejection does not claim format support.
Animation and multipage-image behavior are not covered.
