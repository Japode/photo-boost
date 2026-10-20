# Improvements

## Block A — Project foundation

## Block B — Model management

### §PB59 First-use download driven end to end

PB58's consent case reads the badge, the panel and "Not now", and ModelDownloadsTests
drives resume, retry and verification against a stand-in handler, but nothing watches
the window through a real download: the card's MB, speed and time left, the status-bar
chip, Pause and Resume, a failure's Try again, and the operation starting by itself once
its models are verified. A case cannot reach the network, and should not fetch 267 MB
anyway. A small fixture server on localhost serving tiny files under a test manifest,
with an app option to read that manifest, would let one case download, pause, resume and
auto-run a stand-in model; the option has to stay https-only in release builds, or be a
test-only path, so it never weakens the pinned manifest.

## Block C — Inference and Real-ESRGAN end-to-end flow

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising
