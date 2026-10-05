"""
BIM Assist - Tools Catalog
تعریف ساختار ابزارهای استاندارد (Function Calling) جهت اتصال ایجنت‌های جمنای به دستورات Revit و Navisworks.
"""

from typing import List, Optional, Dict, Any
from pydantic import BaseModel, Field

# --- مدل‌های داده‌ای ورودی توابع ---

class SetPipeSlopeParams(BaseModel):
    element_ids: List[str] = Field(..., description="لیست شناسه‌های یکتای لوله‌ها در رویت (Revit Element IDs)")
    slope_percentage: float = Field(..., description="درصد شیب مورد نظر (مثلاً 0.015 برای 1.5%)")
    adjust_connected_fittings: bool = Field(True, description="آیا فیتینگ‌ها و رایزرهای متصل به صورت خودکار تراز شوند؟")

class RerouteDuctParams(BaseModel):
    duct_id: str = Field(..., description="شناسه کانال مورد نظر جهت بازطراحی")
    obstacle_ids: List[str] = Field(..., description="شناسه‌های موانع فیزیکی (تیر سازه‌ای، کابل‌تری و غیره)")
    clearance_mm: float = Field(50.0, description="حداقل فاصله ایمن از موانع به میلی‌متر")
    preferred_elbow_angle: int = Field(45, description="زاویه زانویی‌های پیشنهادی (۴۵ یا ۹۰ درجه)")
    bypass_direction: str = Field("Bottom", description="جهت دور زدن مانع: Bottom, Top, Left, Right")

class PlaceSleeveParams(BaseModel):
    clash_id: Optional[str] = Field(None, description="شناسه کلش در نویس‌ورکس در صورت وجود")
    pipe_or_duct_id: str = Field(..., description="شناسه المان تأسیساتی عبورکننده")
    structural_element_id: str = Field(..., description="شناسه تیر یا دیوار بتنی")
    sleeve_shape: str = Field("Rectangular", description="شکل اسلیو: Rectangular یا Circular")
    clearance_envelope_mm: float = Field(50.0, description="حاشیه تلرانس دور لوله به میلی‌متر")
    fire_rating_hours: int = Field(2, description="درجه مقاومت در برابر حریق (FRL) برای فایراستاپ")

class BatchUpdateParametersParams(BaseModel):
    element_ids: List[str] = Field(..., description="لیست المان‌های هدف")
    parameter_updates: Dict[str, str] = Field(..., description="دیکشنری نام پارامتر و مقدار جدید (مثلاً {'UniclassPr_Code': 'Pr_65_52_63'})")

class GroupNavisworksClashesParams(BaseModel):
    clash_test_name: str = Field(..., description="نام تست تداخل در نویس‌ورکس")
    group_by: str = Field("Grid_And_System", description="معیار گروه‌بندی: Grid_And_System, Level, Primary_Element")
    folder_prefix: str = Field("AI_Resolved", description="پیشوند نام پوشه گروه‌بندی شده")

# --- رجیستری ابزارها جهت تزریق به Gemini SDK ---

TOOL_DEFINITIONS = [
    {
        "name": "tool_set_pipe_slope",
        "description": "اصلاح و تنظیم شیب لوله‌های تأسیساتی در رویت و بالانس خودکار اتصالات و فیتینگ‌ها",
        "parameters": SetPipeSlopeParams.model_json_schema()
    },
    {
        "name": "tool_reroute_duct",
        "description": "بازطراحی و تغییر مسیر خودکار کانال هوا برای دور زدن موانع فیزیکی با حفظ حداقل کلیرنس",
        "parameters": RerouteDuctParams.model_json_schema()
    },
    {
        "name": "tool_place_sleeve_at_clash",
        "description": "ایجاد و درج فمیلی بازشوی استاندارد (Sleeve/Opening) در تیر یا دیوار سازه‌ای در نقطه تداخل",
        "parameters": PlaceSleeveParams.model_json_schema()
    },
    {
        "name": "tool_batch_update_parameters",
        "description": "تزریق و پر کردن دسته‌جمعی پارامترهای اجباری (مانند Uniclass یا نام سیستم) در رویت",
        "parameters": BatchUpdateParametersParams.model_json_schema()
    },
    {
        "name": "tool_group_navisworks_clashes",
        "description": "دسته‌بندی و خوشه‌بندی هوشمند تداخلات در Clash Detective نرم‌افزار نویس‌ورکس",
        "parameters": GroupNavisworksClashesParams.model_json_schema()
    }
]
