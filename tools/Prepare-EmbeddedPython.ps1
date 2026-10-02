param([string]$OutputDirectory = '', [switch]$Offline)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'dist/DcrRuntime' }
$cache = Join-Path $project 'artifacts/runtime-downloads'
New-Item -ItemType Directory -Force $cache | Out-Null
$packages = @(
    @{ Name='python-3.14.8-embed-amd64.zip'; Url='https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip'; Sha256='a93abe456ab01bd96d7a085b3cdb6566b3063f4241360d114142fbdb07f0a310'; Folder='python' },
    @{ Name='numpy-2.5.3-cp314-cp314-win_amd64.whl'; Url='https://files.pythonhosted.org/packages/a4/73/d2c08231e4fde7e415501fd02c715d96e98599b2d8384445933944152984/numpy-2.5.3-cp314-cp314-win_amd64.whl'; Sha256='2c25dfa72943e4336ddb6b0ee4277b47a0c85bede0807530ec68103bf58e2c10'; Folder='packages' }
)
foreach ($package in $packages) {
    $archive = Join-Path $cache $package.Name
    if (-not (Test-Path -LiteralPath $archive)) {
        if ($Offline) { throw "Missing cached runtime package: $archive" }
        Invoke-WebRequest -Uri $package.Url -OutFile $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $package.Sha256) { throw "Runtime package checksum mismatch: $archive" }
    $destination = Join-Path $OutputDirectory $package.Folder
    New-Item -ItemType Directory -Force $destination | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination, $true)
}
# Isolated search paths: no registry, user site-packages, PYTHONPATH, pip or site startup code.
@('python314.zip', '.', '../packages', '../engines/blip25-vocoder-1.0.0') |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'python/python314._pth') -Encoding ascii
$packages | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'runtime-manifest.json') -Encoding utf8
& (Join-Path $OutputDirectory 'python/python.exe') -I -B -c 'import sys, numpy; assert sys.version_info[:3] == (3,14,8); assert numpy.__version__ == "2.5.3"; print("Embedded runtime ready: Python", sys.version.split()[0], "NumPy", numpy.__version__)'
if ($LASTEXITCODE -ne 0) { throw 'Embedded runtime smoke test failed.' }
