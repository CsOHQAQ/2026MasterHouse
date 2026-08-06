# MasterPotion 开发文档

> 类型：持续更新文档（Living Document）  
> 最后更新：2026-08-04  
> 维护者：执行当前开发任务的人或 Agent

## 1. 文档目的

这份文档记录项目“现在是什么状态、正在改什么、已经验证了什么、下一步是什么”。它不是完成后补写的总结，而是开发过程的一部分。

所有会改变代码、资源、场景、配置或用户可见行为的任务，都必须在本文件中留下记录。踩坑原因和可复用经验写入 [RETROSPECTIVE.md](./RETROSPECTIVE.md)，不要堆在开发日志里。

## 2. 更新范式

### 开始开发前

先追加一个工作单元，填写：

- 目标与用户可观察结果
- 当前基线和入口
- 预计修改范围
- 验证方案
- 已知风险

### 开发过程中

出现以下事件时立即更新，不等任务结束：

- 技术方案或范围发生变化
- 新增、删除或更换入口
- 发现阻塞、运行时异常或版本不兼容
- 完成一个可独立验证的里程碑
- 验证结论从“未验证”变为“通过/失败/受阻”

“实时更新”指按开发事件更新，不要求记录每一条命令。

### 任务结束前

必须补齐：

- 实际修改文件
- 验证方式与结果
- 未完成项和残余风险
- 是否产生复盘条目

## 3. 状态定义

| 状态 | 含义 |
| --- | --- |
| 计划中 | 目标已确认，尚未修改代码 |
| 开发中 | 正在修改，暂不可交付 |
| 待验证 | 实现完成，但尚未在目标运行环境验证 |
| 已验证 | 已在目标 Unity 版本中完成编译和关键交互验证 |
| 受阻 | 有明确外部条件阻止继续验证或完成 |
| 已完成 | 验证通过，文档和复盘已同步 |

不能把“静态语法检查通过”写成“Unity 运行验证通过”。

## 4. Definition of Done

一个开发任务只有同时满足以下条件才能标记为“已完成”：

- 用户要求的可见行为已经实现
- Unity Console 没有由本次修改引入的异常
- 使用项目声明的 Unity 版本验证
- 关键入口不依赖开发者当前打开的临时场景
- 新增资源已导入，路径和 `.meta` 文件有效
- 至少验证一条正常流程和一条返回/异常流程
- 本文档已更新
- 新踩坑已写入 `RETROSPECTIVE.md`

## 5. 工作单元模板

复制下面内容到“开发记录”顶部：

```markdown
### YYYY-MM-DD · 工作项名称

- 状态：计划中 / 开发中 / 待验证 / 已验证 / 受阻 / 已完成
- 目标：用户最终能看到或操作什么
- 基线：当前场景、入口和已有行为
- 范围：预计修改的代码、资源、场景或配置
- 不做：本次明确不覆盖的内容
- 验证计划：编译、运行、交互、分辨率或存档验证方式

#### 实时进展

- HH:mm：开始，确认基线……
- HH:mm：完成里程碑……
- HH:mm：发现问题……，方案改为……

#### 修改清单

- `path/to/file`：修改原因

#### 验证结果

- [ ] Unity 编译
- [ ] 目标场景 Play Mode
- [ ] 核心正常流程
- [ ] 返回/异常流程
- [ ] Console 无新增异常

#### 未完成与风险

- 无 / 具体内容

#### 复盘

- 新增：`RETRO-XXX`
```

## 6. 当前项目基线

- 项目：MasterPotion / 2026MasterHouse
- 声明 Unity 版本：`2022.3.62f3`
- 主场景：`Assets/Scenes/SampleScene.unity`
- 核心玩法入口：`Assets/Scripts/UI/Bootstrap.cs`
- 局外 UI 入口：`OutGameUI.AutoBuild()`，通过 `RuntimeInitializeOnLoadMethod` 自动创建
- UI 技术：uGUI 运行时生成
- 动效：DOTween
- 局外 UI 资源：`Assets/Resources/OutGameUI/`
- 网页视觉基准：`web-demo/`

## 7. 开发记录

### 2026-08-06 · 访客 NPC 表现层（移动 / 等待 / 状态切换 / 刷新与消失）

- 状态：开发中
- 目标：起居室场景内出现会走动的访客 NPC：从入口大门进场、在入口等待、于地面区域游走停顿，点击访客直接触发对话；服务成功后播放庆祝动作并在停留时间结束后走向门口离开，拒绝后立即返回门口离开；观景模式（收起界面）平移缩放时访客贴住场景。追加（用户迭代）：①更多动物——6 只「串门邻居」氛围 NPC 随机轮换进场；②头顶小情绪气泡跟随访客随机浮现/消失循环；③邻居在门口排队等待，玩家点击后选择「请进屋 / 请回吧」决定去留；④访客接入存档系统 + 业务访客常驻屋内（任务未完成/不在服务时间也一直在屋里）+ 时间系统改为加速的游戏时钟（不按现实时间）
- 基线：访客只存在于 HUD 卡片（HubGuestRail）与对话页，场景图中无 NPC；家具热点已有「归一化场景坐标 + uvRect 换算」定位方案可复用；参考文档《访客功能》定义了到来/房间中/服务成功/拒绝失败四状态
- 范围：新增 `OutGameVisitorSheetAnimator`（RawImage 序列帧图集播放）、`OutGameVisitorActor`（单个访客状态机）、`OutGameVisitorStage`（访客层管理）三个独立同名脚本；`OutGameUIData` 增加访客动画映射；`OutGameUI` 在 ShowHub/SwapRoom 重建访客层并在服务/拒绝/周结算处通知演员；从 CatVsDog 项目拷入 4 组动物 await/attack 序列帧图集（缩至 2048 宽）到 `Assets/Resources/OutGameUI/Visitors/`
- 不做：正式访客种族美术（狐/鸦/兔/猬暂用猫狗占位）、行走动画（素材只有待机/攻击，移动用待机+跳动表现）、访客系统数值配置表（DA 化）、多房间游走（仅起居室）、对话内容扩展
- 验证计划：离线 Roslyn 编译 → Unity Play Mode：进入 Hub 后访客陆续从左侧大门进场；入口等待后开始游走；点击 NPC 弹出对话；完成服务→庆祝动作+继续游走+计时离场；拒绝→返回门口离场；收起界面后平移缩放访客位置正确；切换房间/回标题无残留与异常

#### 实时进展

- 已读取飞书文档《访客功能》（wiki ZMArwb7IviNIEHkSXidcef4cnJg）：访客四状态（到来入口等待 / 房间中游走可触发对话 / 服务成功后游走配置时间离开 / 拒绝失败返回入口）；本轮实现其表现层
- 素材管线：CatVsDog 的 `*_sheet.png + *_sheet.json`（列/行/帧数）直接用 RawImage.uvRect 逐帧播放，不切片不建 Sprite；4 组动物（orange_cat/rottweiler/xueqiu/wangcai）await+attack 共 8 组图集经 PIL 缩至 2048 宽入库，JSON 原样拷贝运行时 JsonUtility 解析（各表布局不同：5×5、6×5、4×7、4×6 混排，硬编码行列会踩坑）
- 定位方案与家具热点同源：演员持「场景归一化坐标」，舞台层每帧按 sceneArt.uvRect 换算锚点，观景模式平移缩放天然跟随；深度=按 y 排兄弟序（近前远后）+ 近大远小缩放 + 远处减速
- 入口=画面左侧大门（0.115, 0.32），门前等待点与 7 个避开沙发/茶几/书架的手摆游走落点写在 Stage 常量里
- 状态机：Hidden(错峰刷新延迟)→Arriving(进门淡入走向等待点)→Waiting(9~15s)→Wandering(走-停循环)；NotifyServed→attack 图集庆祝一次→继续游走 10~16s→Leaving 走回门口淡出销毁；NotifyRefused→加速直接 Leaving；素材无行走动画，移动用待机+节奏跳动代替步态
- 交互：演员透明点击区+悬停名牌卡（名字+状态），点击→SelectGuest 触发对话（观景模式先展开界面；家具模式/对话中/切房间中忽略）；served 的访客不再刷新；周结算与 GM 重置整体重建访客层
- 用户迭代追加三项并已实现：①剩余 6 只动物（laoda/laomao/longhair_cat/panghu/sangbiao/tufu，其中 3 只无 attack 表）入库为「串门邻居」：随机挑 3 只错峰进场，离场后冷却 8~16s 换一只不在场的补位（刷新循环）；点击游走中的邻居会跳一下+播放一次动作（无 attack 表的只跳）。②`OutGameVisitorBubble` 情绪气泡：随机间隔在头顶浮现 ♪？！…♥★ 等符号，上飘后消失循环，内容随状态变化（排队「？！…」、游走「♪…★」、满足「♥♪★」），选择弹窗打开时不冒泡。③邻居到场后在门口 3 个排队点等待（前面走了自动补位挪动），点击弹出「请进屋/请回吧」选择卡（6s 未决定自动收起）；请进→进屋游走 22~45s 自行离开；请回→加速离场；耐心 45~75s 耗尽自己走
- 业务访客保持原流程（点击=触发对话，服务/拒绝驱动离场），不走邻居的去留选择
- 第四轮迭代（时钟+存档+常驻）：新增静态 `OutGameClock`（现实 1 秒=游戏 1 分钟，一天 24 分钟现实时间；只在 Hub 内流动，标题/过场暂停，家具模式期间继续）；`OutGameUIData.CurrentPhase` 与顶栏时钟/时段全部改读游戏时钟（顶栏显示 DAY NN + HH:mm + 时段，跨时段/跨天自动刷新），不再读 DateTime.Now（日历面板的装饰性现实时间除外）
- 访客配置增加 `visitHour/serviceStart/serviceEnd`（洛恩 8 点到访全天可服务=特殊客人；赫墨 9 到访 10–18；米娅 10 到访 12–20；霍奇 13 到访 14–22）。到点从大门走进来（半游戏小时踩点窗口），**进屋后常驻**：服务窗口外点击只弹「可服务时间是 XX–XX」提示、人留在屋内游走；服务完成/拒绝才离场；周结算 NextDay 跳到次日 08:00 重新按拜访时间进场
- 存档 v3：新增 `gameDay/gameMinute`（游戏时钟）与 `guestArrived[4]`（已到访标记，保证读档回来访客直接「已在屋内」淡入而不是重新进门）；SaveCurrent/ApplySave/ResetProgress/GM 重置全部接线，v2 及更早旧档回落第 1 天 08:00、未到访
- 时钟是持续流动的状态，只靠事件节点写档会丢挂机进度——补三个自动写档口子：①Hub→标题（ESC/品牌按钮）前静默写档；②Hub 内每 60 现实秒（=1 游戏小时）周期静默写档；③OnApplicationQuit（关游戏/退出 Play）写档
- 两程序集离线编译返回 0（runtime rsp 追加 5 个新文件，末行换行已按 RETRO-016 处理）

#### 修改清单

- `Assets/Scripts/UI/OutGameVisitorSheetAnimator.cs`：RawImage uvRect 序列帧播放器 + 图集 JSON 描述类（新增）
- `Assets/Scripts/UI/OutGameVisitorActor.cs`：单个访客 NPC 状态机演员（业务访客 + 串门邻居两种模式、去留选择弹窗、被逗反应）（新增）
- `Assets/Scripts/UI/OutGameVisitorBubble.cs`：头顶情绪气泡随机循环（新增）
- `Assets/Scripts/UI/OutGameVisitorStage.cs`：访客舞台层：业务访客按时钟进场与常驻回填/邻居轮换与门口队位分配/锚点换算/深度排序（新增）
- `Assets/Scripts/UI/OutGameClock.cs`：加速游戏时钟静态服务（新增）
- `Assets/Scripts/UI/OutGameUIData.cs`：VisitorSheets 访客→图集映射；AmbientVisitors 邻居名册；OutGameGuest 拜访/服务时间字段；存档 v3 字段；CurrentPhase 改读游戏时钟
- `Assets/Scripts/UI/OutGameUI.cs`：时钟 Tick 与顶栏 DAY/时段显示；服务窗口门控；存档 v3 读写；ShowHub/SwapRoom 重建访客层；ServeGuest/RefuseGuest 通知演员；EndWeek 跳次日并刷新访客；GM 重置连时钟一起归零
- `Assets/Resources/OutGameUI/Visitors/*.png|*.json`：10 组动物图集（await 全量 + 7 组 attack；生成，.meta 待 Unity 首次导入）

#### 验证结果

- [x] 离线编译（Assembly-CSharp / Assembly-CSharp-Editor 返回 0；非 Unity 运行时验证）
- [ ] Play Mode：进 Hub 访客错峰从大门进场→入口等待→游走；点击业务访客弹对话
- [ ] 完成服务→庆祝动作→停留计时→走回门口消失；拒绝→立即返回门口消失
- [ ] 邻居在门口排队张望、前位走后补位；点击弹「请进屋/请回吧」，请进后游走一段时间自行离开，请回立即离场；离场后过一阵换新邻居进场
- [ ] 情绪气泡随机浮现/上飘/消失循环，内容随状态变化
- [ ] 周结算/GM 重置后访客重新进场；切厨房/书房无访客、回起居室正常
- [ ] 收起界面（观景模式）平移缩放时访客/气泡/名牌卡位置正确
- [ ] 游戏时钟：顶栏 DAY/HH:mm 按 60× 走动，时段（早晨→上午…）随之切换；标题页时间暂停
- [ ] 按拜访时间进场：8 点洛恩、9 点赫墨、10 点米娅、13 点霍奇依次从大门进来；服务窗口外点击弹时间提示且访客留在屋内
- [ ] 存档：Hub 内保存→回标题→继续游戏，时钟/已到访访客还原（已在屋内直接淡入，不重新进门）；v2 旧档读取回落第 1 天 08:00
- [ ] 时钟自动落档：Hub 挂机 1 分钟以上直接停止 Play → 重进读档，时间接近退出时刻（误差 ≤1 游戏小时）；ESC 回标题→继续游戏时间无回退
- [ ] Console 无新增异常

#### 未完成与风险

- 动物素材为占位（狐/鸦/兔/猬 ↔ 猫狗不对应），await 姿态偏战斗（炸毛），正式版需替换专属素材
- 无行走动画，移动表现依赖跳动；拜访时间/可服务时间未接入（全天在场），访客配置未 DA 化
- 入口/排队点/游走落点为硬编码常量，仅覆盖起居室；换背景图需同步调整
- 气泡符号（♪♥★ 等）依赖运行时字体字形，若回退字体缺字形需换字符集；邻居去留目前无业务后果（纯表现）
- 时钟加速倍率是 `OutGameClock.MinutesPerRealSecond` 常量（60×），未开 GM 调速入口；「结束本周」语义实际是「跳到下一天早晨」，周/天概念尚未统一；日历面板仍显示装饰性的现实日期，与游戏时钟并存待后续整合

#### 复盘

- 待定

### 2026-08-06 · 五个系统面板整页 Prefab 化（日历 / 委托 / 设备 / 日记 / 档案）

- 状态：待验证
- 目标：按用户逐条要求，把「日程与时间」「今日委托」「设备图鉴」「日记与成就」「叙事资源档案」五个页面做成**整页** Prefab（含头部返回/标题/角标），并修复日历第 7 列被右侧时段卡压住的布局问题
- 基线：系统面板 = 共享壳 SystemPanel.prefab + 运行时代码填充内容；日历/委托内容 Prefab 上一工作项已建
- 范围：新增共享整页外壳 View `OutGamePanelPageView` + 三个内容 View（Device/Journal/Archive）；生成器新增 3 个内容 Prefab 构建与通用 `BuildPanelPage`（外壳 + Nested 内容 Prefab）生成 5 个整页；OpenPanel 增加整页 Prefab 优先分支 `TryOpenPanelPage`（外壳动效与共享壳一致），内容按类型绑定；日历格间距 78→64
- 不做：通讯录/仓库/设置/个人/商城面板整页化（仍走共享壳）；档案格子、设备卡、日记文章等**数量随数据变化**的集合仍运行时生成到 Prefab 挂点（符合"稳定区域 Prefab、运行时只更新数据"约定）
- 验证计划：生成器补齐 8 个新 Prefab（3 内容 + 5 整页）→ Play 逐页打开对比布局/交互/开合动效；页签与选中态切换正常（内部走重新开页）；日历第 7 列不再被遮挡

#### 实时进展

- 整页结构：`XxxPage.prefab` = 遮罩 + 右侧 1280 面板 + 头部（返回/标题/角标可手调）+ Nested 内容 Prefab（`CalendarPanel/TasksPanel/DevicePanel/JournalPanel/ArchivePanel`），五页共用一个外壳 View 类
- 用户 Play 实测反馈两点并已修复：①CalendarPage 里看不到日历（日期格是运行时生成）——改为 Prefab 内烘焙 6×7=42 个日期槽位，运行时只设置数字/显隐/今日高亮，跨月由显隐控制；旧版 Prefab 保留运行时生成兜底。②面板向左弹入距离写死 `PanelWidth/2+80 → -PanelWidth/2`——改为以 Prefab 作者摆放的位置为静止点、按面板实际宽度计算起点，改 Prefab 尺寸后动画自适应
- ⚠ 需在 Unity 中删除旧的 `CalendarPanel.prefab`（CalendarPage 嵌套引用会随之更新；若报缺失把 `CalendarPage.prefab` 一并删除），生成器会自动按新布局补齐
- 两程序集离线编译返回 0

#### 修改清单

- `Assets/Scripts/UI/OutGamePanelPageView.cs`、`OutGameDevicePanelView.cs`、`OutGameJournalPanelView.cs`、`OutGameArchivePanelView.cs`：新增 View
- `Assets/Scripts/UI/OutGamePrefabResourcePaths.cs`：新增 8 路径
- `Assets/Editor/OutGameUIPrefabGenerator.cs`：3 个内容构建 + 通用整页构建 + 注册
- `Assets/Scripts/UI/OutGameUI.cs`：TryOpenPanelPage 整页优先；BindDevicePanel/BindJournalPanel/BindArchivePanel；日历格间距修复（Prefab 绑定与代码兜底两处）

#### 验证结果

- [x] 离线编译（两程序集返回 0；非 Unity 运行时验证）
- [ ] 8 个新 Prefab 自动补齐；五页打开显示/交互/动效与原版一致
- [ ] 日历第 7 列（2/9/16/23/30）完整可见

#### 未完成与风险

- 通讯录/仓库/设置/个人/商城仍为共享壳 + 代码内容；家具模式 HUD 未 Prefab 化
- 五页内部的动态集合是运行时生成，Prefab 里看不到这些占位（编辑布局时以挂点为准）

#### 复盘

- 无

### 2026-08-06 · Hub 绑定判空修复 + 四个新 Prefab（收起按钮 / 日历 / 今日委托 / 访客对话）

- 状态：待验证
- 目标：①修复用户报告的「按钮和字体消失」：Editor.log 显示 `BindSceneOverlay` NRE（Prefab 字段缺失）在 ShowHub/RebuildHubChrome 中断绑定链，导致声望条、家具摆放、收起界面按钮全部未创建；②按用户要求把收起按钮与三个界面（日程与时间、今日委托、访客对话）拆分为可编辑 Prefab
- 基线：Hub 绑定方法直接解引用 Prefab 字段；收起按钮/日历/委托/对话均为运行时代码构建
- 范围：`BindSceneOverlay` 逐项判空；新增 4 个 View（`OutGameHubImmersiveToggleView/OutGameCalendarPanelView/OutGameTasksPanelView/OutGameDialogueView`）；生成器新增 4 个 Build 并注册到补缺/重建/按钮动效修复清单；OutGameUI 四处改为 Prefab 优先 + 代码兜底
- 不做：日历的日期格子仍运行时生成（数量随月份变化，生成到 `dayGridRoot`）；其余系统面板（设备/档案等）本轮不拆
- 验证计划：Unity 打开后生成器自动补 4 个新 Prefab → Play：收起按钮/日历/委托/对话来自 Prefab 且数据绑定正确；切房间不再抛 NRE；ShowHub 后声望条/家具摆放/收起按钮齐全

#### 实时进展

- NRE 根因：`HubSceneOverlay` Prefab 实例存在但内部 Text 引用缺失，`BindSceneOverlay` 解引用抛异常；ShowHub 中该调用位于运行时控件创建之前，异常把后续全部截断（页脚也停留在 Prefab 默认文本）——绑定层现已全部判空降级
- 对话 Prefab 的背景图优先使用家具布局合成图（与 Hub 联动），回退原图
- 生成器沿用「只补缺失、不覆盖手调」约定；重建入口仍需菜单确认
- 两程序集离线编译返回 0

#### 修改清单

- `Assets/Scripts/UI/OutGameUI.cs`：BindSceneOverlay 判空；收起按钮/日历/委托/对话 Prefab 优先绑定（BindCalendarPanel/BindTasksPanel/ShowDialogueFromPrefab）
- `Assets/Scripts/UI/OutGameHubImmersiveToggleView.cs`、`OutGameCalendarPanelView.cs`、`OutGameTasksPanelView.cs`、`OutGameDialogueView.cs`：新增 View（各自独占同名文件）
- `Assets/Scripts/UI/OutGamePrefabResourcePaths.cs`：新增 4 个路径
- `Assets/Editor/OutGameUIPrefabGenerator.cs`：新增 4 个 Build 方法并注册

#### 验证结果

- [x] 离线编译（两程序集返回 0；非 Unity 运行时验证）
- [ ] 生成器自动补齐 4 个新 Prefab（打开 Unity 后查看 Prefabs 目录）
- [ ] ShowHub 无 NRE；声望条/家具摆放/收起界面按钮齐全；切房间正常
- [ ] 日历/今日委托/访客对话来自 Prefab 且数据、动效正确

#### 未完成与风险

- 需检查 `HubSceneOverlay.prefab` 内部引用为何缺失（Inspector 查看 View 字段是否 Missing，必要时用「Rebuild Default Prefabs」重建该件）
- 商城/设备等其余面板与家具模式 HUD 仍未 Prefab 化

#### 复盘

- 判空降级已写入绑定层；「绑定链中断导致后续控件全部消失」的排查路径（查 Editor.log 而非猜 UI 层级）值得沿用

### 2026-08-06 · 家具布局烘焙回背景图 + 自动存档 + GM 全量重置

- 状态：待验证
- 目标：①家具摆放与背景图联动：摆放完成（退出模式/读档）后，当前布局合成为起居室背景图；②修复「摆完家具再进来回到初始状态」：关键节点自动写档，存档槽始终最新；③GM 面板增加「恢复所有状态到初始态」按钮
- 基线：Hub 起居室固定显示原始美术图，家具变化只在模式内可见；存档只有设置页手动保存，回标题→继续游戏会用旧档冲掉未保存的布局（用户报告的复现路径）；GM 面板只有加值按钮
- 范围：新增 `FurnitureSceneComposer`（正交相机渲布局到 RenderTexture）；OutGameUI 场景图优先用合成图 + AutoSave 接线；GM 面板重置按钮与广播事件；控制器暴露 CloseActive
- 不做：其他房间的布局烘焙（当前仅起居室）、烘焙图持久化到磁盘（每次运行时重渲）
- 验证计划：摆放→退出→Hub 背景变为新布局；服务/购买/摆放后直接回标题再继续游戏，进度不回退；GM 重置后三值/访客/布局/背景全部回初始并写档

#### 实时进展

- 烘焙方案：正交相机（内容近似共面，正交与像素坐标精确对应）渲「干净背景+按摆放配置静态计算锚点的家具」到 1672×941 RT；URP 下不用 Camera.Render()，改为启用相机等待一帧后销毁；RT 复用，重烘焙时引用该 RT 的 RawImage 自动更新
- 自动存档节点：家具模式退出、完成/拒绝服务、周结算、商城购买（静默，无 toast）；家具模式内购买依赖退出时落档
- 存档回退根因确认：非 bug 而是保存时序——旧档在摆放前保存，继续游戏重新应用旧档；自动存档从机制上消除
- GM「恢复所有状态到初始态」：关家具模式→流通数值回默认→布局回默认→广播事件；OutGameUI 响应：访客 served/refused 归零、重烘焙背景、刷新 HUD、写档
- 用户 Play 实测发现：点「完成」后背景消失（露出节点棋盘）。根因=临时相机+等帧的烘焙在 URP 下从未真正写入 RT（透明），已改为 Graphics.DrawTexture 按像素坐标同步合成（见 RETRO-017），失败路径回落原图
- 用户确认合成背景生效后追加两项：①背景家具热点——已摆放家具区域可悬停（弹「＋ 家具名 / 查看设备」卡，对齐黑胶唱机热点样式）、可点击（暂接设备图鉴面板）；热点用归一化锚点定位（Composer 暴露 GetPlacedFurniture），随布局烘焙同步重建。②「收起界面」观景模式——右下角开关按钮，收起四周全部 HUD（CanvasGroup 淡出+禁点击），此时左键拖拽平移背景、滚轮以鼠标为中心缩放（1~3.5 倍，RawImage.uvRect 实现，边界钳制），ESC 或再点按钮展开并复位视图
- 用户实测反馈观景模式两问题并已修复：①拖拽无效——场景图/压暗层 RawImage 默认 raycastTarget=true 全屏挡住指针，平移起手的 IsPointerOverGameObject 判定恒真；已把场景层全部设为不拦截指针并移除该判定。②家具无互动——原实现在观景时隐藏热点层；改为热点按当前 uvRect 实时换算锚点（UpdateFurnitureHotspotAnchors），平移缩放时热点贴住家具，悬停/点击在观景模式同样可用
- 离线编译返回 0

#### 修改清单

- `Assets/Scripts/Furniture/FurnitureSceneComposer.cs`：布局烘焙器（新增）
- `Assets/Scripts/Furniture/HouseGmConsole.cs`：重置按钮 + FullResetRequested 事件
- `Assets/Scripts/Furniture/FurnitureRoomController.cs`：CloseActive 静态接口
- `Assets/Scripts/UI/OutGameUI.cs`：ApplySceneArt/AutoSave/GM 重置响应；服务/结算/购买/模式退出接自动存档；读档按布局烘焙或回落原图

#### 验证结果

- [x] 离线编译（返回 0；非 Unity 运行时验证）
- [x] 摆放退出后 Hub 背景显示新布局（用户截图确认；相机烘焙透明问题已改同步合成修复）
- [ ] 摆放→回标题→继续游戏，布局不回退（自动存档生效）
- [ ] GM F1 → 恢复初始态：三值/访客/布局/背景全部回初始
- [ ] 背景家具悬停出热点卡、点击打开设备图鉴；摆放变化后热点位置跟随
- [ ] 「收起界面」：HUD 全收起、拖拽平移、滚轮缩放、ESC 展开复位

#### 未完成与风险

- 烘焙仅覆盖起居室；烘焙图含修补痕迹与切片毛边（与家具模式内观感一致，正式版靠正式切片解决）
- GM 重置在家具模式打开时会先强制关闭模式，此时先后两次烘焙按启动顺序覆盖，最终为默认布局（已按协程顺序保证）

#### 复盘

- 无

### 2026-08-06 · 存档系统接入流通数值与家具布局（存档 v2）+ 标题菜单 hover 修正

- 状态：待验证
- 目标：①存档完整化：货币/声望/GM 装饰分加成/装饰品所有权/家具摆放布局/拒绝记录全部随槽位存取；新游戏与旧档读取不再被上一局会话状态污染；旧存档兼容（回落默认值）。②标题菜单的橙色 hover 渐变默认不显示，仅鼠标悬停或键盘导航时出现
- 基线：存档只存房间/served/音量/视窗；流通数值与家具为会话级静态；标题页加载时自动 Select 首个菜单项导致「继续游戏」默认亮起橙色渐变
- 范围：`OutGameSaveData` 加 version=2 与新字段；`HouseEconomy.Capture/Restore/ResetToDefaults`；`FurnitureRoomController` 布局存取静态接口（含不开模式也回写装饰分）；`OutGameUI` 保存/读档/新游戏三处接线；标题两条构建路径移除初始 Select 并强制 hover 图 alpha 归零
- 不做：跨槽位云同步、存档加密、周数进度扩展
- 验证计划：离线编译 → Play Mode：存档→重启→读档三值与布局还原；新游戏数值归零且家具回默认摆放；旧档读取不报错且回落默认；标题页默认无橙色高亮，悬停/方向键后出现

#### 实时进展

- hover 根因：`ShowTitle`/`BindTitlePrefab` 末尾 `titleMenuButtons[titleMenuIndex].Select()` 触发 `OutGameTweenButton.OnSelect`（选中与悬停共用高亮）；已移除初始 Select（键盘 ↑↓/Enter 仍可用，`MoveTitleSelection` 会正常 Select 并出反馈），并在 Prefab 绑定时强制 hover 图 alpha=0
- 存档 v2：`version` 字段做兼容门（JsonUtility 对旧档嵌套对象会给零值实例，不能用 null 判断）；`hasFurnitureLayout` 区分「摆空了」与「从未编辑（用房间默认摆放）」
- `HouseEconomy.Restore` 时把 price≤0 的基础家具强制并入所有权，防改表/旧档丢家具；读档/新游戏通过 `SyncDecorationFromSession` 不开家具模式即回写正确装饰分
- 离线编译两程序集返回 0

#### 修改清单

- `Assets/Scripts/UI/OutGameUIData.cs`：OutGameSaveData 扩展 v2 字段
- `Assets/Scripts/Furniture/HouseEconomy.cs`：新增 HouseEconomySaveData 与 Capture/Restore/ResetToDefaults
- `Assets/Scripts/Furniture/FurnitureRoomController.cs`：新增布局存取静态接口与装饰分同步
- `Assets/Scripts/UI/OutGameUI.cs`：SaveCurrent/ApplySave/ResetProgress 接线；标题菜单 hover 修正

#### 验证结果

- [x] 离线编译（两程序集返回 0；非 Unity 运行时验证）
- [ ] Play Mode：保存→退出 Play→再进→读档，三值/所有权/布局还原
- [ ] 新游戏后数值与家具回初始；旧档兼容
- [ ] 标题菜单默认无高亮，悬停与键盘导航正常

#### 未完成与风险

- 家具模式开启期间无法保存（局外 Canvas 被禁用），Capture 已做 active 兜底但无入口触发
- served/refused 仍是 4 位固定数组，周数不持久

#### 复盘

- 无（JsonUtility 嵌套对象零值实例的坑已记录在本工作项，暂不单列 RETRO）

### 2026-08-06 · 流通数值循环系统（货币 / 声望 / 装饰分）

- 状态：待验证
- 目标：按飞书文档《大House》流通数值章节实现三值循环：货币（来源=客人服务；去处=购买设备/装饰品）、玩家声望（来源=完成服务；去处=拒绝服务、未完成服务）、House 装饰分（无去处；来源=房间数量+房间装饰品+房间设备）；Item 解锁条件=声望值，未解禁 Item 呈「？」状态
- 基线：家具系统 Unity 侧已完成（解锁走信用点直购）；OutGameUI 访客服务只翻转 served 标记；顶栏 HOUSE CREDIT 为写死文本
- 范围：新增 `HouseEconomyConfig`（数值配置表）与 `HouseEconomy`（静态流通服务）；家具配置表行增加 `unlockReputation` 与 `decorationScore` 字段；家具购买改为「声望解禁 + 货币购买」；OutGameUI 接入服务奖励/拒绝惩罚/周结算未完成惩罚与三值 HUD 显示
- 不做：商店独立页面（文档标注投放方式待定）、消耗品类 Item、数值存档持久化（会话内）、图鉴页
- 验证计划：离线 Roslyn 编译 → Unity Play Mode：服务客人加货币声望、拒绝扣声望、结束本周按未完成数扣声望、家具「？→可购买→已购入」三态流转、摆放变化实时改装饰分

#### 实时进展

- 已读取飞书文档全文（wiki HGGfwslVIi7jMEkyc2qckgBan4f）：三值来源/去处、Item 大类通用配置（含解锁条件=声望值）、未知 Item 呈「？」
- 用户补充流通循环图：商城是货币去处枢纽（购买装饰品/设备），声望作用是解锁商城货架；据此把 Hub 的 Market 面板升级为真商城（装饰品货架），与家具模式共享 `HouseEconomy` 的所有权数据
- 默认数值：初始货币 2480 / 声望 40；服务奖励 ◈320 + 声望 25；拒绝 -15；未完成每项 -30；装饰分 = 房间×50 + 设备×30 + 家具装饰分和 + GM 加成
- 已实现闭环：对话框新增「拒绝接待」（-声望）；「结束本周」改为真结算（每项未完成 -声望并重置一周）；完成服务 +货币+声望；家具三态「？→可购买→已拥有」由声望与货币双门控制；摆放变化实时回写装饰分
- 用户追加需求：GM 系统。已实现 `HouseGmConsole`（F1 开关，常驻），可加货币/声望/装饰分（装饰分走独立 GM 加成项，因其本体是派生值）；声望增减会实时翻转商城/收纳栏的「？」状态
- 离线编译：两程序集返回 0（Unity 已把家具文件收进新 rsp，追加重复仅产生 CS2002 警告，无碍）

#### 修改清单

- `Assets/Scripts/Furniture/HouseEconomyConfig.cs`：流通数值配置表 SO（新增）
- `Assets/Scripts/Furniture/HouseEconomy.cs`：流通数值服务（三值 + 装饰品所有权 + GM 接口，会话级单一数据源）（新增）
- `Assets/Scripts/Furniture/HouseGmConsole.cs`：GM 面板（F1，加货币/声望/装饰分）（新增）
- `Assets/Scripts/Furniture/FurnitureTable.cs`：行增加 `unlockReputation`、`decorationScore`
- `Assets/Scripts/Furniture/FurnitureRoomHud.cs`：槽位增加「？」未知态；解锁弹窗改购买弹窗；顶栏显示三值
- `Assets/Scripts/Furniture/FurnitureRoomController.cs`：货币/所有权改走 HouseEconomy；摆放变化回写装饰分
- `Assets/Scripts/Editor/FurnitureConfigSetupUtility.cs`：生成 HouseEconomyConfig；默认表补声望阈值与装饰分
- `Assets/Scripts/UI/OutGameUI.cs`：顶栏货币动态化 + 声望/装饰分展示条；服务/拒绝/周结算接入流通；Market 面板改为商城装饰品货架

#### 验证结果

- [x] 离线编译（两程序集返回 0；非 Unity 运行时验证）
- [ ] Unity Play Mode：服务/拒绝/周结算的三值变化与 toast
- [ ] 商城与家具模式的「？/可购买/已拥有」三态联动（用 GM 调声望验证实时翻转）
- [ ] 摆放/收纳装饰分实时变化；GM 面板 F1 开关与加值

#### 未完成与风险

- 设备货架未投放（文档标注投放方式待定）；消耗品类 Item 与局内产出链路未做
- 三值为会话级状态，未接入存档体系；GM 面板无权限门禁（原型阶段）

#### 复盘

- 待定

### 2026-08-06 · 家具系统 Unity 迁移（配置表驱动）

- 状态：待验证
- 目标：Unity 内在 House Hub 点击「家具摆放」进入起居室家具模式：所有 HUD 收起，家具从背景抠图切片摆放/收纳/解锁；家具与房间均由配置表（ScriptableObject）驱动；家具按类型（地面/桌面/壁挂）限制可放置网格；背景为带 3D 感的平面（透视相机 + 鼠标视差）并有景深表现（远景常驻渐变模糊 + 拖拽时背景失焦）
- 基线：web-demo `furniture-editor.tsx` 已验证交互；Unity 侧 OutGameUI（Prefab Hub + 代码兜底）运行正常；家具素材目录 `Assets/Resources/OutGameUI/Furniture/` 已有 5 件叙事家具图
- 范围：新增 `Assets/Scripts/Furniture/`（数据 + 运行时）、`Assets/Scripts/Editor/FurnitureConfigSetupUtility.cs`（配置表生成菜单）、家具切片与房间背景素材入库、OutGameUI 增加入口按钮与模式开关
- 不做：本轮不做家具旋转、存档持久化（会话内保留）、正式 HUD Prefab 化（家具模式 HUD 先用运行时 uGUI，验收后再固化 Prefab）、真实 URP DoF 后处理（2D Renderer 深度不可用，用预烘焙模糊层替代）
- 验证计划：离线 Roslyn 编译 → Unity 编译无报错 → Play Mode 从 Hub 进入家具模式，验证摆放吸附/占位/层级/级联收纳/解锁/退出还原，以及视差与景深表现

#### 实时进展

- 已确认接入点：`OutGameUI.ShowHub` 的 chrome 根下追加「家具摆放」运行时按钮（不改动 Hub Prefab 布局）；模式打开时禁用 OutGameUI Canvas，退出回调恢复
- 配置表方案：`FurnitureTable`（一张表，行=家具：类型/占格/显示尺寸/价格/桌面格配置/精灵引用）与 `FurnitureRoomTable`（一张表，行=房间：背景三层/网格列表/场景占用格/初始摆放/初始信用点），资产放 `Assets/Resources/OutGameUI/` 供运行时 Resources.Load
- 3D 感与景深方案：所有内容仍在同一平面保证与背景像素对齐，透视相机围绕画面中心做鼠标视差微转 + 家具按深度行给微小 Z 偏移；景深 = 预烘焙「远景渐变模糊层」常驻 + 「整幅模糊层」拖拽时淡入（2D Renderer 不产出深度纹理，不使用 URP DoF）
- 已完成素材入库：8 件抠图切片 + `room-living-clean/blur/depthblur` 三层背景拷入 `Assets/Resources/OutGameUI/Furniture/`（.meta 待 Unity 首次导入生成；项目为 2D 默认导入，PPU 100）
- 已完成数据层、运行时（控制器/网格/相机/HUD）与 Editor 生成工具；家具配置表默认 13 行 = 8 件切片 + 5 件叙事家具（鲸声电话亭/月亮花架/蒲公英灯/兔耳风铃/琴弦窗户，均为待解锁）
- 离线编译：Assembly-CSharp 与 Assembly-CSharp-Editor 均以 Unity 同款 Roslyn 编译返回 0（追加 rsp 时踩到末行无换行的坑，见 RETRO-016）

#### 修改清单

- `Assets/Scripts/Furniture/FurnitureSurfaceType.cs`：表面类型枚举（新增）
- `Assets/Scripts/Furniture/FurnitureTable.cs`：家具配置表 SO（一行一件家具，含桌面格配置）（新增）
- `Assets/Scripts/Furniture/FurnitureRoomTable.cs`：房间配置表 SO（背景三层/网格/场景占用格/初始摆放）（新增）
- `Assets/Scripts/Furniture/FurnitureRuntimeGrid.cs`：运行时网格（占位 + 单元格高亮）（新增）
- `Assets/Scripts/Furniture/FurnitureRuntimeItem.cs`：运行时家具实例（新增）
- `Assets/Scripts/Furniture/FurnitureCameraRig.cs`：透视相机视差（3D 感平面）（新增）
- `Assets/Scripts/Furniture/FurnitureRoomHud.cs`：模式 HUD（收纳栏/解锁弹窗/提示条，原型阶段运行时 uGUI）（新增）
- `Assets/Scripts/Furniture/FurnitureRoomController.cs`：模式主控（拖拽吸附/占位预览/层级/级联收纳/解锁/会话状态）（新增）
- `Assets/Scripts/Editor/FurnitureConfigSetupUtility.cs`：配置表生成菜单（补齐缺失 / 覆盖重建带确认）（新增）
- `Assets/Scripts/UI/OutGameUI.cs`：Hub 增加「家具摆放」入口按钮与模式开关（家具模式期间挂起局外 UI 输入）
- `Assets/Resources/OutGameUI/Furniture/*.png`：11 张新素材（8 切片 + 3 背景层）

#### 验证结果

- [x] 离线编译（Assembly-CSharp / Assembly-CSharp-Editor 返回 0；非 Unity 运行时验证）
- [ ] Unity 编译 + 资源导入（待用户打开 Unity 触发）
- [ ] 执行菜单 MasterPotion → 家具系统 → 创建配置表（补齐缺失）
- [ ] Play Mode：Hub →「家具摆放」进入模式，验证摆放吸附/绿红预览/场景占用格/层级遮挡/茶几级联收纳/解锁扣费/ESC 退出还原局外 UI
- [ ] 视差与景深表现（鼠标移动镜头微转、拖拽时背景失焦）

#### 未完成与风险

- 家具模式 HUD 为运行时 uGUI，尚未 Prefab 化；素材 .meta 依赖 Unity 首次导入生成
- 解锁/信用点为会话内状态，未接入存档体系

#### 复盘

- 待定

### 2026-08-06 · 家具系统（摆放 / 收纳 / 解锁）· Web 原型先行

- 状态：待验证
- 目标：一套 2D 家具系统：从收纳栏拖拽摆放家具（网格吸附 + 绿/红占位预览）、拖回或双击收纳、金币解锁；家具分三类（地面 / 桌面 / 壁挂）各 2 件；地面按深度行产生前后层级遮挡；房间背景与网格、每件家具都对应独立 Prefab
- 基线：Unity 侧尚无家具系统；先做 Web 交互原型验证手感，再迁移 Unity
- 范围：本轮只产出 Web 原型（Claude Artifact 页面，源文件暂存 scratchpad，未入库）；Unity 侧零改动
- 不做：本轮不改任何 Unity 代码/资源；不做家具旋转、多房间、存档持久化
- 验证计划：浏览器中人工验证摆放吸附、占位冲突、层级遮挡、桌面家具跟随桌子移动/级联收纳、解锁扣金币、重置

#### 实时进展

- 已设计并发布 Web 原型：https://claude.ai/code/artifact/f8b4f5fa-c98f-4573-9ee7-781084af2e75
- 原型内数据结构对齐未来 Unity 拆分：`FurnitureDef`（含 surfaceType/footprint/价格）、`SurfaceGrid`（cols/rows/cellSize + 占位表）、房间 Prefab 含地面格与墙面格、炼金木桌自带嵌套桌面格（移动时子家具跟随、收纳时级联收纳）
- 家具清单：地面=炼金木桌(4×2,自带4格桌面)、绒布沙发(3×2)；桌面=黄铜台灯(1×1)、魔药瓶(1×1,80金解锁)；壁挂=落地摆钟(1×2)、星象挂画(2×2,160金解锁)；初始金币 200
- 层级规则：地面家具 z = 底边深度行；桌面家具 z = 宿主桌 z + 常数；壁挂低于地面家具
- 页面内附「Unity 迁移映射」表，规划 Prefab 拆分与组件职责（复用 PlacementController 的 ghost/tint 与 BoardGrid 的占位思想）
- 已将原型集成进 `web-demo`：静态页 `public/furniture.html`（补全 HTML 骨架 + 「返回主界面」链接），主界面 right-dock 新增「家具摆放」入口
- 原 3000 端口 dev server 进程僵死（监听但零响应、大量 CLOSE_WAIT），已终止并重启；`http://localhost:3000/furniture.html` 与主页均返回 200，SSR 编译无报错
- 用户反馈方向调整：家具摆放要**在起居室场景内就地进行**（点击后收起全部 HUD），且家具直接从 `house-hub-v2.png` 背景图中抠出
- 已用 PIL 脚本（scratchpad `gen_furniture.py`）从背景图抠出 8 件带 alpha 家具切片到 `public/furniture/`（茶几/蒲团/花瓶/茶杯/红台灯/挂画/悬挂绿植/挂包），并生成洞位修补后的干净背景 `public/house-hub-clean.png`（环形取色填充+高斯模糊）；桌面小物先抠先补再抠茶几，避免切片互相残留
- 已新增 `app/furniture-editor.tsx`：全屏家具摆放模式（React 挂载 + 命令式引擎），覆盖三块基础网格（地面 14×4、左墙 6×3、窗边 4×3）+ 茶几自带 3 格桌面格；沙发/人物/落地灯等背景物件对应格子标记为场景占用（灰色不可放）；支持吸附、绿/红占位预览、深度行层级、桌面家具随茶几移动/级联收纳、双击收纳、HOUSE CREDIT 解锁（红台灯 150 / 挂包 300）、布局在会话内跨进出保留；ESC 用 capture 拦截避免触发主界面 goBack
- right-dock 「家具摆放」入口改为打开就地编辑模式（不再跳转 furniture.html；旧独立页保留作参考）
- `npx tsc --noEmit` 仅剩项目原有的 3 个 Cloudflare Worker 类型报错；主页、清洁背景、切片、编辑器模块均 200

#### 修改清单

- `web-demo/public/furniture.html`：家具系统独立原型页（新增，现为参考版）
- `web-demo/public/furniture/*.png`：8 件从背景抠出的家具切片（生成）
- `web-demo/public/house-hub-clean.png`：修补家具洞位后的干净背景（生成）
- `web-demo/app/furniture-editor.tsx`：就地家具摆放模式组件（新增）
- `web-demo/app/globals.css`：追加 `.fe-*` 家具模式样式
- `web-demo/app/page.tsx`：引入 FurnitureEditor，「家具摆放」按钮改为进入就地编辑模式
- `Docs/DEVELOPMENT.md`：登记本工作项（Unity 工程本轮无改动）

#### 验证结果

- [x] 本地服务：`npm run dev`（0.0.0.0:3000）运行中，主页/背景/切片/编辑器模块均 200，tsc 无新增报错
- [ ] 浏览器交互验证（待用户体验并反馈）
- [ ] Unity 迁移（未开始，等原型确认后立项）

#### 未完成与风险

- Unity 迁移未开始；原型未覆盖家具旋转与存档
- 桌面格尺寸(54px)与地面格(60px)不同，迁移时 cellSize 需按表面实例配置而非全局常量

#### 复盘

- 无

### 2026-08-04 · web-demo 局外 UI 移植到 Unity

- 状态：开发中
- 目标：进入 Play Mode 后显示与 `web-demo` 对应的标题页，并可进入 House HUD、切换房间、操作访客和功能面板
- 基线：项目只有节点玩法运行时 UI；DOTween 已安装；网页 Demo 已实现完整交互
- 范围：迁移网页美术资源，新增局外 UI 运行时框架、存档和 DOTween 动效
- 不做：本轮不重写原节点模拟系统，不制作独立正式构建包
- 验证计划：网页逐页比对、C# 静态编译、Unity Console 检查、目标版本 Play Mode 验证

#### 实时进展

- 已确认 DOTween 位于 `Assets/Plugins/Demigiant/DOTween/`，UI、Sprite、Physics、Audio 等模块存在。
- 已运行 `web-demo`，检查标题页、存档页、开门过场、House HUD 和主要交互状态。
- 已迁移背景、访客、家具和世界观图片到 `Assets/Resources/OutGameUI/`。
- 已新增运行时 UI 工厂、数据定义与主控制器。
- 已实现主菜单、三档存档、开门动画、四房间切换、访客事件、对话和十类系统面板。
- 已将原 `Bootstrap` 作为兼容入口。
- 首次运行发现标题菜单在创建 `CanvasGroup` 时中断。
- 已修复 Unity `Object` 伪空值与 `??` 不兼容的问题。
- 已改为 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 自动创建 UI，避免依赖当前打开的场景。
- 已把 EventSystem 兜底创建延迟到场景加载后的 `Start()`，避免双 EventSystem。
- 当前 Unity 仍在旧 Play 会话中，必须退出 Play、等待重编译后重新进入才能完成最终验证。
- 收到首轮 Unity/网页对照截图后确认：首版只完成了功能与大体风格，并未达到逐像素复刻标准；旧 Play 会话还停留在 `CanvasGroup` 异常发生时，只创建出第一项菜单。
- 已将用户给出的两张截图固化到 `Docs/References/`，后续视觉判断不再依赖临时剪贴板文件。
- 已逐项读取网页标题页 DOM 与 CSS，把字体栈、字距、菜单坐标、按钮尺寸、颜色、透明度、横纵遮罩、菜单渐变、分隔线、禁用态和 hover 缩放转换成 Unity 参数。
- 已增加 uGUI 字距 Mesh Effect、程序化渐变纹理与 `object-fit: cover` 等价布局，标题页背景不再在超宽屏被拉伸。
- 已使用 Unity 生成的 `Assembly-CSharp.rsp` 和相同 Roslyn 编译器做离线编译，返回码为 0。
- Unity Editor 尚未加载最新程序集；下一里程碑是退出旧 Play、完成刷新后在固定分辨率截图并叠图比对。
- Unity 随后成功编译并进入了包含新版标题视觉的 Play 会话，Bee 的 `Assembly-CSharp` 编译与 IL 后处理返回码均为 0，入口日志正常。
- 新 Play 日志发现页面销毁后仍有独立 DOTween 写入旧 RawImage/RectTransform；已调整为“先 Kill、后 Destroy”，并把页面级 Tween 统一绑定到 `OutGameUI` target。
- 已补齐标题菜单的键盘焦点、↑/↓ 跳过禁用项、Enter 确认，以及与 hover 共用的 1.055 倍焦点动效。
- 状态圆点与文字改为自动水平布局，严格保持网页的 12px 间距，不再使用与文案长度耦合的绝对坐标。
- 上述生命周期与键盘补丁已再次离线编译通过；因 Editor 当前仍在 Play，会在退出后由 Unity 载入。
- 根据新的维护要求，局外 UI 架构从“全部运行时代码造布局”迁移为“Prefab 负责视图、控制器只负责逻辑”。
- 已新增强类型 Prefab 引用组件，标题、纸张页、存档条目、House 外壳和系统面板均可由 Project 窗口直接打开编辑。
- 已改造 `OutGameUI`：运行时优先加载 Prefab，绑定按钮、存档文本、页面状态和 DOTween；资源缺失时才回退旧代码布局。
- 已新增非覆盖式 Editor 生成器：只自动创建缺失 Prefab，脚本刷新不会覆盖用户手调结果；恢复默认布局必须从 Tools 菜单二次确认。
- 首次生成遇到 Unity 2022.3 的内置字体名兼容问题（`Arial.ttf` 已失效），已改用 `LegacyRuntime.ttf`。
- Unity 主 Editor 已成功生成 5 个 Prefab 及 `.meta`，运行时与 Editor 程序集均完成实际编译。
- 首次保存 Prefab 暴露出 Unity 脚本序列化约束：多个可挂载组件共用一个 `.cs` 文件会在域重载后变成 Missing Script。
- 已将 7 个 Prefab/特效组件拆成同名独立脚本，并加入无损迁移器：只清理失效组件、恢复序列化引用，不修改现有 RectTransform 或视觉参数。
- 已恢复首页 6 个 DOTween Hover/Press 组件、14 个字距组件，并在运行时为 Unity 内置字体切换到 Georgia/楷体/微软雅黑回退字体栈。
- Unity Bee 已重新编译 runtime/editor 程序集；5 个 Prefab 均已写入新的强类型 View GUID，YAML 中 Missing Script 数量为 0。
- 根据用户对 Prefab 粒度的澄清，确认此前 `PaperPage.prefab` 只是共享外壳，并不满足“每个完整界面一个 Prefab”。
- 已新增 `SavePage`、`GalleryPage`、`SettingsPage`、`ExitPage` 四个完整页面 View：静态视觉节点全部进入各自 Prefab，控制器只更新存档数据、页签状态、Toggle 状态和按钮事件。
- `SavePage` 内含三个嵌套 `SaveSlot` 实例；新游戏与读取存档共用同一个可编辑布局，不再运行时创建存档条目。
- 新运行时与 Editor 生成器源码已使用 Unity Roslyn 响应文件分别编译通过；退出旧 Play 会话后，Unity 已由非覆盖式生成器落盘四个新 Prefab 并完成实际程序集重编译。
- House HUD 继续拆分为 9 个组件 Prefab：顶部栏、任务卡、访客列表/条目、右侧菜单/按钮、房间导航/按钮和场景提示；这些组件作为嵌套 Prefab 进入 `HouseHubPage`。
- `OutGameUI` 已改为绑定并刷新现有 HUD 组件；完成访客、切换房间时不再销毁 UI 后用代码重建。
- 9 个组件 View 与生成器已使用 Unity Roslyn runtime/editor 参数编译通过；当前 Play 会话尚未结束，组件 Prefab 等待域重载后落盘。
- 已定位 HUD Prefab 化后的 DOTween 回归：新组件保留了 `Button`，但生成器遗漏 `OutGameTweenButton`，因此点击逻辑正常而 Hover/Press 反馈消失。
- 已为现有页面和 HUD Prefab 增加无损动效迁移，只补行为组件、不修改 RectTransform/颜色/层级；运行时 `BindButton` 同时提供兜底。
- 页面切换现在会在销毁旧层级前清理其 Transform、CanvasGroup、Graphic Tween，避免 DOTween 在下一帧访问已销毁对象。

#### 修改清单

- `Assets/Scripts/UI/OutGameUI.cs`：局外 UI 状态、交互、存档和 DOTween 转场
- `Assets/Scripts/UI/OutGameUIFactory.cs`：uGUI 控件工厂和按钮动效
- `Assets/Scripts/UI/OutGameUIData.cs`：房间、访客、设备和档案数据
- `Assets/Scripts/UI/Bootstrap.cs`：保留场景级兼容入口
- `Assets/Resources/OutGameUI/`：网页美术资源的 Unity 副本
- `Assets/Resources/OutGameUI/Prefabs/`：可手工编辑的局外 UI 页面与控件模板
- `Assets/Scripts/UI/OutGame*View.cs`：每个 Prefab 一个同名的序列化引用组件，不包含业务逻辑
- `Assets/Scripts/UI/OutGameTweenButton.cs`：首页按钮 DOTween Hover/Press 反馈
- `Assets/Scripts/UI/OutGameLetterSpacing.cs`：首页网页字距效果
- `Assets/Editor/OutGameUIPrefabGenerator.cs`：只补缺失资产的默认 Prefab 生成器
- `Docs/PREFAB_UI_GUIDE.md`：Prefab 调整和引用保护说明
- `Docs/References/web-title-reference.png`：网页标题页像素验收基准
- `Docs/References/unity-title-before.png`：首轮 Unity 偏差样本

#### 验证结果

- [x] 对照网页实际运行状态检查页面结构
- [x] 独立 Roslyn + Unity 程序集语法检查
- [x] Unity 实际编译生成 `Assembly-CSharp.dll`
- [x] Unity Console 捕获并定位首次运行异常
- [x] 修复全场景自动入口
- [x] 保存视觉基准图并完成标题页 CSS 参数映射
- [x] 最新源码使用 Unity Roslyn 参数离线编译通过
- [x] Unity 实际生成 5 个可编辑 Prefab 与 `.meta`
- [x] Unity 实际编译 Prefab runtime/editor 脚本
- [x] 全部九个 Prefab Missing Script 清零并成功重新保存
- [x] 标题 Prefab 恢复 6 个 DOTween 按钮组件与 14 个字距组件
- [x] 四个完整页面 View 与控制器逻辑通过 Unity Roslyn runtime/editor 编译
- [x] 退出旧 Play 会话并生成 `SavePage/GalleryPage/SettingsPage/ExitPage.prefab`
- [ ] 分别打开四个完整页面 Prefab，确认无需修改 C# 即可移动内部控件
- [x] House HUD 组件 View、嵌套生成器与运行时绑定通过 Unity Roslyn 编译
- [x] 退出旧 Play 会话，生成 9 个 `Hub*.prefab` 并迁移 `HouseHubPage.prefab`
- [x] 检查所有 HUD 子 Prefab无 Missing Script，且页面中保留嵌套连接
- [x] DOTween 修复源码使用 Unity runtime/editor Roslyn 参数编译通过
- [x] Unity 域重载后确认现有 HUD 叶子 Prefab 已补入 `OutGameTweenButton`
- [x] 迁移后 Editor 日志未再出现 DOTween destroyed-target 警告
- [x] 退出首轮旧 Play 会话后重新编译，Bee C# 与 IL 后处理返回码为 0
- [ ] 退出当前 Play 会话，让最后一批 Tween/键盘补丁完成 Unity 域重载
- [ ] 在 1920×1080 与超宽 Game View 验证标题页完整显示
- [ ] 截取最新 Unity 标题页，与网页基准做透明叠图比对
- [ ] 验证新游戏 → 开门 → House HUD
- [ ] 验证面板、房间、访客、存档返回流程
- [ ] 确认 Console 无新增异常

#### 未完成与风险

- 当前使用中的 Editor 显示为 Unity `2022.3.9f1`，与项目声明的 `2022.3.62f3` 不一致；最终结果必须在声明版本复验。
- 旧版 Editor 已改写部分 `ProjectSettings` 和包锁文件，这些不是局外 UI 的功能修改，提交前必须单独审查。
- 当前日志里还有原节点模拟的 `ResourceNode.SimTick()` 越界异常；它不是本次标题 UI 引入，但会污染 Console 验收，需在最终验收时隔离或另行修复。
- “完全一致”按网页 DOM/CSS 为真值；在不同比例屏幕上验收响应式规则一致，而不是强制所有比例显示相同裁切画面。

#### 复盘

- 新增：`RETRO-001` 至 `RETRO-014`
