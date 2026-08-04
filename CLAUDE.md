# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

"MasterPotion" (repo name 2026MasterHouse) — a Unity prototype of a node-graph factory/automation game. Players place node cards (resource producers, processors, storage), then drag links between typed ports to move resources, similar to UE Blueprint pins. All code lives in the `MasterPotion` namespace under `Assets/Scripts/`.

- Unity **2022.3.62f3** with URP 14 (2D). Single scene: `Assets/Scenes/SampleScene.unity`.
- Code comments, UI strings, and commit messages are in **Simplified Chinese** — keep that convention.
- No test assemblies, build scripts, or CI exist. All development/compilation happens through the Unity Editor.

## Development Documentation Discipline

Before changing code, resources, scenes, packages, or ProjectSettings, read and update `Docs/DEVELOPMENT.md`. Update its active work item whenever scope, implementation approach, validation status, or blockers change.

When a task exposes a reusable failure mode or repeated source of delay, add a structured entry to `Docs/RETROSPECTIVE.md` before marking the task complete. Static compilation must never be reported as Unity runtime verification.

## Running the Game

节点玩法仍主要由运行时代码生成；局外 UI 已采用 Prefab 视图 + 代码控制器：

- 可编辑局外 UI Prefab：`Assets/Resources/OutGameUI/Prefabs/`
- Prefab 引用组件：`Assets/Scripts/UI/OutGameTitleView.cs`、`OutGamePaperView.cs`、`OutGameSaveSlotView.cs`、`OutGameHubView.cs`、`OutGameSystemPanelView.cs`
- 页面逻辑控制器：`Assets/Scripts/UI/OutGameUI.cs`
- 首次缺失资产生成器：`Assets/Editor/OutGameUIPrefabGenerator.cs`

调整局外 UI 布局时只修改 Prefab，禁止把坐标重新写回控制器。生成器只自动补齐缺失资产，不覆盖手调 Prefab；菜单中的“Rebuild Default Prefabs”会覆盖布局，必须明确确认后才能使用。详见 `Docs/PREFAB_UI_GUIDE.md`。

Prefab 粒度以“一个完整界面一个 Prefab”为准：存档、画廊、设置、退出各自独立；共同纸张风格只通过 View 基类和生成器默认值复用，不能让多个正式界面共用一个运行时空壳再由代码生成内部布局。

复杂页面内部的稳定区域和重复项继续使用 Nested Prefab：House HUD 的 `Hub*.prefab` 是组件真值，`HouseHubPage.prefab` 只负责组合位置；运行时代码只更新数据、状态和事件，不销毁重建已有 HUD 布局。

任何从 `OutGameUIFactory.Button()` 迁移到 Prefab 的按钮，都必须同时保留 `OutGameTweenButton`；Prefab 验收除了点击逻辑，还必须验证 Hover、Press 和键盘选中反馈。迁移器只能补行为组件，不得为了恢复动效重建或覆盖手调布局。

所有需要挂载到 GameObject/Prefab 的 `MonoBehaviour` 与 `BaseMeshEffect` 必须独占一个与类名完全一致的 `.cs` 文件；禁止把多个可序列化组件塞进同一个脚本文件，否则 Unity 域重载后会产生 Missing Script。

节点玩法启动流程：

1. Editor menu **MasterPotion → 1. 创建示例数据**: generates all ScriptableObject assets into `Assets/GameData/` (resources, recipes, node defs, `GameConfig`). Implemented in `Assets/Scripts/Editor/DemoSetupUtility.cs`.
2. Editor menu **MasterPotion → 2. 搭建演示场景**: wires the scene (camera + a `GameRoot` object holding `SimulationManager`, `LinkManager`, `InteractionController`, `PlacementController`, `BoardEditController`, `BoardGrid`, `Bootstrap` with preset nodes).
3. Enter Play mode. `Bootstrap.Start()` builds the toolbar UI and spawns preset resource nodes (snapped onto the board).

Editor menu **MasterPotion → 节点编辑器** (`Assets/Scripts/Editor/NodeEditorWindow.cs`) authors new `NodeDef` assets: name, color, grid size, node kind (resource/processor/storage), production rates, recipes (with an inline `RecipeDef` creator), storage whitelist; pins are always derived from that data. It can also load an existing `NodeDef` for editing and optionally registers the asset into `GameConfig.buildableNodes`.

## Architecture

Five layers, one data flow: ScriptableObject defs → board grid placement → runtime nodes → simulation ticks → procedural visuals.

### Data (`Assets/Scripts/Data/`) — static definitions
ScriptableObjects only, no behavior. `NodeDef` is abstract with three subclasses: `ResourceNodeDef` (produces on a timer), `ProcessorNodeDef` (recipes + buffer caps), `StorageNodeDef` (resource whitelist, unlimited capacity). `NodeDef.gridSize` (`Vector2Int`) is the card footprint in board cells; world size = 1 unit per cell via `WorldSize`. `RecipeDef` holds input/output `ResourceAmount` lists. `GameConfig` sets the global link transfer interval and the buildable-node toolbar list. Asset instances live in `Assets/GameData/`.

### Grid (`Assets/Scripts/Grid/`) — the board ("画布")
- `BoardGrid` (singleton): a `HashSet<Vector2Int>` of 1×1 cells (cell (x,y) covers world rect [x,x+1)×[y,y+1)) plus a node-occupancy map. Everything placed must satisfy `CanPlace` (footprint fully on cells + no node overlap). Any cell/occupancy change bumps the static `BoardGrid.Version`, which is what triggers link re-routing.
- `LinkRouter`: 4-directional A* with a turn penalty over free (on-board, unoccupied) cells; returns the polyline through cell centers or `null` when no route exists.
- `BoardEditController` (singleton): runtime board editing mode (toolbar toggle). Left click/drag paints cells — starting on an empty cell adds, starting on an existing cell removes; occupied cells refuse removal. Mutually exclusive with placement mode.

### Sim (`Assets/Scripts/Sim/`) — the heartbeat
- `SimulationManager` holds **static** `Nodes`/`Links` lists and calls `SimTick(dt)` on each every frame. Nodes and links self-register in `OnEnable`/`OnDisable` — nothing else should touch these lists.
- `NodeBase` defines the transfer contract every node implements: target side `CanAcceptInput`/`ReceiveInput`, source side `HasOutput`/`TakeOutput` (all quantities are 1 item at a time), plus a protected `outputBuffer` (`ResourceBuffer`). `SetGridPlacement(origin)` is the only way to position a node: it moves the transform and registers occupancy with `BoardGrid` (repeat calls = move; occupancy released in `OnDisable`).
- `Link` moves **1 item per interval** from its `From` port's node to its `To` port's node. Its polyline comes from `LinkRouter` and is cached, re-routing lazily in `LateUpdate` when `BoardGrid.Version` or an endpoint moved. No route → drawn red and transfer suspended; source empty/target refusing → gray (blocked) with the timer kept primed.
- `LinkManager` (singleton) validates and creates/deletes links. Connection rules: opposite port directions, same `ResourceDef`, different nodes, no duplicates, and a route must exist on the board. After any link change it calls `OnConnectionsChanged()` on both nodes.
- `ProcessorNode` recipe selection is the subtle part: the active recipe is chosen by **exact set-match** between the resource types of *connected* input ports and a recipe's input types. Zero matches or multiple matches (配方冲突) → no active recipe → the node refuses all input, which blocks incoming links. Switching recipes mid-craft refunds consumed inputs.
- `StorageNode` uses its `outputBuffer` as the inventory itself, so received items are immediately available to outgoing links (pass-through).

### View (`Assets/Scripts/View/`) — 100% procedural visuals
No prefabs, no imported art. `NodeFactory.CreateNodeAt(def, gridOrigin)` builds the whole card (background, header, info `TextMesh`, diamond port pins, progress bar) from a `NodeDef` at a board cell origin — callers must check `BoardGrid.CanPlace` first. `VisualAssets` lazily creates the shared 1x1 white sprite, unlit material, and built-in font. `SortingOrders` centralizes all render ordering constants.

**Adding a new node type requires touching:** a new `NodeDef` subclass (Data), a new `NodeBase` subclass (Sim), and two switches in `NodeFactory` (`CreateNodeAt` component selection and `BuildPorts` port derivation). Ports are derived automatically from the def (e.g., processor ports = union of all recipe inputs/outputs).

### Interaction & UI
- `InteractionController`: all world-space mouse input via `Physics2D.OverlapPointAll`, priority port > card > link. Drag from port creates a link; drag card moves the node cell-by-cell (only ever landing on `CanPlace`-valid origins); double-click deletes a link. It defers to `PlacementController` while placing and to `BoardEditController` while board-editing.
- `PlacementController` (singleton): ghost snaps to the node's cell footprint, tinted red when the spot is invalid; clicks only place on valid spots. Sets `JustPlacedFrame` so `InteractionController` skips the same-frame click.
- `PaletteUI` builds the uGUI toolbar at runtime from `GameConfig.buildableNodes` plus the board-edit toggle button; `Bootstrap` is the scene entry point (also backfills `BoardGrid`/`BoardEditController` for old scenes).
