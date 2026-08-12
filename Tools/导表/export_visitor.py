# -*- coding: utf-8 -*-
"""将访客三张 Excel 导出为 Unity 读取的 CSV。
数据唯一来源：Excel/访客种族表.xlsx、访客日程表.xlsx、访客调参表.xlsx；
输出进 Assets/Configs/，Unity 资产管线检测到变化后由 VisitorCsvImporter 自动重建对应 SO
（VisitorRaces/Race_*.asset / VisitorScheduleTable / VisitorTuningConfig）。
"""
import os
import re
import sys

import openpyxl

# 表格第二行是「Unity 字段名」参考行（如 raceId / displayName），全 ASCII 标识符时跳过
FIELD_NAME = re.compile(r"^[A-Za-z][A-Za-z0-9_.]*$")


def is_field_name_row(values):
    filled = [v for v in values if v]
    return bool(filled) and all(FIELD_NAME.match(v) for v in filled)


ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
EXCEL_DIR = os.path.join(ROOT, "Excel")
OUTPUT_DIR = os.path.join(ROOT, "Assets", "Configs")

# 每项：(xlsx 文件, 工作表, CSV 输出, 必须包含的列)——列名与 Unity 侧 VisitorCsvImporter 一致，勿改文字
JOBS = [
    ("访客种族表.xlsx", "种族", "访客种族表.csv",
     ["种族id", "显示名", "等搭话超时tick", "等交货超时tick", "闲逛上限tick", "跨天留宿概率%",
      "需求权重", "需求数下限", "需求数上限", "立绘差分", "序列帧", "对话池"]),
    ("访客日程表.xlsx", "日程", "访客日程表.csv",
     ["天", "出现时刻(分钟)", "种族id", "具名覆写"]),
    ("访客调参表.xlsx", "调参", "访客调参表.csv",
     ["参数", "值"]),
    ("访客调参表.xlsx", "氛围访客", "访客氛围表.csv",
     ["id", "显示名", "序列帧"]),
]


def cell_text(value):
    if value is None:
        return ""
    if isinstance(value, float) and value == int(value):
        return str(int(value))  # Excel 把整数存成 1.0，导出时还原
    return str(value).strip()


def csv_cell(text):
    if any(ch in text for ch in ',"\n'):
        return '"' + text.replace('"', '""') + '"'
    return text


def export(xlsx, sheet, csv_name, required):
    excel_path = os.path.join(EXCEL_DIR, xlsx)
    if not os.path.exists(excel_path):
        print(f"[ERROR] Excel not found: {os.path.abspath(excel_path)}")
        sys.exit(1)

    wb = openpyxl.load_workbook(excel_path, read_only=True, data_only=True)
    if sheet not in wb.sheetnames:
        print(f"[ERROR] sheet '{sheet}' missing in {xlsx}")
        sys.exit(1)
    rows = list(wb[sheet].iter_rows(values_only=True))
    wb.close()
    if len(rows) < 2:
        print(f"[ERROR] {xlsx}[{sheet}] is empty or has no data rows.")
        sys.exit(1)

    header = [cell_text(cell) for cell in rows[0]]
    missing = [col for col in required if col not in header]
    if missing:
        print(f"[ERROR] {xlsx}[{sheet}] missing columns: {missing}")
        sys.exit(1)

    lines = [",".join(csv_cell(h) for h in header)]
    count = 0
    for row in rows[1:]:
        values = [cell_text(cell) for cell in row[:len(header)]]
        values += [""] * (len(header) - len(values))
        if not any(values):
            continue
        if count == 0 and is_field_name_row(values):
            continue  # 第二行英文字段名参考行
        lines.append(",".join(csv_cell(v) for v in values))
        count += 1

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    out_path = os.path.join(OUTPUT_DIR, csv_name)
    with open(out_path, "w", encoding="utf-8-sig", newline="") as f:
        f.write("\r\n".join(lines) + "\r\n")
    print(f"[OK] {csv_name}: {count} rows")


def main():
    for xlsx, sheet, csv_name, required in JOBS:
        export(xlsx, sheet, csv_name, required)


if __name__ == "__main__":
    main()
