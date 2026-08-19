import re

SFX_PICKUP_GROUND  = "d266ca6b55b879f4bbdbdbe0e90e7209"
SFX_PICKUP_DESKTOP = "a3486f451b0408447b69e1b7ecf88052"
SFX_PUTDOWN        = "05be7d7a45dc71c41b689374d2baf524"

def ue(s):
    out = []
    for c in s:
        code = ord(c)
        if code > 127:
            out.append("\\u" + "{:04X}".format(code))
        else:
            out.append(c)
    return "".join(out)

def surfaces_flag(t):
    return {"地面": "00000001", "桌面": "00000002", "壁挂": "00000004"}.get(t, "00000000")

FAMILIES = [
    ("vanity",          "梳妆台",    "桌椅", "", "地面",  8, 2, 35, True),
    ("retro_radio",     "复古收音机","摆件", "", "桌面",  2, 2, 12, False),
    ("music_player",    "音乐播放器","摆件", "", "地面",  6, 2, 20, True),
    ("piano",           "钢琴",      "桌椅", "", "地面", 10, 2, 40, True),
    ("flashlight",      "手电筒",    "摆件", "", "桌面",  2, 2,  8, False),
    ("iron",            "熨斗",      "摆件", "", "桌面",  2, 2,  8, False),
    ("telescope",       "望远镜",    "摆件", "", "桌面",  2, 2, 10, False),
    ("colorful_cabinet","彩色柜子",  "桌椅", "", "地面",  8, 2, 28, True),
    ("floor_lamp",      "落地灯",    "灯具", "", "地面",  2, 2, 18, True),
    ("chalkboard",      "黑板",      "摆件", "", "地面",  4, 3, 15, True),
    ("macaron_sofa",    "马卡龙沙发","桌椅", "", "地面",  8, 2, 30, True),
    ("metronome",       "节拍器",    "摆件", "", "桌面",  2, 2, 10, False),
]

FURNITURES = [
    ("vanity_01",          "vanity_01",          "彩色梳妆台·01", "vanity",          240, 306, "7836d1024fcf6fd408f9869fe7941713", "#DBBF9E"),
    ("retro_radio_01",     "retro_radio_01",     "复古收音机·01", "retro_radio",      60,  43, "b06558452320c7e44b211caf5631f184", "#8DA092"),
    ("music_player_01",    "music_player_01",    "音乐播放器·01", "music_player",    180, 140, "181b943a9f501f146aaa600f4ce822c0", "#BA9A8D"),
    ("piano_01",           "piano_01",           "奶油钢琴·01",   "piano",           300, 291, "24253a62ab21ec14fbacf703ddaf632d", "#858B98"),
    ("flashlight_01",      "flashlight_01",      "手电筒·01",     "flashlight",       60,  31, "7f93e4d18eb0171498eb1482697020f5", "#80A6BA"),
    ("iron_01",            "iron_01",            "复古熨斗·01",   "iron",             60,  46, "3469f149e2b3ba14abdb8792b2b1ae7a", "#B2CADB"),
    ("telescope_01",       "telescope_01",       "复古望远镜·01", "telescope",        60,  74, "177409c3d99c44f45ade0304f01fe14f", "#BEA486"),
    ("colorful_cabinet_01","colorful_cabinet_01","彩色柜子·01",   "colorful_cabinet", 240, 130, "3b314eb97d66b1941ba10a0662a7f6a9", "#D0C8AC"),
    ("floor_lamp_01",      "floor_lamp_01",      "艺术落地灯·01", "floor_lamp",       60, 131, "911be811507fe2748aa05d301ad208b8", "#B18F5F"),
    ("chalkboard_01",      "chalkboard_01",      "复古黑板·01",   "chalkboard",      120,  88, "c402ce67d0ed85d418929f6baeb4e79c", "#C4905C"),
    ("macaron_sofa_01",    "macaron_sofa_01",    "马卡龙沙发·01", "macaron_sofa",    240, 106, "b0dc52a600686fa4e99bc73701385220", "#BDCECE"),
    ("metronome_01",       "metronome_01",       "节拍器·01",     "metronome",        60,  92, "3f5ef6e7f8f9a784b86a922985fcdb13", "#89A1B6"),
]

fdict = {f[0]: f for f in FAMILIES}


def fam_block(fid, dn, cat, desc, surf, cols, rows, score, ground):
    sfx = SFX_PICKUP_GROUND if ground else SFX_PICKUP_DESKTOP
    lines = [
        "  - familyId: " + fid,
        "    displayName: \"" + ue(dn) + "\"",
        "    category: \"" + ue(cat) + "\"",
        "    description: \"" + ue(desc) + "\"",
        "    surfaces: " + surfaces_flag(surf),
        "    stackable: 0",
        "    cols: " + str(cols),
        "    rows: " + str(rows),
        "    decorationScore: " + str(score),
        "    pickupSound: {fileID: 8300000, guid: " + sfx + ", type: 3}",
        "    putdownSound: {fileID: 8300000, guid: " + SFX_PUTDOWN + ", type: 3}",
        "    tableSurface:",
        "      enabled: 0",
        "      cols: 3",
        "      cellWidth: 64",
        "      cellHeight: 56",
        "      offsetX: 50",
        "      surfaceHeight: 146",
    ]
    return "\n".join(lines) + "\n"


def furn_block(fid, nk, dn, family_id, dw, dh, sguid, color):
    f = fdict[family_id]
    _, _, cat, desc, surf, cols, rows, score, ground = f
    sfx = SFX_PICKUP_GROUND if ground else SFX_PICKUP_DESKTOP
    h = color.lstrip("#")
    r = int(h[0:2], 16) / 255.0
    g = int(h[2:4], 16) / 255.0
    b = int(h[4:6], 16) / 255.0
    lines = [
        "  - id: " + fid,
        "    nameKey: " + nk,
        "    displayName: \"" + ue(dn) + "\"",
        "    familyId: " + family_id,
        "    category: \"" + ue(cat) + "\"",
        "    description: \"" + ue(desc) + "\"",
        "    surfaces: " + surfaces_flag(surf),
        "    stackable: 0",
        "    cols: " + str(cols),
        "    rows: " + str(rows),
        "    displayWidth: " + str(dw),
        "    displayHeight: " + str(dh),
        "    decorationScore: " + str(score),
        "    sprite: {fileID: 21300000, guid: " + sguid + ", type: 3}",
        "    swatchColor: {r: " + "{:.8f}".format(r) + ", g: " + "{:.8f}".format(g) + ", b: " + "{:.8f}".format(b) + ", a: 1}",
        "    pickupSound: {fileID: 8300000, guid: " + sfx + ", type: 3}",
        "    putdownSound: {fileID: 8300000, guid: " + SFX_PUTDOWN + ", type: 3}",
        "    tableSurface:",
        "      enabled: 0",
        "      cols: 3",
        "      cellWidth: 64",
        "      cellHeight: 56",
        "      offsetX: 50",
        "      surfaceHeight: 146",
    ]
    return "\n".join(lines) + "\n"


fam_path  = "Assets/Resources/OutGameUI/FurnitureFamilyTable.asset"
furn_path = "Assets/Resources/OutGameUI/FurnitureTable.asset"

fam_text  = open(fam_path,  "r", encoding="utf-8").read()
furn_text = open(furn_path, "r", encoding="utf-8").read()

existing_fam  = set(re.findall(r"familyId: (\S+)", fam_text))
existing_furn = set(re.findall(r"^  - id: (\S+)", furn_text, re.M))

new_fam  = "".join(fam_block(*f) for f in FAMILIES   if f[0] not in existing_fam)
new_furn = "".join(furn_block(*f) for f in FURNITURES if f[0] not in existing_furn)

added_fam  = [f[0] for f in FAMILIES   if f[0] not in existing_fam]
added_furn = [f[0] for f in FURNITURES if f[0] not in existing_furn]

if new_fam:
    with open(fam_path, "a", encoding="utf-8") as fp:
        fp.write(new_fam)

if new_furn:
    with open(furn_path, "a", encoding="utf-8") as fp:
        fp.write(new_furn)

print("族表追加:  " + str(added_fam  if added_fam  else "无（已全部存在）"))
print("家具追加: " + str(added_furn if added_furn else "无（已全部存在）"))
