# Patent Notice

This source code is provided for **research and educational purposes only**.
It is a written description of how certain voice encoding and decoding
algorithms could be implemented.

Executable objects compiled or derived from this package, and the act of
running them, may be covered by one or more patents. Readers are strongly
advised to check for any patent restrictions or licensing requirements
before compiling, using, or distributing this source code.

The above patent notice is adapted from the verbatim form used by the
[mbelib](https://github.com/szechyjs/mbelib) and
[JMBE](https://github.com/DSheirer/jmbe) projects.

## Patent-relevant codec components

The project's default codec path is **not derived from the TIA-102
specifications**. The shared analysis and synthesis core in `src/engine/`
was **recovered by reverse engineering a compiled reference vocoder image** —
x86 disassembly transliterated to fixed-point Rust, constants recovered from
the binary's data section, correctness pinned bit-exact
against the vocoder's own output. It is not a copy of the reference vendor's
source code, which the project never obtained or read; it **is** a derivation of
the reference vocoder's behavior, and it reads on the same active patents listed
above.

**This applies to both codecs' audio output, including full-rate IMBE.** The
division is by layer, not by codec, and the two codecs are close to symmetric:

| Layer | Provenance |
|---|---|
| Wire framing, FEC, bit interpretation (both codecs) | **Spec** — TIA-102.BABA / BABA-A |
| Quantizer data | **AMBE+2: firmware** (all VQ codebooks). **IMBE: spec**, except the gain ladder |
| Analysis / encode chain (shared) | **Reverse-engineered** |
| Synthesis / decode audio (shared) | **Reverse-engineered** |

Both wire layers are spec-derived. The one asymmetry in quantizer data is that
AMBE+2's vector-quantizer codebooks are entirely firmware-recovered, whereas
IMBE's quantizer tables are published spec data apart from the gain ladder,
where the reference vendor's real table diverges from Annex-E at its bottom
eight levels.

Do not read "the IMBE bitstream is a published standard" as "the IMBE
implementation here is unencumbered." The bitstream is published; the analysis
and synthesis engineering that turns it into audio is the reference vendor's,
and that is what this crate reproduces — for IMBE exactly as much as for AMBE+2.

State this accurately in both directions: substantial original engineering
surrounds the engine, and the engine itself is derived. See
[`ATTRIBUTION.md`](./ATTRIBUTION.md) — **the authoritative statement of what is
original versus derived**.

| Module | What it is | Provenance |
|---|---|---|
| `src/engine/` | The IMBE (P25 Phase 1) + AMBE+2 (Phase 2) encode/decode path, and its own VQ tables (PRBA24 / PRBA58 / HOC b5–b8 / Annex-O + MONO65 gain) | Reverse-engineered to emulate the reference vocoder, synthesized from many reference resources rather than any single one. Bit-exact against the **reference** IMBE/AMBE+2 software vocoder: the standard tables verified against TIA-102.BABA-A, the reference-specific deviations against the codec's own output. |

This crate is **patent-encumbered**. As an AMBE+2 / MBE implementation it
unavoidably reads on the same active patents catalogued below — notably
**US8359197**, active until **2028-05-20** — and it additionally embodies the
reference vendor's proprietary analysis and synthesis engineering, which the
expired-patent documentation does **not** place in the public domain.

The codec core is a private module of this crate. It is not published
separately and is not obtainable except as part of `blip25-vocoder`, so
depending on this crate means taking on the encumbered core in full.

It is provided strictly for **research and interoperability study**; no
commercial product is shipped from it (see **Project policy** below).
Publishing it distributes a patent-encumbered implementation, which is a
deliberate, documented decision and not an accident of packaging. Anyone
depending on this crate takes on that consideration themselves.

## Specific patents the maintainers are aware of

This list is not exhaustive and is not legal advice. It reflects this
project's own claim-by-claim patent audit of the AMBE-3000 family.

### Active patents that the half-rate (AMBE+2) implementation reads on

| Patent | Subject | Anticipated expiration |
|---|---|---|
| **US8359197** | Half-rate vocoder — mixed pitch+voicing+gain first parameter codeword + Golay FEC + scrambling. The parent grant of US8595002 with ~5 years patent term adjustment. | **2028-05-20** |

Per that claim-by-claim analysis, **any BABA-A-compatible
half-rate (AMBE+2) decoder or encoder unavoidably reads on US8359197
claims 1, 8, 9, 13, 14, 15 (encode side) and 42, 47–51, 60, 72 (decode
side).** The wire format mandates the mixed first-codeword construction
that the patent claims; bitstream interoperability with the AMBE-3000
chip cannot be achieved with a different construction.

### Active patents that overlap potential future frontend improvements

These patents do **not** read on the current implementation, but they
constrain the design space for future analysis-frontend improvements
(e.g. closing the measured PESQ gaps on noisy / tonal / SCBA-mask
content).

| Patent | Subject | Anticipated expiration |
|---|---|---|
| US8265937 | Breathing-apparatus speech enhancement (fireground / SCBA noise) | ~2032 |
| US12254895 | Detecting and compensating for speaker mask | ~2045 |
| US11990144 | Reducing perceived effects of non-voice data | ~2041 |
| US12451151 | Tone frame detector (PTAB-confirmed) | ~2042 |

### Expired patents that the implementation derives from

The following are now public domain and serve as detailed algorithmic
documentation. Implementing them is unrestricted.

| Patent | Subject | Expired |
|---|---|---|
| US5701390 | MBE synthesis with regenerated phase | 2015-02-22 |
| US6199037 | Joint quantization of voicing and pitch | 2017-12-04 |
| US8315860 | Interoperable vocoder | 2022-11-13 |
| US8595002 | Half-rate vocoder (AMBE+2) — sibling of active US8359197 | 2023-04-01 |
| US7634399 | Voice transcoder | 2025-11-07 |

## Project policy

**This project is research / educational software.** The maintainers do
not distribute commercial product, do not sell licenses, and do not
charge for downloads, hosting, or integration.

**No commercial product is shipped from this codebase before
2028-05-20** (US8359197 anticipated expiration). Downstream consumers
who wish to use this code in a commercial product are responsible for
their own patent due diligence and licensing.

## Why this notice exists

The maintainers researched comparable open-source projects (JMBE,
mbelib, OP25, SDRTrunk, DSDcc, dsd-fme) and found a consistent pattern:
all carry a patent disclaimer of this form, all distribute under
permissive or copyleft open-source licenses without payment, and none
have been the subject of patent enforcement action by the reference vendor in
10+ years. The reference vendor's documented enforcement (Codec2 / David Rowe,
2017–2019) targeted commercial use, not the existence of open-source
implementations.

This notice aligns blip25-vocoder with that prevailing posture and signals
the maintainers' awareness of the patent landscape.
