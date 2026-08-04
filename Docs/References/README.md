# 视觉验收基准

此目录保存局外 UI 逐像素比对所需的稳定输入，避免系统临时剪贴板图片被清理后失去验收依据。

- `web-title-reference.png`：用户提供的网页标题页目标图，原始文件尺寸 2560×1279。
- `unity-title-before.png`：用户提供的首轮 Unity 运行图，包含 Editor 窗口；该会话在首个菜单项后被旧的 `CanvasGroup` 异常中断。

验收流程：退出旧 Play 会话 → 等待 Unity 编译 → 进入全新 Play → 使用固定 Game View 分辨率截图 → 裁掉 Editor chrome → 与网页图按同一视口尺寸叠图比较。
