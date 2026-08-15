# -*- coding: utf-8 -*-
"""族化改造的硬验证（家具族体系说明 §8 第一条）：**展开后的 121 行必须与改造前逐项相同**。

离线模拟 Unity 侧 FurnitureCsvImporter.ImportFurnitureCsv 的展开逻辑
（读家具族表.csv + 家具表.csv → 按族id 展开），与改造前的家具表 CSV 基线逐行逐列比对。
第 1 步做完游戏行为不该有任何变化，这个脚本就是那句话的证明。

用法：python verify_family_expansion.py <改造前的家具表.csv>
"""
import csv
import sys

# 展开后应当与基线一致的全部列（= 基线家具表的全部列）
BASELINE_COLUMNS = ["id", "英文索引", "显示名", "分类", "描述", "表面类型", "可叠放", "占格列", "占格行",
                    "显示宽", "显示高", "装饰分", "精灵图", "色值", "拿起音效", "放下音效",
                    "桌面格启用", "桌面格列数", "桌面格宽", "桌面格高", "桌面格偏移X", "桌面高度"]

# 从族表取值的列（族表列名 = 家具表列名，「族显示名」除外——它是新概念，基线里没有）
FAMILY_SOURCED = ["分类", "描述", "表面类型", "可叠放", "占格列", "占格行", "装饰分", "拿起音效", "放下音效",
                  "桌面格启用", "桌面格列数", "桌面格宽", "桌面格高", "桌面格偏移X", "桌面高度"]


def read(path):
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def expand(family_rows, furniture_rows):
    """模拟导入器：逐行按族id 查族表，把族级列填进该行。"""
    families = {row["族id"]: row for row in family_rows}
    expanded = []
    for row in furniture_rows:
        family = families.get(row["族id"])
        if family is None:
            print(f"[ERROR] 家具「{row['id']}」引用了不存在的族 id：{row['族id']}")
            sys.exit(1)
        merged = dict(row)
        for column in FAMILY_SOURCED:
            merged[column] = family[column]
        expanded.append(merged)
    return expanded


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    baseline = read(sys.argv[1])
    expanded = expand(read("Assets/Configs/家具族表.csv"), read("Assets/Configs/家具表.csv"))

    if len(baseline) != len(expanded):
        print(f"[ERROR] 行数不一致：基线 {len(baseline)} 行，展开后 {len(expanded)} 行")
        sys.exit(1)

    diffs = []
    for before, after in zip(baseline, expanded):
        if before["id"] != after["id"]:
            diffs.append(f"  行序错位：基线 {before['id']} vs 展开后 {after['id']}")
            continue
        for column in BASELINE_COLUMNS:
            if before.get(column, "") != after.get(column, ""):
                diffs.append(f"  {before['id']} 的「{column}」：{before.get(column, '')!r} → {after.get(column, '')!r}")

    if diffs:
        print(f"[ERROR] 展开结果与基线有 {len(diffs)} 处不同：")
        print("\n".join(diffs[:40]))
        sys.exit(1)
    print(f"[OK] {len(expanded)} 行 × {len(BASELINE_COLUMNS)} 列与改造前**逐项相同**——族级字段展开无损。")


if __name__ == "__main__":
    main()
