# Build and package

Requires Windows x64 and the .NET 9 SDK. Obtain the official SDRSharp .NET 9 SDK separately and place its compilation reference assemblies in `SDK/sdrplugins/lib/`. SDK files are excluded from Git and release packages.

```powershell
./build.ps1
./tools/Package-Release.ps1
```

The build compiles the solution, runs 19 recording-independent audio tests and prepares the embedded Python/NumPy runtime in `dist/`. First use downloads the pinned distributions and verifies their SHA-256 hashes. The voice engine is not downloaded or bundled.

For an existing runtime download cache:

```powershell
./build.ps1 -OfflineRuntime
```

`-NoRestore` skips .NET dependency restoration when build assets already exist.

Core, vocoder and tests can be built without the SDRSharp SDK:

```powershell
dotnet run --project tests/SDRSharp.DCRDecoder.Tests -c Release
```

GitHub Actions performs this SDK-independent build and test. The offline file decoder, recording-based test utilities and Windows direct-output backend have been removed. Runtime playback uses SDRSharp's audio stream.

Output: `artifacts/SDRSharp.DCRDecoder-v1.0.0-win-x64.zip`. It includes LICENSE and third-party notices, three plugin DLLs, the Python helper and the isolated runtime. Installed voice engines and wheels are rejected by the packaging script.
