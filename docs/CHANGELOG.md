# Shipped Ledger

## Block A — Project foundation

- 🗑 **PB3** **Renderer is not locked down: no contextIsolation, sandboxed preload or restrictive CSP** — abandoned: A native WPF app has no web renderer, so contextIsolation and CSP have nothing to protect.
- 🗑 **PB4** **No typed IPC contract shared by main, preload and renderer** — abandoned: One process replaces main, preload and renderer; the InferenceService API takes the place of IPC.
- ✅ **PB1** **No WPF .NET 10 solution with the Fluent ThemeMode that builds and opens a window** — PhotoBoost.slnx builds App, Imaging, Inference and Render warning-free on .NET 10 x64, and the app opens an empty Fluent window following the system theme.
- ✅ **PB2** **No xunit.v3 test projects, analyzer gate or architecture tests in the build** — run-tests.cmd builds the solution and runs xunit.v3 Imaging, Inference and Architecture tests on Microsoft.Testing.Platform; layering and analyzer-gate rules now fail the run.
- ✅ **PB5** **Unverified which ONNX Runtime package (DirectML, Windows ML, CUDA) runs in a self-contained WPF build** — DirectML 1.24.4 runs exactly in self-contained and single-file publishes and ships; CUDA is rejected and Windows ML is proposed as PB43 (design recorded in `spike/onnx-runtime/README.md`).

## Block B — Model management

- ✅ **PB6** **No verified source URLs for Real-ESRGAN x4plus, GFPGAN v1.4 and RMBG-1.4 weights** — scripts/model-sources.json lists fetched URLs, sizes, SHA-256 and licenses for Real-ESRGAN x4plus and x2plus, GFPGAN v1.4, RMBG-1.4 and the U2-Net fallback.
- ✅ **PB7** **No scripts/download_models.py to fetch the original weights** — scripts/download_models.py fetches the listed weights into .cache/weights, resumes partial files, prints progress, verifies size and SHA-256 and skips valid files.
- ✅ **PB8 (Real-ESRGAN half)** **No scripts/export-and-quantize.py to export each model to ONNX with dynamic axes** — export-and-quantize.py writes Real-ESRGAN x4plus and x2plus as FP32 and FP16 ONNX with dynamic axes, opset 17, checked against PyTorch at untraced sizes.

## Block C — Inference and Real-ESRGAN end-to-end flow

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising

