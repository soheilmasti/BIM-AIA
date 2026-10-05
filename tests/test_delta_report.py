"""
تست‌های اعتبارسنجی موتور گزارش‌دهی مقایسه‌ای و پیشرفت هفتگی
"""

import unittest
import sys
from pathlib import Path

TEST_DIR = Path(__file__).resolve().parent
ROOT_DIR = TEST_DIR.parent
sys.path.insert(0, str(ROOT_DIR))

from agent_hub.delta_reporter import DeltaReporter

class TestDeltaReport(unittest.TestCase):
    def setUp(self):
        self.snapshots_dir = ROOT_DIR / "test_snapshots"
        self.reporter = DeltaReporter(snapshots_dir=self.snapshots_dir)

        # ساخت اسنپ‌شات هفته قبل جهت مقایسه
        self.reporter.save_snapshot("Week_36", {
            "health_score": 81.0,
            "total_clashes": 340,
            "resolved_clashes": 80,
            "uniclass_completion_pct": 83.0,
            "pipe_length_meters": 1200.0,
            "duct_area_sqm": 850.0
        })

    def tearDown(self):
        import shutil
        if self.snapshots_dir.exists():
            shutil.rmtree(self.snapshots_dir)

    def test_delta_report_calculation(self):
        """بررسی محاسبات تفاضلی شاخص‌ها نسبت به هفته گذشته"""
        current_metrics = {
            "project_name": "Hospital Barcelona Nou",
            "health_score": 94.0,
            "total_clashes": 216,
            "resolved_clashes": 142,
            "new_clashes": 18,
            "uniclass_completion_pct": 98.0,
            "pipe_length_meters": 1520.0,
            "duct_area_sqm": 990.0
        }

        report = self.reporter.generate_delta_report(
            current_metrics=current_metrics,
            previous_snapshot_name="Week_36"
        )

        # ارتقای شاخص سلامت (+13%)
        self.assertEqual(report["health_score"]["current"], 94.0)
        self.assertEqual(report["health_score"]["delta"], "+13.0%")

        # تفاضل تداخلات
        self.assertEqual(report["clashes"]["resolved_during_period"], 62)
        self.assertEqual(report["clashes"]["remaining_open"], 216)

        # پیشرفت داده‌ها و احجام
        self.assertEqual(report["parameters"]["delta"], "+15.0%")
        self.assertEqual(report["quantities_added"]["pipes_meters"], "+320.0 m")
        self.assertEqual(report["quantities_added"]["duct_area_sqm"], "+140.0 m²")

        # خروجی مارک‌داون
        md = self.reporter.format_report_markdown(report)
        self.assertIn("Hospital Barcelona Nou", md)
        self.assertIn("+13.0%", md)
        self.assertIn("+320.0 m", md)

if __name__ == "__main__":
    unittest.main()
