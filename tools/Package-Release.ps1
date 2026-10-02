param([string]$Name = 'SDRSharp.DCRDecoder-v1.0.0-win-x64.zip')
$ErrorActionPreference = 'Stop'
if ([System.IO.Path]::GetFileName($Name) -ne $Name -or -not $Name.EndsWith('.zip')) { throw 'Name must be a ZIP filename.' }
$project = Split-Path -Parent $PSScriptRoot
$distribution = Join-Path $project 'dist'
$files = Get-ChildItem -LiteralPath $distribution -File -Recurse -Force
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($distribution, $file.FullName).Replace('\','/')
    if ($relative -match '(^|/)(engines|blip25_vocoder[^/]*|__pycache__|\.venv)(/|$)' -or $relative -match '\.whl$') {
        throw "Do not redistribute user-installed engines or local environments: $relative"
    }
}
foreach ($required in @('LICENSE','SDRSharp.DCRDecoder.Core.dll','SDRSharp.DCRDecoder.dll','SDRSharp.DCRDecoder.Vocoder.dll','dcr_vocoder_host.py','DcrRuntime/python/python.exe','DcrRuntime/python/LICENSE.txt','DcrRuntime/packages/numpy-2.5.3.dist-info/licenses/LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $distribution $required))) { throw "Missing release file: $required" }
}
$destination = Join-Path $project "artifacts/$Name"
New-Item -ItemType Directory -Force (Join-Path $project 'artifacts') | Out-Null
# Open only the explicitly named release file; do not remove directories or installed engines.
$stream = [System.IO.File]::Open($destination, [System.IO.FileMode]::Create)
try {
    $zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            $relative = [System.IO.Path]::GetRelativePath($distribution, $file.FullName).Replace('\','/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Get-Item -LiteralPath $destination | Select-Object FullName,Length
