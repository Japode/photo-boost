# Roadmap (active backlog)

## Block A — Project foundation

- 📋 **PB1** (deps: —) **No Electron + Vite + React + TypeScript scaffold that runs with npm install && npm run dev** — Every later block needs the main, preload, renderer and shared folders to land in. → §PB1
- 📋 **PB2** (deps: PB1) **No ESLint, Prettier or Vitest wired into npm scripts** — Tiling and preprocessing need unit tests and a lint gate before any pipeline code exists. → §PB2
- 📋 **PB3** (deps: PB1) **Renderer is not locked down: no contextIsolation, sandboxed preload or restrictive CSP** — The renderer opens untrusted image files, so it must never reach Node or load remote code. → §PB3
- 📋 **PB4** (deps: PB1, PB3) **No typed IPC contract shared by main, preload and renderer** — Inference, progress and model download all cross the process boundary and must share one set of types. → §PB4
- 📋 **PB5** (deps: PB1) **Unverified whether onnxruntime-node and sharp load under the current Electron ABI** — Both are native modules; a mismatch found late would force a stack change after pipelines exist. → §PB5

## Block B — Model management

- 📋 **PB6** (deps: —) **No verified source URLs for Real-ESRGAN x4plus, GFPGAN v1.4 and RMBG-1.4 weights** — The brief forbids invented URLs; each link must be fetched and its license checked before it is scripted. → §PB6
- 📋 **PB7** (deps: PB6) **No scripts/download-models.ts to fetch the original weights** — Export and quantization start from the upstream weights, which must not live in the repository. → §PB7
- 📋 **PB8** (deps: PB7) **No scripts/export-and-quantize.py to export each model to ONNX with dynamic axes** — The app consumes ONNX only, and tiling needs dynamic batch, height and width axes. → §PB8
- 📋 **PB9** (deps: PB8) **Exported models are not quantized to INT8** — INT8 dynamic quantization is the default variant, chosen to cut download size and memory. → §PB9
- 📋 **PB10** (deps: PB9) **INT8 models are not validated against FP32 on a test image** — A quantized model that silently degrades quality must fail the script, not ship. → §PB10
- 📋 **PB11** (deps: PB9, PB10) **No models/<name>/{fp32.onnx, int8.onnx, meta.json} layout with SHA256 hashes** — The app verifies downloads by hash and reads scale, input layout and thresholds from meta.json. → §PB11
- 📋 **PB12** (deps: PB11) **Generated ONNX models have no download location the app can fetch from** — First launch downloads models, so the exported files need a stable published home and a manifest. → §PB12
- 📋 **PB13** (deps: PB4, PB12) **First launch has no Download models screen with progress and hash verification** — Without models nothing works, and a corrupt file must be caught before onnxruntime loads it. → §PB13

## Block C — Inference and Real-ESRGAN end-to-end flow

- 📋 **PB14** (deps: PB5) **Execution provider is neither detected at runtime nor shown in the UI** — Users need to know if they run on GPU or CPU, and the app must fall back without crashing. → §PB14
- 📋 **PB15** (deps: PB4, PB14) **No InferenceService queue that runs one inference at a time** — Two concurrent sessions on one GPU or in limited RAM is the fastest route to OOM. → §PB15
- 📋 **PB16** (deps: PB15) **ONNX sessions are not loaded on demand nor released after idle minutes** — Keeping every model resident wastes memory the user needs for large images. → §PB16
- 📋 **PB17** (deps: PB2, PB5) **No sharp preprocessing to and from model tensors, and no unit tests for it** — Every pipeline converts images to normalized NCHW float tensors and back; one bug corrupts all outputs. → §PB17
- 📋 **PB18** (deps: PB17) **Large images cannot be processed: no tiling with overlap** — Running Real-ESRGAN on a full 12 MP image exhausts memory; tiles keep peak usage bounded. → §PB18
- 📋 **PB19** (deps: PB11, PB16, PB18) **No Real-ESRGAN upscale pipeline for 2x and 4x** — Upscaling is the first operation and the one the end-to-end flow is built around. → §PB19
- 📋 **PB20** (deps: PB4, PB19) **Renderer receives no tile X of Y progress during inference** — A 4x upscale can take minutes on CPU; without progress the app looks frozen. → §PB20
- 📋 **PB21** (deps: PB19) **An out-of-memory failure aborts the inference instead of retrying with smaller tiles** — OOM depends on the GPU and image; halving the tile is a recoverable fix the user should not need to do. → §PB21
- 📋 **PB22** (deps: PB19) **Inference time and the provider used are not logged per operation** — The README benchmark table and user bug reports both need timing per operation and provider. → §PB22
- 📋 **PB23** (deps: PB4) **No screen to open an image by drag and drop or file picker** — Opening a photo is the first step of every flow. → §PB23
- 📋 **PB24** (deps: PB20, PB23) **No operations panel to run an upscale and pick 2x or 4x** — The user needs a control to trigger the pipeline and see its progress. → §PB24
- 📋 **PB25** (deps: PB19) **Results cannot be exported as PNG, JPG or WebP** — Exporting closes the open, upscale, export loop the brief asks for before any other model. → §PB25
- 📋 **PB26** (deps: PB21, PB22, PB24, PB25) **Open, upscale and export flow is not tested end to end** — The brief gates GFPGAN and background removal on a proven Real-ESRGAN flow. → §PB26

## Block D — Editor: comparison, history and quality modes

- 📋 **PB27** (deps: PB24) **No before and after slider comparison** — Judging an enhancement needs both versions aligned at the same zoom. → §PB27
- 📋 **PB28** (deps: PB24) **No operation history with undo and redo** — Users chain operations and must be able to step back without reprocessing from scratch. → §PB28
- 📋 **PB29** (deps: PB13, PB16) **No UI choice between INT8 and the High quality FP16/FP32 models** — The brief requires both variants selectable, trading speed and memory for fidelity. → §PB29

## Block E — Face restoration and background removal

- 📋 **PB30** (deps: PB26) **No face detection and alignment to feed GFPGAN crops and paste results back** — GFPGAN expects aligned 512px face crops, so a whole photo cannot be passed in directly. → §PB30
- 📋 **PB31** (deps: PB30) **No GFPGAN face restoration pipeline in faceRestore.ts** — Face restoration is the second operation the brief lists. → §PB31
- 📋 **PB32** (deps: PB26) **No background removal pipeline producing a transparent image** — Background removal is the third operation the brief lists. → §PB32

## Block F — Delivery: README, benchmarks and installers

- 📋 **PB33** (deps: PB12, PB26) **No README in Portuguese on running, generating models, Python and hardware requirements** — The brief lists the README as a deliverable, in Portuguese. → §PB33
- 📋 **PB34** (deps: PB10, PB22, PB33) **No table comparing FP32 and INT8 size, inference time and quality** — The brief asks for this table so users can choose a quality mode on evidence. → §PB34
- 📋 **PB35** (deps: PB26) **npm run build does not produce installers for Windows, macOS and Linux** — Installers are a deliverable, and native modules break easily when packaged. → §PB35

## Block G — Phase 2: denoising

- 📋 **PB36** (deps: PB31, PB32) **No denoising operation** — Denoising is listed as an optional phase 2 operation. → §PB36

## Non-goals

- **Cloud inference or external APIs** Every model runs locally; the network is used
  only to download model files on first launch.
- **Models committed to the repository** Weights are fetched and generated by scripts
  and stored in userData/models, never in git.
- **Web or mobile builds** The target is a desktop Electron app for Windows, macOS and
  Linux.
