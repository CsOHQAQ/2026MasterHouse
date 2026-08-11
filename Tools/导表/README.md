# 家具导表流程（仿 CatVsDog export_config.bat）

> 完整文档（表格规范、字段说明、扩展新表、故障排查）见 `Docs/导表工具说明.md`；本文件是快速参考。

```
Excel/家具表.xlsx、家具房间表.xlsx    ← 策划在这里编辑（唯一数据源）
        │  双击 Tools/导表/export_config.bat
        │  （自动检查 python/openpyxl → 逐表跑 export_furniture.py / export_furniture_room.py）
        ▼
Assets/Configs/家具表.csv、家具房间表.csv   ← 自动生成，别手改
        │  CSV 在 Assets 内：Unity 资产管线感知到变化 → CsvPostprocessor 自动导表
        │  （Unity 开着：切回窗口即导；Unity 关着：下次打开时导——无需 batchmode）
        ▼
FurnitureTable.asset / FurnitureRoomTable.asset
```

- **导入是整表重建，以表格为准**：Inspector 里对这两张 SO 的手改会在下次导表时被覆盖。
- 自动导表可在菜单 `MasterHouse → 家具系统 → 自动导表（CSV 变化时）` 开关；也可手动 `从 CSV 导入家具两表`。
- 菜单 `导出家具两表到 CSV` 只回写 CSV，**不会回写 xlsx**——xlsx 是唯一编辑源。
- bat 依赖：python + openpyxl（缺 openpyxl 时 bat 会自动 pip install）。

## 家具表.xlsx · 工作表「家具」

一行一件家具：id、显示名、表面类型（`地面`/`桌面`/`壁挂`）、占格列/行、显示宽/高（场景像素）、
价格（0=初始拥有）、解禁声望、装饰分、精灵图（Resources 相对路径，如 `OutGameUI/Furniture/table`）、
桌面格启用（`是`/`否`）及桌面格 5 参数（仅启用时生效）。

## 家具房间表.xlsx · 四张工作表

| 工作表 | 列 |
|---|---|
| 房间 | 房间id、显示名、场景宽/高、背景图、景深模糊图、失焦模糊图、初始货币 |
| 网格 | 房间id、网格id、表面类型、列数、行数、格宽、格高、X、Y |
| 占用格 | 房间id、网格id、列、行（背景画面里禁止摆放的格子） |
| 初始摆放 | 房间id、家具id、网格id（地面/壁挂）或宿主家具id（桌面家具，二选一）、列、行 |

坐标与尺寸全部为场景图像素，原点左上、Y 向下；明细行通过「房间id」挂到房间上。
列顺序可以在 Excel 里调整（按表头名识别），表头文字别改；行序即导入后条目顺序。
新加一类配置表时：照 export_furniture.py 复制一个导出脚本，在 export_config.bat 里加一步即可。
