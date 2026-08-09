# CLAUDE.md

本项目"MasterHouse"（仓库名 2026MasterHouse）分为局内（节点玩法）与局外（House 经营）两侧，**两侧逻辑层均已按新框架实现**：局内见 `Docs/架构设计.md` §1~§14，局外重构已于 2026-08-10 完成（现状地图见 `Docs/局外系统说明.md`）。

- **架构权威参考：`Docs/架构设计.md`**。动手写任何代码前必读，特别是 §11【确定性与存档守则】和 §13【待定问题清单】——遇到待定项留占位符并注释编号，不要自行拍板。局外相关工作另需读 §16【局外系统架构】。
- Unity 2022.3，URP（2D）。代码注释、UI 文案、提交信息使用**简体中文**。
- 命名空间统一为 `MasterHouse`（不设子命名空间）。目录按**功能模块**划分：Core / NodeSim（局内）/ 局外各功能模块（HouseClock、Economy、Visitor、Codex、Dialogue、HouseUI、Furniture），规则与目标结构见设计文档 §15。
- 架构为 MVVM：Model（策划配置的 Def，运行时只读）→ ViewModel（数据类 + Manager）→ View（只读表现层）。逻辑层以固定 tick 推进，禁止 `Time.deltaTime`；局内局外共用 `GameManager` 的同一心跳。
- 内容数据一律进 Def 资产（§16.6）：改文案/数值 = 改 Inspector，加内容 = 加资产行，都不碰代码。
- 局外 UI 硬约定：**Prefab 是布局唯一真相源**，缺失是报错不是回退；禁止 Bind/Build 双实现与代码兜底布局（§16.2）。动态列表项走"模板 Prefab + 运行时实例化"。
- 存档一律 JSON 文件，禁止 `PlayerPrefs`（待定 #9）。当前局外存档功能已移除、只留接缝；设置项独立于存档，存 `persistentDataPath/house-settings.json`。
