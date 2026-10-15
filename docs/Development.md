# Nova Launcher — 开发流程

## 开发流程

1. 阅读 Agent.md、Product.md、Design.md 和 Architecture.md，检查 Git 状态，确认任务范围与已有用户改动。
2. 把需求转为可验证的行为，明确输入、状态、错误反馈和完成条件。影响产品或架构的决定先同步文档。
3. UI 设计工作读取 ui-ux-pro-max 的 SKILL.md，只采纳与 Windows 桌面适配的设计建议；当前 4.1 仅增加应用导入与卡片，保持既有外壳。
4. 用户明确启动编码后，先创建并验证最小 WPF 工程，再按产品用例逐步实现。依赖版本固定，改动保持单一目的。
5. 运行与改动相称的检查，查看 diff，清理临时文件，更新文档并记录验证结果；未验证事项明确标注。
6. 在用户授权范围内提交；提交与推送分开，未经授权不推送。

## Git 规范

- 默认分支 main；功能使用 feat/<name>，修复使用 fix/<name>，文档使用 docs/<name>。
- 提交格式为 type(scope): summary，其中 scope 可省略。type 使用 feat、fix、docs、refactor、test、chore。
- 首次文档提交为 `docs: initialize project documentation`。
- 提交只包含本次任务相关文件，提交前检查暂存区 diff、空白错误和验证结果；不覆盖或丢弃他人改动。
- 不提交编译输出、缓存、运行数据库、日志、凭据和本地机密配置。第三方技能若安装在用户目录，不复制进业务仓库；仓库保留来源与版本记录。
- 不擅自 amend、reset --hard、强制推送或改写公共历史。合并前解决冲突并重跑受影响检查。

## 阶段验收流程

每阶段按“检查产物 → 执行验证 → 记录证据与遗留项 → 判断通过或返工”执行。未通过项修复后只重验受影响内容；初始化验收通过也不自动启动编码。

| 阶段 | 交付物 | 通过条件 |
| --- | --- | --- |
| 初始化（已完成） | 六份项目文档、技能接入记录、Git 首次提交 | 文档链接有效、范围一致，技能入口可读且辅助检索可执行，无应用或 UI 代码，提交信息正确 |
| 设计确认 | 布局、状态矩阵、材质适配决策 | 首期功能均有交互定义，确定最低 Windows 版本和材质回退方案 |
| 工程骨架（已完成） | WPF 工程、依赖与资源组织 | 可构建、可打开空壳窗口，MVVM 依赖方向清晰，无业务逻辑进入 View |
| UI 外壳（已完成） | 自定义标题栏、侧栏、内容占位和截图 | Release 构建、导航与键盘选择、窗口操作、尺寸适配和真实截图检查通过 |
| 4.1 导入与卡片（已完成） | .exe / .lnk 拖放、名称图标、内存去重和自动换行 | Release 零警告错误、异常与去重验证、真实 WPF/OLE 检查及运行截图；用户已授权验证后提交 |
| 首期功能 | Product.md 第一版本用例 | 添加、编辑、移除、搜索、启动及持久化验证通过，失败路径有反馈 |
| 发布准备 | 发布包和验证记录 | 在目标 Windows 环境完成启动、DPI、键盘、关闭透明效果和重启数据恢复检查 |

后续验证记录应包含日期、提交、环境、检查项、结果与遗留问题。3.x 验证记录及边界见 Design.md / Architecture.md；4.1 使用以下可执行验证工程，不声称运行了 dotnet test。

## 4.1 验证记录（2026-10-15）

环境：Windows 11、125% DPI、.NET SDK 9.0.318，本轮最终收尾已获提交授权。锁定还原通过；整个解决方案 Release build 为 **0 warnings / 0 errors**，未新增 NuGet 包。

复现命令（在仓库根目录运行，需交互式 Windows 桌面）：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.Verification -c Release --no-build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UiShell.ps1 -ScreenshotDirectory docs/screenshots/4.1/shell-regression -ForceFallback
```

NovaLauncher.Verification 通过 37 项断言，进程退出码 0：

- EXE 元数据名称与高分辨率图标；LNK 只读解析、目标路径、快捷方式显示名与来源。
- 损坏 LNK、失效目标、文档目标、目录目标、不支持格式、不存在文件、伪装 EXE 均安全失败。
- 无版本/图标资源的 PE 使用文件名和默认 Nova 图标；测试副本仅在临时目录，未执行。
- EXE 与同目标 LNK、大小写差异去重；混合批次继续导入合法项；重复执行互斥；关闭取消。
- 80 次图标提取压力检查，USER 对象计数 45 → 46，没有随提取次数线性增长；不能据此声称覆盖所有资源泄漏情形。
- 真实 MainWindow 中通过 OLE DoDragDrop/CF_HDROP 导入快捷方式与多个 EXE，验证重复项、错误提示、拖放取消与移出复原。
- 默认窗口、800 × 560 DIP 和 1400 × 800 DIP 布局换行、无横向溢出、Hover 和减少动画资源下无上浮。

测试使用专用 WPF 拖放源，控制 QueryContinueDrag 的 Continue / Drop / Cancel 并移动指针，避免物理按键轮询造成自动化不稳定。它覆盖真实 OLE 和 View 事件链，不等同于手工从 Explorer 拖动。截图来自运行中真实 WPF 窗口的 RenderTargetBitmap，不是设计稿，也不用于证明 DWM 壁纸透视。测试退出清理临时快捷方式/程序副本并恢复鼠标位置。

既有 Test-UiShell 回归覆盖六项导航、方向键、标题栏拖动命中、双击最大化/还原、窗口按钮、侧栏拖动上下限与键盘调整、最小尺寸及正常关闭。高对比度沿用动态语义画刷；未实机切换系统高对比度、混合 DPI 或透明效果，不宣称覆盖。本轮没有新增键盘导入入口，也没有验证任意规模列表的虚拟化性能。

运行截图：

- [应用卡片](screenshots/4.1/applications.png)
- [空状态](screenshots/4.1/empty.png) · [拖入高亮](screenshots/4.1/drag.png) · [Hover](screenshots/4.1/hover.png)
- [最小窗口换行](screenshots/4.1/small.png) · [宽窗口](screenshots/4.1/wide.png)

本次新增/修改文件：新增导入 Model、3 个服务实现与接口文件、Content/ApplicationCard ViewModel、ApplicationCard View、NovaLauncher.Verification 工程及锁文件、4.1 截图。修改 App 装配、MainWindowViewModel、MainWindow 的 Content 绑定、ContentView、ShellTheme 和解决方案；同步 Agent.md、AGENTS.md、README.md、Product.md、Design.md、Architecture.md 与本文。未更改背景平台实现、侧栏或标题栏设计。最终收尾仅调整卡片名称显示与 Tooltip，未修改导入架构或服务；按用户授权以 feat: add application drag and drop import 提交。


最终名称验收：TextBlock 使用 Wrap、CharacterEllipsis、BlockLineHeight，固定行高 19 DIP、文本区域 38 DIP，最多两行；卡片保持 148 DIP 高度。短名称居中、长名称省略、多个卡片等高、名称区域与卡片 Hover 显示完整 DisplayName 的真实 ToolTip、窗口缩放与自动换行均通过。Release build 0 warnings / 0 errors。
