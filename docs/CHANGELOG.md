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
- ✅ **PB8** **No scripts/export-and-quantize.py to export each model to ONNX with dynamic axes** — export-and-quantize.py now also writes GFPGAN v1.4 at 512x512 and adopts BRIA's RMBG-1.4 ONNX after checking it against PyTorch, each with an FP16 variant kept only above 40 dB.
- ✅ **PB9** **Exported models are not quantized to INT8** — export-and-quantize.py writes int8.onnx for all four models with QUInt8 dynamic quantization, leaving no float Conv or MatMul; INT8 runs slower than FP32, so it buys size only.
- ✅ **PB10** **INT8 models are not validated against FP32 on a test image** — export-and-quantize.py checks INT8 and FP16 against FP32 on each model's upstream sample image, writes PSNR and SSIM to meta.json and fails INT8 below a per-model floor.
- ✅ **PB11** **No models/<name>/{fp32.onnx, int8.onnx, meta.json} layout with SHA256 hashes** — Each .cache/models/<name>/ holds fp32, fp16 and int8 ONNX beside a meta.json with IO, ranges, tiling, SHA-256 per variant, license and validation, held to meta.schema.json and read by ModelMeta.
- ✅ **PB12 (manifest builder)** **Generated ONNX models have no download location the app can fetch from** — build-manifest.py turns every meta.json into manifest.json with URL, size and SHA-256 per file, checks each file, and stages flat release assets for upload.

## Block C — Inference and Real-ESRGAN end-to-end flow

- ✅ **PB14** **Execution provider is neither detected at runtime nor shown in the UI** — ProviderDetector runs a probe session on DirectML on the GPU with the most video memory, falls back to CPU with the reason, and the status bar shows the provider and GPU.
- ✅ **PB15** **No InferenceService queue that runs one inference at a time** — InferenceService runs queued jobs one at a time in order on one background thread, with progress through IProgress, cancellation while queued or between tiles, and failures kept per job.
- ✅ **PB16** **ONNX sessions are not loaded on demand nor released after idle minutes** — SessionCache loads a model variant's session on first use, lends it out by lease, drops the model's other variant on a switch and releases sessions idle for 5 minutes.
- ✅ **PB17** **No preprocessing to and from model tensors, and no unit tests for it** — ImageDecoder now reads any WIC format into RgbaImage upright per EXIF, converted to sRGB, with alpha, grayscale and 16-bit samples kept, tested on JPEG, PNG and WebP.
- ✅ **PB18** **Large images cannot be processed: no tiling with overlap** — Tiler runs a model over 256px tiles with 16px overlap and stitches exact cores, padding odd sizes for even-only models; a neighbour-reading model matches a whole-image run bit for bit.
- ✅ **PB19** **No Real-ESRGAN upscale pipeline for 2x and 4x** — Upscale runs Real-ESRGAN x4plus, or native x2plus for 2x, through the tiler and a ModelLibrary session, with 32 px overlap measured at 45 dB against a whole run and alpha resized bilinearly.
- ✅ **PB20** **UI receives no tile X of Y progress during inference** — Jobs report tile X of Y with a stage and a time-left estimate, throttled to 100 ms with first and last kept, and UpscaleJob queues an upscale wired to that progress.

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising

