"use client";

// 家具摆放模式：在起居室场景内就地编辑。家具切片与干净背景由脚本从 house-hub-v2.png 生成。
import { useEffect, useRef } from "react";

const SCENE_W = 1672;
const SCENE_H = 941;
const BG_SRC = "/house-hub-clean.png";

type Surface = "floor" | "table" | "wall";
type TableCfg = { cols: number; cellW: number; cellH: number; offX: number; surfaceH: number };
type Def = { id: string; name: string; surface: Surface; cols: number; rows: number; w: number; h: number; price: number; table?: TableCfg };

const DEFS: Def[] = [
  { id: "table", name: "圆木茶几", surface: "floor", cols: 4, rows: 2, w: 282, h: 184, price: 0, table: { cols: 3, cellW: 64, cellH: 56, offX: 50, surfaceH: 146 } },
  { id: "pouf", name: "黄绒蒲团", surface: "floor", cols: 3, rows: 1, w: 180, h: 100, price: 0 },
  { id: "vase", name: "白花花瓶", surface: "table", cols: 1, rows: 1, w: 117, h: 186, price: 0 },
  { id: "cups", name: "茶杯与书", surface: "table", cols: 1, rows: 1, w: 116, h: 84, price: 0 },
  { id: "lamp", name: "红罩台灯", surface: "table", cols: 1, rows: 1, w: 84, h: 112, price: 150 },
  { id: "picture", name: "山月挂画", surface: "wall", cols: 1, rows: 2, w: 86, h: 118, price: 0 },
  { id: "hangplant", name: "悬挂绿植", surface: "wall", cols: 2, rows: 3, w: 118, h: 162, price: 0 },
  { id: "bag", name: "帆布挂包", surface: "wall", cols: 1, rows: 2, w: 82, h: 138, price: 300 },
];
const SURF_NAME: Record<Surface, string> = { floor: "地面", table: "桌面", wall: "壁挂" };
const defById = (id: string) => DEFS.find((d) => d.id === id)!;

// 场景中不可占用的格子（沙发、人物、落地灯等画在背景里的物件）
const BLOCKED: Array<[number, number]> = [];
for (let c = 8; c <= 13; c++) for (let r = 0; r <= 3; r++) BLOCKED.push([c, r]);
BLOCKED.push([0, 0], [1, 0], [0, 1], [1, 1], [3, 0], [4, 0]);

type SavedPlacement = { defId: string; gridKey: string; col: number; row: number };
type SavedLayout = { placements: SavedPlacement[]; unlocked: string[]; credit: number };
let savedLayout: SavedLayout | null = null; // 模式退出后保留布局（页面刷新前有效）

const DEFAULT_LAYOUT: SavedLayout = {
  placements: [
    { defId: "table", gridKey: "floor", col: 4, row: 2 },
    { defId: "pouf", gridKey: "floor", col: 1, row: 2 },
    { defId: "picture", gridKey: "wallL", col: 4, row: 0 },
    { defId: "hangplant", gridKey: "wallR", col: 1, row: 0 },
    { defId: "vase", gridKey: "tbl@0", col: 1, row: 0 },
    { defId: "cups", gridKey: "tbl@0", col: 2, row: 0 },
  ],
  unlocked: ["table", "pouf", "vase", "cups", "picture", "hangplant"],
  credit: 2480,
};

type Grid = {
  id: string; surface: Surface; cols: number; rows: number; cellW: number; cellH: number;
  x: number; y: number; layer: HTMLDivElement; cells: HTMLDivElement[]; occ: Map<string, string>; hostId?: string;
};
type Item = { id: string; defId: string; gridId: string; col: number; row: number; el: HTMLDivElement };

function mount(root: HTMLDivElement, requestExit: () => void) {
  root.innerHTML = `
    <div class="fe-stage"><img class="fe-bg" src="${BG_SRC}" alt="起居室" draggable="false"><div class="fe-grids"></div><div class="fe-items"></div></div>
    <header class="fe-top">
      <div class="fe-title"><small>FURNITURE MODE</small><strong>家具摆放</strong><span>拖拽摆放 · 拖回下方收纳 · 双击快速收纳</span></div>
      <div class="fe-tools">
        <span class="fe-credit">◈ <b data-fe="credit">0</b></span>
        <button class="fe-btn" data-fe="toggle" aria-pressed="false">显示网格</button>
        <button class="fe-btn fe-done" data-fe="exit">完成 · ESC</button>
      </div>
    </header>
    <div class="fe-inv" data-fe="inv"></div>
    <div class="fe-pop" data-fe="pop"><b data-fe="pop-name"></b><p data-fe="pop-desc"></p><div><button class="fe-btn fe-primary" data-fe="pop-yes">解锁</button><button class="fe-btn" data-fe="pop-no">取消</button></div></div>
    <div class="fe-toast" data-fe="toast" role="status"></div>
    <div class="fe-ghost" data-fe="ghost" hidden></div>`;

  const $ = <T extends HTMLElement>(k: string) => root.querySelector(`[data-fe="${k}"]`) as T;
  const $stage = root.querySelector(".fe-stage") as HTMLDivElement;
  const $gridsBox = root.querySelector(".fe-grids") as HTMLDivElement;
  const $itemsBox = root.querySelector(".fe-items") as HTMLDivElement;
  const $inv = $<HTMLDivElement>("inv");
  const $ghost = $<HTMLDivElement>("ghost");
  const $pop = $<HTMLDivElement>("pop");
  const $toastEl = $<HTMLDivElement>("toast");

  const grids = new Map<string, Grid>();
  const items = new Map<string, Item>();
  const state = { credit: 0, unlocked: new Set<string>(), nextId: 1 };
  let stageScale = 1;
  let toastTimer = 0;
  let entryFlashTimer = 0;
  let popDef: Def | null = null;

  /* ── 网格 ── */
  const cellKey = (c: number, r: number) => c + "," + r;
  function makeGrid(id: string, surface: Surface, cols: number, rows: number, cellW: number, cellH: number, x: number, y: number): Grid {
    const layer = document.createElement("div");
    layer.className = "fe-grid fe-grid-" + surface;
    layer.style.cssText = `left:${x}px;top:${y}px;width:${cols * cellW}px;height:${rows * cellH}px;`;
    const cells: HTMLDivElement[] = [];
    for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) {
      const el = document.createElement("div");
      el.className = "fe-cell";
      el.style.cssText = `left:${c * cellW}px;top:${r * cellH}px;width:${cellW}px;height:${cellH}px;`;
      layer.appendChild(el); cells.push(el);
    }
    $gridsBox.appendChild(layer);
    const g: Grid = { id, surface, cols, rows, cellW, cellH, x, y, layer, cells, occ: new Map() };
    grids.set(id, g);
    return g;
  }
  function destroyGrid(id: string) { const g = grids.get(id); if (g) { g.layer.remove(); grids.delete(id); } }
  function footprintFree(g: Grid, col: number, row: number, def: Def, ignoreId: string | null) {
    if (col < 0 || row < 0 || col + def.cols > g.cols || row + def.rows > g.rows) return false;
    for (let r = row; r < row + def.rows; r++) for (let c = col; c < col + def.cols; c++) {
      const o = g.occ.get(cellKey(c, r));
      if (o && o !== ignoreId) return false;
    }
    return true;
  }
  function setOcc(g: Grid, col: number, row: number, def: Def, id: string, on: boolean) {
    for (let r = row; r < row + def.rows; r++) for (let c = col; c < col + def.cols; c++) {
      if (on) g.occ.set(cellKey(c, r), id); else g.occ.delete(cellKey(c, r));
    }
  }
  function refreshOcc() {
    grids.forEach((g) => g.cells.forEach((el, i) => {
      const o = g.occ.get(cellKey(i % g.cols, Math.floor(i / g.cols)));
      el.classList.toggle("blk", o === "__scene__");
      el.classList.toggle("occ", !!o && o !== "__scene__");
      el.classList.remove("ok", "bad");
    }));
  }
  function clearPreview() { grids.forEach((g) => g.cells.forEach((el) => el.classList.remove("ok", "bad"))); }

  /* ── 家具实例 ── */
  const tableGridId = (itemId: string) => "tbl_" + itemId;
  const childrenOf = (host: Item) => [...items.values()].filter((it) => it.gridId === tableGridId(host.id));
  function anchorOf(item: Item): { left: number; bottom: number; z: number } {
    const g = grids.get(item.gridId)!;
    const def = defById(item.defId);
    const left = g.x + item.col * g.cellW + (def.cols * g.cellW - def.w) / 2;
    if (g.surface === "floor") { const br = item.row + def.rows; return { left, bottom: g.y + br * g.cellH, z: 100 + br * 10 }; }
    if (g.surface === "wall") return { left, bottom: g.y + (item.row + def.rows) * g.cellH, z: 20 + item.row + def.rows };
    const host = items.get(g.hostId!)!;
    return { left, bottom: g.y + g.cellH, z: anchorOf(host).z + 3 };
  }
  function layoutItem(item: Item) {
    const def = defById(item.defId);
    const a = anchorOf(item);
    item.el.style.left = a.left + "px";
    item.el.style.top = a.bottom - def.h + "px";
    item.el.style.zIndex = String(a.z);
  }
  function syncTableGrid(host: Item) {
    const def = defById(host.defId);
    if (!def.table) return;
    const g = grids.get(tableGridId(host.id))!;
    const a = anchorOf(host);
    g.x = a.left + def.table.offX; g.y = a.bottom - def.table.surfaceH - def.table.cellH;
    g.layer.style.left = g.x + "px"; g.layer.style.top = g.y + "px";
    childrenOf(host).forEach(layoutItem);
  }
  function placeItem(defId: string, gridId: string, col: number, row: number, silent?: boolean): Item {
    const def = defById(defId);
    const g = grids.get(gridId)!;
    const id = "it" + state.nextId++;
    const el = document.createElement("div");
    el.className = "fe-item";
    el.dataset.id = id;
    el.innerHTML = `<img src="/furniture/${defId}.png" width="${def.w}" height="${def.h}" alt="${def.name}" draggable="false">`;
    $itemsBox.appendChild(el);
    const item: Item = { id, defId, gridId, col, row, el };
    items.set(id, item);
    setOcc(g, col, row, def, id, true);
    if (def.table) { makeGrid(tableGridId(id), "table", def.table.cols, 1, def.table.cellW, def.table.cellH, 0, 0).hostId = id; }
    layoutItem(item);
    if (def.table) syncTableGrid(item);
    if (!silent) { el.classList.add("pop"); window.setTimeout(() => el.classList.remove("pop"), 320); }
    refreshOcc(); syncGridVisible();
    return item;
  }
  function storeItem(item: Item, silent?: boolean) {
    const def = defById(item.defId);
    const names = [def.name];
    if (def.table) {
      childrenOf(item).forEach((ch) => { names.push(defById(ch.defId).name); storeItem(ch, true); });
      destroyGrid(tableGridId(item.id));
    }
    const g = grids.get(item.gridId);
    if (g) setOcc(g, item.col, item.row, def, item.id, false);
    item.el.remove();
    items.delete(item.id);
    refreshOcc(); renderInv(); syncGridVisible();
    if (!silent) toast("已收纳：" + names.join("、"));
  }
  function moveItem(item: Item, gridId: string, col: number, row: number) {
    const def = defById(item.defId);
    const from = grids.get(item.gridId);
    if (from) setOcc(from, item.col, item.row, def, item.id, false);
    item.gridId = gridId; item.col = col; item.row = row;
    setOcc(grids.get(gridId)!, col, row, def, item.id, true);
    layoutItem(item);
    if (def.table) syncTableGrid(item);
    item.el.classList.add("pop"); window.setTimeout(() => item.el.classList.remove("pop"), 320);
    refreshOcc();
  }
  const placedCount = (defId: string) => [...items.values()].filter((it) => it.defId === defId).length;

  /* ── 收纳栏 ── */
  function renderInv() {
    $inv.innerHTML = "";
    (["floor", "table", "wall"] as Surface[]).forEach((surface) => {
      const grp = document.createElement("div");
      grp.className = "fe-inv-group";
      grp.innerHTML = `<label>${SURF_NAME[surface]}</label><div class="fe-inv-row"></div>`;
      const row = grp.querySelector(".fe-inv-row") as HTMLDivElement;
      DEFS.filter((d) => d.surface === surface).forEach((def) => {
        const locked = !state.unlocked.has(def.id);
        const avail = locked ? 0 : 1 - placedCount(def.id);
        const slot = document.createElement("div");
        slot.className = "fe-slot" + (locked ? " locked" : avail <= 0 ? " empty" : "");
        slot.dataset.def = def.id;
        const ts = Math.min(56 / def.w, 52 / def.h, 1);
        slot.innerHTML = `<span class="fe-thumb"><img src="/furniture/${def.id}.png" style="width:${Math.round(def.w * ts)}px;height:${Math.round(def.h * ts)}px" alt="" draggable="false"></span><small>${def.name}</small>` +
          (locked ? `<span class="fe-lock">🔒<i>◈ ${def.price}</i></span>` : avail <= 0 ? `<em>已摆放</em>` : "");
        row.appendChild(slot);
      });
      $inv.appendChild(grp);
    });
  }
  function renderCredit() { $<HTMLElement>("credit").textContent = state.credit.toLocaleString("en-US"); }

  /* ── 网格显隐 ── */
  function showGridsFor(surface: Surface | null) {
    grids.forEach((g) => g.layer.classList.toggle("show", surface !== null && g.surface === surface));
  }
  function syncGridVisible() {
    if (drag) return;
    const on = $<HTMLButtonElement>("toggle").getAttribute("aria-pressed") === "true";
    grids.forEach((g) => g.layer.classList.toggle("show", on));
  }

  /* ── 拖拽 ── */
  type Drag = { def: Def; source: "inv" | "placed"; item: Item | null; cand: { g: Grid; col: number; row: number; ok: boolean } | null; overInv: boolean };
  let drag: Drag | null = null;

  function stagePoint(ev: PointerEvent | MouseEvent) {
    const r = $stage.getBoundingClientRect();
    return { x: (ev.clientX - r.left) / stageScale, y: (ev.clientY - r.top) / stageScale };
  }
  function hitItem(ev: PointerEvent | MouseEvent): Item | null {
    const p = stagePoint(ev);
    let hit: Item | null = null, hitZ = -1;
    items.forEach((it) => {
      const def = defById(it.defId);
      const a = anchorOf(it);
      if (p.x >= a.left && p.x <= a.left + def.w && p.y >= a.bottom - def.h && p.y <= a.bottom && a.z > hitZ) { hit = it; hitZ = a.z; }
    });
    return hit;
  }
  function beginDrag(ev: PointerEvent, def: Def, source: "inv" | "placed", item: Item | null) {
    drag = { def, source, item, cand: null, overInv: false };
    $ghost.innerHTML = `<img src="/furniture/${def.id}.png" width="${def.w}" height="${def.h}" alt="" draggable="false">`;
    $ghost.hidden = false;
    if (item && def.table) {
      const a = anchorOf(item);
      childrenOf(item).forEach((ch) => {
        const cd = defById(ch.defId);
        const ca = anchorOf(ch);
        const w = document.createElement("span");
        w.className = "fe-ghost-child";
        w.innerHTML = `<img src="/furniture/${ch.defId}.png" width="${cd.w}" height="${cd.h}" alt="" draggable="false">`;
        w.style.left = ca.left - a.left + "px";
        w.style.top = ca.bottom - cd.h - (a.bottom - def.h) + "px";
        $ghost.appendChild(w);
        ch.el.style.visibility = "hidden";
      });
      grids.get(tableGridId(item.id))!.layer.style.visibility = "hidden";
    }
    if (item) {
      setOcc(grids.get(item.gridId)!, item.col, item.row, def, item.id, false);
      item.el.style.visibility = "hidden";
    }
    showGridsFor(def.surface);
    refreshOcc();
    moveDrag(ev);
  }
  function moveDrag(ev: PointerEvent) {
    const d = drag!;
    const def = d.def;
    const p = stagePoint(ev);
    const grabX = def.w / 2, grabY = def.h * 0.75;
    const wantL = p.x - grabX, wantB = p.y - grabY + def.h;

    const ir = $inv.getBoundingClientRect();
    d.overInv = d.source === "placed" && ev.clientX >= ir.left && ev.clientX <= ir.right && ev.clientY >= ir.top && ev.clientY <= ir.bottom;
    $inv.classList.toggle("drop-hint", d.overInv);

    clearPreview();
    d.cand = null;
    if (!d.overInv) {
      const MARGIN = 50;
      let best: { g: Grid; dist: number } | null = null;
      grids.forEach((g) => {
        if (g.surface !== def.surface) return;
        const w = g.cols * g.cellW, h = g.rows * g.cellH;
        if (p.x < g.x - MARGIN || p.x > g.x + w + MARGIN || p.y < g.y - MARGIN || p.y > g.y + h + MARGIN) return;
        const cx = Math.max(g.x, Math.min(g.x + w, p.x)), cy = Math.max(g.y, Math.min(g.y + h, p.y));
        const dist = (p.x - cx) ** 2 + (p.y - cy) ** 2;
        if (!best || dist < best.dist) best = { g, dist };
      });
      if (best) {
        const g = (best as { g: Grid }).g;
        const footW = def.cols * g.cellW, footH = def.rows * g.cellH;
        const col = Math.max(0, Math.min(g.cols - def.cols, Math.round((wantL + (def.w - footW) / 2 - g.x) / g.cellW)));
        const row = Math.max(0, Math.min(g.rows - def.rows, Math.round((wantB - footH - g.y) / g.cellH)));
        const ok = footprintFree(g, col, row, def, d.item ? d.item.id : null);
        d.cand = { g, col, row, ok };
        for (let r = row; r < row + def.rows; r++) for (let c = col; c < col + def.cols; c++)
          g.cells[r * g.cols + c].classList.add(ok ? "ok" : "bad");
      }
    }

    let gl: number, gt: number;
    if (d.cand) {
      const { g, col, row } = d.cand;
      gl = g.x + col * g.cellW + (def.cols * g.cellW - def.w) / 2;
      const bottom = g.surface === "table" ? g.y + g.cellH : g.y + (row + def.rows) * g.cellH;
      gt = bottom - def.h;
    } else { gl = wantL; gt = wantB - def.h; }
    const rr = $stage.getBoundingClientRect();
    $ghost.style.left = rr.left + gl * stageScale + "px";
    $ghost.style.top = rr.top + gt * stageScale + "px";
    $ghost.style.transform = `scale(${stageScale * (d.overInv ? 0.55 : 1)})`;
    $ghost.classList.toggle("bad", !d.overInv && !(d.cand && d.cand.ok));
  }
  function endDrag(commit: boolean) {
    const d = drag;
    if (!d) return;
    drag = null;
    $inv.classList.remove("drop-hint");
    $ghost.hidden = true; $ghost.innerHTML = ""; $ghost.style.transform = "";
    clearPreview();
    const restoreVisibility = () => {
      if (!d.item) return;
      d.item.el.style.visibility = "";
      if (d.def.table) {
        grids.get(tableGridId(d.item.id))!.layer.style.visibility = "";
        childrenOf(d.item).forEach((ch) => (ch.el.style.visibility = ""));
      }
    };
    const restoreOcc = () => { if (d.item) setOcc(grids.get(d.item.gridId)!, d.item.col, d.item.row, d.def, d.item.id, true); };

    if (commit && d.overInv && d.item) {
      restoreVisibility(); restoreOcc();
      storeItem(d.item);
    } else if (commit && d.cand && d.cand.ok) {
      if (d.item) { restoreVisibility(); moveItem(d.item, d.cand.g.id, d.cand.col, d.cand.row); }
      else { placeItem(d.def.id, d.cand.g.id, d.cand.col, d.cand.row); renderInv(); }
    } else {
      if (commit && !d.item) {
        const hasSurface = [...grids.values()].some((g) => g.surface === d.def.surface);
        if (d.def.surface === "table" && !hasSurface) toast("需要先摆放「圆木茶几」才能放置桌面家具");
        else if (d.cand && !d.cand.ok) toast("该位置无法摆放");
      }
      restoreVisibility(); restoreOcc();
    }
    refreshOcc(); syncGridVisible();
  }

  /* ── 解锁 ── */
  function openUnlock(slot: HTMLElement, def: Def) {
    popDef = def;
    $<HTMLElement>("pop-name").textContent = `解锁「${def.name}」`;
    const enough = state.credit >= def.price;
    $<HTMLElement>("pop-desc").textContent = enough ? `花费 ◈ ${def.price}（当前 ◈ ${state.credit.toLocaleString("en-US")}）` : `需要 ◈ ${def.price}，当前只有 ◈ ${state.credit.toLocaleString("en-US")}`;
    ($<HTMLButtonElement>("pop-yes")).disabled = !enough;
    const r = slot.getBoundingClientRect();
    $pop.classList.add("show");
    const pw = $pop.offsetWidth;
    $pop.style.left = Math.max(8, Math.min(window.innerWidth - pw - 8, r.left + r.width / 2 - pw / 2)) + "px";
    $pop.style.top = Math.max(8, r.top - $pop.offsetHeight - 10) + "px";
  }
  function closeUnlock() { $pop.classList.remove("show"); popDef = null; }

  /* ── 其它 ── */
  function toast(msg: string) {
    $toastEl.textContent = msg;
    $toastEl.classList.add("show");
    window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(() => $toastEl.classList.remove("show"), 2200);
  }
  function fitStage() {
    stageScale = Math.min(window.innerWidth / SCENE_W, window.innerHeight / SCENE_H);
    $stage.style.transform = `scale(${stageScale})`;
    $stage.style.left = (window.innerWidth - SCENE_W * stageScale) / 2 + "px";
    $stage.style.top = (window.innerHeight - SCENE_H * stageScale) / 2 + "px";
  }

  /* ── 事件 ── */
  const onStageDown = (ev: PointerEvent) => {
    if (drag || (ev.target as HTMLElement).closest(".fe-top,.fe-inv,.fe-pop")) return;
    const hit = hitItem(ev);
    if (hit) { ev.preventDefault(); beginDrag(ev, defById(hit.defId), "placed", hit); }
  };
  const onInvDown = (ev: PointerEvent) => {
    if (drag) return;
    const slot = (ev.target as HTMLElement).closest(".fe-slot") as HTMLElement | null;
    if (!slot) return;
    const def = defById(slot.dataset.def!);
    if (!state.unlocked.has(def.id)) { openUnlock(slot, def); return; }
    if (1 - placedCount(def.id) <= 0) return;
    ev.preventDefault();
    beginDrag(ev, def, "inv", null);
  };
  const onMove = (ev: PointerEvent) => { if (drag) moveDrag(ev); };
  const onUp = (ev: PointerEvent) => { if (drag) { moveDrag(ev); endDrag(true); } };
  const onDbl = (ev: MouseEvent) => {
    if ((ev.target as HTMLElement).closest(".fe-top,.fe-inv,.fe-pop")) return;
    const hit = hitItem(ev);
    if (hit) storeItem(hit);
  };
  const onKey = (ev: KeyboardEvent) => {
    if (["ArrowLeft", "ArrowRight"].includes(ev.key)) { ev.stopPropagation(); return; }
    if (ev.key !== "Escape") return;
    ev.stopPropagation();
    if ($pop.classList.contains("show")) { closeUnlock(); return; }
    if (drag) { endDrag(false); return; }
    requestExit();
  };
  const onDocDown = (ev: PointerEvent) => {
    if ($pop.classList.contains("show") && !$pop.contains(ev.target as Node) && !(ev.target as HTMLElement).closest(".fe-slot.locked")) closeUnlock();
  };

  root.addEventListener("pointerdown", onStageDown);
  $inv.addEventListener("pointerdown", onInvDown);
  document.addEventListener("pointermove", onMove);
  document.addEventListener("pointerup", onUp);
  root.addEventListener("dblclick", onDbl);
  window.addEventListener("keydown", onKey, true);
  document.addEventListener("pointerdown", onDocDown, true);
  window.addEventListener("resize", fitStage);
  $<HTMLButtonElement>("exit").addEventListener("click", requestExit);
  $<HTMLButtonElement>("toggle").addEventListener("click", (ev) => {
    const b = ev.currentTarget as HTMLButtonElement;
    b.setAttribute("aria-pressed", b.getAttribute("aria-pressed") === "true" ? "false" : "true");
    syncGridVisible();
  });
  $<HTMLButtonElement>("pop-no").addEventListener("click", closeUnlock);
  $<HTMLButtonElement>("pop-yes").addEventListener("click", () => {
    if (!popDef || state.credit < popDef.price) return;
    state.credit -= popDef.price;
    state.unlocked.add(popDef.id);
    const name = popDef.name;
    renderCredit(); closeUnlock(); renderInv();
    toast("已解锁：" + name);
  });

  /* ── 初始化 ── */
  makeGrid("floor", "floor", 14, 4, 60, 45, 400, 610);
  makeGrid("wallL", "wall", 6, 3, 60, 60, 90, 290);
  makeGrid("wallR", "wall", 4, 3, 60, 60, 1290, 260);
  const floor = grids.get("floor")!;
  BLOCKED.forEach(([c, r]) => floor.occ.set(cellKey(c, r), "__scene__"));

  const layout = savedLayout ?? DEFAULT_LAYOUT;
  state.credit = layout.credit;
  state.unlocked = new Set(layout.unlocked);
  const placedByIndex: (Item | null)[] = [];
  layout.placements.forEach((pl) => {
    let gridId = pl.gridKey;
    if (pl.gridKey.startsWith("tbl@")) {
      const host = placedByIndex[Number(pl.gridKey.slice(4))];
      if (!host) { placedByIndex.push(null); return; }
      gridId = tableGridId(host.id);
    }
    placedByIndex.push(grids.has(gridId) ? placeItem(pl.defId, gridId, pl.col, pl.row, true) : null);
  });
  renderCredit(); renderInv(); refreshOcc(); fitStage();

  // 进入时短暂显示全部网格，提示可编辑区域
  grids.forEach((g) => g.layer.classList.add("show"));
  entryFlashTimer = window.setTimeout(syncGridVisible, 1600);

  return () => {
    // 序列化布局：先存基础表面上的家具，再存桌面家具（记录宿主序号）
    const base = [...items.values()].filter((it) => !it.gridId.startsWith("tbl_"));
    const placements: SavedPlacement[] = base.map((it) => ({ defId: it.defId, gridKey: it.gridId, col: it.col, row: it.row }));
    [...items.values()].filter((it) => it.gridId.startsWith("tbl_")).forEach((it) => {
      const host = items.get(grids.get(it.gridId)!.hostId!)!;
      placements.push({ defId: it.defId, gridKey: "tbl@" + base.indexOf(host), col: it.col, row: it.row });
    });
    savedLayout = { placements, unlocked: [...state.unlocked], credit: state.credit };

    window.clearTimeout(toastTimer);
    window.clearTimeout(entryFlashTimer);
    root.removeEventListener("pointerdown", onStageDown);
    $inv.removeEventListener("pointerdown", onInvDown);
    document.removeEventListener("pointermove", onMove);
    document.removeEventListener("pointerup", onUp);
    root.removeEventListener("dblclick", onDbl);
    window.removeEventListener("keydown", onKey, true);
    document.removeEventListener("pointerdown", onDocDown, true);
    window.removeEventListener("resize", fitStage);
    root.innerHTML = "";
  };
}

export default function FurnitureEditor({ onExit }: { onExit: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const exitRef = useRef(onExit);
  exitRef.current = onExit;
  useEffect(() => mount(ref.current!, () => exitRef.current()), []);
  return <div className="fe-root" ref={ref} />;
}
