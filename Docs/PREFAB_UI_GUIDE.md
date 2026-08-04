# 局外 UI Prefab 调整指南

## 目标

局外 UI 使用“Prefab 管布局，C# 管逻辑”的分层：位置、尺寸、锚点、字号、颜色和层级在 Prefab 中调整；按钮事件、存档数据、页面切换和 DOTween 由 `OutGameUI` 在运行时绑定。

## Prefab 目录

`Assets/Resources/OutGameUI/Prefabs/`

| Prefab | 用途 | 主要可调内容 |
| --- | --- | --- |
| `TitlePage.prefab` | 标题与主菜单 | 背景、遮罩、菜单位置、6 个按钮、状态与提示 |
| `SavePage.prefab` | 新游戏/读取存档完整界面 | 标题区、说明、返回按钮、三个存档位及整体排版 |
| `GalleryPage.prefab` | 画廊完整界面 | 日志/成就页签、日志卡片、成就卡片 |
| `SettingsPage.prefab` | 标题设置完整界面 | 界面与存档区、保存/读取按钮、游戏性 Toggle |
| `ExitPage.prefab` | 退出确认完整界面 | 页面说明、返回按钮、退出确认按钮 |
| `PaperPage.prefab` | 旧版公共纸张外壳 | 仅作为资源缺失时的兼容回退，不再承载正式页面布局 |
| `SaveSlot.prefab` | 单个存档位 | 编号、状态、信息、操作按钮的内部排版 |
| `HouseHubPage.prefab` | House 主界面外壳 | Scene、Chrome、Modal 三层及页脚 |
| `SystemPanel.prefab` | 右侧功能面板外壳 | 遮罩、面板宽度、Header 与 Content 区域 |

## House HUD 组件 Prefab

`HouseHubPage.prefab` 会嵌套以下组件，既可以单独打开组件 Prefab 调整，也可以在页面 Prefab 中调整实例位置：

| Prefab | 对应区域 |
| --- | --- |
| `HubTopBar.prefab` | 顶部日期、信用点、品牌、欢迎语和设置入口 |
| `HubTaskCard.prefab` | 左上当前访客任务 |
| `HubGuestRail.prefab` | 左侧访客事件列表容器 |
| `HubGuestCard.prefab` | 单个可复用访客事件条目 |
| `HubRightDock.prefab` | 右侧 House 菜单容器 |
| `HubDockButton.prefab` | 单个右侧菜单按钮 |
| `HubRoomNavigation.prefab` | 底部房间导航容器 |
| `HubRoomButton.prefab` | 单个房间按钮及锁定房间按钮 |
| `HubSceneOverlay.prefab` | 当前房间说明和场景设备热点 |

## 日常调整流程

1. 退出 Play Mode。
2. 在 Project 窗口打开目标 Prefab。
3. 调整 RectTransform、锚点、字体、颜色或层级。
4. 保存 Prefab，重新进入 Play 验证。
5. 不需要修改 `OutGameUI.cs`，已有按钮引用会继续绑定原逻辑。

## 引用保护规则

- 可以移动、缩放和重命名普通子节点；运行时主要使用序列化引用，不依赖名称查找。
- 不要删除根节点上的 `OutGameTitleView`、`OutGamePaperView`、`OutGameSaveSlotView`、`OutGameHubView` 或 `OutGameSystemPanelView`。
- 删除已被引用的按钮、文本或容器后，必须在根组件 Inspector 中重新赋值。
- 挂在 Prefab 上的每个自定义组件都必须保留其同名独立脚本文件；不要把多个 `MonoBehaviour` 合并进一个 `.cs` 文件。
- 首页的 `OutGameLetterSpacing` 负责网页字距，`OutGameTweenButton` 负责 DOTween Hover/Press；调整布局时不要删除它们。
- 所有可点击的 `HubTopBar`、任务卡、访客卡、菜单按钮、房间按钮和热点都必须保留 `OutGameTweenButton`。删除该组件不会影响点击事件，但会让 Hover/Press 动效静默消失。
- `HouseHubPage.prefab` 中的 HUD 是嵌套 Prefab 实例：调整整个区域位置应修改页面实例的 RectTransform；调整组件内部排版应打开对应 `Hub*.prefab`。
- `Tools/MasterPotion/OutGame UI/Rebuild Default Prefabs...` 会覆盖手动布局，只用于明确恢复默认值；普通脚本刷新不会覆盖 Prefab。

## 当前动态内容边界

标题、存档、画廊、设置和退出均为独立完整 Prefab；新游戏与读取存档只复用 `SavePage.prefab` 的布局，通过代码切换标题、数据和按钮行为。House 中随房间/访客变化的列表，以及系统面板内部的数据卡片仍由控制器填入 `ChromeRoot`/`ContentRoot`；可在 Prefab 中调整这些区域整体的位置和尺寸。后续新增完整页面时必须建立独立 Prefab，重复条目继续拆为子 Prefab，禁止把新的布局坐标写进控制器。
