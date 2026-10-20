# Command line

PhotoBoost starts like any Windows app with no arguments. These flags exist so an automated UI
case can reach a state without a file dialog or drag and drop, and they work for anyone.

```
PhotoBoost.exe [--open <image>] [--models <folder>] [--provider auto|winml|dml|cpu]
               [--export <file>] [--quality compact|high]
```

Each flag takes its value after a space or an equals sign (`--provider cpu` or `--provider=cpu`)
and may be given once. An unknown flag, a missing value or a value outside the list is refused:
PhotoBoost says which one in a message box and exits with code 2.

| Flag | What it does |
| --- | --- |
| `--open <image>` | Opens this image at start instead of the empty screen. |
| `--models <folder>` | Reads and downloads models in this folder alone, instead of the `models` folder the installer put beside the app followed by `%LOCALAPPDATA%\PhotoBoost\models`. |
| `--provider auto\|winml\|dml\|cpu` | `auto` (the default) runs on DirectML on the GPU with the most video memory and falls back to the CPU, and where TensorRT-RTX runs on that GPU the upscalers use it once their engines are built in the background; `winml` first tries a provider Windows ML installed for this PC (TensorRT-RTX on NVIDIA, OpenVINO on Intel), then does what `auto` does, and fetches a provider not installed yet in the background for the next start; `dml` asks for DirectML; `cpu` never tries a GPU. The status bar shows what was chosen. |
| `--export <file>` | When the run finishes, writes the result here (PNG, JPG or WebP by extension) and stays open. |
| `--quality compact\|high` | Starts on the FP16 models the installer carries (compact) or the FP32 ones (high) instead of the saved choice. |

`--provider` takes effect today. `--open`, `--models`, `--export` and `--quality` are parsed and
validated now, and take effect as the open screen, the model download, the export and the
quality choice arrive.
