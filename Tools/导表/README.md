# 导表流程（仿 CatVsDog export_config.bat）

> 完整文档（表格规范、字段说明、扩展新表、故障排查）见 `Docs/导表工具说明.md`；本文件是快速参考。

```
Excel/*.xlsx（家具/商店/家具房间/访客三表/音效/对话）  ← 策划在这里编辑（唯一数据源）
        │  双击 Tools/导表/export_config.bat
        │  （自动检查 python/openpyxl → 逐表跑 export_*.py）
        ▼
Assets/Configs/*.csv                              ← 自动生成，别手改
        │  CSV 在 Assets 内：Unity 资产管线感知到变化 → CsvPostprocessor 自动导表
        │  （Unity 开着：切回窗口即导；Unity 关着：下次打开时导——无需 batchmode）
        ▼
FurnitureTable / StoreTable / FurnitureRoomTable / 访客三资产 / SfxTable / DialogueTable
```

- **导入是整表重建，以表格为准**：Inspector 里对这些 SO 的手改会在下次导表时被覆盖。
- 自动导表开关分四套，各自在菜单 `MasterHouse → 家具系统 / 访客系统 / 音效系统 / 对话系统 → 自动导表（CSV 变化时）`；
  同菜单下也有手动的「从 CSV 导入…」。
- 菜单「导出…到 CSV」只回写 CSV，**不会回写 xlsx**——xlsx 是唯一编辑源。
  **对话表没有导出回写**：SO 是产物不是源，要看内容就打开 xlsx。
- bat 依赖：python + openpyxl（缺 openpyxl 时 bat 会自动 pip install）。

## 家具表.xlsx · 工作表「家具」

一行一件家具：id、显示名、分类、描述、表面类型（`地面`/`桌面`/`壁挂`，可多选用 `/` 分隔）、
占格列/行、显示宽/高（场景像素）、装饰分、精灵图（Resources 相对路径或 `Assets/` 完整路径）、
桌面格启用（`是`/`否`）及桌面格 5 参数（仅启用时生效）。

## 商店表.xlsx · 工作表「商店」

家具的**售卖配置**（2026-08-13 从家具表拆出，独立成 `StoreTable.asset`）：id、显示名（仅对照用）、
价格（0=初始拥有）、解禁声望。**不在本表里的家具 = 非卖品**，等价于价格 0 / 解禁 0。

## 家具房间表.xlsx · 四张工作表

| 工作表 | 列 |
|---|---|
| 房间 | 房间id、显示名、场景宽/高、背景图、景深模糊图、失焦模糊图、初始货币 |
| 网格 | 房间id、网格id、表面类型、列数、行数、格宽、格高、X、Y |
| 占用格 | 房间id、网格id、列、行（背景画面里禁止摆放的格子） |
| 初始摆放 | 房间id、家具id、网格id（地面/壁挂）或宿主家具id（桌面家具，二选一）、列、行、翻转 |

坐标与尺寸全部为场景图像素，原点左上、Y 向下；明细行通过「房间id」挂到房间上。

## 访客三表 · 音效表

- `访客种族表.xlsx`（种族）、`访客日程表.xlsx`（日程）、`访客调参表.xlsx`（调参 + 氛围访客两页）
  → `Race_*.asset` / `VisitorScheduleTable` / `VisitorTuningConfig`。
  引用列写法：立绘差分 `表情=Resources路径`、日程的需求写 `NeedDef` 资产名。
  ⚠️ 日程行下标参与派生种子，**加内容请追加在表尾，别重排**。
- `音效表.xlsx`（音效）→ `SfxTable`：音效id 写 `ESfx` 枚举名、剪辑路径、音量、最短间隔秒。

## 对话表.xlsx · 两页（+ 一页参考）

详细规范见 `Docs/导表工具说明.md` §6.7——它是目前最复杂的一张表，这里只列骨架：

| 页 | 列 |
|---|---|
| 对话组 | 对话组ID、种族（`通用` 或 `/` 多选）、需求ID、所属对话池（八个 key）、进入条件、备注 |
| 对话内容 | 对话组ID、说话人、表情、**步骤/选项/句序**、类型（Line/Action/Branch）、文本、条件 |

- **三列数字序号**，行序没有语义（可任意排序）；对话组ID 每组首行写一次，后续行留空继承。
- Action 行的「文本」写函数调用串（`Accept` / `CompleteNeed(perfect)`），可用函数见「参考」页。
- 有任何错误就**整表不落盘**，Console 里每条报错都带 sheet 与行号。
- 重建一张空模板：`python Tools/导表/make_dialogue_template.py --force`（**会丢掉现有台词**）。

列顺序可以在 Excel 里调整（按表头名识别），表头文字别改；行序即导入后条目顺序。
新加一类配置表时：照 export_furniture.py 复制一个导出脚本，在 export_config.bat 里加一步即可。
