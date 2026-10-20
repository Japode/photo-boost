"""PB8: export-and-quantize.py on a tiny RRDBNet, so the path the real weights take runs in seconds.

Needs the export requirements (scripts/requirements.txt); without torch the whole class is
skipped and unittest says so in its summary.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent.parent

try:
    import numpy as np
    import onnx
    import torch
    from PIL import Image
except ImportError:
    torch = None

try:
    import jsonschema
except ImportError:
    jsonschema = None


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

    def validation_folder(self, floor: float):
        """A tiny x4 export with an int8 variant, and a test image, laid out as the script expects."""
        model, _, fp32, _ = self.exported(4)
        self.ex.quantize_int8(fp32, self.folder / "int8.onnx")
        images = self.folder / "test-images"
        images.mkdir()
        rng = np.random.default_rng(3)
        Image.fromarray(rng.integers(0, 256, (20, 30, 3), dtype=np.uint8)).save(images / "probe.png")
        return self.spec(4, test_image="probe.png", int8_min_psnr=floor), images

    def test_validation_writes_psnr_and_ssim_to_meta_json(self):
        # PB10: the numbers the README table reads.
        spec, images = self.validation_folder(floor=0.0)
        lines = self.ex.validate(spec, self.folder, images)
        meta = json.loads((self.folder / "meta.json").read_text(encoding="utf-8"))["validation"]
        self.assertEqual([1, 3, 20, 30], meta["input"])
        self.assertIn("psnr_db", meta["int8"])
        self.assertIn("ssim", meta["fp16"])
        self.assertEqual(2, len(lines))

    def test_int8_below_its_floor_fails_the_script(self):
        spec, images = self.validation_folder(floor=200.0)
        with self.assertRaises(self.ex.ExportError):
            self.ex.validate(spec, self.folder, images)

    @unittest.skipIf(jsonschema is None, "jsonschema not installed")
    def test_describe_writes_a_schema_valid_meta_json_and_keeps_validation(self):
        # PB11: the whole file the app reads, beside PB10's validation key.
        spec, images = self.validation_folder(floor=0.0)
        spec = self.ex.Export(**{**spec.__dict__, "scale": 4, "source": "realesrgan-x4plus", "tile": 256})
        model = self.tiny(4)
        self.ex.validate(spec, self.folder, images)
        self.ex.describe(spec, model, self.folder)
        meta = json.loads((self.folder / "meta.json").read_text(encoding="utf-8"))
        schema = json.loads((SCRIPTS / "meta.schema.json").read_text(encoding="utf-8"))
        jsonschema.validate(meta, schema)
        self.assertIn("validation", meta)
        self.assertEqual("dynamic", meta["input"]["batch"])
        self.assertEqual(hashlib.sha256((self.folder / "int8.onnx").read_bytes()).hexdigest(), meta["variants"]["int8"]["sha256"])
        # Tiny RRDBNet: 8 features at 4x the side, in FP32 bytes.
        self.assertEqual(8 * 16 * 4, meta["tile"]["largest_activation_bytes_per_pixel"])

    @unittest.skipIf(jsonschema is None, "jsonschema not installed")
    def test_fixtures_are_nearest_upscalers_with_schema_valid_meta(self):
        # PB42: the stand-ins UI cases run, and the committed copies must be what the script writes.
        import onnxruntime as ort

        schema = json.loads((SCRIPTS / "meta.schema.json").read_text(encoding="utf-8"))
        committed = SCRIPTS.parent / "cases" / "fixtures" / "models"
        rng = np.random.default_rng(7)
        image = rng.random((1, 3, 5, 7)).astype(np.float32)
        for name in self.ex.write_fixtures(self.folder):
            meta = json.loads((self.folder / name / "meta.json").read_text(encoding="utf-8"))
            jsonschema.validate(meta, schema)
            scale = meta["scale"]
            session = ort.InferenceSession(str(self.folder / name / "fp32.onnx"), providers=["CPUExecutionProvider"])
            out = session.run(None, {"input": image})[0]
            np.testing.assert_array_equal(image.repeat(scale, axis=2).repeat(scale, axis=3), out)
            for file in ("fp32.onnx", "int8.onnx", "meta.json"):
                self.assertEqual((committed / name / file).read_bytes(), (self.folder / name / file).read_bytes(), f"{name}/{file}")

    def test_ssim_is_one_for_identical_images_and_lower_otherwise(self):
        rng = np.random.default_rng(5)
        a = rng.random((1, 3, 32, 32)).astype(np.float32)
        self.assertAlmostEqual(1.0, self.ex.ssim(a, a), places=6)
        self.assertLess(self.ex.ssim(a, np.clip(a + rng.normal(0, 0.2, a.shape), 0, 1)), 0.9)

    def test_every_model_has_a_test_image_and_an_int8_floor(self):
        for spec in self.ex.EXPORTS:
            with self.subTest(spec.name):
                self.assertTrue(spec.test_image)
                self.assertGreater(spec.int8_min_psnr, 0)

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
