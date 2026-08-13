# -*- coding: utf-8 -*-
"""对话表导出：Excel/对话表.xlsx → Assets/Configs/对话内容表.csv + 对话池表.csv

策划编辑 Excel/对话表.xlsx（两个 Sheet：对话内容、对话池）
→ 双击 Tools/导表/export_config.bat
→ CSV 写入 Assets/Configs/
→ Unity 资产管线检测到变化，DialogueCsvImporter 自动重建 DialogueGroupDef / DialoguePoolDef。
"""
import csv
import os
import sys

import openpyxl

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
EXCEL_PATH = os.path.join(ROOT, "Excel", "对话表.xlsx")
OUTPUT_DIR = os.path.join(ROOT, "Assets", "Configs")

CONTENT_SHEET  = "对话内容"
POOL_SHEET     = "对话池"
PREVIEW_SHEET  = "交付预览"

CONTENT_REQUIRED = ["对话组ID", "步骤", "类型", "文本"]
POOL_REQUIRED    = ["对话组ID", "种族", "触发分类", "权重"]
PREVIEW_REQUIRED = ["种族", "触发档位", "文本"]


def cell(value):
    if value is None:
        return ""
    if isinstance(value, float) and value == int(value):
        return str(int(value))
    return str(value).strip()


def csv_write(path, rows):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        for row in rows:
            w.writerow(row)


def export_sheet(wb, sheet_name, csv_name, required_cols):
    if sheet_name not in wb.sheetnames:
        print(f"[ERROR] Sheet '{sheet_name}' not found in {os.path.basename(EXCEL_PATH)}")
        sys.exit(1)

    ws = wb[sheet_name]
    all_rows = [[cell(v) for v in row] for row in ws.iter_rows(values_only=True)]

    if not all_rows:
        print(f"[ERROR] Sheet '{sheet_name}' is empty")
        sys.exit(1)

    headers = all_rows[0]
    for col in required_cols:
        if col not in headers:
            print(f"[ERROR] Column '{col}' not found in sheet '{sheet_name}'. "
                  f"Required: {required_cols}")
            sys.exit(1)

    csv_path = os.path.join(OUTPUT_DIR, csv_name)
    csv_write(csv_path, all_rows)

    data_rows = sum(1 for r in all_rows[1:] if any(v for v in r))
    print(f"[OK] {sheet_name} → {csv_name} ({data_rows} rows)")


def main():
    if not os.path.exists(EXCEL_PATH):
        print(f"[ERROR] Excel not found: {os.path.abspath(EXCEL_PATH)}")
        print(f"       Run 'MasterHouse > 对话系统 > 导出对话到 CSV' in Unity first to generate it,")
        print(f"       or run Tools/导表/init_dialogue_excel.py to generate from existing assets.")
        sys.exit(1)

    wb = openpyxl.load_workbook(EXCEL_PATH, read_only=True, data_only=True)

    export_sheet(wb, CONTENT_SHEET, "对话内容表.csv", CONTENT_REQUIRED)
    export_sheet(wb, POOL_SHEET,    "对话池表.csv",   POOL_REQUIRED)

    if PREVIEW_SHEET in wb.sheetnames:
        export_sheet(wb, PREVIEW_SHEET, "交付预览表.csv", PREVIEW_REQUIRED)
    else:
        print(f"[SKIP] Sheet '{PREVIEW_SHEET}' not found, 交付预览表.csv not exported")

    wb.close()


if __name__ == "__main__":
    main()
