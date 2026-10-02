# Provenance / dependency status

`StandardPrivacy.cs` is a C# port of the user-provided
standard-privacy reference script `descramble_known_key.py`.
The archives are not modified or included in build output. No redistribution license
for these supplied artifacts has been established.

The audio backend is `blip25-vocoder==1.0.0`, obtained separately by the user.
The release ZIP contains neither its wheel nor its compiled engine. The plugin
verifies the known Windows x64 wheel hash, preserves all wheel licenses/metadata,
and loads it in a private Python subprocess after an offline import.
Upstream: https://github.com/OpenBLIP25/blip25-vocoder
Python binding: https://pypi.org/project/blip25-vocoder/1.0.0/

Bundled runtime: CPython 3.14.8 Windows x64 embeddable distribution from
https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip
The full original license and bundled-library notices are retained at
`DcrRuntime/python/LICENSE.txt`. No Python source modifications are made;
`python314._pth` is configured for isolated application-local packages.

Bundled NumPy: 2.5.3, CPython 3.14 Windows x64 wheel from PyPI.
https://pypi.org/project/numpy/2.5.3/
All wheel metadata, native dependencies and license files are retained,
including `DcrRuntime/packages/numpy-2.5.3.dist-info/licenses/`.
The main license includes notices for the wheel's bundled third-party components.
Pinned distribution URLs and SHA-256 values are in `DcrRuntime/runtime-manifest.json`.

`AmbeFec.cs` uses Golay generator and correction conventions ported/adapted from
the MIT-licensed `src/fec.rs` at upstream commit
`d1762813b7142c021f4105c832bf7f060d1249fa` (Copyright 2026 Chance Lindsey).
During development, the DCR wire permutation was verified against 724 supplied
clean frame pairs; 968 private frame pairs verified correction. Recording-based
test tools were removed from the initial release source.
The original reference source is retained locally outside Git tracking.
MIT license, PATENT_NOTICE and ATTRIBUTION are preserved in `licenses/`
and copied to `dist/licenses`.
The upstream patent/provenance notices accompany the MIT license; the MIT
copyright license is not represented here as a patent clearance.

`ReferenceDsp` and `DcrProtocol` are ports of the user-provided
`DCR_IQ_to_AMBE_Codex_Reference.zip/dcr_iq_to_ambe_reference.py`.
The original files are retained locally under `old/Reference/` without modification.
Reference source and archives are excluded from Git tracking.
The reference README mentions `tallcat4/std-t98-tools` (GPL-3.0); no source
from that repository was fetched or copied during this implementation.
The supplied reference's redistribution license has not been established.

The supplied SDRSharp SDK uses SDRSHARP REFERENCE LICENSE, not MIT.
The locally supplied terms are in `SDK/sdrplugins/LICENSE.txt`. They grant
reference use for plugin development and do not include SDK distribution
outside the licensee company. SDK source, binaries and archives are excluded
from Git. Obtain the official SDK separately and review its accompanying terms.
Its assemblies are used as compilation references, with `Private=false`;
the build script excludes SDK reference assemblies and the SDRSharp application.

This project is licensed under MIT; see LICENSE. Third-party components retain
their own copyright notices and licenses. The project license does not relicense
the SDRSharp SDK, CPython, NumPy or the separately acquired voice engine.
