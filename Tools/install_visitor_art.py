# -*- coding: utf-8 -*-
"""把美术交付的「场景QQ人 / 对话大头」原样拷进 Resources，并补出 .meta。

- **不改像素、不改原图**：Assets/Arts 下的交付件保持原样，这里只做「复制 + 按管线约定改文件名」。
  之所以必须复制而不是直接引用，是因为运行时走 Resources.Load（立绘表的资源路径列、
  访客种族表的序列帧列都是 Resources 相对路径），素材必须落在某个 Resources 目录下。
- 文件名按现有约定对齐：
  场景小人 → OutGameUI/Visitors/<raceId>_await_sheet.png（+ _attack_sheet.png 表示第二个姿势）
  对话立绘 → OutGameUI/Portraits/<raceId>.png
- .meta 从现有访客序列帧的 meta 复制导入设置，只换 guid（按目标路径取 md5，可重复执行且稳定）。

一次性脚本，跑完即留档；素材有增补时改 MAP 再跑一次即可。
"""
import hashlib
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
META_TEMPLATE_PATH = "Assets/Resources/OutGameUI/Visitors/laoda_await_sheet.png.meta"

QQ_SRC = "Assets/Arts/场景QQ人"
HEAD_SRC = "Assets/Arts/对话大头"
QQ_DST = "Assets/Resources/OutGameUI/Visitors"
HEAD_DST = "Assets/Resources/OutGameUI/Portraits"

# 场景小人：一个种族一到两个姿势。第一个姿势当待机（await），第二个当庆祝（attack，
# 服务完成时播一次再回待机）；只有一个姿势的种族不配 attack，OutGameVisitorActor 本来就允许缺。
QQ_MAP = {
    "QQ-兔1.png": "rabbit_await_sheet.png",
    "QQ-兔2.png": "rabbit_attack_sheet.png",
    "QQ-羊1.png": "goat_await_sheet.png",
    "QQ-狼1.png": "wolf_await_sheet.png",
    "QQ-狼2.png": "wolf_attack_sheet.png",
    "QQ-帽子豹1.png": "leopard_await_sheet.png",
    "QQ-棒球豹1.png": "cheetah_await_sheet.png",
    "QQ-牛1.png": "ox_await_sheet.png",
    "QQ-牛2.png": "ox_attack_sheet.png",
    "QQ-猫1.png": "cat_await_sheet.png",
    "QQ-牦牛1.png": "yak_await_sheet.png",
    "QQ-牦牛2.png": "yak_attack_sheet.png",
    # QQ-牦牛3.png（抱臂「…」）暂时没有第三个槽位，留在 Arts 里不入库，等有「闲逛」姿势位再接
}

# 对话立绘：一角色一张，立绘ID 在 Excel/立绘表.xlsx 里配成 <raceId>_平静
HEAD_MAP = {
    "head-1.png": "rabbit.png",   # 白兔·蓝斗篷
    "head-2.png": "goat.png",     # 白山羊·捧盆栽
    "head-3.png": "wolf.png",     # 白狼·白西装
    "head-4.png": "leopard.png",  # 豹·棒球帽
    "head-5.png": "cheetah.png",  # 猎豹·棒球服
    "head-6.png": "ox.png",       # 褐牛·毛线帽
    "head-7.png": "cat.png",      # 黑猫·警官
    "head-8.png": "yak.png",      # 白牦牛·蓝衬衫
}


def write_meta(png_rel_path):
    """按目标路径生成稳定 guid 的 .meta（导入设置整段沿用现有访客序列帧）。"""
    template = open(os.path.join(ROOT, META_TEMPLATE_PATH), encoding="utf-8").read()
    lines = template.split("\n")
    assert lines[1].startswith("guid: "), lines[1]
    lines[1] = "guid: " + hashlib.md5(png_rel_path.encode("utf-8")).hexdigest()
    with open(os.path.join(ROOT, png_rel_path + ".meta"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def install(src_dir, dst_dir, mapping):
    os.makedirs(os.path.join(ROOT, dst_dir), exist_ok=True)
    for src, dst in mapping.items():
        src_path = os.path.join(ROOT, src_dir, src)
        dst_rel = dst_dir + "/" + dst
        shutil.copyfile(src_path, os.path.join(ROOT, dst_rel))
        write_meta(dst_rel)
        print("  %-22s -> %s" % (src, dst_rel))


if __name__ == "__main__":
    print("[场景小人]")
    install(QQ_SRC, QQ_DST, QQ_MAP)
    print("[对话立绘]")
    install(HEAD_SRC, HEAD_DST, HEAD_MAP)
    print("完成：共 %d 张，原图未改动。" % (len(QQ_MAP) + len(HEAD_MAP)))
