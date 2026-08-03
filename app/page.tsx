"use client";

import { useEffect, useMemo, useState } from "react";

type PanelKey =
  | "tasks"
  | "device"
  | "journal"
  | "contacts"
  | "calendar"
  | "inventory"
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
    id: "lin",
    name: "林默",
    tag: "普通访客",
    status: "等待咖啡",
    hint: "他总在下雨前来到这里。",
    color: "cyan",
    affinity: 68,
    need: "一杯不过甜的热咖啡",
  },
  {
    id: "yue",
    name: "月白",
    tag: "特殊访客",
    status: "可交谈",
    hint: "似乎记得这栋房子从前的样子。",
    color: "violet",
    affinity: 42,
    need: "找到唱片《无人知晓的春天》",
  },
  {
    id: "momo",
    name: "墨墨",
    tag: "普通访客",
    status: "阅读中",
    hint: "会把看过的书重新按颜色排列。",
    color: "amber",
    affinity: 81,
    need: "一本关于远行的旧书",
  },
];

const phases = [
  { name: "早晨", time: "08:20", code: "morning" },
  { name: "午后", time: "15:40", code: "afternoon" },
  { name: "夜晚", time: "20:15", code: "evening" },
  { name: "深夜", time: "23:48", code: "midnight" },
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
  calendar: { eyebrow: "WEEK 01", title: "日程与时间", mark: "历" },
  inventory: { eyebrow: "STORAGE / 12", title: "House 仓库", mark: "仓" },
  settings: { eyebrow: "SYSTEM", title: "设置与存档", mark: "设" },
  profile: { eyebrow: "RESIDENT 001", title: "主角信息", mark: "我" },
  market: { eyebrow: "NIGHT MARKET", title: "经济与商城", mark: "店" },
};

function TinyIcon({ children }: { children: React.ReactNode }) {
  return <span className="tiny-icon" aria-hidden="true">{children}</span>;
}

export default function Home() {
  const [panel, setPanel] = useState<PanelKey | null>(null);
  const [history, setHistory] = useState<PanelKey[]>([]);
  const [room, setRoom] = useState("living");
  const [guestId, setGuestId] = useState("lin");
  const [phase, setPhase] = useState(2);
  const [dialogue, setDialogue] = useState(false);
  const [served, setServed] = useState<string[]>([]);
  const [toast, setToast] = useState("欢迎回家。今晚有 3 位访客。 ");
  const [bgm, setBgm] = useState(64);
  const [sfx, setSfx] = useState(78);
  const [windowMode, setWindowMode] = useState("无边框");
  const [journalTab, setJournalTab] = useState<"log" | "achievement">("log");
  const [selectedDevice, setSelectedDevice] = useState(0);

  const guest = guests.find((item) => item.id === guestId) ?? guests[0];
  const currentRoom = rooms.find((item) => item.id === room) ?? rooms[0];
  const currentDevices = devicesByRoom[room];

  const week = useMemo(
    () => [
      { day: "MON", state: "done", face: "林" },
      { day: "TUE", state: "done", face: "墨" },
      { day: "WED", state: "now", face: "月" },
      { day: "THU", state: "future", face: "?" },
      { day: "FRI", state: "future", face: "?" },
      { day: "SAT", state: "locked", face: "·" },
      { day: "SUN", state: "locked", face: "·" },
    ],
    [],
  );

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
  }, [panel, dialogue, history]);

  const selectRoom = (id: string) => {
    setRoom(id);
    setSelectedDevice(0);
    const next = rooms.find((item) => item.id === id);
    notify(`已移动到${next?.name ?? "房间"} · ${next?.note ?? ""}`);
  };

  const serveGuest = () => {
    setServed((items) => (items.includes(guest.id) ? items : [...items, guest.id]));
    setDialogue(false);
    notify(`${guest.name} 的委托已推进，亲密度 +6`);
  };

  const saveGame = () => {
    localStorage.setItem(
      "sweet-house-demo-save",
      JSON.stringify({ room, phase, served, bgm, sfx, windowMode, savedAt: new Date().toISOString() }),
    );
    notify("进度已保存到本机 · Slot 01");
  };

  const loadGame = () => {
    const raw = localStorage.getItem("sweet-house-demo-save");
    if (!raw) {
      notify("还没有存档，先在 House 里探索一下吧");
      return;
    }
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
    notify("Slot 01 已读取 · 欢迎回来");
  };

  const renderPanel = () => {
    if (!panel) return null;

    if (panel === "tasks") {
      return (
        <div className="panel-content task-list">
          <div className="focus-card">
            <span className="status-pip active" />
            <div><small>MAIN / 可服务时间</small><h3>为林默准备一杯热咖啡</h3><p>前往厨房，使用手冲咖啡台。甜度不要超过 30%。</p></div>
            <b>01</b>
          </div>
          {["寻找月白提到的旧唱片", "整理书房中被打乱的书", "检查明日访客预告"].map((item, index) => (
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
          <div className="contact-list">{guests.map((item) => <button key={item.id} className={guestId === item.id ? "selected" : ""} onClick={() => setGuestId(item.id)}><span className={`portrait mini ${item.color}`}>{item.name.slice(0, 1)}</span><div><b>{item.name}</b><small>{item.tag}</small></div><em>{item.affinity}%</em></button>)}</div>
          <div className="contact-profile"><span className={`portrait large ${guest.color}`}>{guest.name.slice(0, 1)}<i /></span><div><small>{guest.tag} / No. 0{guests.findIndex((item) => item.id === guest.id) + 1}</small><h3>{guest.name}</h3><p>“{guest.hint}”</p><div className="affinity"><span>信赖</span><i><u style={{ width: `${guest.affinity}%` }} /></i><b>{guest.affinity}</b></div><dl><div><dt>当前需求</dt><dd>{guest.need}</dd></div><div><dt>最近来访</dt><dd>2086.06.17 · 夜晚</dd></div></dl><button className="primary-button" onClick={() => { setPanel(null); setDialogue(true); }}>与 TA 交谈</button></div></div>
        </div>
      );
    }

    if (panel === "calendar") {
      return (
        <div className="panel-content calendar-layout">
          <div className="big-date"><small>2086 / JUNE</small><strong>17</strong><span>WEDNESDAY · {phases[phase].name}</span></div>
          <div className="calendar-grid">{Array.from({ length: 28 }, (_, index) => <button className={index === 16 ? "today" : index === 18 ? "event" : ""} key={index}><small>{["M","T","W","T","F","S","S"][index % 7]}</small>{index + 1}{index === 18 && <i />}</button>)}</div>
          <div className="schedule"><h3>今日安排</h3><div><time>15:00</time><span>墨墨来访 <small>普通事件</small></span></div><div className="special"><time>20:00</time><span>月白 · 未知请求 <small>特殊事件</small></span></div><button className="ghost-button" onClick={() => notify("明日：有 2 位普通访客，天气晴")}>查看明日预告</button></div>
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
          {journalTab === "log" ? <div className="log-pages"><article><small>06 / 17 · 雨转晴</small><h3>他们在灯亮起前到来</h3><p>林默说这间屋子闻起来像很久以前的夏天。月白没有回答，只是看着那台唱机。</p><span>关键词：旧唱片 / 第一次重逢</span></article><article><small>06 / 16 · 阴</small><h3>书架上的空位</h3><p>墨墨坚持那里原本放着一本蓝色封面的书。</p></article></div> : <div className="achievement-grid">{[["夜的主人","在深夜完成一次服务",true],["初次相识","录入 3 位访客",true],["家的轮廓","解锁全部房间",false],["无人知晓","发现特殊访客的秘密",false]].map(([title, desc, done], index) => <div className={done ? "done" : ""} key={String(title)}><span>{done ? "✓" : index + 1}</span><strong>{title}</strong><small>{desc}</small></div>)}</div>}
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
          <section><small>SAVE DATA</small><h3>Slot 01</h3><p>House LV.03 · WEEK 01 · {served.length}/3 委托推进</p><div className="button-pair"><button className="primary-button" onClick={saveGame}>保存进度</button><button className="ghost-button" onClick={loadGame}>读取存档</button></div></section>
          <section><small>DISPLAY</small><label>视窗模式<select value={windowMode} onChange={(event) => setWindowMode(event.target.value)}><option>无边框</option><option>全屏</option><option>窗口</option></select></label><label>分辨率<select defaultValue="2560 × 1440"><option>2560 × 1440</option><option>1920 × 1080</option><option>1600 × 900</option></select></label></section>
          <section><small>AUDIO</small><label>BGM <b>{bgm}</b><input type="range" min="0" max="100" value={bgm} onChange={(event) => setBgm(Number(event.target.value))} /></label><label>SFX <b>{sfx}</b><input type="range" min="0" max="100" value={sfx} onChange={(event) => setSfx(Number(event.target.value))} /></label></section>
          <footer><span>ESC 返回上一级</span><button onClick={() => notify("设置已应用")}>应用设置</button></footer>
        </div>
      );
    }

    return null;
  };

  return (
    <main className={`game-shell phase-${phases[phase].code} room-${room}`}>
      <div className="noise" />
      <div className="ambient-orb orb-one" /><div className="ambient-orb orb-two" />

      <header className="top-hud">
        <button className="brand-lockup" onClick={() => { setPanel(null); notify("已回到 House 主界面"); }}><span>SH</span><div><b>SWEET<br />HOUSE</b><small>MEMORY LODGE / 2086</small></div></button>
        <button className="time-card" onClick={() => openPanel("calendar")}><span className="live-dot" /><div><small>WED · JUN 17</small><strong>{phases[phase].time}</strong></div><em>{phases[phase].name} · 可服务时间</em></button>
        <div className="phase-switch" aria-label="切换时间氛围">{phases.map((item, index) => <button aria-label={item.name} className={phase === index ? "selected" : ""} key={item.code} onClick={() => { setPhase(index); notify(`时间氛围切换为${item.name}`); }}><span /></button>)}</div>
        <button className="currency" onClick={() => openPanel("market")}><small>HOUSE CREDIT</small><strong>◈ 2,480</strong><span>＋</span></button>
      </header>

      <aside className="guest-rail" aria-label="访客列表">
        <div className="rail-label"><small>VISITORS</small><strong>03</strong></div>
        {guests.map((item, index) => <button key={item.id} className={`${guestId === item.id ? "selected" : ""} ${served.includes(item.id) ? "served" : ""}`} onClick={() => { setGuestId(item.id); setDialogue(true); }}><span className={`portrait ${item.color}`}>{item.name.slice(0, 1)}<i /></span><div><small>0{index + 1}</small><b>{item.name}</b><em>{served.includes(item.id) ? "已推进" : item.status}</em></div></button>)}
        <button className="profile-chip" onClick={() => openPanel("profile")}><span>弈</span><div><small>HOUSE KEEPER</small><b>状态稳定 · 82%</b></div></button>
      </aside>

      <section className="house-stage" aria-label="House 场景">
        <div className="window"><span className="city c1" /><span className="city c2" /><span className="city c3" /><i className="moon" /><i className="rain rain-a" /><i className="rain rain-b" /></div>
        <div className="wall-art art-one"><span>NOCTURNE</span></div><div className="wall-art art-two" />
        <div className="lamp"><i /><span /></div>
        <div className="bookcase">{Array.from({ length: 18 }, (_, i) => <i key={i} />)}</div>
        <div className="plant"><i /><i /><i /><i /><span /></div>
        <div className="sofa"><i /><i /><span /></div>
        <div className="rug" />
        <div className="table"><i /><span /><b /></div>
        <div className="character char-left"><span className="head" /><span className="body" /><small>墨墨</small></div>
        <div className="character char-center"><span className="head" /><span className="body" /><small>弈</small></div>
        <div className="character char-right"><span className="head" /><span className="body" /><small>林默</small></div>
        <button className="stage-hotspot hotspot-device" onClick={() => openPanel("device")}><span>＋</span><div><b>{room === "kitchen" ? "手冲咖啡台" : room === "study" ? "旧书检索机" : "黑胶唱机"}</b><small>查看设备</small></div></button>
        <div className="stage-caption"><small>NOW VIEWING</small><strong>{currentRoom.name}</strong><span>{currentRoom.note}</span></div>
      </section>

      <aside className="right-dock" aria-label="功能菜单">
        {(["tasks","device","journal","contacts","calendar","inventory","settings"] as PanelKey[]).map((key) => <button key={key} className={panel === key ? "selected" : ""} onClick={() => openPanel(key)}><TinyIcon>{panelMeta[key].mark}</TinyIcon><span>{panelMeta[key].title.replace("与成就", "").replace("访客", "")}</span>{key === "tasks" && <em>3</em>}{key === "contacts" && <i />}</button>)}
      </aside>

      <nav className="room-nav" aria-label="房间切换">
        <div className="nav-title"><small>HOUSE MAP</small><span>← → 快速切换</span></div>
        {rooms.map((item) => <button className={room === item.id ? "selected" : ""} key={item.id} onClick={() => selectRoom(item.id)}><small>{item.short}</small><span className={`room-icon icon-${item.id}`}><i /><i /></span><strong>{item.name}</strong></button>)}
        <button className="locked-room" onClick={() => notify("仓库房间将在 House LV.04 解锁")}><small>LOCKED</small><span className="room-icon"><i /></span><strong>地下仓库</strong></button>
      </nav>

      <div className="quest-peek"><span>01</span><div><small>TRACKING</small><b>为林默准备热咖啡</b><i><u style={{ width: served.includes("lin") ? "100%" : "35%" }} /></i></div><button onClick={() => openPanel("tasks")}>查看</button></div>

      {panel && <div className="panel-scrim" onClick={goBack} />}
      <aside className={`system-panel ${panel ? "open" : ""}`} aria-hidden={!panel}>
        {panel && <><header><button className="back-button" onClick={goBack} aria-label="返回">←<small>ESC</small></button><div><small>{panelMeta[panel].eyebrow}</small><h2>{panelMeta[panel].title}</h2></div><span className="panel-mark">{panelMeta[panel].mark}</span></header>{renderPanel()}</>}
      </aside>

      {dialogue && <div className="dialogue-layer">
        <button className="dialogue-close" onClick={() => setDialogue(false)}>ESC · 结束交谈</button>
        <div className={`dialogue-portrait ${guest.color}`}><span>{guest.name.slice(0, 1)}</span><i /></div>
        <div className="dialogue-box"><header><small>{guest.tag}</small><strong>{guest.name}</strong><em>信赖 {guest.affinity}%</em></header><p>{served.includes(guest.id) ? "谢谢。这里好像比刚才更暖了一点……也许我会再想起些什么。" : guest.id === "lin" ? "可以麻烦你吗？今天想喝一点暖的东西。不要太甜——我想记住它原本的味道。" : guest.id === "yue" ? "唱机下面有一道很浅的划痕。你真的不记得，是谁留下的吗？" : "我在书架上留了一个空位。等你找到那本书，就放在那里吧。"}</p><div className="dialogue-actions"><button onClick={() => notify("已记录新的访客线索")}>追问线索</button><button className="primary-button" disabled={served.includes(guest.id)} onClick={serveGuest}>{served.includes(guest.id) ? "委托已推进" : "回应委托"}</button></div></div>
      </div>}

      {toast && <div className="toast"><span />{toast}</div>}
      <footer className="footer-note"><span>DEMO BUILD · UI/UX CONCEPT</span><span>ESC 返回 · ← → 切换房间</span></footer>
    </main>
  );
}
