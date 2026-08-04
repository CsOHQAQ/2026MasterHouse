# MasterPotion 开发复盘与踩坑库

> 类型：持续积累的工程经验库  
> 最后更新：2026-08-04

## 1. 使用方式

这里记录“为什么会卡住，以及下次如何更早发现”。单纯的功能进度写入 [DEVELOPMENT.md](./DEVELOPMENT.md)。

每条复盘必须包含：

- 现象
- 根因
- 为什么一开始容易判断错
- 修复方式
- 防复发规则
- 最小检测方法

遇到相似问题时，先按标签和症状搜索本文件，再开始试错。

## 2. 复盘模板

```markdown
### RETRO-XXX · 标题

- 标签：`Unity` `UI` `DOTween`
- 触发场景：问题发生的上下文
- 现象：用户或开发者看到什么
- 根因：真正导致问题的机制
- 误导信号：为什么容易走错方向
- 修复：本次如何解决
- 防复发规则：以后必须遵守的规则
- 最小检测：最快确认或排除的方法
- 关联文件：代码或配置路径
```

## 3. 已记录问题

### RETRO-001 · 运行时生成 UI 不等于没有搭场景

- 标签：`Unity` `UI` `入口`
- 触发场景：UI 全部通过代码运行时生成，Hierarchy 编辑态看不到完整页面
- 现象：开发者认为“场景没搭”，Play 后也没有内容
- 根因：真正的问题可能是启动入口未执行或构建过程抛异常，而不是缺少 Prefab/场景对象
- 误导信号：Hierarchy 中确实没有大量 UI 节点，容易把“实现方式”误判为“未实现”
- 修复：提供全局运行时入口，并在入口创建时输出明确日志
- 防复发规则：运行时 UI 必须有不依赖具体场景的入口；开发文档必须说明 UI 是运行时还是编辑态搭建
- 最小检测：Console 搜索 `[OutGameUI] 局外界面入口已创建。`
- 关联文件：`Assets/Scripts/UI/OutGameUI.cs`

### RETRO-002 · UnityEngine.Object 的伪空值不能用 `??` 判断

- 标签：`Unity` `C#` `运行时异常`
- 触发场景：通过 `GetComponent<T>() ?? AddComponent<T>()` 获取或创建组件
- 现象：代码看起来会自动添加组件，但设置属性时抛出 `MissingComponentException`
- 根因：Unity 对象使用重载的 `== null` 实现“伪空值”；空合并运算符 `??` 使用 CLR 引用判断，在特定 Unity 版本或定制引擎中可能拿到不可用包装对象
- 误导信号：相同代码在普通 .NET 或其他 Unity 版本中可能表现正常，静态编译也不会报错
- 修复：拆成显式判断：

```csharp
var component = go.GetComponent<CanvasGroup>();
if (component == null) component = go.AddComponent<CanvasGroup>();
```

- 防复发规则：Unity 对象、组件和资源引用一律使用 `if (obj == null)`，不要使用 `??`、`?.` 或 `ReferenceEquals` 代替 Unity 空值检查
- 最小检测：在目标 Unity 版本中运行一次组件创建路径，并观察 Console
- 关联文件：`Assets/Scripts/UI/OutGameUIFactory.cs`

### RETRO-003 · `BeforeSceneLoad` 阶段创建 EventSystem 会产生重复实例

- 标签：`Unity` `EventSystem` `生命周期`
- 触发场景：全局 UI 在 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 创建
- 现象：Console 持续提示场景中存在两个 EventSystem
- 根因：全局对象的 `Awake()` 早于场景反序列化；此时 `EventSystem.current` 为空，于是创建一个，随后场景自带 EventSystem 又被加载
- 误导信号：代码已经判断 `EventSystem.current == null`，看起来不会重复
- 修复：UI 可在场景前创建，但 EventSystem 兜底必须延迟到 `Start()` 或场景加载完成之后
- 防复发规则：全局启动阶段只创建不依赖场景状态的对象；需要扫描场景单例的逻辑放在场景加载后
- 最小检测：Play 后 Hierarchy 搜索 `EventSystem`，数量必须为 1
- 关联文件：`Assets/Scripts/UI/OutGameUI.cs`

### RETRO-004 · Unity 仍在旧 Play 会话时不会使用刚修复的代码

- 标签：`Unity` `编译` `热重载`
- 触发场景：Play Mode 中发生异常后继续编辑脚本
- 现象：文件已经修改，但同一条异常持续出现；`Assembly-CSharp.dll` 时间戳不更新
- 根因：当前 Editor 配置或定制版本在 Play Mode 中暂停自动刷新/重编译
- 误导信号：编辑器仍然响应，容易以为它已经热重载
- 修复：退出 Play Mode，等待编译完成，再重新进入
- 防复发规则：修复启动期异常后必须从一个全新的 Play 会话验证；不能在旧会话上判断修复是否生效
- 最小检测：比较脚本和 `Library/ScriptAssemblies/Assembly-CSharp.dll` 的修改时间
- 关联文件：`Library/ScriptAssemblies/Assembly-CSharp.dll`

### RETRO-005 · Unity 小版本不一致会静默改写项目配置和包版本

- 标签：`Unity` `版本` `PackageManager` `高风险`
- 触发场景：声明为 `2022.3.62f3` 的项目被 `2022.3.9f1` 打开
- 现象：`ProjectVersion.txt` 被改写，URP 和 2D 相关包发生降级，多个 ProjectSettings 文件产生无关 diff
- 根因：旧 Editor 按自身内置包和序列化格式重新解析项目
- 误导信号：场景仍能打开、脚本也可能编译，让人忽略配置损伤
- 修复：使用 `ProjectSettings/ProjectVersion.txt` 声明的准确版本打开；对版本误开产生的 diff 单独审查，不混入功能提交
- 防复发规则：打开项目前先核对 Editor 版本；版本不一致时停止并切换，不接受自动升级/降级作为普通操作
- 最小检测：启动后比较窗口标题、`ProjectVersion.txt` 和 Git 状态
- 关联文件：`ProjectSettings/ProjectVersion.txt`、`Packages/packages-lock.json`

### RETRO-006 · 静态编译通过不能代替 Unity 运行验证

- 标签：`Unity` `验证` `C#`
- 触发场景：使用 Roslyn 和 Unity 程序集对新脚本进行离线语法检查
- 现象：静态检查通过，但运行时仍发生 `MissingComponentException`
- 根因：静态检查只能确认类型和语法，无法覆盖 Unity 生命周期、伪空值、资源导入和场景状态
- 误导信号：“编译通过”很容易被口头简化成“验证通过”
- 修复：将验证拆成语法、Unity 编译、Play Mode、交互和 Console 五层
- 防复发规则：开发日志必须写清验证层级；没有目标环境 Play Mode 结果时只能标记“待验证”
- 最小检测：检查开发文档的验证清单是否包含 Console 和核心交互
- 关联文件：`Docs/DEVELOPMENT.md`

### RETRO-007 · 本地网页验证要使用隔离端口并确认页面身份

- 标签：`Web` `本地服务` `验证环境`
- 触发场景：机器上同时存在多个本地开发服务
- 现象：访问 `5173` 打开了另一个 House 管理工具；`3000` 上的旧进程无响应
- 根因：只根据常见端口猜测目标服务，没有验证进程工作目录和页面标题
- 误导信号：错误页面也包含 House 概念，看起来像相关项目
- 修复：为目标 `web-demo` 启动独立端口 `3002`，确认进程命令行、HTTP 状态和页面 DOM 后再比对
- 防复发规则：本地页面验证必须同时确认端口、进程工作目录和页面可见标题；任务结束关闭自己启动的服务
- 最小检测：检查进程命令行是否指向当前仓库的 `web-demo`
- 关联文件：`web-demo/package.json`

### RETRO-008 · 功能复刻不等于像素复刻

- 标签：`Unity` `UI` `视觉验收`
- 触发场景：把已有网页 UI 迁移到 Unity，并使用“完全复刻”作为交付标准
- 现象：按钮和页面都能工作，但菜单位置、字号、字距、透明度、遮罩和留白明显不同
- 根因：首轮实现按功能结构和主观观感重建，没有把网页 DOM、CSS 数值和参考截图转换成可量化验收项
- 误导信号：背景美术相同、文案相同且交互可用，容易让开发者过早认为复刻已经完成
- 修复：保存网页与 Unity 对照图，逐项映射 CSS；固定分辨率截图并通过叠图检查边缘、基线和透明度
- 防复发规则：任务包含“一模一样/完全复刻”时，Definition of Done 必须包含基准截图、固定视口、逐屏状态清单和图像差异检查；未做视觉比对只能标记“开发中”
- 最小检测：将 Unity 截图以 50% 透明度叠在网页基准上，主布局边缘和文字中心不得出现明显双影
- 关联文件：`Docs/References/`、`Assets/Scripts/UI/OutGameUI.cs`

### RETRO-009 · 网页 `object-fit: cover` 不能用 RawImage 拉伸替代

- 标签：`Unity` `uGUI` `响应式` `宽高比`
- 触发场景：同一张 16:9 背景需要在 16:9 与超宽视口显示
- 现象：Unity 画面中的人物和标题在不同 Game View 比例下被横向压缩或纵向拉长
- 根因：网页使用 `object-fit: cover` 保持图片比例并裁切溢出区域，Unity 的 Stretch RawImage 默认直接匹配父节点宽高
- 误导信号：在与原图宽高比接近的 1920×1080 下几乎看不出问题，到超宽分辨率才明显
- 修复：为背景 RawImage 添加 `AspectRatioFitter.EnvelopeParent`，使用纹理原始宽高比
- 防复发规则：迁移网页 `cover/contain/object-position` 时必须逐一映射到 Unity 的等价布局，不能统一使用 Stretch
- 最小检测：在 16:9 和 2:1 Game View 切换，圆形/人物头部比例应保持不变，只允许画面边缘裁切变化
- 关联文件：`Assets/Scripts/UI/OutGameUI.cs`

### RETRO-010 · 页面对象销毁前必须先终止页面级 DOTween

- 标签：`Unity` `DOTween` `生命周期` `Console`
- 触发场景：运行时生成的 UI 页面带入场动画，切页或脚本域重载时整棵 UI 被销毁
- 现象：DOTween Safe Mode 报告 RawImage/RectTransform 已销毁，但 Tween 仍尝试修改颜色或坐标
- 根因：部分直接调用的 `DOFade/DOScale/DOAnchorPos` 没有设置统一 target；`DOTween.Kill(this)` 只能终止绑定到控制器的 Sequence，遗漏独立 Tween，并且旧代码先 Destroy 后 Kill
- 误导信号：DOTween Sequence 已经设置了 target，大多数流程正常，只有切页时机恰好落在动画期间才出现
- 修复：所有页面级独立 Tween 统一 `.SetTarget(this)`；`NewView()` 先 `DOTween.Kill(this)`，再禁用和销毁旧页面
- 防复发规则：可被整体销毁的页面必须有统一 Tween owner；销毁顺序固定为 Kill → Disable → Destroy；控件自身 hover Tween 在 `OnDisable()` 中 `DOKill()`
- 最小检测：动画尚未结束时连续打开/关闭页面并返回标题，DOTween 日志不得出现 `Target or field is missing/null`
- 关联文件：`Assets/Scripts/UI/OutGameUI.cs`、`Assets/Scripts/UI/OutGameUIFactory.cs`

### RETRO-011 · 最终 UI 不应把全部布局硬编码在控制器里

- 标签：`Unity` `uGUI` `Prefab` `可维护性`
- 触发场景：原型阶段使用代码快速生成整页 UI，后续需要美术或策划逐像素调整
- 现象：任何位置、尺寸、字号或层级修改都必须改 C# 并重新进入 Play；4K 下的裁切问题无法在 Prefab Mode 直观看到
- 根因：视图结构和业务逻辑没有分层，控制器同时承担节点创建、布局、数据和交互
- 误导信号：运行时生成减少了早期资源数量，也容易复制控件，但这不等于适合最终视觉迭代
- 修复：完整页面与重复条目保存为 Prefab，使用序列化引用组件暴露控件；控制器仅实例化、写入数据、绑定事件和播放 DOTween
- 防复发规则：用户可见布局必须以 Prefab 为真值；C# 不新增页面绝对坐标。生成器只能补缺失资产，自动流程不得覆盖手调 Prefab
- 最小检测：不修改任何 C#，只移动 Prefab 中一个按钮，重新 Play 后位置应随之改变且点击逻辑仍可用
- 关联文件：`Assets/Resources/OutGameUI/Prefabs/`、`Assets/Scripts/UI/OutGameTitleView.cs`

### RETRO-012 · 可序列化 Unity 组件必须独占同名脚本文件

- 标签：`Unity` `Prefab` `MonoBehaviour` `Missing Script`
- 触发场景：把多个 `MonoBehaviour`/`BaseMeshEffect` 类定义在 `OutGameUIFactory.cs` 或一个汇总 View 脚本中，并将这些类挂到 Prefab
- 现象：当前编辑会话里组件能添加、功能也可能运行；脚本域重载后 Prefab 上的组件变为 Missing Script，保存时报 “You are trying to save a Prefab with a missing script”
- 根因：Unity 的 `MonoScript` 资产以脚本 GUID 和同名主类解析可挂载类型；类名与文件名不一致时无法稳定恢复 Prefab 的序列化类型
- 误导信号：C# 编译通过、`AddComponent<T>()` 成功、首次 Play 正常，都不能证明 Prefab 能跨域重载保存
- 修复：每个可挂载类拆为同名 `.cs`；迁移现有 Prefab 时只移除失效组件并重新绑定引用，禁止重建覆盖手调 RectTransform
- 防复发规则：一文件一可挂载组件，文件名严格等于类名；新增 Prefab 后必须完成“退出 Play → 域重载 → 打开 Prefab → 保存”验证
- 最小检测：扫描 Prefab YAML 不得出现 `m_Script: {fileID: 0}`，并在 Unity 中实际执行一次 `SaveAsPrefabAsset`
- 关联文件：`Assets/Scripts/UI/OutGame*View.cs`、`Assets/Scripts/UI/OutGameTweenButton.cs`、`Assets/Scripts/UI/OutGameLetterSpacing.cs`、`Assets/Editor/OutGameUIPrefabGenerator.cs`

### RETRO-013 · “Prefab 化”必须先确认用户期望的资源粒度

- 标签：`Unity` `Prefab` `需求澄清` `UI 架构`
- 触发场景：用户要求“把这些界面做成 Prefab”，实现时把存档、画廊、设置和退出统一套在一个 `PaperPage` 外壳中
- 现象：虽然存在 Prefab，也能调整公共边框，但各页面内部卡片、页签和按钮仍由 C# 创建，无法在 Project 窗口直接编辑完整界面
- 根因：把“共享视觉外壳 Prefab”误当成“完整页面 Prefab”，没有以用户的实际编辑工作流定义资源边界
- 误导信号：运行时确实实例化了 Prefab、代码量也减少了，但美术仍需修改 C# 才能调整页面内容
- 修复：存档、画廊、设置、退出各自建立完整 Prefab；共享纸张结构通过同名 View 基类和编辑器生成辅助复用，运行时只绑定数据和交互
- 防复发规则：Prefab 化前明确列出最终 Project 目录中的资源清单，并用“只改这个 Prefab 能否调整整个界面”作为验收问题
- 最小检测：关闭代码编辑器，只打开目标页面 Prefab，应能选中并移动页面内所有静态按钮、标题、卡片与容器
- 关联文件：`Assets/Resources/OutGameUI/Prefabs/`、`Docs/PREFAB_UI_GUIDE.md`

## 4. 开发前快速检查表

- [ ] Unity 窗口版本与 `ProjectVersion.txt` 完全一致
- [ ] 当前 Git 状态已记录，能区分用户修改与本次修改
- [ ] 当前打开的是目标场景或功能拥有全局入口
- [ ] 运行时生成 UI 的入口日志可见
- [ ] EventSystem 数量为 1
- [ ] DOTween 核心 DLL 和所需 Module 存在
- [ ] 新资源已被 Unity 导入并生成 `.meta`
- [ ] 修复启动异常后重新进入全新 Play 会话
- [ ] “完全复刻”任务已保存网页基准图，并固定验收分辨率
- [ ] 网页图片的 `cover/contain/object-position` 已逐项映射
- [ ] 页面 Tween 已绑定统一 owner，并在销毁前 Kill
- [ ] 所有挂载到 Prefab 的自定义组件均位于同名独立 `.cs` 文件
- [ ] Prefab 已经历域重载并能再次保存，且无 Missing Script
- [ ] Console 无新增异常
- [ ] 开发文档和复盘已同步
