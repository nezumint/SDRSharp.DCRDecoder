# Attribution & provenance

This document states plainly what in this repository is original work, what is
reverse-engineered for interoperability, and how the two were built. It exists
so the project's provenance is represented **accurately** — neither overstating
originality nor understating the substantial original engineering here.

It complements, and does not replace, [`PATENT_NOTICE.md`](./PATENT_NOTICE.md).

## The short version

The P25 MBE vocoder family (IMBE, AMBE+2) is proprietary and patent-encumbered.
This project did **not** obtain, read, or copy the reference vocoder's source
code. What it did was **reverse-engineer the vocoder's observable behavior**
from a compiled binary image and reproduce it bit-for-bit, then build an
extensive original codebase — protocol, DSP, tooling, and test infrastructure —
around that matched engine.

That distinction matters and cuts both ways:

- It is **not** a copy of the reference vocoder's source. The numeric engine was
  recovered by disassembly, observation, and measurement — not lifted from source.
- It **is** derived from the reference vocoder. Reproducing a patented algorithm's
  behavior by reverse engineering is still a derivation of that behavior, and
  the result reads on the same active patents. This is stated, not hidden.

## The line that matters: recovered from a binary vs. verified against the standard

Every constant table in this crate falls on one side of a single line, and
which side it falls on is the most important fact in this document:

- **Recovered from a compiled binary.** Read out of the reference vocoder's data
  section, or reconstructed by observing its arithmetic. Nothing published
  authorises these values. They live in `src/engine/`.
- **Verified against the published standard.** Transcribed from the normative
  Annex data of TIA-102.BABA / BABA-A and checked against the standard's own
  text. These live in `src/generated/`, and are indexed by the wire layers in
  `src/fullrate/` and `src/halfrate/`.

The two sets are kept in separate files and never merged, because the answer to
"where did this number come from?" has to stay answerable per table.

## What is reverse-engineered (derived from the reference vocoder)

| Component | What it is | How it was obtained |
|---|---|---|
| `src/engine/` | The complete AMBE+2 / IMBE codec engine: analysis, synthesis, quantization, and the constant tables all of it indexes. Both codecs are one fixed-point engine selected by a mode flag. | **Recovered from a binary.** Reverse-engineered from a compiled image of the DVSI reference software vocoder — x86 disassembly transliterated function by function to fixed-point Rust, constants recovered from the binary's data section. |

## What is spec-derived

| Component | What it is | How it was obtained |
|---|---|---|
| `src/halfrate/` (AMBE+2 wire layer) | Deframing, Golay/PN FEC, Annex-S deinterleave, bit interpretation | **Verified against the standard.** Written from TIA-102.BABA-A §2.4–§2.6, §2.11–§2.13; the Annex L/M/N/O/S and bit-prioritization tables are extracted from the standard and frozen into source. |
| `src/fullrate/` (IMBE wire layer) | Deframing, FEC, bit prioritization and dequantization for full-rate IMBE — **not** its synthesis | **Verified against the standard.** TIA-102.BABA publishes the IMBE bitstream and its bit-exact fixed-point reference, so this was built from that standard plus ITU-T G.191 basic operators. Other implementations of the same standard (OP25's `imbe_vocoder`) were consulted as a cross-check only. |
| `src/generated/` (Annex E/O/P/Q/R) | The **published** IMBE / AMBE+2 quantizer tables (gain levels, PRBA, HOC) that the `halfrate` / `fullrate` wire-layer dequantizers index | **Verified against the standard.** Normative TIA-102.BABA-A Annex data. These are separate data from the tables inside `src/engine/`, which were recovered from the binary. |

`src/engine/` is a private module with no public API surface: nothing in it is
exported, and consumers reach it only through the `Vocoder` facade. That is a
packaging fact and changes nothing about what it is — patent-encumbered,
derived from the reference vocoder, and provided for research and
interoperability study. See `PATENT_NOTICE.md` for the specific patents it
reads on (notably **US8359197**, active to **2028-05-20**).

## What is original work

A large majority of the repository is first-party engineering that is **not**
the reference vocoder's and was not reverse-engineered from it:

- **P25 protocol layer** — the wire formats, NID/framing, FEC (Golay, Hamming,
  interleavers), bit orderings, and rate conversion, derived from the
  public TIA-102 standards, not from any vendor binary.
- **The library and its API** — the `Vocoder` surface, the single-pass streaming
  encoders/decoders (`EncodeStream`/`DecodeStream`/`LiveEncoder`), soft-decision
  decode, the enhancement/AGC layer, and the parametric rate-conversion path.
- **The specification effort** — the derived TIA-102 implementation specs
  written for this work, an independent reimplementation track in its own right.
- **The wire-layer tone path** — Annex-T tone frame construction and parsing.
- **The methodology and tooling** — the differential-testing harness that makes
  the engine verifiable at function granularity, the golden-vector capture
  and replay that lets the result prove itself with no emulator present, and
  the conformance fixtures. Establishing bit-exactness against a black-box
  target, and being able to demonstrate it, is itself substantial original
  work — it is the labor of interoperability engineering, not of copying.

## How correctness is established

By differential testing against the reference binary, not by access to its
source. Each ported function is driven with hundreds of inputs alongside the
original executing under emulation, demanding **exact integer equality** rather
than an audio-similarity tolerance. Both directions are verified end to
end — every output sample, every payload bit, and every word of codec state, on
every call, from the first sample after initialization.

Those answers are frozen into two golden fixtures — `tests/engine_golden.bin`
for the engine and `tests/dvsi_gold.bin` for the wire layer above it — which
replay the verification on any host with no emulator, no reference binary and
no x86.

**Neither fixture is tracked.** Each is a capture of the reference vocoder's
own behaviour, and the corpus behind them is not ours to redistribute; keeping
a repacking of it in a public repository would contradict that. They are
regenerated locally from the capture (`examples/build_dvsi_gold.rs` for the
wire-layer fixture) and the tests that read them skip, with a printed note,
when they are absent. Run them before releasing any change to the engine — they
are the only thing that answers whether the codec is still *correct* rather
than merely unchanged.

What ships instead is `tests/golden_corpus.bin`, built from freely-licensed
speech and synthetic sources. It is produced by this crate, so it detects drift
and cross-platform divergence and makes no claim about correctness. The
distinction is deliberate and worth preserving: a regression fixture
regenerated after a mistake will faithfully enshrine the mistake.

## Honest bottom line

Represent it as what it is: **original interoperability engineering built around
a reverse-engineered, patent-encumbered vocoder core.** That framing credits the
real and considerable work done here without misstating the codec's origin — and
it is the framing that holds up if the project is ever examined.

## Speech material

`tests/golden_corpus.bin` embeds short excerpts of recorded speech from the
**Open Speech Repository**
(<https://www.voiptroubleshooter.com/open_speech/american.html>) — Harvard
sentences, American English. The publisher makes the material freely available
"for use in VoIP testing, research, development, marketing and any other
reasonable application" and asks one thing in return:

> Identify the source of the speech materials as "Open Speech Repository".

That credit is carried here, in the fixture's own header, and in
`tests/golden_corpus.rs`. Keep it on anything derived from those bytes.

The `syn-*` sources in the same fixture are synthesised from the constants in
`examples/build_golden_corpus.rs` — tones, silence, noise and level ramps — and
carry no third-party rights.

## Trademarks

**IMBE**, **AMBE**, **AMBE+**, and **AMBE+2** are trademarks of Digital Voice
Systems, Inc. (DVSI). **MOTOTRBO** is a trademark of Motorola Solutions, Inc.
**NXDN** is a trademark of Icom Incorporated and JVC KENWOOD Corporation.
**IDAS** is a trademark of Icom Incorporated. This project is not affiliated
with, endorsed by, or sponsored by any of them.

Those marks appear here only to identify the systems this code interoperates
with — the referential use that lets anyone say what their product is
compatible with. They are not used as names for anything this project ships.
The public API names its rates by what they are (`FullRate7200x4400`,
`HalfRate3600x2450`), which follows TIA-102.BABA-A's own vocabulary: the
standard is titled *IMBE Vocoder and Half-Rate Vocoder*, using DVSI's mark for
the full-rate codec but deliberately calling the half-rate one the
"Half-Rate Vocoder" rather than AMBE+2.

`MBE` is the generic technique — Multi-Band Excitation, from the Griffin–Lim
analysis/synthesis literature — not a DVSI mark.
