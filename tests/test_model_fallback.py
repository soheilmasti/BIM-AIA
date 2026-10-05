"""
تست‌های اعتبارسنجی سوییچ خودکار میان مدل‌های جمنای در زمان اتمام سهمیه یا خطای 429
"""

import unittest
import sys
from pathlib import Path
from unittest.mock import MagicMock

TEST_DIR = Path(__file__).resolve().parent
ROOT_DIR = TEST_DIR.parent
sys.path.insert(0, str(ROOT_DIR))

from agent_hub.model_router import GeminiModelRouter
from agent_hub.orchestrator import SeniorBIMOrchestrator

class TestModelFallback(unittest.TestCase):
    def setUp(self):
        self.router = GeminiModelRouter(
            model_priority=["gemini-primary-mock", "gemini-secondary-mock", "gemini-tertiary-mock"],
            cooldown_seconds=10
        )

    def test_automatic_failover_on_429_quota_error(self):
        """تست سوییچ بی‌درنگ به مدل دوم هنگامی که مدل اول خطای ۴۲۹ یا اتمام سهمیه می‌دهد"""
        mock_client = MagicMock()

        def side_effect(model, contents):
            if model == "gemini-primary-mock":
                raise Exception("429 ResourceExhausted: Quota exceeded for model gemini-primary-mock")
            elif model == "gemini-secondary-mock":
                mock_resp = MagicMock()
                mock_resp.text = "پاسخ موفقیت‌آمیز از مدل جایگزین دوم (Secondary)"
                return mock_resp
            raise Exception("Unexpected model")

        mock_client.models.generate_content.side_effect = side_effect

        response_text, model_used, attempted_models = self.router.execute_with_fallback(
            client=mock_client,
            prompt_contents="تست سوییچ مدل"
        )

        # اعتبارسنجی سوییچ موفقیت‌آمیز
        self.assertEqual(model_used, "gemini-secondary-mock")
        self.assertIn("پاسخ موفقیت‌آمیز", response_text)
        self.assertEqual(attempted_models, ["gemini-primary-mock", "gemini-secondary-mock"])

        # بررسی اینکه مدل اول در لیست Cooldown ثبت شده است
        self.assertIn("gemini-primary-mock", self.router.cooldown_registry)

    def test_cooldown_bypasses_exhausted_model(self):
        """تست اینکه در درخواست‌های بعدی، مدل تمام‌شده اصلاً فراخوانی نمی‌شود تا تاخیر ایجاد نکند"""
        mock_client = MagicMock()

        # علامت‌گذاری دستی مدل اول به عنوان تمام‌شده
        self.router.mark_exhausted("gemini-primary-mock", "Rate limit 429")

        def side_effect(model, contents):
            if model == "gemini-secondary-mock":
                mock_resp = MagicMock()
                mock_resp.text = "پاسخ سریع بدون تست مدل قبلی"
                return mock_resp
            raise Exception("Should not call primary!")

        mock_client.models.generate_content.side_effect = side_effect

        response_text, model_used, attempted = self.router.execute_with_fallback(
            client=mock_client,
            prompt_contents="تست دور زدن کوول‌داون"
        )

        self.assertEqual(model_used, "gemini-secondary-mock")
        self.assertNotIn("gemini-primary-mock", attempted)
        self.assertEqual(attempted, ["gemini-secondary-mock"])

if __name__ == "__main__":
    unittest.main()
