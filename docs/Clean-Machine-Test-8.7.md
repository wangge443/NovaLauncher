# Nova Launcher 8.7 — Clean Machine Validation

## 8.7.1 闭环更新（当前状态）

**NOT READY FOR PREVIEW**。用户已人工确认 Profile → Settings 与 Glass Alt+Tab 正常；两项自动失败均归类 TEST HARNESS ISSUE，修正验证方式后完整 Portable Smoke 12 项、系统窗口专项 8 项通过。历史旧库由候选 EXE 升级与重启已 PASS；针对已升级库要求重复迁移的旧脚本为 NOT APPLICABLE。Startup ON/OFF 两轮真实 Windows Restart 仍为 NOT TESTED。产品与透明实现未修改，候选 SHA256 保持不变。详情见 [8.7.1闭环报告](Preview-Release-Gate-Closure-8.7.1.md)。下文 8.7 失败统计保留为历史记录，不代表这三项仍未闭环。

**结论：NOT READY FOR PREVIEW。** 本文包含开发机验证结果和待用户填写的干净机器验收表，不是 Clean Machine 通过证明。用户已明确由其人工测试真实干净 Windows。未创建 Git tag、GitHub Release 或新提交；没有新增产品功能、依赖、Schema、Migration 或视觉改动。

## 环境与候选身份

- 日期：2026-10-23（America/Anchorage；日志使用 UTC，因此部分日志日期为 2026-10-24）。
- 基线：`16fde14f7476329273050d3afada78e167ac662e`，8.6 已正式提交；8.7 开始前工作区 Clean。
- 版本：`0.1.0-preview.1`，Self-contained win-x64 Portable 候选。
- 开发机：Windows 11 家庭版中文版，10.0.26200 / Build 26200，x64；Intel Core i7-13620H；RAM 16,858,472,448 bytes；HP Victus 16-r0xxx 实机，非 VM；安装 .NET SDK 9.0.318。
- **Clean Windows 版本 / Build / CPU / RAM / VM 配置：NOT TESTED，待人工填写。不得使用上述开发机参数代填。**
- ZIP：`D:\openAI\codex\NovarLauncher\artifacts\publish\NovaLauncher-0.1.0-preview.1-win-x64-portable.zip`
- ZIP 大小：**77,813,436 bytes**（约 74.21 MiB）。
- SHA256：`7898C3EAA257D38E9BF12D835DCA86B4DD9C0F496DC6EE642F95CC8966DA109C`

执行 `dotnet restore NovaLauncher.sln`、`dotnet build NovaLauncher.sln -c Release`，结果 **0 warnings / 0 errors**。统一测试探针目录额外构建同样零警告零错误。正式候选通过现有 Publish.ps1 先校验路径、清空 self-contained 目标目录，再完整生成；没有复用过期发布文件。

ZIP 的 491 个文件与正式发布目录逐项 SHA256 一致；从 ZIP 解压后再次验证 manifest。未发现 nova.db、WAL/SHM、用户头像/背景、自定义图标、缓存、Logs、测试截图、docs、源码、bin/obj、Registry 导出或其他用户数据。

EXE 元数据：Product Name `Nova Launcher`；File Description `Personal Windows Application Launcher`；File Version `0.1.0.0`；Product Version `0.1.0-preview.1+16fde14f7476329273050d3afada78e167ac662e`。`+commit` 是既有 .NET informational version 构建元数据，Version.props 中产品版本仍为 `0.1.0-preview.1`，本轮未改版本策略。EXE/窗口/托盘引用既有 Nova.ico；干净机上任务栏图标仍需人工目视核验。

## 本轮自动验证统计

**33 组最终验证记录：30 组完整通过，3 组未通过或前提不适用。完整通过组共 943 项断言。** 另有未通过组的 21 项前置检查通过，未混入 943。该数量不是独立用户场景数，也不包括错误调用与已复跑的重复断言。

| 验证组 | 结果 | 完整通过断言 |
| --- | --- | ---: |
| 原有 20 组业务/持久化/视觉回归 | PASS | 728 |
| Hotkey 服务、持久化、冲突 | PASS | 46 |
| Tray、设置、注册抽象、Migration | PASS | 29 |
| Glass 与背景专项，300 应用 | PASS | 28 |
| 托盘实际生命周期与 30 秒空闲 | PASS | 14 |
| 真实全局热键 / Enter 启动探针 | PASS | 42 |
| 真实 HKCU Run 注册、删除、原值恢复 | PASS | 5 |
| 候选 ZIP 的实际 EXE 托盘菜单/热键/退出 | PASS（原样复跑） | 15 |
| 候选 Portable 启动、运行时、单实例、强杀恢复 | PASS | 6 |
| Chrome 前台 / F11 / 热键修改、冲突、重启 | PASS | 15 |
| 历史旧库副本由候选 EXE 升级并重新启动 | PASS | 15 |
| 当前真实库副本的旧版升级专用脚本 | FAIL / 前提不适用；10 个前置检查通过 | — |
| 完整 Portable UI 冒烟 | FAIL；11 个前置检查通过 | — |
| 原生透明窗口 Alt+Tab / Win+D / Snap 专项 | FAIL；首项未通过，后续未执行 | — |

回归 803 项来自 22 组：Persistence 25、AllApplications 26、Favorites 31、Recent 22、Profile 44、Management 46、AppEdit 59、Pinyin 123、Launch 32、Toast 11、Polish 19、Import 37、Categories 34、Settings 44、Migrations 30、ReleaseSafety 13、Logging 11、PerformanceBehavior 13、Appearance 39、QuickLauncher 69、Hotkey 46、Tray 29。

原始日志：`artifacts/verification/8.7/`。`regression/results.json` 是逐组结果；`restore.log` / `build.log` / `publish.log` / `package-audit.json` / `package-files.txt` 为构建和包证据。测试没有并发抢占桌面。

## 30 项最终输出

| # | 项目 | 开发机结果与真实边界 |
| ---: | --- | --- |
| 1 | Release Build | PASS，0 warnings / 0 errors。 |
| 2 | 自动测试 | 33 组：30 完整通过、3 未通过/不适用；943 完整通过组断言，不能标为全绿。 |
| 3–5 | ZIP / 大小 / SHA256 | 见上方候选身份，文件已生成，但不是获准对外发布的 Release。 |
| 6 | Clean Windows | NOT TESTED，用户人工执行。 |
| 7 | 首次启动 | 开发机复制包 PASS，无 Runtime/DLL/SQLite 安装提示；干净机 NOT TESTED。 |
| 8 | 数据库初始化 | 开发机隔离新库自动创建且 Migration 正常；干净机 NOT TESTED。 |
| 9 | 导入 / 启动 | 回归真实 EXE/LNK 与发布版 OLE/启动探针通过；干净机真实应用 NOT TESTED。 |
| 10 | Quick Launcher | 回归 69、发布 UI 全拼、热键 Enter 启动通过；干净机 NOT TESTED。 |
| 11 | Global Hotkey | Chrome 前台、F11、最小化、冲突/修改/关闭/重启通过；管理员前台 NOT TESTED。 |
| 12 | Tray | 候选 EXE 实际通知区、四个菜单入口、退出 PASS；首次 UIA 失败保留，见问题表。 |
| 13 | CloseToTray | 隐藏后进程、托盘与热键仍在；Off 时 X 退出；最小化保持普通行为，PASS。 |
| 14 | Restart 后 Startup | **NOT TESTED / Awaiting Manual Reboot Verification**；`--startup` 模拟不替代真实登录。 |
| 15 | Glass 真实透明 | 开发机 PASS：后窗移动和 Nova 移动独立改变合成像素；不是只有壁纸。干净机 NOT TESTED。 |
| 16 | Glass 恢复 | 隐藏/热键恢复保留 HWND、AllowsTransparency 和 Window.Opacity=1，PASS。 |
| 17 | Single Instance | 手动二次启动及隐藏状态激活通过，无第二个长期实例。 |
| 18 | 强制结束恢复 | Portable EXE 杀进程后重新启动通过；独立热键脚本验证释放与重新注册。 |
| 19 | Logs | 正常生成 UTF-8 日志；数据库、迁移、Tray、Hotkey、Shutdown 有证据；回归 11 项通过。 |
| 20 | Portable 路径 | 从 ZIP 解压到源码外 `%TEMP%\Nova Preview 8.7 中文 <guid>`，实际 EXE 的托盘与热键通过。仍为开发机 Portable Path Test。 |
| 21 | Upgrade | 保留的 8.6 前旧库备份复制到隔离目录，候选 EXE 自动迁移；所有 Applications/Categories/UserProfiles/AppSettings 旧字段不变，第二次启动不重复备份。完整干净机“替换程序目录 + 用户资产”仍待人工。 |
| 22 | 后台资源 | 本轮隐藏稳定 3 秒后测 30 秒：CPU 0.094%（占总算力）；托管 83,858,312 → 85,313,960 bytes；工作集 582,909,952 bytes。无持续高 CPU，托管净增加约 1.39 MiB，不能据单窗口断言长期完全稳定。非 Task Manager 人工读数。 |
| 23 | 多显示器 | NOT TESTED。 |
| 24 | 管理员前台 | NOT TESTED。 |
| 25 | 全屏 | Chrome F11 PASS；独占全屏游戏 NOT TESTED。 |
| 26 | Explorer 重启 | NOT TESTED；未重启用户 Explorer。 |
| 27 | P0 / P1 | 当前未确认产品 P0/P1；存在未澄清的窗口专项失败，不能据其他通过项排除缺陷。 |
| 28 | P2 / P3 | 见问题记录：完整冒烟终止、系统前台测试、测试前提和目录预期差异。 |
| 29 | Known Limitations | 见后文，Glass 实验性、Portable 路径依赖、未运行时热键无效、Windows 前台限制等。 |
| 30 | 最终结论 | **NOT READY FOR PREVIEW**：未通过自动化记录待处理，真实干净机及两次登录启动验收尚缺。 |

## 问题与复现（本轮不修改产品实现）

| ID / 等级 | 问题、复现与证据 | 发布影响 |
| --- | --- | --- |
| V87-01 / P2 验证脚本 | `Test-Publish.ps1` 完整运行至第二次 Profile 检查，Profile flyout 已打开；最终 Close-Nova 调 Open-Settings 再次点击头像，随后找不到 ProfileSettings。日志 `publish-smoke.log`，截图 `publish-smoke/failure.png`，11 个前置检查通过。代码表现与“切换已打开菜单使其关闭”一致，但未修改脚本或把这组标 PASS。 | 完整包验收未闭环，阻止声明全绿；不是已确认产品无法退出。独立托盘真正退出、启动冒烟退出通过。 |
| V87-02 / P2 待定位 | `--transparency-system <fixture-300> <system>` 第一项要求 Alt+Tab 精确切到指定 companion HWND，实际断言失败，`system.log`。后续 Win+D / Snap 未执行。未加入强抢前台或修改窗口代码。 | 原生窗口专项 FAIL；原因尚未区分系统前台条件、测试时序或产品缺陷，发布 Gate 未通过。 |
| V87-03 / P2 测试前提 | `--tray-real-upgrade <real-upgrade>` 针对当前真实库只读备份。备份已包含全部 9 个 Migration，仍无条件断言必须生成 1 个迁移前备份，失败。10 个前置检查确认数据与生产源库不变。 | 此命令不适用当前已升级库；没有新迁移时不备份符合现有设计。另用历史旧库与实际候选 EXE 完成 15 项验证，旧脚本失败仍保留。 |
| V87-04 / P2 自动化时序 | 候选托盘首次运行报 Nova Launcher Button 无屏幕矩形；原样复跑 15 项通过，未改脚本。记录 `published-tray-first.log` 与 `published-tray.log`。 | 不是稳定复现的产品故障，仍说明通知区 UIA 验证不稳定；人工须点验四个菜单。 |
| V87-05 / P2 测试部署 | 默认 Verification 输出目录有 LaunchProbe.exe，但没有对应 DLL；热键最后启动探针超时。改用既有统一 OutputPath 构建后 42 项通过。保留 `hotkey-live-incomplete-probe.log`。 | 测试工具部署问题，候选包不包含 LaunchProbe；未改产品。 |
| V87-06 / P3 验收预期 | Profile / Background / CustomIcons 在首次保存相应资产时创建；并非空白首次启动就保证所有目录存在。存储服务中的 CreateDirectory 与当前行为一致。 | 不应单凭空目录缺失误判数据库初始化失败；按用户原始要求记录差异，待确认是否接受按需创建，不做产品改动。 |

最初批处理参数拼接错误曾调用默认导入测试，相关错误调用记录已排除；被覆盖的历史截图已恢复，随后 22 组按正确参数完整通过。各历史失败日志保留，不以重复运行累加通过数。

## 人工 Clean Machine 验收表

下表每项目前均 **NOT TESTED**。请把“实际结果/证据”填写为 PASS / FAIL / NOT TESTED，并记录失败步骤。P0/P1 必须阻塞发布。只能在下列核心项和自动验证异常均处理后改为 READY FOR PREVIEW。

测试机记录：测试人 ____；日期/时区 ____；Windows 版本 ____；Build ____；x64 ____；CPU ____；RAM ____；VM 名称/CPU/RAM/显示配置（如适用）____。

| 顺序 | 操作与通过标准 | 实际结果 / 证据 |
| ---: | --- | --- |
| 1 | 干净 Windows 10/11 x64；无源码/VS/SDK/Git 依赖，不预装 Desktop Runtime；确认无 Nova 进程、无 LOCALAPPDATA/NovaLauncher、无 Run/NovaLauncher。已有数据时换干净用户或 VM，不直接删除真实数据。 | NOT TESTED |
| 2 | 核对本报告 ZIP 文件名与 SHA256，解压到固定目录，普通权限直接双击 EXE。 | NOT TESTED |
| 3 | 无 .NET / DLL / SQLite 缺失提示，无管理员要求；图标、TitleBar、Sidebar、All Applications 正常。 | NOT TESTED |
| 4 | LOCALAPPDATA/NovaLauncher/Data/nova.db 和 Logs 自动出现，Migration 自动完成；资产目录按 V87-06 记录。 | NOT TESTED |
| 5 | 导入 notepad.exe 和真实 .lnk；名称/图标/重复检测/Toast/卡片正确。 | NOT TESTED |
| 6 | 启动两应用，LaunchCount、LastLaunchTime、Recent 正常；托盘退出再打开仍保留。 | NOT TESTED |
| 7 | 新建分类、移动应用、收藏/取消收藏、Recent 倒序；退出重启后全部保持。 | NOT TESTED |
| 8 | Ctrl+K：英文、中文、全拼、首字母（仅搜索实际已导入项），Up/Down/Enter/Esc 和焦点正常。 | NOT TESTED |
| 9 | 浏览器/Explorer/记事本前台、Nova 最小化时 Ctrl+Alt+Space 恢复并聚焦；修改快捷键立即生效，冲突保留旧键，恢复默认。 | NOT TESTED |
| 10 | X 隐藏：任务栏按钮消失，进程与 Tray 保留；热键唤醒。Off 时 X 退出，最小化仍为普通行为。 | NOT TESTED |
| 11 | 托盘打开 Nova / 快速启动 / 设置 / 退出逐项正确；退出后进程、图标、热键消失；重新启动全部恢复。 | NOT TESTED |
| 12 | 已运行和隐藏时二次双击只保留一个主实例，并恢复窗口。 | NOT TESTED |
| 13 | Profile 改名/头像、托盘隐藏恢复、退出重启、恢复默认头像；Settings 的记忆尺寸/位置及三种 StartupPage 分别重启核验。 | NOT TESTED |
| 14 | Default / DesktopWallpaper / CustomImage / Glass 分别切换并退出重启，设置保留。 | NOT TESTED |
| 15 | Glass 后放记事本/Explorer/浏览器，移动后窗与 Nova，透明实时变化；Settings/Quick Launcher/Context Menu 可读，无黑白屏/冻结。 | NOT TESTED |
| 16 | Glass 下 X 隐藏再热键恢复、Minimize/Maximize/Restore/Resize；补测 Alt+Tab 双向切换及 Win+D / Snap，观察 Resize 延迟。 | NOT TESTED |
| 17 | Task Manager End Task 后再打开，Mutex/Pipe/Tray/Hotkey 重新工作，已保存数据保留。 | NOT TESTED |
| 18 | 检查 Logs 的 Startup/Database/Migration/Tray/Hotkey/Shutdown，中文正常且没有持续刷屏。 | NOT TESTED |
| 19 | 开启 Startup，确认 HKCU Run/NovaLauncher 为带引号实际 EXE 路径加 --startup。 | NOT TESTED |
| 20 | **真实重启或注销登录**：主窗口不自动出现，后台进程和 Tray 存在，热键打开并聚焦 Quick Launcher，数据保留，只有一个实例。 | NOT TESTED |
| 21 | 关闭 Startup，确认 Run 值删除；**再次重启/登录**，Nova 不自动运行。测试最终保持 Off。 | NOT TESTED |
| 22 | 开启 Startup 后移动程序到带空格目录并手动启动，确认 Run 路径修复；方便时再测中文目录。结束后关闭 Startup。 | NOT TESTED |
| 23 | 重命名、自定义/恢复图标、打开文件位置、应用信息、移除再导入；真实 EXE/LNK 未被删除。 | NOT TESTED |
| 24 | 隐藏到 Tray 至少 30 秒，记录 Task Manager CPU/Memory 与时间；方便时测 300 应用滚动/搜索。 | NOT TESTED |
| 25 | 保持测试用户数据，换成本轮程序目录；核对 Applications/Categories/Favorites/Recent/Profile/Settings/Background/Hotkey/CloseToTray 均保留。 | NOT TESTED |
| 26 | 多显示器窗口位置/Glass/热键恢复；无设备则 NOT TESTED。 | NOT TESTED |
| 27 | 普通权限 Nova 对管理员前台程序唤醒，记录 PASS/FAIL/LIMITED；不默认提升 Nova。 | NOT TESTED |
| 28 | 浏览器 F11；独占全屏游戏方便时另测，否则 NOT TESTED。 | NOT TESTED |
| 29 | 测试机上安全时重启 Explorer，记录 Tray 恢复；未执行保持 NOT TESTED。 | NOT TESTED |

建议回传：测试环境信息、每项结果、首次启动/Glass 动态变化/托盘菜单截图、两次登录启动结果、失败复现和相关日志。无需把真实数据库或个人资产放进发布包。

## Known Limitations 与截图

Glass Mode 为 Experimental；透明 Resize 存在既有额外开销，本轮不重做精确 Benchmark。退出 Nova 后全局热键不可用。Startup 依赖 Portable 路径，移动后须手动启动修复，删除文件夹可能留下失效 Run 值。Windows 前台/UIPI/独占全屏策略可能限制激活。多屏、管理员前台、独占游戏、Explorer 恢复尚未验证。Single-file 仍为部分验证，不作为推荐包。

下列均为**开发机本轮截图**，不是干净机证据：

- [候选包首次启动](screenshots/8.7/portable-first-start.png)
- [Glass 设置与托盘恢复](screenshots/8.7/tray-settings-glass.png)
- [实际 Tray](screenshots/8.7/tray-icon.png) / [右键菜单](screenshots/8.7/tray-menu.png)
- [真实透明：移动前](screenshots/8.7/transparent-before.png) / [后窗移动](screenshots/8.7/transparent-behind-moved.png) / [Nova 移动](screenshots/8.7/transparent-nova-moved.png)

本轮没有产品改动。待人工结果和验证失败澄清后再评估 Release Gate，不能把候选 ZIP 的存在等同于 READY。
