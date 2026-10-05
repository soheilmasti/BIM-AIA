# BIM Assist Copilot - Multi-Agent Hub Container
FROM python:3.11-slim

# تنظیم متغیرهای محیطی
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PORT=5055

WORKDIR /app

# نصب ابزارهای پایه سیستم‌عامل در صورت نیاز
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

# کپی و نصب نیازمندی‌های پایتون
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# کپی کل پروژه به داخل کانتینر
COPY . .

# پورت ۵۰۵۵ جهت ارتباط با افزونه رویت و کلاینت‌ها
EXPOSE 5055

# سلامت‌سنجی کانتینر (Healthcheck)
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5055/api/health || exit 1

# اجرای سرویس ارکستریتور
CMD ["python", "run_copilot.py"]
