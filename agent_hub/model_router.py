"""
BIM Assist - Gemini Model Fallback & Quota Router
مدیریت سهمیه‌ها و سوییچ هوشمند خودکار میان مدل‌های در دسترس جمنای در زمان اتمام محدودیت (429 / Quota Exceeded).
"""

import time
from typing import List, Dict, Any, Optional, Tuple

class GeminiModelRouter:
    """
    روتر و مدیر اولویت مدل‌های جمنای با قابلیت Circuit Breaker جهت سوییچ خودکار
    در زمان مواجهه با خطاهای Rate Limit (429) یا اتمام سهمیه (ResourceExhausted).
    """
    DEFAULT_MODELS = [
        "gemini-3.6-flash",
        "gemini-2.5-flash",
        "gemini-2.0-flash",
        "gemini-1.5-flash",
        "gemini-1.5-pro"
    ]

    def __init__(self, model_priority: Optional[List[str]] = None, cooldown_seconds: int = 120):
        self.model_priority = model_priority or list(self.DEFAULT_MODELS)
        self.cooldown_seconds = cooldown_seconds
        # نگهداری زمان پایان دوره استراحت برای هر مدل: {model_name: timestamp}
        self.cooldown_registry: Dict[str, float] = {}
        self.active_model_stats: Dict[str, int] = {m: 0 for m in self.model_priority}

    def get_available_models(self) -> List[str]:
        """لیست مدل‌هایی که در حال حاضر در حالت اتمام سهمیه/Cooldown نیستند"""
        now = time.time()
        available = []
        for model in self.model_priority:
            cooldown_until = self.cooldown_registry.get(model, 0)
            if now >= cooldown_until:
                available.append(model)
        
        # اگر همه مدل‌ها در Cooldown بودند، کلیدها را بازنشانی می‌کنیم تا مجدداً تست شوند
        if not available:
            print("[ModelRouter] All models in cooldown. Resetting registry for retry.")
            self.cooldown_registry.clear()
            available = list(self.model_priority)
            
        return available

    def mark_exhausted(self, model_name: str, error_message: str):
        """علامت‌گذاری مدل به عنوان دارای محدودیت سهمیه و فعال‌سازی Cooldown"""
        self.cooldown_registry[model_name] = time.time() + self.cooldown_seconds
        print(f"[ModelRouter] [WARN] Model '{model_name}' quota exhausted/unavailable. Switching to fallback models.")

    def is_quota_error(self, error: Exception) -> bool:
        """تشخیص خطای اتمام سهمیه یا محدودیت نرخ درخواست (429 / ResourceExhausted)"""
        msg = str(error).lower()
        quota_indicators = [
            "429",
            "resource_exhausted",
            "quota exceeded",
            "rate limit",
            "rate_limit_exceeded",
            "too many requests",
            "not_found", # برای مدل‌هایی که ممکن است در ریجن کاربر یا نسخه فعال نباشند
            "model not found"
        ]
        return any(indicator in msg for indicator in quota_indicators)

    def execute_with_fallback(self, client: Any, prompt_contents: str) -> Tuple[Optional[str], Optional[str], List[str]]:
        """
        تلاش برای فراخوانی مدل‌ها به ترتیب اولویت و سوییچ لحظه‌ای در صورت خطا.
        خروجی: (متن_پاسخ, مدل_موفق, لیست_مدل‌های_خطاخورده)
        """
        if not client:
            return None, None, []

        attempted_models = []
        available_models = self.get_available_models()

        for model_name in available_models:
            attempted_models.append(model_name)
            try:
                response = client.models.generate_content(
                    model=model_name,
                    contents=prompt_contents
                )
                if response and response.text:
                    self.active_model_stats[model_name] = self.active_model_stats.get(model_name, 0) + 1
                    return response.text, model_name, attempted_models
            except Exception as e:
                # ثبت محدودیت سهمیه و سوییچ بدون معطلی به مدل بعدی
                self.mark_exhausted(model_name, str(e))
                continue

        return None, None, attempted_models
