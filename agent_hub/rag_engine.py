"""
BIM Assist - Project Documents RAG Engine
موتور نمایه‌سازی و بازیابی اسناد پروژه (BEP, EIR, Specs) جهت استناد دقیق ایجنت‌ها.
"""

import os
import re
from pathlib import Path
from typing import List, Dict, Any, Optional

class DocumentChunk:
    def __init__(self, doc_name: str, section: str, content: str):
        self.doc_name = doc_name
        self.section = section
        self.content = content.strip()

    def to_dict(self) -> Dict[str, str]:
        return {
            "document": self.doc_name,
            "section": self.section,
            "content": self.content
        }

class ProjectRAGEngine:
    def __init__(self, docs_directory: Optional[Path] = None):
        if docs_directory is None:
            self.docs_dir = Path(__file__).resolve().parent.parent / "project_docs"
        else:
            self.docs_dir = Path(docs_directory)
        
        self.chunks: List[DocumentChunk] = []
        self.reload_documents()

    def reload_documents(self):
        """بارگذاری مجدد و خرد کردن تمامی اسناد موجود در پوشه پروژه"""
        self.chunks.clear()
        if not self.docs_dir.exists():
            self.docs_dir.mkdir(parents=True, exist_ok=True)
            return

        for file_path in self.docs_dir.glob("*.md"):
            self._index_markdown_file(file_path)

    def _index_markdown_file(self, file_path: Path):
        doc_name = file_path.name
        try:
            with open(file_path, "r", encoding="utf-8") as f:
                text = f.read()
        except Exception as e:
            print(f"[RAG] خطا در خواندن فایل {doc_name}: {e}")
            return

        # بخش‌بندی بر اساس هدرهای مارک‌داون (## یا ###)
        sections = re.split(r"\n(?=#{1,3}\s)", text)
        for sec in sections:
            lines = sec.strip().split("\n")
            if not lines:
                continue
            header = lines[0].strip("#").strip()
            content = "\n".join(lines[1:]).strip() if len(lines) > 1 else lines[0]
            if content:
                self.chunks.append(DocumentChunk(doc_name, header, content))

    def search(self, query: str, top_k: int = 3) -> List[Dict[str, Any]]:
        """جستجوی هیبریدی کلمات کلیدی و محتوای مرتبط با کوئری"""
        query_words = set(re.findall(r"\w+", query.lower()))
        if not query_words or not self.chunks:
            return []

        scored_chunks = []
        for chunk in self.chunks:
            content_lower = chunk.content.lower()
            section_lower = chunk.section.lower()
            score = 0
            for word in query_words:
                if len(word) < 3:
                    continue
                # تطبیق کلمات در عنوان بخش و متن
                if word in section_lower:
                    score += 3
                if word in content_lower:
                    score += 1

            if score > 0:
                scored_chunks.append((score, chunk))

        scored_chunks.sort(key=lambda x: x[0], reverse=True)
        results = []
        for score, chunk in scored_chunks[:top_k]:
            results.append({
                "score": score,
                "document": chunk.doc_name,
                "section": chunk.section,
                "content": chunk.content,
                "citation": f"[{chunk.doc_name} > {chunk.section}]"
            })
        return results

    def format_context_for_prompt(self, query: str) -> str:
        """آماده‌سازی متن اسناد استخراج‌شده برای تزریق به پرامپت جمنای"""
        matches = self.search(query, top_k=3)
        if not matches:
            return "هیچ سند مرجع اختصاصی منطبق با این موضوع در مخزن پروژه یافت نشد."

        context_blocks = []
        for idx, m in enumerate(matches, 1):
            context_blocks.append(
                f"--- منبع {idx}: {m['citation']} ---\n{m['content']}"
            )
        return "\n\n".join(context_blocks)
