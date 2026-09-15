import importlib.util
import json
import pathlib
import sys
import tempfile
import unittest


MODULE_PATH = pathlib.Path(__file__).parents[2] / "tools" / "probe-pet.py"
SPEC = importlib.util.spec_from_file_location("probe_pet", MODULE_PATH)
probe_pet = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = probe_pet
SPEC.loader.exec_module(probe_pet)


class ProbePetPureLogicTests(unittest.TestCase):
    def test_accepts_only_packaged_codex_main_executable(self):
        good = r"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.4834.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"
        self.assertTrue(probe_pet.is_codex_executable(good))
        self.assertFalse(probe_pet.is_codex_executable(r"C:\Temp\ChatGPT.exe"))
        self.assertFalse(probe_pet.is_codex_executable(good + ".old"))

    def test_extracts_package_version_from_verified_path(self):
        path = r"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.4834.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"
        self.assertEqual("26.908.4834.0", probe_pet.extract_version(path))

    def test_geometry_is_version_gated(self):
        self.assertEqual({"width": 112, "height": 121, "dpi": 96},
                         probe_pet.geometry_profile("26.908.4834.0"))
        self.assertIsNone(probe_pet.geometry_profile("26.999.0.0"))

    def test_state_reader_returns_only_overlay_fields_and_negative_coordinates(self):
        document = {
            "secret": "must-not-escape",
            "electron-avatar-overlay-open": True,
            "electron-avatar-overlay-bounds": {"x": -245, "y": 150},
        }
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            path.write_text(json.dumps(document), encoding="utf-8")
            result = probe_pet.read_overlay_state(path)
        self.assertEqual(
            {"open": True, "bounds": {"x": -245, "y": 150}}, result
        )
        self.assertNotIn("secret", result)

    def test_state_reader_rejects_malformed_json_without_echoing_content(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.json"
            path.write_text('{"private":"DO_NOT_ECHO",', encoding="utf-8")
            with self.assertRaises(probe_pet.StateReadError) as raised:
                probe_pet.read_overlay_state(path)
        self.assertNotIn("DO_NOT_ECHO", str(raised.exception))

    def test_candidate_selection_requires_one_matching_overlay(self):
        windows = [
            probe_pet.WindowInfo(1, 44, True, "Chrome_WidgetWin_1", 0x2800A8, (-552, 0, 0, 1080)),
            probe_pet.WindowInfo(2, 44, False, "Chrome_WidgetWin_1", 0x2800A8, (-245, 150, -109, 312)),
        ]
        selected, reason = probe_pet.select_unique_overlay(windows, {44}, (-245, 150))
        self.assertEqual(1, selected.hwnd)
        self.assertEqual("unique-compatible-window", reason)

    def test_candidate_selection_refuses_ambiguous_windows(self):
        windows = [
            probe_pet.WindowInfo(1, 44, True, "Chrome_WidgetWin_1", 0x2800A8, (-552, 0, 0, 1080)),
            probe_pet.WindowInfo(2, 44, True, "Chrome_WidgetWin_1", 0x2800A8, (-600, 0, 0, 1080)),
        ]
        selected, reason = probe_pet.select_unique_overlay(windows, {44}, (-245, 150))
        self.assertIsNone(selected)
        self.assertEqual("ambiguous-compatible-windows:2", reason)


if __name__ == "__main__":
    unittest.main()
