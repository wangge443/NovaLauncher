# Nova Launcher — 开发流程

## 开发流程

1. 阅读 Agent.md、Product.md、Design.md 和 Architecture.md，检查 Git 状态，确认任务范围与已有用户改动。
2. 把需求转为可验证的行为，明确输入、状态、错误反馈和完成条件。影响产品或架构的决定先同步文档。
3. UI 设计工作读取 ui-ux-pro-max 的 SKILL.md，只采纳与 Windows 桌面适配的设计建议；当前 4.2 增加应用启动与会话统计，保持既有导入流程和外壳。
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
| 4.2 启动与状态（已完成） | 单击/键盘启动、会话统计、Hover 信息 | Release 零警告错误，EXE/LNK 真实启动、失败不计数、防重和键盘验证；最终 UI 收尾验证后按用户授权提交 |
| 首期功能 | Product.md 第一版本用例 | 添加、编辑、移除、搜索、启动及持久化验证通过，失败路径有反馈 |
| 发布准备 | 发布包和验证记录 | 在目标 Windows 环境完成启动、DPI、键盘、关闭透明效果和重启数据恢复检查 |

后续验证记录应包含日期、提交、环境、检查项、结果与遗留问题。3.x 验证记录及边界见 Design.md / Architecture.md；4.1 使用以下可执行验证工程，不声称运行了 dotnet test。

## 4.2 验证记录（2026-10-15）

基线：4.1 提交 `58bf6dc9d006b504bfce2c2a0869eda963f88a3b`。Windows 11、125% DPI、.NET SDK 9.0.318。Release 构建整个解决方案 **0 warnings / 0 errors**；没有新增 NuGet 包。用户已授权最终 UI 收尾验证后提交：feat: add application launching and session statistics。

复现（需要可见的交互式桌面，测试期间会移动鼠标并操作测试窗口）：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --launch
dotnet run --project NovaLauncher.Verification -c Release --no-build -- docs/screenshots/4.2/import-regression
```

4.2 通过 **32 项断言**，进程退出码 0：

- 时间格式：从未、刚刚、分钟前、今天、昨天、较早日期。
- 启动结果确认前不计数；成功更新模型，失败保留原统计；模拟权限异常安全反馈；取消前置请求不创建进程。
- 同一卡片 30 次快速 Command 请求只提交一次，不同卡片独立；窗口内实际 12 次快速鼠标点击只创建一个进程。
- 原生鼠标单击真实 Button 启动 EXE；Enter、Space 分别再次启动；计数和本地 LastLaunchTime 更新。
- 原始 LNK 交给 Shell，探针记录确认保留 `--from-shortcut`、含空格参数与自定义工作目录；计数独立。
- 删除源 EXE / LNK 后安全失败且统计不变；无效 EXE 被 Windows 拒绝并返回失败。
- 未启动 Hover 与已启动 Hover、固定卡片高度、800 × 560 / 1400 × 800 DIP 窗口换行；失败用例没有产生额外进程。

测试使用独立的 NovaLauncher.LaunchProbe（仅测试项目，不被 App 引用）作为真实启动目标；探针在临时目录记录实收参数、工作目录与 PID 并立即退出，共确认 5 次有效进程启动。Verification 的异常断言使用可控服务；未实际修改用户权限或执行 UAC 提权，权限不足为模拟覆盖。服务成功只指 Shell 接受请求，不承诺目标程序完成初始化。

原有 4.1 回归 **37 项断言全部通过**，覆盖解析、损坏文件、无图标、去重、真实 OLE 拖放、名称截断和完整 ToolTip、缩放与减少动画。首次真实鼠标验证因用户切换桌面/前台遮挡未命中测试窗口，在用户确认切回后通过；测试增加了指针下窗口校验，不向其他应用发送点击。

截图来自运行中的真实 WPF 窗口 RenderTargetBitmap，不验证实验性 DWM 背景。已检查状态层次和布局，原导入回归截图单独保存，未覆盖 4.1 历史截图。测试结束恢复鼠标位置、环境变量，等待测试探针退出并清理临时文件；生产启动服务不等待目标退出。

- [从未启动 Hover](screenshots/4.2/never-launched.png)
- [启动后 Hover](screenshots/4.2/launched-hover.png)
- [删除文件后的错误提示](screenshots/4.2/launch-error.png)
- [最小窗口 Hover](screenshots/4.2/small-hover.png) · [宽窗口](screenshots/4.2/wide.png)

新增文件：Services/IApplicationLaunchService.cs、ApplicationLaunchService.cs；ViewModels/LaunchTimeFormatter.cs；Verification/LaunchVerification.cs；NovaLauncher.LaunchProbe 项目、Program.cs 与锁文件；4.2 截图。

修改文件：Models/LauncherApplication.cs、ApplicationCardViewModel.cs、ContentViewModel.cs（仅注入启动服务、错误订阅与清理）、ApplicationCardView.xaml/.cs、ShellTheme.xaml、App.xaml.cs；Verification 工程与入口、解决方案；Agent.md、README.md、Product.md、Design.md、Architecture.md、本文。未修改 ApplicationImportService、ShortcutResolver、ApplicationIconService、ContentView 拖放适配、MainWindow、Sidebar、ShellBackground 或 DesktopBackdrop。

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


4.2 最终 UI 收尾：名称继续使用固定两行区域与省略号，卡片维持 136 × 148 DIP。统计文字 10.5 DIP + MutedTextBrush，上间距 4 DIP、文本高度 15 DIP；删除成功导入数量提示，不改解析、去重、启动服务、400ms guard 或背景。Release build 0 warnings / 0 errors，现有 32 项启动/窗口断言通过，最终 Hover 与窄窗口截图已检查。
