# Set aside

## Block A — Project foundation

## Block B — Model management

## Block C — Inference and Real-ESRGAN end-to-end flow

- ⏸ **PB52** (deps: PB43 ✅) **TensorRT-RTX builds its engine for 10 to 17 s at every session creation, so auto cannot prefer it** — set aside (owner: should auto prefer TensorRT-RTX?): Engines are cached and built ahead (PB55); auto still never tries TensorRT-RTX. → §PB52

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising
