# Changelog

## 1.0.0 — 2026-10-03

First release for SDRSharp revision 1921, .NET 9, Windows x64.

- Real-time DCR IQ decoding and call information display.
- AMBE error correction and standard privacy decoding with a user-supplied code.
- Decoded audio through SDRSharp's `IRealProcessor` audio stream, with stereo sample-rate conversion.
- Bundled isolated Python and NumPy runtime; voice engine obtained separately by the user.
- Offline import of the supported Windows x64 wheel with SHA-256 verification.
- Visible privacy-code input; codes remain in memory only.
- Status display flicker fixed.

User verification: embedded-runtime audio playback, stereo playback and status display work.
Regression verification before release: 116 checks, including full PCM comparison for clear and privacy reference recordings.

Source cleanup: MIT license added; offline file decoder, recording-dependent tests, Windows direct-output helper and NAudio dependency removed. Retained 19 self-contained audio regression tests.
