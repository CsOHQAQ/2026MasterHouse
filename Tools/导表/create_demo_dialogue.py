# -*- coding: utf-8 -*-
"""demo 对话文案生成脚本 —— 4 种族首版（简版）"""
import csv, os

ROOT       = os.path.join(os.path.dirname(__file__), "..", "..")
OUTPUT_DIR = os.path.join(ROOT, "Assets", "Configs")

CONTENT_HEADERS = ["对话组ID","备注","文件夹","步骤","类型","说话人","表情","文本","动作","动作参数","跳转","跳转目标组","选项条件"]
POOL_HEADERS    = ["对话组ID","文件夹","种族","触发分类","权重","进入条件"]

content_rows, pool_rows = [], []

def L(step, speaker, text, emotion=""):
    return {"step":step,"kind":"台词","speaker":speaker,"emotion":emotion,"text":text}
def V(step, text, action="", jump="结束", condition=""):
    return {"step":step,"kind":"选项","text":text,"action":action,"action_param":"","jump":jump,"condition":condition}

def group(gid, note, steps, folder="通用"):
    first = True
    for s in steps:
        content_rows.append([
            gid if first else "", note if first else "", folder if first else "",
            str(s.get("step","")), s.get("kind","台词"), s.get("speaker",""),
            s.get("emotion",""), s.get("text",""), s.get("action",""),
            s.get("action_param",""), s.get("jump",""), s.get("jump_group",""), s.get("condition",""),
        ])
        first = False

def pool(gid, race, trigger, weight=1, condition=""):
    pool_rows.append([gid,"通用",race,trigger,str(weight),condition])

# ── 乌鸦 ──────────────────────────────────────────────────────────────────
group("crow_firstMeeting_01","乌鸦·初次见面",[
    L(1,"访客","……我听说这里很安静。","平静"),
    V(2,"请进。",action="接待",condition="有空房"),
    V(2,"今天客满了。",action="拒绝"),
])
pool("crow_firstMeeting_01","crow","firstMeeting")

group("crow_serviceStart_01","乌鸦·开始服务",[
    L(1,"访客","房间有窗户吗？面朝海的那种。","平静"),
])
pool("crow_serviceStart_01","crow","serviceStart")

group("crow_serviceCheck_01","乌鸦·服务中",[
    L(1,"访客","窗框的风声……我觉得是什么东西在应答我。","平静"),
])
pool("crow_serviceCheck_01","crow","serviceCheck")

group("crow_serviceCheck_02","乌鸦·完成",[
    L(1,"访客","昨晚我唱了一句，它应了一句。不是重复——是答案。","高兴"),
    V(2,"听起来不错。",action="完成需求",condition="房间有家具"),
    V(2,"（继续打理房间）"),
])
pool("crow_serviceCheck_02","crow","serviceCheck")

group("crow_rejected_01","乌鸦·拒绝",[
    L(1,"访客","……好，我再去找找。","平静"),
])
pool("crow_rejected_01","crow","rejected")

group("crow_doneMismatch_01","乌鸦·不对味",[
    L(1,"访客","……不是这个。说不清哪里不对。","失望"),
])
pool("crow_doneMismatch_01","crow","doneMismatch")

group("crow_donePlain_01","乌鸦·一般",[
    L(1,"访客","……还行，比以前住的好一点。","平静"),
])
pool("crow_donePlain_01","crow","donePlain")

group("crow_doneSatisfied_01","乌鸦·满意",[
    L(1,"访客","谢谢你。……这句话我不常说。","高兴"),
])
pool("crow_doneSatisfied_01","crow","doneSatisfied")

group("crow_donePerfect_01","乌鸦·完美",[
    L(1,"访客","它把我每一句都接住了。没有一句落地。","高兴"),
    L(2,"访客","……我三年没说过不错了。","平静"),
])
pool("crow_donePerfect_01","crow","donePerfect")

group("crow_wanderChat_01","乌鸦·闲逛",[L(1,"访客","（他对着海，嘴唇微微动了一下）","平静")])
pool("crow_wanderChat_01","crow","wanderChat")

# ── 狐狸 ──────────────────────────────────────────────────────────────────
group("fox_firstMeeting_01","狐狸·初次见面",[
    L(1,"访客","这地方没有评分？没有评分的地方要么很好要么很烂。","平静"),
    V(2,"那进来看看才知道。",action="接待",condition="有空房"),
    V(2,"今天客满了。",action="拒绝"),
])
pool("fox_firstMeeting_01","fox","firstMeeting")

group("fox_serviceStart_01","狐狸·开始服务",[
    L(1,"访客","采光、隔音……还有一个我说不清楚的东西。你布置就好，我会告诉你哪里不对。","平静"),
])
pool("fox_serviceStart_01","fox","serviceStart")

group("fox_serviceCheck_01","狐狸·服务中",[
    L(1,"访客","还差一点。等我知道了再告诉你。","平静"),
])
pool("fox_serviceCheck_01","fox","serviceCheck")

group("fox_serviceCheck_02","狐狸·完成",[
    L(1,"访客","我算过了——这是我住过最划算的一次。","高兴"),
    V(2,"那就好。",action="完成需求",condition="房间有家具"),
    V(2,"（继续完善房间）"),
])
pool("fox_serviceCheck_02","fox","serviceCheck")

group("fox_rejected_01","狐狸·拒绝",[
    L(1,"访客","理解。下次再说。","平静"),
])
pool("fox_rejected_01","fox","rejected")

group("fox_doneMismatch_01","狐狸·不对味",[
    L(1,"访客","……不是我要的。也不是你的问题。","失望"),
])
pool("fox_doneMismatch_01","fox","doneMismatch")

group("fox_donePlain_01","狐狸·一般",[
    L(1,"访客","中规中矩。比预期差一点，但也没差太多。","平静"),
])
pool("fox_donePlain_01","fox","donePlain")

group("fox_doneSatisfied_01","狐狸·满意",[
    L(1,"访客","不错。……我很少这样说。","高兴"),
])
pool("fox_doneSatisfied_01","fox","doneSatisfied")

group("fox_donePerfect_01","狐狸·完美",[
    L(1,"访客","我住过的地方有一百多个。这是第一个让我不想算账的。","高兴"),
])
pool("fox_donePerfect_01","fox","donePerfect")

group("fox_wanderChat_01","狐狸·闲逛",[L(1,"访客","（他看着窗外，眼神很远）","平静")])
pool("fox_wanderChat_01","fox","wanderChat")

# ── 刺猬 ──────────────────────────────────────────────────────────────────
group("hedgehog_firstMeeting_01","刺猬·初次见面",[
    L(1,"访客","这里……大家都很安静吗？","平静"),
    V(2,"是的，欢迎进来。",action="接待",condition="有空房"),
    V(2,"今天客满了。",action="拒绝"),
])
pool("hedgehog_firstMeeting_01","hedgehog","firstMeeting")

group("hedgehog_serviceStart_01","刺猬·开始服务",[
    L(1,"访客","我想要一个柔软的房间。……我也不知道什么叫柔软的房间。","平静"),
])
pool("hedgehog_serviceStart_01","hedgehog","serviceStart")

group("hedgehog_serviceCheck_01","刺猬·服务中",[
    L(1,"访客","我……没有扎到什么东西吧？","平静"),
])
pool("hedgehog_serviceCheck_01","hedgehog","serviceCheck")

group("hedgehog_serviceCheck_02","刺猬·完成",[
    L(1,"访客","那个沙发……它抱住我了。我以为我会被弹走。","高兴"),
    V(2,"没有弹走你。",action="完成需求",condition="房间有家具"),
    V(2,"（继续完善房间）"),
])
pool("hedgehog_serviceCheck_02","hedgehog","serviceCheck")

group("hedgehog_rejected_01","刺猬·拒绝",[
    L(1,"访客","……没关系，我习惯了。","平静"),
])
pool("hedgehog_rejected_01","hedgehog","rejected")

group("hedgehog_doneMismatch_01","刺猬·不对味",[
    L(1,"访客","……还是扎到了什么。不是你的错，是我太尖了。","失望"),
])
pool("hedgehog_doneMismatch_01","hedgehog","doneMismatch")

group("hedgehog_donePlain_01","刺猬·一般",[
    L(1,"访客","……还好，比我预计的好一点。","平静"),
])
pool("hedgehog_donePlain_01","hedgehog","donePlain")

group("hedgehog_doneSatisfied_01","刺猬·满意",[
    L(1,"访客","我睡得很好。……好久没说过这句话了。","高兴"),
])
pool("hedgehog_doneSatisfied_01","hedgehog","doneSatisfied")

group("hedgehog_donePerfect_01","刺猬·完美",[
    L(1,"访客","我的刺只有害怕的时候才会硬。昨晚我一根都没硬。","高兴"),
])
pool("hedgehog_donePerfect_01","hedgehog","donePerfect")

group("hedgehog_wanderChat_01","刺猬·闲逛",[L(1,"访客","（她轻手轻脚地走着，生怕碰到什么）","平静")])
pool("hedgehog_wanderChat_01","hedgehog","wanderChat")

# ── 兔子 ──────────────────────────────────────────────────────────────────
group("rabbit_firstMeeting_01","兔子·初次见面",[
    L(1,"访客","你好！我想住一住，如果可以的话！","高兴"),
    V(2,"请进！",action="接待",condition="有空房"),
    V(2,"今天客满了。",action="拒绝"),
])
pool("rabbit_firstMeeting_01","rabbit","firstMeeting")

group("rabbit_serviceStart_01","兔子·开始服务",[
    L(1,"访客","怎么布置都可以！……没有人问过我想要什么。","高兴"),
])
pool("rabbit_serviceStart_01","rabbit","serviceStart")

group("rabbit_serviceCheck_01","兔子·服务中",[
    L(1,"访客","有什么可以帮忙的吗？……哦，那我就休息一下？","困惑"),
])
pool("rabbit_serviceCheck_01","rabbit","serviceCheck")

group("rabbit_serviceCheck_02","兔子·完成",[
    L(1,"访客","外面很吵的时候风铃不动。夜深了，它自己晃起来了。","高兴"),
    V(2,"好好休息。",action="完成需求",condition="房间有家具"),
    V(2,"（继续完善房间）"),
])
pool("rabbit_serviceCheck_02","rabbit","serviceCheck")

group("rabbit_rejected_01","兔子·拒绝",[
    L(1,"访客","没关系的！完全没关系！……（耳朵垂了下来）","高兴"),
])
pool("rabbit_rejected_01","rabbit","rejected")

group("rabbit_doneMismatch_01","兔子·不对味",[
    L(1,"访客","挺好的！……对不起，我不知道怎么说不好。","高兴"),
])
pool("rabbit_doneMismatch_01","rabbit","doneMismatch")

group("rabbit_donePlain_01","兔子·一般",[
    L(1,"访客","谢谢你！……（低头想了一下）确实不错。","高兴"),
])
pool("rabbit_donePlain_01","rabbit","donePlain")

group("rabbit_doneSatisfied_01","兔子·满意",[
    L(1,"访客","你第一个问过我想要什么。我一直在想这个问题。","平静"),
])
pool("rabbit_doneSatisfied_01","rabbit","doneSatisfied")

group("rabbit_donePerfect_01","兔子·完美",[
    L(1,"访客","我昨晚听见自己的脚步声了。以前走路很轻，怕打扰别人。昨晚我就正常走路了。","高兴"),
])
pool("rabbit_donePerfect_01","rabbit","donePerfect")

group("rabbit_wanderChat_01","兔子·闲逛",[L(1,"访客","（她路过，想说什么又憋回去了）","高兴")])
pool("rabbit_wanderChat_01","rabbit","wanderChat")

# ── 写出 CSV ──────────────────────────────────────────────────────────────
def write_csv(path, headers, rows):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path,"w",newline="",encoding="utf-8-sig") as f:
        csv.writer(f).writerows([headers]+rows)
    print(f"[OK] {os.path.basename(path)}  ({len(rows)} 行)")

write_csv(os.path.join(OUTPUT_DIR,"对话内容表.csv"), CONTENT_HEADERS, content_rows)
write_csv(os.path.join(OUTPUT_DIR,"对话池表.csv"),   POOL_HEADERS,    pool_rows)
print("完成。Unity 检测到 CSV 变化后自动导入。")
