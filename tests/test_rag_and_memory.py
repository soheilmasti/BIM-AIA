"""
تست‌های اعتبارسنجی موتور RAG اسناد پروژه و حافظه مداوم یادگیری
"""

import unittest
import sys
from pathlib import Path

TEST_DIR = Path(__file__).resolve().parent
ROOT_DIR = TEST_DIR.parent
sys.path.insert(0, str(ROOT_DIR))

from agent_hub.rag_engine import ProjectRAGEngine
from agent_hub.memory_engine import ContinuousMemoryEngine

class TestRAGAndMemory(unittest.TestCase):
    def setUp(self):
        self.docs_dir = ROOT_DIR / "project_docs"
        self.test_memory_file = ROOT_DIR / "test_memory_rag.json"
        self.rag = ProjectRAGEngine(docs_directory=self.docs_dir)
        self.memory = ContinuousMemoryEngine(memory_file_path=self.test_memory_file)

    def tearDown(self):
        if self.test_memory_file.exists():
            self.test_memory_file.unlink()

    def test_rag_document_indexing_and_search(self):
        """بررسی صحت ایندکس‌گذاری اسناد BEP و جستجوی موضوعی با ارجاع دقیق"""
        self.assertGreater(len(self.rag.chunks), 0)
        results = self.rag.search("sanitary slope", top_k=2)
        self.assertGreater(len(results), 0)
        top_res = results[0]
        self.assertIn("sample_bep_hospital.md", top_res["citation"])
        self.assertIn("1.5%", top_res["content"])

    def test_continuous_learning_and_persistence(self):
        """بررسی ثبت و ماندگاری قانون آموخته‌شده از اصلاحات کاربر"""
        entry = self.memory.learn_rule(
            category="Sleeves",
            rule_override="همیشه ۱۰ سانتیمتر تلرانس دور لوله در نظر گرفته شود",
            user_instruction="کاربر دستی اندازه اسلیو را از ۵ به ۱۰ سانت تغییر داد"
        )
        self.assertEqual(entry["category"], "Sleeves")
        self.assertTrue(self.test_memory_file.exists())

        # بارگذاری مجدد و بررسی ماندگاری
        new_mem = ContinuousMemoryEngine(memory_file_path=self.test_memory_file)
        summary = new_mem.get_learned_rules_summary()
        self.assertIn("۱۰ سانتیمتر", summary)

if __name__ == "__main__":
    unittest.main()
