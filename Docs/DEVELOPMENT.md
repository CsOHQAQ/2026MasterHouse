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
