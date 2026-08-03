"use client";

import { useEffect, useState } from "react";

type PanelKey =
  | "tasks"
  | "device"
  | "journal"
  | "contacts"
  | "calendar"
  | "inventory"
  | "archive"
  | "settings"
  | "profile"
  | "market";

const rooms = [
  { id: "living", name: "起居室", short: "HOME", note: "客人会在这里等待服务" },
  { id: "bedroom", name: "卧室", short: "REST", note: "恢复状态并推进至下一天" },
  { id: "kitchen", name: "厨房", short: "MAKE", note: "使用设备制作访客需要的物品" },
  { id: "study", name: "书房", short: "READ", note: "解锁配方与客人线索" },
];

const guests = [
  {
    id: "lorn",
    name: "洛恩",
    tag: "特殊访客",
    status: "初次来访",
    hint: "总能从旧物中认出不属于这个时代的细节。",
    color: "fox",
    affinity: 12,
    need: "一杯温热的赤茶，以及关于这栋房子的答案",
    day: "DAY 1",
    weekday: "MON",
    portrait: "/feishu-assets/fox.png",
    art: "/feishu-assets/fox.png",
    solution: "鲸声电话亭",
    gift: "一枚停在 08:20 的怀表",
  },
  {
    id: "crow",
    name: "赫墨",
    tag: "星夜访客",
    status: "等待回应",
    hint: "总把最糟糕的句子，改写成可以继续走下去的话。",
    color: "violet",
    affinity: 46,
    need: "一扇能唱回来的窗户",
    day: "DAY 3",
    weekday: "WED",
    portrait: "/feishu-assets/crow.png",
    art: "/feishu-assets/crow.png",
    solution: "琴弦窗户",
    gift: "一根沾着星尘的黑羽毛",
  },
  {
    id: "rabbit",
    name: "米娅",
    tag: "普通访客",
    status: "悄悄观察",
    hint: "她不太会开口请求，却会把想说的话写在风铃下面。",
    color: "rose",
    affinity: 31,
    need: "一串能替她说话的回声风铃",
    day: "DAY 5",
    weekday: "FRI",
    portrait: "/feishu-assets/rabbit.png",
    art: "/feishu-assets/rabbit.png",
    solution: "兔耳回声风铃",
    gift: "一张画着胡萝卜的小纸条",
  },
  {
    id: "hedgehog",
    name: "霍奇",
    tag: "普通访客",
    status: "坐在门边",
    hint: "他的刺总比话先竖起来，但会替屋里坏掉的东西包扎。",
    color: "amber",
    affinity: 58,
    need: "一盏不会逼人开口的暖灯",
    day: "DAY 7",
    weekday: "SUN",
    portrait: "/feishu-assets/hedgehog.png",
    art: "/feishu-assets/hedgehog.png",
    solution: "蒲公英吊灯",
    gift: "一卷重新缠好的绷带",
  },
];

const storyFurniture = [
  { id: "whale", name: "鲸声电话亭", image: "/feishu-assets/whale-call.png", forGuest: "洛恩", kind: "回应家具", note: "没有号码，也没有接线员。拿起话筒，只会听见很远的鲸鸣。" },
  { id: "strings", name: "琴弦窗户", image: "/feishu-assets/string-window.png", forGuest: "赫墨", kind: "回应家具", note: "白天收下屋里的话，夜晚用另一种情绪唱回来。" },
  { id: "chimes", name: "兔耳回声风铃", image: "/feishu-assets/wind-chimes.png", forGuest: "米娅", kind: "纪念家具", note: "每一张垂纸都能留下一句没来得及说出口的话。" },
  { id: "lamp", name: "蒲公英吊灯", image: "/feishu-assets/dandelion-lamp.png", forGuest: "霍奇", kind: "照明家具", note: "灯亮起时会有种子般的微光飘开，让沉默也有安全的位置。" },
  { id: "planter", name: "月牙植物台", image: "/feishu-assets/moon-planter.png", forGuest: "所有访客", kind: "纪念家具", note: "客人留下的植物与小物会逐周长进这弯月亮里。" },
];

const worldResources = [
  { id: "house", name: "雨夜之家", image: "/feishu-assets/dream-house.png", forGuest: "HOME NODE", kind: "场景概念", note: "一栋刚有人住进来的老房子：什么都有，却还不像一个家。" },
  { id: "map", name: "模糊宇宙路线", image: "/feishu-assets/universe-map.png", forGuest: "WORLD MAP", kind: "世界观", note: "旅馆、星光酒廊与孤独加油站组成的远行路线。" },
  { id: "fox-sheet", name: "狐狸访客设定", image: "/feishu-assets/fox-sheet.png", forGuest: "CHARACTER 01", kind: "角色设定", note: "精致、克制、习惯计算，也相信所有关系都能够交换。" },
  { id: "owl", name: "猫头鹰访客", image: "/feishu-assets/owl.png", forGuest: "FUTURE GUEST", kind: "角色候选", note: "抱着一本不愿让人翻开的旧书，似乎知道房子的历史。" },
];

const phases = [
  { name: "早晨", time: "08:00", range: "07:00–09:00", code: "morning", service: true },
  { name: "上午", time: "10:30", range: "09:00–12:00", code: "forenoon", service: true },
  { name: "中午", time: "13:00", range: "12:00–14:00", code: "noon", service: true },
  { name: "下午", time: "16:00", range: "14:00–18:00", code: "afternoon", service: true },
  { name: "晚上", time: "20:00", range: "18:00–22:00", code: "evening", service: true },
  { name: "深夜", time: "23:30", range: "22:00–07:00", code: "midnight", service: false },
];

const devicesByRoom: Record<string, { name: string; level: number; output: string; ready: boolean }[]> = {
  living: [
    { name: "黑胶唱机", level: 2, output: "舒缓情绪", ready: true },
    { name: "旧式壁炉", level: 1, output: "提高停留时长", ready: true },
  ],
  bedroom: [
    { name: "梦境捕捉器", level: 1, output: "获得记忆碎片", ready: false },
    { name: "床头留声机", level: 2, output: "恢复主角状态", ready: true },
  ],
  kitchen: [
    { name: "手冲咖啡台", level: 3, output: "制作热饮", ready: true },
    { name: "微波烤箱", level: 1, output: "制作简餐", ready: true },
    { name: "玻璃药罐", level: 2, output: "合成特殊配方", ready: false },
  ],
  study: [
    { name: "旧书检索机", level: 2, output: "发现线索", ready: true },
    { name: "观星镜", level: 1, output: "预测特殊访客", ready: false },
  ],
};

const panelMeta: Record<PanelKey, { eyebrow: string; title: string; mark: string }> = {
  tasks: { eyebrow: "TODAY / 03", title: "今日委托", mark: "任" },
  device: { eyebrow: "HOUSE INDEX", title: "设备图鉴", mark: "器" },
  journal: { eyebrow: "MEMORY LOG", title: "日记与成就", mark: "记" },
  contacts: { eyebrow: "VISITOR FILE", title: "访客通讯录", mark: "录" },
  archive: { eyebrow: "HOUSE ARCHIVE", title: "叙事资源档案", mark: "集" },
  calendar: { eyebrow: "WEEK 01", title: "日程与时间", mark: "历" },
  inventory: { eyebrow: "STORAGE / 12", title: "House 仓库", mark: "仓" },
  settings: { eyebrow: "SYSTEM", title: "设置与存档", mark: "设" },
  profile: { eyebrow: "RESIDENT 001", title: "主角信息", mark: "我" },
  market: { eyebrow: "NIGHT MARKET", title: "经济与商城", mark: "店" },
};

function TinyIcon({ children }: { children: React.ReactNode }) {
  return <span className="tiny-icon" aria-hidden="true">{children}</span>;
}

function GuestPortrait({ guest, size = "" }: { guest: (typeof guests)[number]; size?: string }) {
  return <span className={`portrait ${size} ${guest.color}`}><img src={guest.portrait} alt="" /><i /></span>;
}

type EntryScreen = "menu" | "opening" | "game";
type MenuPanel = "saves" | "gallery" | "settings" | "exit" | null;
type SaveMode = "new" | "load";
type SaveSummary = { slot: number; occupied: boolean; savedAt: string; progress: string };

const SAVE_PREFIX = "guesthouse-of-meros-save-slot-";
const LEGACY_SAVE_KEY = "sweet-house-demo-save";

export default function Home() {
  const [entryScreen, setEntryScreen] = useState<EntryScreen>("menu");
  const [menuPanel, setMenuPanel] = useState<MenuPanel>(null);
  const [saveMode, setSaveMode] = useState<SaveMode>("new");
  const [galleryView, setGalleryView] = useState<"log" | "achievement">("log");
  const [activeSlot, setActiveSlot] = useState(1);
  const [saveSlots, setSaveSlots] = useState<SaveSummary[]>([1, 2, 3].map((slot) => ({ slot, occupied: false, savedAt: "", progress: "" })));
  const [menuNotice, setMenuNotice] = useState("");
  const [panel, setPanel] = useState<PanelKey | null>(null);
  const [history, setHistory] = useState<PanelKey[]>([]);
  const [room, setRoom] = useState("living");
  const [roomTransition, setRoomTransition] = useState<"idle" | "closing" | "opening">("idle");
  const [guestId, setGuestId] = useState("lorn");
  const [phase, setPhase] = useState(2);
  const [dialogue, setDialogue] = useState(false);
  const [served, setServed] = useState<string[]>([]);
  const [toast, setToast] = useState("欢迎回家。本周有 4 位访客。 ");
  const [bgm, setBgm] = useState(64);
  const [sfx, setSfx] = useState(78);
  const [windowMode, setWindowMode] = useState("无边框");
  const [journalTab, setJournalTab] = useState<"log" | "achievement">("log");
  const [selectedDevice, setSelectedDevice] = useState(0);
  const [archiveTab, setArchiveTab] = useState<"furniture" | "world">("furniture");
  const [selectedArchiveId, setSelectedArchiveId] = useState("whale");
  const [fogRadius, setFogRadius] = useState(5);
  const [placedFurniture, setPlacedFurniture] = useState("whale");

  const guest = guests.find((item) => item.id === guestId) ?? guests[0];
  const waitingGuests = guests.filter((item) => !served.includes(item.id));
  const activeQueueGuest = waitingGuests[0] ?? null;
  const currentRoom = rooms.find((item) => item.id === room) ?? rooms[0];
  const currentDevices = devicesByRoom[room];
  const hasSave = saveSlots.some((item) => item.occupied);

  const readSaveRaw = (slot: number) => localStorage.getItem(`${SAVE_PREFIX}${slot}`) ?? (slot === 1 ? localStorage.getItem(LEGACY_SAVE_KEY) : null);

  const refreshSaveSlots = () => {
    const summaries = [1, 2, 3].map((slot) => {
      const raw = readSaveRaw(slot);
      if (!raw) return { slot, occupied: false, savedAt: "", progress: "" };
      try {
        const data = JSON.parse(raw) as { savedAt?: string; served?: string[]; room?: string };
        const date = data.savedAt ? new Date(data.savedAt) : null;
        return {
          slot,
          occupied: true,
          savedAt: date && !Number.isNaN(date.getTime()) ? date.toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" }) : "旧存档",
          progress: `WEEK 01 · ${data.served?.length ?? 0}/4 委托 · ${rooms.find((item) => item.id === data.room)?.name ?? "起居室"}`,
        };
      } catch {
        return { slot, occupied: true, savedAt: "旧存档", progress: "存档信息待恢复" };
      }
    });
    setSaveSlots(summaries);
  };

  useEffect(() => {
    refreshSaveSlots();
  }, []);

  const notify = (message: string) => {
    setToast(message);
    window.setTimeout(() => setToast(""), 3600);
  };

  const openPanel = (key: PanelKey) => {
    if (panel && panel !== key) setHistory((items) => [...items, panel].slice(-6));
    setPanel(key);
    setDialogue(false);
  };

  const goBack = () => {
    if (dialogue) {
      setDialogue(false);
      return;
    }
    if (history.length) {
      const previous = history[history.length - 1];
      setHistory((items) => items.slice(0, -1));
      setPanel(previous);
      return;
    }
    setPanel(null);
  };

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (entryScreen !== "game") {
        if (event.key === "Escape") setMenuPanel(null);
        return;
      }
      if (event.key === "Escape") goBack();
      if (!panel && ["ArrowLeft", "ArrowRight"].includes(event.key)) {
        setRoom((current) => {
          const index = rooms.findIndex((item) => item.id === current);
          const delta = event.key === "ArrowRight" ? 1 : -1;
          return rooms[(index + delta + rooms.length) % rooms.length].id;
        });
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [panel, dialogue, history, entryScreen]);

  const selectRoom = (id: string) => {
    const next = rooms.find((item) => item.id === id);
    if (id === room) {
      notify(`当前位于${next?.name ?? "房间"} · ${next?.note ?? ""}`);
      return;
    }
    if (roomTransition !== "idle") return;
    const usesDoor = id === "bedroom" || room === "bedroom";
    if (!usesDoor) {
      setRoom(id);
      setSelectedDevice(0);
      notify(id === "kitchen" ? "镜头聚焦至厨房料理台" : id === "study" ? "视角旋转 90° · 已进入书房" : `已回到${next?.name ?? "起居室"}`);
      return;
    }
    setRoomTransition("closing");
    window.setTimeout(() => {
      setRoom(id);
      setSelectedDevice(0);
      setRoomTransition("opening");
      notify(`已进入${next?.name ?? "房间"} · ${next?.note ?? ""}`);
      window.setTimeout(() => setRoomTransition("idle"), 780);
    }, 430);
  };

  const advancePhase = () => {
    const nextIndex = (phase + 1) % phases.length;
    setPhase(nextIndex);
    notify(nextIndex === 0 ? "新的一天开始了 · 早晨 08:00" : `时间推进至${phases[nextIndex].name} · ${phases[nextIndex].time}`);
  };

  const serveGuest = () => {
    if (activeQueueGuest && guest.id !== activeQueueGuest.id) {
      notify(`${activeQueueGuest.name} 还在柜台前，请按顺序接待`);
      return;
    }
    const nextGuest = guests.find((item) => item.id !== guest.id && !served.includes(item.id));
    setServed((items) => (items.includes(guest.id) ? items : [...items, guest.id]));
    setDialogue(false);
    if (nextGuest) {
      setGuestId(nextGuest.id);
      notify(`${guest.name} 已完成接待 · 下一位 ${nextGuest.name} 上前`);
    } else {
      notify(`${guest.name} 已完成接待 · 今日队列已清空`);
    }
  };

  const selectQueuedGuest = (item: (typeof guests)[number]) => {
    if (served.includes(item.id)) {
      notify(`${item.name} 已完成接待并离开旅店`);
      return;
    }
    const queueIndex = waitingGuests.findIndex((queued) => queued.id === item.id);
    if (activeQueueGuest?.id !== item.id) {
      notify(`${item.name} 正在排队 · 前方还有 ${queueIndex} 位客人`);
      return;
    }
    setGuestId(item.id);
    setDialogue(true);
  };

  const applySave = (raw: string) => {
    const data = JSON.parse(raw) as {
      room?: string;
      phase?: number;
      served?: string[];
      bgm?: number;
      sfx?: number;
      windowMode?: string;
    };
    if (data.room) setRoom(data.room);
    if (typeof data.phase === "number") setPhase(data.phase);
    if (data.served) setServed(data.served);
    if (typeof data.bgm === "number") setBgm(data.bgm);
    if (typeof data.sfx === "number") setSfx(data.sfx);
    if (data.windowMode) setWindowMode(data.windowMode);
  };

  const enterHouse = (slot: number, loadExisting: boolean) => {
    const raw = readSaveRaw(slot);
    if (loadExisting && !raw) {
      setMenuNotice(`Slot 0${slot} 还没有存档`);
      return;
    }
    setActiveSlot(slot);
    if (loadExisting && raw) applySave(raw);
    if (!loadExisting) {
      setRoom("living");
      setGuestId("lorn");
      setPhase(2);
      setServed([]);
      setPanel(null);
      setDialogue(false);
    }
    setMenuPanel(null);
    setMenuNotice("");
    setEntryScreen("opening");
    window.setTimeout(() => {
      setEntryScreen("game");
      setToast(loadExisting ? `Slot 0${slot} 已读取 · 欢迎回来` : `Slot 0${slot} · 新的一周开始了`);
    }, 1550);
  };

  const continueGame = () => {
    const latest = [...saveSlots].filter((item) => item.occupied).sort((a, b) => b.savedAt.localeCompare(a.savedAt))[0];
    if (!latest) {
      setMenuNotice("还没有存档，请先开始新游戏");
      return;
    }
    enterHouse(latest.slot, true);
  };

  const saveGame = () => {
    localStorage.setItem(`${SAVE_PREFIX}${activeSlot}`, JSON.stringify({ room, phase, served, bgm, sfx, windowMode, savedAt: new Date().toISOString() }));
    refreshSaveSlots();
    notify(`进度已保存到本机 · Slot 0${activeSlot}`);
  };

  const loadGame = () => {
    const raw = readSaveRaw(activeSlot);
    if (!raw) {
      notify(`Slot 0${activeSlot} 还没有存档`);
      return;
    }
    applySave(raw);
    notify(`Slot 0${activeSlot} 已读取 · 欢迎回来`);
  };

  const renderPanel = () => {
    if (!panel) return null;

    if (panel === "tasks") {
      return (
        <div className="panel-content task-list">
          <div className="focus-card">
            <span className="status-pip active" />
            <div><small>MAIN / {guest.tag}</small><h3>{guest.name} · {guest.need}</h3><p>{guest.hint} 推荐使用「{guest.solution}」，完成后可能留下「{guest.gift}」。</p></div>
            <b>01</b>
          </div>
          {["为赫墨制造琴弦窗户", "把米娅的纸条挂上风铃", "检查明日访客预告"].map((item, index) => (
            <button className="list-row" key={item} onClick={() => notify(`已追踪：${item}`)}>
              <span className="row-number">0{index + 2}</span><span>{item}</span><em>{index === 2 ? "未解锁" : "进行中"}</em>
            </button>
          ))}
          <div className="progress-block"><span>本周 House 进度</span><b>37%</b><i><u style={{ width: "37%" }} /></i></div>
        </div>
      );
    }

    if (panel === "device") {
      const device = currentDevices[selectedDevice] ?? currentDevices[0];
      return (
        <div className="panel-content device-layout">
          <div className="room-rail">
            {rooms.map((item) => <button className={room === item.id ? "selected" : ""} key={item.id} onClick={() => selectRoom(item.id)}>{item.name}<small>{devicesByRoom[item.id].length} DEVICES</small></button>)}
          </div>
          <div className="device-grid">
            {currentDevices.map((item, index) => (
              <button className={`device-card ${selectedDevice === index ? "selected" : ""}`} key={item.name} onClick={() => setSelectedDevice(index)}>
                <span className="device-shape"><i /><i /><i /></span><small>LV.{item.level} · {item.ready ? "可使用" : "待修复"}</small><strong>{item.name}</strong><em>{item.output}</em>
              </button>
            ))}
          </div>
          <div className="recipe-card"><small>当前设备</small><h3>{device.name}</h3><p>{device.output}</p><div className="material-row"><span>咖啡豆 ×2</span><span>温水 ×1</span></div><button className="primary-button" onClick={() => notify(`${device.name} 已开始运作`)} disabled={!device.ready}>{device.ready ? "开始制作" : "需要修复"}</button></div>
        </div>
      );
    }

    if (panel === "contacts") {
      return (
        <div className="panel-content contact-layout">
          <div className="contact-list">{guests.map((item) => <button key={item.id} className={guestId === item.id ? "selected" : ""} onClick={() => setGuestId(item.id)}><GuestPortrait guest={item} size="mini" /><div><b>{item.name}</b><small>{item.tag}</small></div><em>{item.affinity}%</em></button>)}</div>
          <div className="contact-profile"><GuestPortrait guest={guest} size="large" /><div><small>{guest.tag} / No. 0{guests.findIndex((item) => item.id === guest.id) + 1}</small><h3>{guest.name}</h3><p>“{guest.hint}”</p><div className="affinity"><span>信赖</span><i><u style={{ width: `${guest.affinity}%` }} /></i><b>{guest.affinity}</b></div><dl><div><dt>当前需求</dt><dd>{guest.need}</dd></div><div><dt>适配家具</dt><dd>{guest.solution}</dd></div><div><dt>可能留下</dt><dd>{guest.gift}</dd></div></dl><button className="primary-button" onClick={() => { setPanel(null); setDialogue(true); }}>与 TA 交谈</button></div></div>
        </div>
      );
    }

    if (panel === "archive") {
      const items = archiveTab === "furniture" ? storyFurniture : worldResources;
      const selected = items.find((item) => item.id === selectedArchiveId) ?? items[0];
      return <div className="panel-content archive-layout">
        <div className="archive-tabs"><button className={archiveTab === "furniture" ? "selected" : ""} onClick={() => { setArchiveTab("furniture"); setSelectedArchiveId("whale"); }}>叙事家具</button><button className={archiveTab === "world" ? "selected" : ""} onClick={() => { setArchiveTab("world"); setSelectedArchiveId("house"); }}>世界与角色</button></div>
        <div className="archive-grid">{items.map((item, index) => <button key={item.id} className={selected.id === item.id ? "selected" : ""} onClick={() => setSelectedArchiveId(item.id)}><span><img src={item.image} alt={item.name} /></span><small>0{index + 1} / {item.kind}</small><strong>{item.name}</strong><em>{item.forGuest}</em></button>)}</div>
        <aside className="archive-detail"><div className={`archive-preview ${selected.id === "map" ? "fog-map-preview" : ""}`}><img src={selected.image} alt={selected.name} />{selected.id === "map" && <><span className="fog-layer" style={{ WebkitMaskImage: `radial-gradient(circle ${42 + fogRadius * 5}px at 52% 56%, transparent 0, transparent 74%, #000 100%)`, maskImage: `radial-gradient(circle ${42 + fogRadius * 5}px at 52% 56%, transparent 0, transparent 74%, #000 100%)` }} /><span className="map-player-dot"><i /><b>{fogRadius}m</b></span></>}</div><small>{selected.kind} · {selected.forGuest}</small><h3>{selected.name}</h3><p>{selected.id === "map" ? `角色移动时，以当前位置为中心永久揭开迷雾。当前探索半径 ${fogRadius} 米。` : selected.note}</p>{archiveTab === "furniture" ? <><dl><div><dt>触发</dt><dd>访客提出需要后解锁制造</dd></div><div><dt>余韵</dt><dd>客人离开后留下物品与新的生活习惯</dd></div></dl><button className="primary-button" onClick={() => { setPlacedFurniture(selected.id); notify(`${selected.name} 已加入访客房间快捷栏`); }}>放入房间</button></> : selected.id === "map" ? <div className="fog-radius-control"><small>REVEAL RADIUS / 开图半径</small><div>{[5,10,15,20].map((radius) => <button className={fogRadius === radius ? "selected" : ""} key={radius} onClick={() => setFogRadius(radius)}>{radius}m</button>)}</div><em>基础 5m · 最大 20m</em></div> : <button className="ghost-button" onClick={() => notify(`${selected.name} 已设为追踪资料`)}>追踪这份资料</button>}</aside>
      </div>;
    }

    if (panel === "calendar") {
      return (
        <div className="panel-content calendar-layout">
          <div className="big-date"><small>2086 / JUNE</small><strong>17</strong><span>WEDNESDAY · {phases[phase].name}</span></div>
          <div className="calendar-grid">{Array.from({ length: 28 }, (_, index) => <button className={index === 16 ? "today" : index === 18 ? "event" : ""} key={index}><small>{["M","T","W","T","F","S","S"][index % 7]}</small>{index + 1}{index === 18 && <i />}</button>)}</div>
          <div className="schedule"><h3>时间阶段</h3><div className="time-phase-list">{phases.map((item,index)=><button className={phase === index ? "selected" : ""} key={item.code} onClick={() => setPhase(index)}><span>{item.name}<small>{item.range}</small></span><em>{item.service ? "可服务" : "睡觉"}</em></button>)}</div><button className="primary-button advance-time" onClick={advancePhase}>推进至下一阶段 →</button><button className="ghost-button" onClick={() => notify("明日：有 2 位普通访客，天气晴")}>查看明日预告</button></div>
        </div>
      );
    }

    if (panel === "inventory") {
      const items = ["深烘咖啡豆", "空白磁带", "生锈的钥匙", "蓝色干花", "记忆碎片", "旧书页", "蜂蜜方糖", "损坏齿轮", "夜光粉末"];
      return <div className="panel-content inventory-layout"><div className="inventory-filter"><button className="selected">全部</button><button>材料</button><button>线索</button><button>消耗品</button></div><div className="inventory-grid">{items.map((item, index) => <button key={item} onClick={() => notify(`${item} · ${index % 3 === 0 ? "可用于访客委托" : "House 收藏物"}`)}><span className={`item-gem gem-${index % 4}`} /><strong>{item}</strong><small>×{(index * 3) % 8 + 1}</small></button>)}</div><aside><small>STORAGE</small><strong>12 / 40</strong><p>部分物品可在设备中合成，也可能改变访客对话。</p></aside></div>;
    }

    if (panel === "journal") {
      return (
        <div className="panel-content journal-layout">
          <div className="segmented"><button className={journalTab === "log" ? "selected" : ""} onClick={() => setJournalTab("log")}>日记</button><button className={journalTab === "achievement" ? "selected" : ""} onClick={() => setJournalTab("achievement")}>成就</button></div>
          {journalTab === "log" ? <div className="log-pages"><article><small>06 / 17 · 雨转晴</small><h3>窗户唱回来的那句话</h3><p>赫墨说“今天糟透了”。琴弦轻轻响了一下，唱回：“但你还是走到了这里。”</p><span>关键词：琴弦窗户 / 反向情绪</span></article><article><small>06 / 16 · 阴</small><h3>风铃下的纸条</h3><p>米娅没有说再见，只留下一张画着胡萝卜的小纸条。</p></article></div> : <div className="achievement-grid">{[["夜的主人","在深夜完成一次服务",true],["初次相识","录入 3 位访客",true],["家的轮廓","解锁全部房间",false],["无人知晓","发现特殊访客的秘密",false]].map(([title, desc, done], index) => <div className={done ? "done" : ""} key={String(title)}><span>{done ? "✓" : index + 1}</span><strong>{title}</strong><small>{desc}</small></div>)}</div>}
        </div>
      );
    }

    if (panel === "profile") {
      return <div className="panel-content profile-layout"><div className="profile-silhouette"><span>Y</span><i /></div><div className="profile-copy"><small>HOUSE KEEPER / 001</small><h3>弈</h3><p>记忆修复师。每一次帮助访客，都会让他们看起来更像“人”，也让自己离答案更近一点。</p><dl><div><dt>状态</dt><dd>稳定</dd></div><div><dt>病情</dt><dd>雾化 18%</dd></div><div><dt>House 等级</dt><dd>LV. 03</dd></div><div><dt>服务次数</dt><dd>12</dd></div></dl><div className="trait-row"><span>细致</span><span>夜行</span><span>共感</span></div></div></div>;
    }

    if (panel === "market") {
      return <div className="panel-content market-layout"><div className="wallet"><small>HOUSE CREDIT</small><strong>2,480</strong><span>本周收入 +680</span></div><div className="market-grid">{[["旧式咖啡磨","设备","680"],["窗边吊灯","装饰","420"],["访客线索包","线索","180"],["记忆匣","抽取 ×1","300"]].map(([name, type, price], index) => <button key={name} onClick={() => notify(index === 3 ? "演示抽取：获得「蓝色干花」" : `${name} 已加入愿望单`)}><span className={`market-object object-${index}`} /><small>{type}</small><strong>{name}</strong><em>◈ {price}</em></button>)}</div><p className="market-note">Demo 仅展示经济循环：服务访客 → 获得信用点 → 购买设备与线索 → 解锁新的访客响应。</p></div>;
    }

    if (panel === "settings") {
      return (
        <div className="panel-content settings-layout">
          <section><small>SAVE DATA</small><h3>Slot 0{activeSlot}</h3><p>House LV.03 · WEEK 01 · {served.length}/4 委托推进</p><div className="button-pair"><button className="primary-button" onClick={saveGame}>保存进度</button><button className="ghost-button" onClick={loadGame}>读取存档</button></div></section>
          <section><small>DISPLAY</small><label>视窗模式<select value={windowMode} onChange={(event) => setWindowMode(event.target.value)}><option>无边框</option><option>全屏</option><option>窗口</option></select></label><label>分辨率<select defaultValue="2560 × 1440"><option>2560 × 1440</option><option>1920 × 1080</option><option>1600 × 900</option></select></label></section>
          <section><small>AUDIO</small><label>BGM <b>{bgm}</b><input type="range" min="0" max="100" value={bgm} onChange={(event) => setBgm(Number(event.target.value))} /></label><label>SFX <b>{sfx}</b><input type="range" min="0" max="100" value={sfx} onChange={(event) => setSfx(Number(event.target.value))} /></label></section>
          <footer><span>ESC 返回上一级</span><button onClick={() => notify("设置已应用")}>应用设置</button></footer>
        </div>
      );
    }

    return null;
  };

  if (entryScreen !== "game") {
    return (
      <main className={`title-screen ${entryScreen === "opening" ? "is-opening" : ""}`}>
        <div className="title-grain" />
        {entryScreen === "opening" ? <>
          <div className="entry-home-reveal"><img src="/house-hub-v2.png" alt="Meros 旅店温暖的室内" /><div><small>THE DOOR IS OPEN</small><strong>欢迎回家</strong></div></div>
          <div className="cover-door cover-door-left"><img src="/og-meros.png" alt="" /></div>
          <div className="cover-door cover-door-right"><img src="/og-meros.png" alt="" /></div>
          <span className="door-light" />
        </> : <>
        <img className="title-cover" src="/og-meros.png" alt="The Guesthouse of Meros 封面，四位动物访客站在暮色旅店中" draggable="false" />
        <div className="title-vignette" />
        {!menuPanel && <section className="main-menu corner-menu" aria-label="游戏菜单">
            <div className="menu-save-state"><i /><span>{hasSave ? "检测到本地存档" : "等待第一位住客"}</span></div>
            <div className="start-actions">
              <button className="menu-action menu-primary" onClick={() => { setSaveMode("new"); setMenuPanel("saves"); setMenuNotice(""); }}><span>新游戏</span><small>NEW STORY</small></button>
              <button className="menu-action" disabled={!hasSave} onClick={continueGame}><span>继续游戏</span><small>{hasSave ? "CONTINUE" : "NO SAVE DATA"}</small></button>
            </div>
            <nav>
              <button onClick={() => { setSaveMode("load"); setMenuPanel("saves"); setMenuNotice(""); }}><b>读取存档</b><small>LOAD</small></button>
              <button onClick={() => { setMenuPanel("gallery"); setGalleryView("log"); }}><b>画廊</b><small>LOG / ACHIEVEMENT</small></button>
              <button onClick={() => setMenuPanel("settings")}><b>设置</b><small>OPTIONS</small></button>
              <button onClick={() => setMenuPanel("exit")}><b>退出游戏</b><small>QUIT</small></button>
            </nav>
            <footer><span>ENTER 选择</span><span>ESC 返回</span></footer>
        </section>}

          {menuPanel === "saves" && <aside className="menu-page save-select" aria-label="选择存档"><div className="menu-page-inner">
            <header><div><small>{saveMode === "new" ? "START A NEW STORY" : "LOAD YOUR STORY"}</small><h2>{saveMode === "new" ? "选择新游戏存档" : "读取存档"}</h2></div><button onClick={() => setMenuPanel(null)}>← 返回主菜单</button></header>
            <p>{saveMode === "new" ? "选择存档位后开始新的旅店故事。已有存档只有在你下一次保存时才会被覆盖。" : "选择一段已保存的旅店记忆。"}</p>
            <div className="save-slot-list">{saveSlots.map((item) => <button key={item.slot} className={item.occupied ? "occupied" : "empty"} disabled={saveMode === "load" && !item.occupied} onClick={() => enterHouse(item.slot, saveMode === "load")}><span>0{item.slot}</span><div><small>SAVE SLOT</small><strong>{item.occupied ? item.progress : "空存档"}</strong><em>{item.occupied ? item.savedAt : saveMode === "new" ? "从这里开始" : "NO DATA"}</em></div><b>{saveMode === "new" ? item.occupied ? "选择 · 将覆盖" : "选择" : item.occupied ? "读取" : "—"}</b></button>)}</div>
            {menuNotice && <div className="menu-notice">{menuNotice}</div>}
          </div></aside>}

          {menuPanel === "gallery" && <aside className="menu-page gallery-page" aria-label="画廊"><div className="menu-page-inner">
            <header><div><small>MEMORIES OF THE GUESTHOUSE</small><h2>画廊</h2></div><button onClick={() => setMenuPanel(null)}>← 返回主菜单</button></header>
            <div className="gallery-tabs"><button className={galleryView === "log" ? "selected" : ""} onClick={() => setGalleryView("log")}>游戏日志</button><button className={galleryView === "achievement" ? "selected" : ""} onClick={() => setGalleryView("achievement")}>成就系统</button></div>
            {galleryView === "log" ? <div className="title-log-grid"><article><small>WEEK 01 · 06/17</small><h3>窗户唱回来的那句话</h3><p>赫墨说“今天糟透了”。琴弦回答：“但你还是走到了这里。”</p></article><article><small>WEEK 01 · 06/16</small><h3>风铃下的纸条</h3><p>米娅没有说再见，只留下了一张画着胡萝卜的小纸条。</p></article></div> : <div className="title-achievement-grid">{[["初次相识","记录第一位访客",true],["夜的主人","在深夜完成服务",true],["家的轮廓","解锁全部房间",false],["无人知晓","发现特殊访客的秘密",false]].map(([name,desc,done],index)=><article className={done ? "done" : ""} key={String(name)}><span>{done ? "✓" : `0${index+1}`}</span><div><h3>{name}</h3><p>{desc}</p></div><small>{done ? "已完成" : "未解锁"}</small></article>)}</div>}
          </div></aside>}

          {menuPanel === "settings" && <aside className="menu-page settings-page"><div className="menu-page-inner">
            <header><div><small>HOUSE PREFERENCES</small><h2>设置</h2></div><button onClick={() => setMenuPanel(null)}>← 返回主菜单</button></header>
            <div className="title-settings-grid">
              <section><small>INTERFACE & DATA</small><h3>界面与存档</h3><label>界面切换<select defaultValue="沉浸式"><option>沉浸式</option><option>简洁模式</option></select></label><div className="setting-actions"><button onClick={() => { saveGame(); setMenuNotice(`已保存到 Slot 0${activeSlot}`); }}>保存</button><button onClick={() => { setSaveMode("load"); setMenuPanel("saves"); setMenuNotice(""); }}>读取存档</button></div></section>
              <section><small>GAMEPLAY</small><h3>游戏性</h3><label className="setting-toggle">对话自动播放<input type="checkbox" /></label><label className="setting-toggle">显示交互提示<input type="checkbox" defaultChecked /></label><label className="setting-toggle">镜头轻微晃动<input type="checkbox" defaultChecked /></label></section>
              <section><small>GRAPHICS</small><h3>图形</h3><label>视窗模式<select value={windowMode} onChange={(event) => setWindowMode(event.target.value)}><option>全屏</option><option>窗口</option><option>无边框</option></select></label><label>分辨率<select defaultValue="1920 × 1080"><option>2560 × 1440</option><option>1920 × 1080</option><option>1600 × 900</option></select></label></section>
              <section><small>AUDIO</small><h3>音乐音效</h3><label>BGM <b>{bgm}</b><input type="range" min="0" max="100" value={bgm} onChange={(event) => setBgm(Number(event.target.value))} /></label><label>SFX <b>{sfx}</b><input type="range" min="0" max="100" value={sfx} onChange={(event) => setSfx(Number(event.target.value))} /></label></section>
            </div>
          </div></aside>}
          {menuPanel === "exit" && <aside className="menu-page compact-page"><div className="menu-page-inner"><header><div><small>LEAVE THE GUESTHOUSE?</small><h2>退出游戏</h2></div><button onClick={() => setMenuPanel(null)}>← 返回主菜单</button></header><p>网页 Demo 无法直接关闭浏览器。你可以安全关闭这个标签页，或返回主菜单继续体验。</p><button className="paper-confirm" onClick={() => setMenuPanel(null)}>返回主菜单</button></div></aside>}
          {menuNotice && menuPanel !== "saves" && <div className="title-toast">{menuNotice}</div>}
        </>}
      </main>
    );
  }

  return (
    <main className={`game-shell phase-${phases[phase].code} room-${room}`}>
      <div className="noise" />
      <div className="ambient-orb orb-one" /><div className="ambient-orb orb-two" />

      <header className="top-hud">
        <button className="time-system-card" onClick={() => openPanel("calendar")} aria-label="打开时间与日历"><div className="time-date"><small>WEEK 01 · 2086</small><strong>06 / 17</strong><em>WEDNESDAY</em></div><div className="time-current"><small>{phases[phase].service ? "● 可服务时间" : "○ 休息时间"}</small><span><strong>{phases[phase].name}</strong><time>{phases[phase].time}</time></span><em>{phases[phase].range}</em></div><div className="time-stage-track">{phases.map((item,index)=><i className={phase === index ? "selected" : ""} key={item.code} />)}</div></button>
        <button className="currency" onClick={() => openPanel("market")}><small>HOUSE CREDIT</small><strong>◈ 2,480</strong><span>＋</span></button>
        <div className="top-story-lockup">
          <button className="brand-lockup" onClick={() => { setPanel(null); setDialogue(false); setEntryScreen("menu"); }}><span>The Guesthouse<br />of Meros</span><div><b>NEW CHAPTER</b><small>MEMORY LODGE / 2086</small></div></button>
          <button className="time-card" onClick={() => openPanel("calendar")}><span className="live-dot" /><div><small>WELCOME HOME.</small><strong>本周将有 <mark>4</mark> 位访客来访</strong></div></button>
        </div>
        <button className="top-settings-button" onClick={() => openPanel("settings")} aria-label="打开设置"><span>设</span><div><small>OPTIONS</small><strong>设置</strong></div></button>
      </header>

      <button className="visitor-task-card" onClick={() => openPanel("tasks")} aria-label={`查看${guest.name}的任务详情`}>
        <header><small>CURRENT VISITOR TASK</small><span>{served.includes(guest.id) ? "已完成" : "进行中"}</span></header>
        <strong>{guest.name} · {guest.need}</strong>
        <p>{guest.hint}</p>
        <div className="visitor-task-progress"><i><u style={{ width: served.includes(guest.id) ? "100%" : guest.id === "lorn" ? "35%" : "20%" }} /></i><b>{served.includes(guest.id) ? "100" : guest.id === "lorn" ? "35" : "20"}%</b></div>
        <footer>点击查看任务详情 <span>→</span></footer>
      </button>

      <aside className="guest-rail" aria-label="访客列表">
        <div className="rail-label"><small>WAITING LINE / 接待队列</small><strong>{String(waitingGuests.length).padStart(2,"0")}</strong></div>
        {guests.map((item, index) => { const queueIndex = waitingGuests.findIndex((queued) => queued.id === item.id); const isActive = activeQueueGuest?.id === item.id; const isServed = served.includes(item.id); return <button key={item.id} className={`${isActive ? "selected queue-active" : ""} ${isServed ? "served" : `queue-waiting queue-pos-${Math.max(queueIndex + 1,1)}`}`} onClick={() => selectQueuedGuest(item)}><GuestPortrait guest={item} /><div><small>{isActive ? "NOW SERVING" : `0${index + 1}`}</small><b>{item.name}</b><em>{isServed ? "已离开" : isActive ? "正在接待" : `排队中 · 前方 ${queueIndex} 位`}</em></div><span className="queue-position">{isServed ? "✓" : isActive ? "柜台" : queueIndex + 1}</span></button>; })}
        <button className="profile-chip" onClick={() => openPanel("profile")}><span>弈</span><div><small>HOUSE KEEPER</small><b>状态稳定 · 82%</b></div></button>
      </aside>

      <section className="house-stage" aria-label="House 场景">
        <img className="scene-art" src={room === "bedroom" ? "/feishu-assets/dream-house.png" : room === "study" ? "/study-room-clean.png" : "/house-hub-v2.png"} alt={room === "bedroom" ? "The Guesthouse of Meros 独立卧室空间" : room === "study" ? "The Guesthouse of Meros 旋转视角后的独立书房场景" : "手绘风格的 The Guesthouse of Meros 暮色室内，访客在书架、厨房与沙发旁活动"} draggable="false" />
        <div className="scene-wash" />
        {room === "study" && <div className="study-turn-plane" aria-hidden="true"><i /><i /><span /></div>}
        {roomTransition !== "idle" && <div className={`room-door-transition ${roomTransition}`} aria-hidden="true"><div className="room-door room-door-left"><i /><i /></div><div className="room-door room-door-right"><i /><i /></div><span className="room-door-light" /></div>}
        <span className="art-sticker">NEW<br />HOME</span>
        <button className="stage-hotspot hotspot-device" onClick={() => openPanel("device")}><span>＋</span><div><b>{room === "kitchen" ? "手冲咖啡台" : room === "study" ? "旧书检索机" : "黑胶唱机"}</b><small>查看设备</small></div></button>
        <div className="stage-caption"><small>CURRENT ROOM / 04</small><strong>{currentRoom.name}</strong><span>{currentRoom.note}</span></div>
      </section>

      <aside className="right-dock" aria-label="功能菜单">
        {(["device","journal","contacts","archive"] as PanelKey[]).map((key) => <button key={key} className={panel === key ? "selected" : ""} onClick={() => openPanel(key)}><TinyIcon>{panelMeta[key].mark}</TinyIcon><span>{panelMeta[key].title.replace("与成就", "").replace("访客", "").replace("叙事资源", "")}</span>{key === "contacts" && <i />}</button>)}
      </aside>

      <nav className="room-nav" aria-label="房间切换">
        <div className="nav-title"><small>MAKE IT HOME</small><span>← → 快速切换</span></div>
        {rooms.map((item) => <button className={room === item.id ? "selected" : ""} key={item.id} onClick={() => selectRoom(item.id)}><small>{item.short}</small><span className={`room-icon icon-${item.id}`}><i /><i /></span><strong>{item.name}</strong></button>)}
        <button className="locked-room" onClick={() => notify("仓库房间将在 House LV.04 解锁")}><small>LOCKED</small><span className="room-icon"><i /></span><strong>地下仓库</strong></button>
      </nav>

      {panel && <div className="panel-scrim" onClick={goBack} />}
      <aside className={`system-panel ${panel ? "open" : ""}`} aria-hidden={!panel}>
        {panel && <><header><button className="back-button" onClick={goBack} aria-label="返回">←<small>ESC</small></button><div><small>{panelMeta[panel].eyebrow}</small><h2>{panelMeta[panel].title}</h2></div><span className="panel-mark">{panelMeta[panel].mark}</span></header>{renderPanel()}</>}
      </aside>

      {dialogue && <div className="dialogue-layer has-visitor-scene">
        <img className="visitor-scene" src="/house-hub-v2.png" alt={`${guest.name}到访 The Guesthouse of Meros`} draggable="false" />
        <div className="visitor-scene-vignette" />
        <button className="dialogue-close" onClick={() => setDialogue(false)}>ESC · 结束交谈</button>
        <div className={`visitor-character-card ${guest.color}`}><img src={guest.art} alt={`${guest.name}角色概念图`} /><span>VISITOR / {guest.weekday}</span></div>
        {placedFurniture && <button className="placed-story-item" onClick={() => openPanel("archive")}><img src={storyFurniture.find((item) => item.id === placedFurniture)?.image} alt="" /><span><small>ROOM RESPONSE</small><b>{storyFurniture.find((item) => item.id === placedFurniture)?.name}</b></span></button>}
        <aside className="visitor-week-panel" aria-label="本周访客">
          <header><small>WEEK 01</small><strong>VISITOR THIS WEEK</strong></header>
          {guests.map((item) => <button className={guest.id === item.id ? "selected" : ""} key={item.id} onClick={() => setGuestId(item.id)}><GuestPortrait guest={item} size="mini" /><div><b>{item.name}</b><small>{item.day} · {item.weekday}</small></div>{item.id === "lorn" && <em>NEW</em>}</button>)}
        </aside>
        <div className="dialogue-box"><header><small>{guest.tag}</small><strong>{guest.name}</strong><em>信赖 {guest.affinity}%</em></header><p>{served.includes(guest.id) ? `谢谢。等我离开后，也许会把「${guest.gift}」留在这里。` : guest.id === "lorn" ? "看来你已经开始安顿下来了。这里有没有一台电话，能让人听见很远的声音？" : guest.id === "crow" ? "如果我说『今天糟透了』，那扇窗能不能唱回一句不一样的话？" : guest.id === "rabbit" ? "我写了一句话，可是还不想自己念出来……可以把它挂在风铃下面吗？" : "不用问我发生了什么。给我一盏安静的灯，我会自己坐一会儿。"}</p><div className="dialogue-actions"><button onClick={() => { openPanel("archive"); setArchiveTab("furniture"); }}>查看需求家具</button><button className="primary-button" disabled={served.includes(guest.id)} onClick={serveGuest}>{served.includes(guest.id) ? "委托已推进" : "回应委托"}</button></div></div>
        <nav className="visitor-tools" aria-label="家具快捷栏"><div><small>MAKE FOR VISITOR</small><span>根据来客需求制造并摆放</span></div>{storyFurniture.map((item) => <button className={placedFurniture === item.id ? "selected" : ""} key={item.id} onClick={() => { setPlacedFurniture(item.id); notify(`已摆放：${item.name}`); }}><img src={item.image} alt="" /><small>{item.name}</small></button>)}<button className="end-week" onClick={() => { setDialogue(false); notify("本周结算将在正式版本开放"); }}>结束本周 →</button></nav>
      </div>}

      {toast && <div className="toast"><span />{toast}</div>}
      <footer className="footer-note"><span>NEW LIFE, NEW HOME · UI/UX CONCEPT</span><span>ESC 返回 · ← → 切换房间</span></footer>
    </main>
  );
}
