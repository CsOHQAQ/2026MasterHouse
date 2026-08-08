# CLAUDE.md

本项目"MasterHouse"（仓库名 2026MasterHouse）正在进行运行架构重构：旧原型代码已移除，新骨架位于 `Assets/Scripts/`，大部分类尚为空壳。

- **架构权威参考：`Docs/架构设计.md`**。动手写任何代码前必读，特别是 §11【确定性与存档守则】和 §13【待定问题清单】——遇到待定项留占位符并注释编号，不要自行拍板。
- Unity 2022.3，URP（2D）。代码注释、UI 文案、提交信息使用**简体中文**。
- 命名空间统一为 `MasterHouse`（现有代码中混用的 `Data` 命名空间待清理，见设计文档 §12）。
- 架构为 MVVM：Model（策划配置的 Def，运行时只读）→ ViewModel（数据类 + Manager）→ View（只读表现层）。逻辑层以固定 tick 推进，禁止 `Time.deltaTime`。
