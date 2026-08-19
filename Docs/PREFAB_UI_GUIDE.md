# 局外 UI Prefab 调整指南

> 2026-08-10 校正：局外重构后，运行时绑定方由已删除的 `OutGameUI` 变为 `HouseUI` 模块（页面 = `HousePage` 派生类，一页一文件；面板 = `PanelHost` + 各 Binder）。
> **Prefab 缺失现在是报错、不再回退代码布局**（架构设计 §16.2）。

## 目标

局外 UI 使用“Prefab 管布局，C# 管逻辑”的分层：位置、尺寸、锚点、字号、颜色和层级在 Prefab 中调整；按钮事件、页面切换和 DOTween 由 `HouseUI` 模块在运行时绑定。

## Prefab 目录

`Assets/Resources/OutGameUI/Prefabs/`

| Prefab | 用途 | 主要可调内容 |
| --- | --- | --- |
| `TitlePage.prefab` | 标题与主菜单 | 背景、遮罩、菜单位置、6 个按钮、状态与提示 |
| `SavePage.prefab` | 新游戏/读取存档完整界面 | 标题区、说明、返回按钮、三个存档位及整体排版 |
| `GalleryPage.prefab` | 画廊完整界面 | 日志/成就页签、日志卡片、成就卡片 |
| `SettingsPage.prefab` | 标题设置完整界面 | 界面与存档区、保存/读取按钮、游戏性 Toggle |
| `ExitPage.prefab` | 退出确认完整界面 | 页面说明、返回按钮、退出确认按钮 |
| `PaperPage.prefab` | 公共纸张外壳 | 当前承载「存档功能重构中」占位页（§16.5） |
| `SaveSlot.prefab` | 单个存档位 | 编号、状态、信息、操作按钮的内部排版（存档回归前闲置） |
| `HouseHubPage.prefab` | House 主界面外壳（2026-08-20 换 2.0 设计图） | Scene、Chrome、Modal 三层 + Chrome 层里的八块 2.0 壳 |
| `SystemPanel.prefab` | 右侧功能面板外壳 | 遮罩、面板宽度、Header 与 Content 区域 |
| `MarketPage/MarketPanel/MarketCard.prefab` | 商城整页 / 内容 / 货架卡模板 | 钱包区、说明、卡片排布与三态样式 |
| `PlaceholderPage/PlaceholderPanel.prefab` | 「尚未开放」统一占位页 | 仓库/个人/通讯录共用（§16.8） |
| `DeviceCard/ArchiveCard/JournalArticle/AchievementRow.prefab` | 面板内动态列表项模板 | 单条目的排版与配色 |

## House 主界面 2.0 壳（2026-08-20）

`HouseHubPage.prefab` 的 `ChromeRoot` 下直接放着八块，全部可在页面 Prefab 里调位置尺寸：

| 节点 | 内容 |
| --- | --- |
| `TimeCard` | 时间牌：`Clock`（HH:MM）+ `Day`（DAY-N）；底板按时段在白天/夜晚两张素材间换图 |
| `CodexButton` / `StoreButton` | 左侧两颗整图按钮（文字烘在素材里，无需改文本） |
| `DecorationChip` / `ReputationChip` | 右上两块数值牌：`Caption` 静态抬头 + `Value` 数值 |
| `EndDayButton` | 右下「结束今日营业」 |
| `RoomGroup` → `RoomBody` → `RoomCard` / `FurnishButton` | 左下房间卡与「布置房间」，仅第三档可见 |

**改哪一块在哪个相机档位出现 = 改 Inspector**：每块根上的 `HubTierVisibility` 勾选三个档位，
并可调隐藏时的浮动偏移与时长。代码里没有任何显隐判断，不要往 `HubChromeBinder` 里加。

根节点上的 `OutGameHubChromeView` 是 Prefab 与代码之间的唯一契约，不要删；
`daySprite` / `nightSprite` 两个 Sprite 槽是时间牌换图的来源，留空则底板不随昼夜变化。

`Tools/MasterHouse/OutGame UI/重建主页面（2.0 设计图）` 会带确认弹窗覆盖重建整页。

## House HUD 组件 Prefab（1.0，2026-08-20 起不再装配）

下列组件在 2.0 版式里全部撤下，Prefab 与绑定代码仍保留以备回滚；
`HouseHubPage.prefab` 已不再嵌套它们，`OutGameHubView` 上对应槽位留空是正常态。
若要接回，在生成器里改回 `EmbedHubComponents` 并重建主页面：

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
5. 不需要修改任何 C# 文件，已有按钮引用会继续绑定原逻辑。

## 引用保护规则

- 可以移动、缩放和重命名普通子节点；运行时主要使用序列化引用，不依赖名称查找。
- 不要删除根节点上的视图组件（字段袋）：`OutGameTitleView`、`OutGamePaperView`、`OutGameSaveSlotView`、`OutGameHubView`、`OutGamePanelPageView`、`MarketPanelView`，以及各列表项模板的 `DeviceCardView` / `ArchiveCardView` / `JournalArticleView` / `AchievementRowView` / `MarketCardView`。它们是 Prefab 与代码之间的唯一契约，删掉后页面会直接报错（不再有代码兜底）。
- 删除已被引用的按钮、文本或容器后，必须在根组件 Inspector 中重新赋值。
- 挂在 Prefab 上的每个自定义组件都必须保留其同名独立脚本文件；不要把多个 `MonoBehaviour` 合并进一个 `.cs` 文件。
- 首页的 `OutGameLetterSpacing` 负责网页字距，`OutGameTweenButton` 负责 DOTween Hover/Press；调整布局时不要删除它们。
- 所有可点击的 `HubTopBar`、任务卡、访客卡、菜单按钮、房间按钮和热点都必须保留 `OutGameTweenButton`。删除该组件不会影响点击事件，但会让 Hover/Press 动效静默消失。
- `HouseHubPage.prefab` 中的 HUD 是嵌套 Prefab 实例：调整整个区域位置应修改页面实例的 RectTransform；调整组件内部排版应打开对应 `Hub*.prefab`。
- `Tools/MasterHouse/OutGame UI/Rebuild Default Prefabs...` 会覆盖手动布局，只用于明确恢复默认值；普通脚本刷新不会覆盖 Prefab（生成器位于 `Assets/Scripts/HouseUI/Editor/`，自动入口只补缺失）。

## 当前动态内容边界

所有整页界面均为独立完整 Prefab；**面板内的动态列表项一律走「模板 Prefab + 运行时实例化」**（设备卡、档案卡、日记文章、成就行、商城卡各有模板），可直接打开对应模板 Prefab 调整单条目样式，容器区域的位置与尺寸在所属页面 Prefab 中调整。

运行时代码仍会生成的只剩**动态表现件**：Toast、开门过场、房间切换门扇、家具背景热点与悬停卡、经济数值条、档案详情区的上下文按钮——它们没有固定布局归属，由 `HouseUIRuntime` 构建。除此之外禁止把布局坐标写进代码：新增页面必须建独立 Prefab，新增重复条目必须拆模板 Prefab。

例外（原型期遗留，二轮处理）：家具摆放模式的 HUD 与 F1 GM 面板仍是纯代码构建，尚未 Prefab 化（架构设计 §16.7）。
