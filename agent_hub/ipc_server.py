"""
BIM Assist - Local IPC & WebSocket Server
پل ارتباطی ناهمگام و سریع میان پلاگین‌های Revit / Navisworks و هسته چندعامله هوش مصنوعی (Port 5055).
"""

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from pydantic import BaseModel
from typing import Dict, Any, List, Optional
import uvicorn
import asyncio
import json

from pathlib import Path
from fastapi.staticfiles import StaticFiles
from fastapi.responses import RedirectResponse

from .orchestrator import SeniorBIMOrchestrator
from .delta_reporter import DeltaReporter
from .memory_engine import ContinuousMemoryEngine

app = FastAPI(title="BIM Assist Agent Hub Local Server", version="2.0.0")

# نمونه‌سازی از هسته هوشمند
orchestrator = SeniorBIMOrchestrator()
delta_reporter = DeltaReporter()
memory_engine = ContinuousMemoryEngine()

# اتصال رابط کاربری وب پنل شناور (Revit WebView2 & Browser)
static_dir = Path(__file__).resolve().parent / "static"
if static_dir.exists():
    app.mount("/ui", StaticFiles(directory=static_dir, html=True), name="static_ui")

@app.get("/")
async def root():
    return RedirectResponse(url="/ui")

# --- مدل‌های درخواست REST ---

class QueryRequest(BaseModel):
    prompt: str
    mode: str = "consult" # "consult" یا "execute"
    context: Optional[Dict[str, Any]] = None

class LearnRequest(BaseModel):
    category: str
    rule_override: str
    user_instruction: str
    context: Optional[str] = None

class DeltaReportRequest(BaseModel):
    current_metrics: Dict[str, Any]
    previous_snapshot: str = "Week_36_Baseline"
    decisions_log: Optional[List[str]] = None

class RevitCommandRequest(BaseModel):
    tool: str
    action_title: str
    parameters: Optional[Dict[str, Any]] = None

class ProjectContextRequest(BaseModel):
    project_name: str
    active_view: str = "Active View"
    elements_count: int = 0

class DiagnoseRequest(BaseModel):
    error_message: str
    context: Optional[Dict[str, Any]] = None

# صف درون‌حافظه‌ای دستورات ارسالی از وب به پلاگین رویت
revit_command_queue: List[Dict[str, Any]] = []

# --- اندپوینت‌های REST API ---

@app.post("/api/set_active_project")
async def set_active_project(req: ProjectContextRequest):
    """به‌روزرسانی پویای نام و مشخصات پروژه فعال بدون پیش‌فرض اولیه"""
    orchestrator.memory.set_project_name(req.project_name)
    return {
        "status": "synchronized",
        "active_project": req.project_name,
        "active_view": req.active_view,
        "elements_count": req.elements_count
    }

@app.get("/api/active_project")
async def get_active_project():
    """دریافت نام پروژه و اطلاعات کانتکس جاری"""
    return {
        "project_name": orchestrator.memory.memory_data.get("project_name", "مدل جاری رویت"),
        "learned_rules_count": len(orchestrator.memory.memory_data.get("learned_rules", []))
    }

@app.get("/api/health")
async def health_check():
    return {
        "status": "online",
        "service": "BIM Assist Copilot Agent Hub",
        "gemini_connected": orchestrator.client is not None,
        "active_rules_loaded": len(orchestrator.memory.memory_data.get("learned_rules", []))
    }

@app.post("/api/query")
async def handle_query(req: QueryRequest):
    """پردازش پرامپت کاربر در هر دو حالت مشورت و اجرا"""
    result = orchestrator.process_query(req.prompt, mode=req.mode, active_context=req.context)
    return result

@app.post("/api/diagnose_and_heal")
async def handle_diagnose_and_heal(req: DiagnoseRequest):
    """تحلیل هوشمند خطای زمان اجرای رویت و تجویز خودکار ریکاوری"""
    result = orchestrator.diagnose_revit_error(req.error_message, context=req.context)
    return result

@app.post("/api/learn")
async def record_user_learning(req: LearnRequest):
    """ثبت قانون یا ترجیح جدید آموخته‌شده از اصلاحات کاربر"""
    entry = memory_engine.learn_rule(
        category=req.category,
        rule_override=req.rule_override,
        user_instruction=req.user_instruction,
        context=req.context
    )
    # همگام‌سازی حافظه با ارکستریتور
    orchestrator.memory.load_memory()
    return {"status": "success", "learned_entry": entry}

@app.post("/api/delta_report")
async def generate_delta_report(req: DeltaReportRequest):
    """تولید گزارش مقایسه‌ای پیشرفت هفتگی"""
    report = delta_reporter.generate_delta_report(
        current_metrics=req.current_metrics,
        previous_snapshot_name=req.previous_snapshot,
        decisions_log=req.decisions_log
    )
    markdown_output = delta_reporter.format_report_markdown(report)
    return {
        "report_data": report,
        "markdown": markdown_output
    }

@app.post("/api/revit_queue_command")
async def queue_revit_command(cmd: RevitCommandRequest):
    """دریافت دستور از پنل وب و قرار دادن در صف اجرای رویت"""
    revit_command_queue.append(cmd.model_dump())
    return {"status": "queued", "action": cmd.action_title, "queue_size": len(revit_command_queue)}

@app.get("/api/revit_poll_commands")
async def poll_revit_commands():
    """واکشی دستورات آماده اجرا توسط پلاگین رویت"""
    global revit_command_queue
    commands = list(revit_command_queue)
    revit_command_queue.clear()
    return {"commands": commands}

# --- وب‌سوکت دوطرفه برای ارتباط زنده درون رویت و نویس‌ورکس ---

@app.websocket("/ws/bim_assist")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    print("[IPC] پلاگین رویت/نویس‌ورکس متصل شد.")
    try:
        while True:
            data = await websocket.receive_text()
            payload = json.loads(data)
            action = payload.get("action", "query")

            if action == "query":
                resp = orchestrator.process_query(
                    payload.get("prompt", ""),
                    mode=payload.get("mode", "consult"),
                    active_context=payload.get("context", {})
                )
                await websocket.send_text(json.dumps(resp, ensure_ascii=False))
            elif action == "ping":
                await websocket.send_text(json.dumps({"status": "pong"}))
            else:
                await websocket.send_text(json.dumps({"error": f"عملیات ناشناخته {action}"}))
    except WebSocketDisconnect:
        print("[IPC] ارتباط پلاگین قطع شد.")
    except Exception as e:
        print(f"[IPC] خطای وب‌سوکت: {e}")

def start_server(host: str = "127.0.0.1", port: int = 5055):
    uvicorn.run(app, host=host, port=port, log_level="info")

if __name__ == "__main__":
    start_server()
