"""PB8: export-and-quantize.py on a tiny RRDBNet, so the path the real weights take runs in seconds.

Needs the export requirements (scripts/requirements.txt); without torch the whole class is
skipped and unittest says so in its summary.
"""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent.parent

try:
    import numpy as np
    import onnx
    import torch
except ImportError:
    torch = None


def _load_export_module():
    spec = importlib.util.spec_from_file_location("export_and_quantize", SCRIPTS / "export-and-quantize.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


@unittest.skipIf(torch is None, "export requirements not installed (scripts/requirements.txt)")
class ExportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ex = _load_export_module()
        from archs.rrdbnet import RRDBNet

        cls.RRDBNet = RRDBNet

    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)

    def tiny(self, scale: int, seed: int = 0):
        torch.manual_seed(seed)
        return self.RRDBNet(scale=scale, num_feat=8, num_block=1, num_grow_ch=4).eval()

    def spec(self, scale: int, **kwargs):
        return self.ex.Export(f"tiny-x{scale}", "unused.pth", lambda folder: self.tiny(scale), 2 if scale == 2 else 1, **kwargs)

    def exported(self, scale: int, **kwargs):
        model, spec = self.tiny(scale), self.spec(scale, **kwargs)
        fp32, fp16 = self.folder / "fp32.onnx", self.folder / "fp16.onnx"
        self.ex.export_fp32(model, spec, fp32)
        self.ex.export_fp16(fp32, fp16)
        return model, spec, fp32, fp16

    def test_axes_are_dynamic_and_named(self):
        _, _, fp32, _ = self.exported(4)
        graph = onnx.load(str(fp32)).graph
        dims = [d.dim_param for d in graph.input[0].type.tensor_type.shape.dim]
        self.assertEqual(["batch", "", "height", "width"], dims)
        self.assertEqual(self.ex.OPSET, onnx.load(str(fp32)).opset_import[0].version)

    def test_x4_matches_torch_at_untraced_sizes(self):
        model, spec, fp32, fp16 = self.exported(4)
        lines, fp16_ok = self.ex.verify(model, spec, fp32, fp16)
        self.assertIn("24x40 -> 96x160", lines[0])
        self.assertTrue(fp16_ok)

    def test_x2_unshuffle_matches_torch(self):
        model, spec, fp32, fp16 = self.exported(2)
        lines, _ = self.ex.verify(model, spec, fp32, fp16)
        self.assertIn("48x80 -> 96x160", lines[0])

    def test_fixed_size_export_has_no_dynamic_axes(self):
        # GFPGAN's shape: one aligned crop at a fixed size, input in [-1, 1].
        model, spec, fp32, _ = self.exported(4, size=16, input_range=(-1.0, 1.0), output_range=(-1.0, 1.0))
        dims = [d.dim_value for d in onnx.load(str(fp32)).graph.input[0].type.tensor_type.shape.dim]
        self.assertEqual([1, 3, 16, 16], dims)
        lines, _ = self.ex.verify(model, spec, fp32, None)
        self.assertIn("16x16 -> 64x64", lines[0])

    def test_a_model_that_differs_from_its_source_fails(self):
        _, spec, fp32, _ = self.exported(4)
        other = self.tiny(4, seed=1)
        with self.assertRaises(self.ex.ExportError):
            self.ex.verify(other, spec, fp32, None)

    def test_an_fp16_variant_below_the_threshold_is_not_kept(self):
        model, spec, fp32, _ = self.exported(4)
        # A different model's FP16 output stands in for a conversion that lost too much.
        wrong = self.folder / "wrong.onnx"
        self.ex.export_fp32(self.tiny(4, seed=1), spec, wrong)
        _, fp16_ok = self.ex.verify(model, spec, fp32, wrong)
        self.assertFalse(fp16_ok)

    def test_int8_quantizes_every_conv_and_loads_on_cpu(self):
        # PB9: dynamic INT8 with unsigned weights; the report names what stayed float.
        _, _, fp32, _ = self.exported(4)
        int8 = self.folder / "int8.onnx"
        line = self.ex.quantize_int8(fp32, int8)
        ops = {node.op_type for node in onnx.load(str(int8)).graph.node}
        self.assertIn("ConvInteger", ops)
        self.assertNotIn("Conv", ops)
        self.assertIn("float compute ops left: none", line)
        weights = [i for i in onnx.load(str(int8)).graph.initializer if i.name.endswith("_quantized")]
        self.assertTrue(weights)
        self.assertTrue(all(w.data_type == onnx.TensorProto.UINT8 for w in weights))

    def test_psnr_uses_the_output_range(self):
        a = np.zeros((1, 1, 4, 4), dtype=np.float32)
        b = np.full((1, 1, 4, 4), 0.1, dtype=np.float32)
        self.assertAlmostEqual(20.0, self.ex.psnr(a, b, (0.0, 1.0)), places=3)
        self.assertAlmostEqual(26.0206, self.ex.psnr(a, b, (-1.0, 1.0)), places=3)

    def test_missing_weights_name_the_download_script(self):
        spec = self.ex.EXPORTS[0]
        with self.assertRaises(self.ex.ExportError) as caught:
            self.ex.load(spec, self.folder)
        self.assertIn("download_models.py", str(caught.exception))

    def test_the_rmbg_shim_refuses_a_changed_upstream_file(self):
        changed = self.folder / "briarmbg.py"
        changed.write_text("import torch\n", encoding="utf-8")
        with self.assertRaises(self.ex.ExportError):
            self.ex.load_briarmbg(changed)


if __name__ == "__main__":
    unittest.main()
