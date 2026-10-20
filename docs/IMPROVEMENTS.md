# Improvements

## Block A — Project foundation

## Block B — Model management

### §PB58 Lean installer, models on first use

Owner, 2026-10-20: a lean installer (upscale, denoise, YuNet, about 100 MB of models);
face restore (GFPGAN FP32, 325 MB) and background removal (RMBG-1.4 FP32 from BRIA, 168
MB, and ViTMatte FP32, 99 MB) fetch their models on first use. The download must never
be why a user gives up on the app:

1. Before: a missing model shows a "one-time download" badge (what, size, source, license),
   and the button reads "Download and <operation>".
2. Consent: the click opens an in-app panel: each file, the total, its source and folder, that
   the photo never leaves the PC, and the license; "Download and continue" or "Not now".
3. Never blocking: the download runs beside the app, every other operation keeps working, and
   the asked-for operation starts by itself once its models are verified.
4. Visible: MB, speed and time left in the card and the status bar, then "Verifying…", then
   "Ready: <operation> now works offline".
5. Resumable: a cancelled or interrupted download keeps its .part and resumes by HTTP Range;
   transient failures retry; a file failing SHA-256 is deleted, never loaded.
6. Errors that help: offline, unreachable or corrupt each say what to do, with Try again.
7. Control: About lists downloaded models and their disk use, removes them, and downloads
   everything ahead for offline use.

Hosting: ViTMatte FP32 joins a manifest version 2, which the app embeds so sizes and
hashes are known offline. stage-models.ps1 drops the three models.

## Block C — Inference and Real-ESRGAN end-to-end flow

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising
