param([switch]$NoRestore, [switch]$OfflineRuntime)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    if (-not (Test-Path -LiteralPath 'SDK/sdrplugins/lib/SDRSharp.Radio.dll')) {
        throw 'Place the official .NET 9 SDRSharp SDK under SDK/sdrplugins/lib before building the plugin. See BUILD.md.'
    }
    $buildArgs = @('build', 'SDRSharp.DCRDecoder.sln', '-c', 'Release')
    if ($NoRestore) { $buildArgs += '--no-restore' }
    dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/SDRSharp.DCRDecoder.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Audio tests failed.' }
    New-Item -ItemType Directory -Force dist | Out-Null
    $pluginOutput = Join-Path $PSScriptRoot 'src/SDRSharp.DCRDecoder.Plugin/bin/Release/net9.0-windows'
    foreach ($file in @('SDRSharp.DCRDecoder.dll','SDRSharp.DCRDecoder.Core.dll','SDRSharp.DCRDecoder.Vocoder.dll','dcr_vocoder_host.py','SDRSharp.DCRDecoder.deps.json')) {
        Copy-Item -LiteralPath (Join-Path $pluginOutput $file) -Destination dist
    }
    Copy-Item -LiteralPath THIRD_PARTY_NOTICES.md -Destination dist
    Copy-Item -LiteralPath README.md -Destination dist
    New-Item -ItemType Directory -Force dist/licenses | Out-Null
    Copy-Item -Path licenses/* -Destination dist/licenses
    Copy-Item -LiteralPath CHANGELOG.md -Destination dist
    Copy-Item -LiteralPath LICENSE -Destination dist
    & ./tools/Prepare-EmbeddedPython.ps1 -Offline:$OfflineRuntime

}
finally { Pop-Location }
