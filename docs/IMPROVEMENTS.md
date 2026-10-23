# Improvements

## Block A — Project foundation

## Block B — Model management

## Block C — Inference and Real-ESRGAN end-to-end flow

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising

## Block H — Editor: basic edits and toolbar

### §PB68 Invert colours

The owner listed "crop, invert, flip" on 2026-10-23. Flip is PB66, so invert is read
here as inverting the colours: each RGB sample becomes its complement, alpha kept, which
turns a scanned colour or black-and-white negative into a positive. It is an exact
operation on the 16-bit samples, so inverting twice gives back the same bytes, and it is
a version in the history like the other edits. Left as an idea until the owner says
whether "invert" meant this or mirroring, which PB66 already covers; if it meant
mirroring, this line is retired.
