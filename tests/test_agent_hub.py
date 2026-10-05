"""
تست‌های اعتبارسنجی هسته چندعامله و حالت دوگانه (مشورت و اجرا)
"""

import unittest
import sys
from pathlib import Path

# افزودن مسیر به sys.path
TEST_DIR = Path(__file__).resolve().parent
ROOT_DIR = TEST_DIR.parent
sys.path.insert(0, str(ROOT_DIR))

from agent_hub.orchestrator import SeniorBIMOrchestrator
from agent_hub.tools_catalog import TOOL_DEFINITIONS

class TestAgentHub(unittest.TestCase):
    def setUp(self):
        docs_dir = ROOT_DIR / "project_docs"
        memory_file = ROOT_DIR / "test_memory.json"
        self.orchestrator = SeniorBIMOrchestrator(docs_dir=docs_dir, memory_file=memory_file)

    def tearDown(self):
        test_mem = ROOT_DIR / "test_memory.json"
        if test_mem.exists():
            test_mem.unlink()

    def test_tool_catalog_definitions(self):
        """بررسی صحت وجود و اسکیمای توابع استاندارد رویت و نویس‌ورکس"""
        self.assertGreaterEqual(len(TOOL_DEFINITIONS), 4)
        tool_names = [t["name"] for t in TOOL_DEFINITIONS]
        self.assertIn("tool_set_pipe_slope", tool_names)
        self.assertIn("tool_reroute_duct", tool_names)
        self.assertIn("tool_place_sleeve_at_clash", tool_names)
        self.assertIn("tool_batch_update_parameters", tool_names)

    def test_consult_mode_with_action_bridge(self):
        """تست حالت مشورت و ایجاد خودکار دکمه انتقال مستقیم به اجرا"""
        res = self.orchestrator.process_query(
            "آیا مجازیم لوله فاضلاب رو از سقف کاذب اتاق سرور رد کنیم؟",
            mode="consult"
        )
        self.assertEqual(res["mode"], "consult")
        self.assertTrue("NFPA" in res["response"] or "ANSI" in res["response"] or "ممنوع" in res["response"])
        self.assertTrue(res["has_suggested_action"])
        self.assertIsNotNone(res["suggested_action"])
        self.assertIn(res["suggested_action"]["tool"], ["tool_place_sleeve_at_clash", "tool_reroute_duct"])

    def test_execute_mode_reroute_duct(self):
        """تست حالت اجرا و تولید پیش‌نمایش شبح (Ghost Preview) برای بازطراحی داکت"""
        res = self.orchestrator.process_query(
            "داکت هوا رو از زیر کابل‌تری دور بزن",
            mode="execute"
        )
        self.assertEqual(res["mode"], "execute")
        self.assertEqual(res["status"], "ReadyToExecute")
        self.assertIn("plan", res)
        self.assertEqual(res["plan"]["tool"], "tool_reroute_duct")
        self.assertIn("ghost_preview", res["plan"])
        self.assertEqual(res["plan"]["ghost_preview"]["type"], "GeometricBypassCurve")

if __name__ == "__main__":
    unittest.main()
