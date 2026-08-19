import uuid, os

NEED_DIR = "Assets/GameData/Needs"
SCRIPT_GUID = "194b3197d51ff7b419751bff3d1ce26c"

def to_unity_str(s):
    result = []
    for ch in s:
        if ord(ch) > 127:
            result.append("\\u{:04X}".format(ord(ch)))
        else:
            result.append(ch)
    return "".join(result)

def make_asset(name, need_id, description, furniture_ids=None, family_ids=None):
    furniture_ids = furniture_ids or []
    family_ids = family_ids or []
    # 空列表用行内 [] 避免换行后 YAML 解析歧义
    def list_yaml(items):
        if not items:
            return " []"
        return "\n" + "\n".join("  - " + x for x in items)
    content = (
        "%YAML 1.1\n"
        "%TAG !u! tag:unity3d.com,2011:\n"
        "--- !u!114 &11400000\n"
        "MonoBehaviour:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        "  m_GameObject: {fileID: 0}\n"
        "  m_Enabled: 1\n"
        "  m_EditorHideFlags: 0\n"
        "  m_Script: {fileID: 11500000, guid: " + SCRIPT_GUID + ", type: 3}\n"
        '  m_Name: "' + to_unity_str(name) + '"\n'
        "  m_EditorClassIdentifier: \n"
        "  needId: " + need_id + "\n"
        '  description: "' + to_unity_str(description) + '"\n'
        "  familyIds:" + list_yaml(family_ids) + "\n"
        "  furnitureIds:" + list_yaml(furniture_ids) + "\n"
    )
    meta_guid = uuid.uuid4().hex
    meta = (
        "fileFormatVersion: 2\n"
        "guid: " + meta_guid + "\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: 11400000\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )
    return content, meta

NEEDS = [
    ("Need_黑-手电筒",  "黑-手电筒",  "今晚要出去巡逻，但是没有手电筒怎么行，能帮我弄一个吗？",  ["flashlight_01"],       []),
    ("Need_黑-黑板",         "黑-黑板",         "我需要一块黑板，把案情线索都记录下来才能理清思路！",            ["chalkboard_01"],       []),
    ("Need_冲-熨斗",         "冲-熨斗",         "明天有比赛，我得把训练服熨得平平整整的，家里有熨斗吗？",    ["iron_01"],             []),
    ("Need_冲-柜子",         "冲-柜子",         "我的棒球奖杯越来越多了，需要一个柜子好好存放它们！",              ["colorful_cabinet_01"], []),
    ("Need_莱-钢琴",         "莱-钢琴",         "我最近在练一首新曲子，要是家里有钢琴就好了……",                         ["piano_01"],            []),
    ("Need_莱-节拍器",   "莱-节拍器",   "没有节拍器练习效率好低，能帮我找一个嘛？",                                        ["metronome_01"],        []),
    ("Need_兔-化妆台",   "兔-化妆台",   "明天有打歌舞台，想要一个好看的化妆台可以嘛？",                            ["vanity_01"],           []),
    ("Need_兔-播放器",   "兔-播放器",   "我每天都要听音乐热身，房间里要是有音乐播放器就太好啊！",  ["music_player_01"],     []),
    ("Need_嘉-沙发",         "嘉-沙发",         "我对居住品质很有要求，一张舒适的高档沙发是必不可少的。",  ["macaron_sofa_01"],     []),
    ("Need_嘉-落地灯",   "嘉-落地灯",   "氛围灯光很重要，一盏精致的落地灯能让整个房间都变得高雅起来。",  ["floor_lamp_01"],  []),
    ("Need_顿-收音机",   "顿-收音机",   "我要收集各方线索，一台收音机可以帮我监听更多信息！",              ["retro_radio_01"],      []),
    ("Need_顿-望远镜",   "顿-望远镜",   "探索世界从观察开始，有了望远镜我就能发现更多秘密！",              ["telescope_01"],        []),
]

created = 0
for fname, need_id, desc, fids, famids in NEEDS:
    asset_path = os.path.join(NEED_DIR, fname + ".asset")
    meta_path  = asset_path + ".meta"
    if os.path.exists(asset_path):
        print("跳过已存在: " + fname + ".asset")
        continue
    content, meta = make_asset(fname, need_id, desc, fids, famids)
    with open(asset_path, "w", encoding="utf-8") as f:
        f.write(content)
    with open(meta_path, "w", encoding="utf-8") as f:
        f.write(meta)
    print("创建: " + fname + ".asset")
    created += 1

print("\n共创建 {} 个 Need asset".format(created))
