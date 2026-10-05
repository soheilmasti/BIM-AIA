"""
BIM Assist Copilot - Main Service Launcher
نقطه شروع اجرای سرویس محلی ارکستریتور و سرور ارتباطی پلاگین‌ها.
"""

import sys
from pathlib import Path

# افزودن مسیر پکیج به sys.path
BASE_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(BASE_DIR))

from agent_hub.ipc_server import start_server

if __name__ == "__main__":
    print("=" * 60)
    print("   BIM AIA - Advanced Multi-Agent Copilot for Revit 2026")
    print("   Listening on Port 5055 (WebView2 & WebSocket Bridge)")
    print("=" * 60)
    start_server(host="127.0.0.1", port=5055)
