# Shipped Ledger

## Block A — Project foundation

- 🗑 **PB3** **Renderer is not locked down: no contextIsolation, sandboxed preload or restrictive CSP** — abandoned: A native WPF app has no web renderer, so contextIsolation and CSP have nothing to protect.
- 🗑 **PB4** **No typed IPC contract shared by main, preload and renderer** — abandoned: One process replaces main, preload and renderer; the InferenceService API takes the place of IPC.
- ✅ **PB1** **No WPF .NET 10 solution with the Fluent ThemeMode that builds and opens a window** — PhotoBoost.slnx builds App, Imaging, Inference and Render warning-free on .NET 10 x64, and the app opens an empty Fluent window following the system theme.

## Block B — Model management

## Block C — Inference and Real-ESRGAN end-to-end flow

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising

