"""
BIM Assist - Continuous Learning & Memory Engine
حافظه مداوم جهت ثبت تصحیحات کاربر و اعمال سلیقه‌های پروژه (Zero Repeated Mistake).
"""

import json
from pathlib import Path
from datetime import datetime
from typing import List, Dict, Any, Optional

class ContinuousMemoryEngine:
    def __init__(self, memory_file_path: Optional[Path] = None):
        if memory_file_path is None:
            self.memory_file = Path(__file__).resolve().parent.parent / "project_memory.json"
        else:
            self.memory_file = Path(memory_file_path)
        
        self.memory_data: Dict[str, Any] = {
            "project_name": "Active Model",
            "last_updated": datetime.now().isoformat(),
            "learned_rules": []
        }
        self.load_memory()

    def set_project_name(self, name: str):
        """تنظیم پویای نام پروژه فعال از رویت بدون پیش‌فرض قبلی"""
        if name:
            self.memory_data["project_name"] = name
            self.save_memory()

    def load_memory(self):
        """بارگذاری فایل حافظه پایدار پروژه در صورت وجود"""
        if self.memory_file.exists():
            try:
                with open(self.memory_file, "r", encoding="utf-8") as f:
                    self.memory_data = json.load(f)
            except Exception as e:
                print(f"[Memory] خطا در بارگذاری حافظه: {e}")

    def save_memory(self):
        """ذخیره تغییرات حافظه در دیسک"""
        self.memory_data["last_updated"] = datetime.now().isoformat()
        try:
            with open(self.memory_file, "w", encoding="utf-8") as f:
                json.dump(self.memory_data, f, ensure_ascii=False, indent=2)
        except Exception as e:
            print(f"[Memory] خطا در ذخیره حافظه: {e}")

    def learn_rule(self, category: str, rule_override: str, user_instruction: str, context: Optional[str] = None):
        """ثبت یک قانون یا ترجیح جدید آموخته‌شده از رفتار کاربر"""
        new_entry = {
            "id": f"LRN-{len(self.memory_data['learned_rules']) + 1:03d}",
            "timestamp": datetime.now().isoformat(),
            "category": category,
            "rule_override": rule_override,
            "user_instruction": user_instruction,
            "context": context or "کلی"
        }
        self.memory_data["learned_rules"].append(new_entry)
        self.save_memory()
        return new_entry

    def get_learned_rules_summary(self) -> str:
        """ارائه قوانین یادگرفته‌شده جهت تزریق به System Prompt جمنای"""
        rules = self.memory_data.get("learned_rules", [])
        if not rules:
            return "هنوز هیچ ترجیح خاصی از جانب کاربر برای این پروژه ثبت نشده است."

        summary_lines = ["قوانین و استثنائات اختصاصی که کاربر در طول مدلسازی آموزش داده است:"]
        for r in rules:
            summary_lines.append(f"- [{r['category']}] {r['rule_override']} (مبنای آموزش: {r['user_instruction']})")
        return "\n".join(summary_lines)
