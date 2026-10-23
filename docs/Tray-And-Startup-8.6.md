# 8.6 System Tray + Startup

用户已通过验收并授权最终收尾提交 `feat: add system tray and startup support`。基线：`df30026d870b9f90f715237a30790ce904235475`。真实登录启动状态：**Awaiting Manual Reboot Verification**。本轮未进行 Auto Update、Installer 或 Clean Machine Test。

## 产品行为与实现

| 验收项 | 结果 |
| --- | --- |
| 1. Tray 技术 | Windows Forms `NotifyIcon`，WPF 互操作；复用 Resources/Nova.ico，Tooltip 为 Nova Launcher。 |
| 2. 第三方依赖 | 无。仅启用 `UseWindowsForms`，排除冲突的隐式命名空间；依赖锁文件未变化。 |
| 3. 服务结构 | `ITrayIconService` / `TrayIconService` 管理图标、菜单、一次提示和 Dispose，以 `TrayAction` 事件请求窗口操作；不承担数据库或搜索。 |
| 4. 菜单 | 打开 Nova / 快速启动 / 分隔线 / 设置 / 分隔线 / 退出 Nova；原生 ContextMenuStrip。单击、双击均恢复主窗口。 |
| 5. X 按钮 | CloseToTray 为 true 时关闭浮层、取消快捷键录制、保存窗口位置并 Hide；最小化仍走任务栏。首次隐藏提示，后续不重复。 |
| 6. 真正退出 | 托盘“退出 Nova”标记 ExplicitExit，抑制热键激活、完成待保存工作，注销热键、Dispose 托盘、停止单实例 IPC，再 Close / Shutdown。关闭后台驻留后 X 同样退出。 |
| 7. Startup 机制 | 当前用户 Run 注册，无管理员权限、服务或辅助常驻进程。 |
| 8. Registry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `NovaLauncher`，内容 `"实际绝对路径\NovaLauncher.exe" --startup`。 |
| 9. --startup | 创建 HWND 但不 Show、不抢前台、不打开 Quick Launcher；初始化核心数据、索引、托盘和已有热键。后台应用卡片视觉控件数为 0。 |
| 10. 开机启动默认值 | Off，不自动写入 Run。 |
| 11. CloseToTray 默认值 | 新用户与升级用户均为 true，满足手动打开后热键继续可用的目标；首次提示和明确退出菜单解释行为，Settings 可关闭。托盘始终存在。 |
| 12. Migration | `20261023092432_AddCloseToTraySetting`，仅 AppSettings 增加 `CloseToTray INTEGER NOT NULL DEFAULT 1`。需要跨启动保存关闭行为，因此采用最小迁移；无 StartWithWindows 字段。 |
| 13. Source of Truth | Registry 是启动注册的唯一来源；Settings 加载/窗口恢复时读取，不轮询。删除值后不会自动重新开启；读写失败反馈并呈现实际状态。 |
| 14. 移动目录 | 下次手动启动仅修复已有、可识别的 Nova 注册路径；缺失或外来命令不自动重写。直接删除 Portable 文件夹可能留下失效值。 |
| 15. 单实例 | 隐藏时手动二次启动通过原 IPC 恢复完整主窗口；第二个 --startup 静默退出，不唤出窗口。协议未改。 |
| 16. 后台热键 | 复用 8.5 RegisterHotKey 与原 Quick Launcher；隐藏后可以唤醒并聚焦搜索框。Ctrl+K 保持应用内行为。 |
| 17. Glass | 隐藏/恢复保持原 HWND 与 Window.Opacity=1；后台切换背景不显示窗口；透明底层实现未改变。 |
| 18. CPU / Memory | 隐藏并稳定 3 秒后观察 30 秒：CPU 占总算力约 0.003%；托管内存 90,882,456 → 90,907,128 bytes，基本稳定；工作集 578,359,296 bytes。未强制 GC，包含曾显示的 300 应用 UI。 |
| 19. 启动性能 | 见下方 10 / 100 / 300 应用的三次中位数。后台 Ready 早于全部卡片完整显示。 |
| 20. Self-contained | 重新生成 win-x64 Portable；实际 EXE 验证记录见下文。没有将 Single-file 升级为主交付。 |
| 21. Release Build | 0 warnings / 0 errors。 |
| 22. 自动测试 | 见下方分组，不重复累计同一测试的复跑。 |
| 23. 人工重启 | **Awaiting Manual Reboot Verification**，步骤见下方。 |
| 24. Settings 截图 | [Default](screenshots/8.6/tray-settings-default.png)、[Glass](screenshots/8.6/tray-settings-glass.png)。 |
| 25. Tray 截图 | [真实通知区域](screenshots/8.6/tray-icon.png)。 |
| 26. 右键菜单截图 | [原生托盘菜单](screenshots/8.6/tray-menu.png)。 |

托盘初始化失败时保持可见主窗口并允许 X 退出，避免无入口的后台进程。首次提示标记保存在应用数据目录 `tray-hint-shown`，不增加数据库字段。隐藏时暂停已有 Progressive Rendering 调度和卡片 Hover/时间刷新计时器；再次显示继续原有首批 40、每批 20 策略。没有改搜索、业务集合或 8.2 性能架构。性能探针结束使用显式退出，避免被新的 X 行为留在托盘。

## 生命周期

```text
Windows Login → HKCU Run → NovaLauncher.exe --startup
→ Single Instance → Core Initialization → TrayIconService
→ GlobalHotkeyService → Background Ready（不 Show）

MainWindow X → CloseToTray?
  Yes → 关闭浮层 / 保存位置 → Hide → 保持热键
  No  → Flush → 释放热键 / 托盘 / IPC → Shutdown

Tray Exit → ExplicitExit → 抑制激活 / Flush
→ UnregisterHotKey → DisposeTray → Dispose IPC → Shutdown
```

日志记录初始化、隐藏/恢复、注册开关/失败/路径修复、--startup 和显式退出，不进行高频轮询。

## 性能

Verification 宿主运行生产 App 初始化链，隔离数据库和图标缓存；各规模各模式测量三次取中位数，时间起点为进程 StartTime。普通模式终点为全部应用卡片就绪，不是 8.2 首屏指标；后台终点不包含 UI 卡片。此数据不能替代实际 Windows 登录测量。

| 应用数 | Tray Ready | Hotkey Ready | Background Ready | 手动完整 UI Ready |
| ---: | ---: | ---: | ---: | ---: |
| 10 | 2045.50 ms | 2058.54 ms | 2061.51 ms | 2373.39 ms |
| 100 | 2278.13 ms | 2291.07 ms | 2293.30 ms | 3665.59 ms |
| 300 | 2863.56 ms | 2875.43 ms | 2877.67 ms | 7180.33 ms |

所有后台样本卡片数为 0，普通模式为对应的 10 / 100 / 300。原始记录：`artifacts/verification/8.6/probes/`。后台内存稳定，但不表示已释放曾打开的整个工作区；这保持现有窗口恢复策略。

## 验证记录

| 验证 | 通过断言数 |
| --- | ---: |
| 原有完整回归 | 728 |
| 热键服务回归 | 46 |
| 实际全局热键 / 原启动管线 | 42 |
| Glass / 背景切换 | 28 |
| 托盘、注册抽象、设置回滚、迁移 | 29 |
| 实际隐藏启动 / 恢复 / 空闲观察 | 14 |
| 真实 HKCU Run 开关及恢复 | 5 |
| 真实数据库备份副本升级 | 11 |
| 最终 Self-contained 托盘 / 隐藏热键 / 菜单 / 退出 | 15 |
| 最终 Portable 启动、安全隔离与退出冒烟 | 6 |

合计 **924 项通过断言**（728 项原有回归 + 196 项热键、Glass、托盘和发布验证），不是 924 个独立测试用例。最终发布版运行 Glass，验证所有菜单入口、启动开关、二次启动以及退出后立即重新注册原热键成功。发布包复制到临时路径启动，实际加载随包 coreclr.dll；无数据库、日志、PDB 混入包内，生产用户目录元数据未改变。

托盘脚本首次新增菜单检查失败，原因包括 PowerShell 字符串重载、Shell 中图标/Tooltip 重名和通知区布局时序；最终按 Button / MenuItem 类型定位并等待隐藏后的 Shell 布局，15 项全部通过。没有为通过自动化而改动产品托盘行为。

原有回归包括 Persistence 25、AllApplications 26、Favorites 31、Recent 22、Profile 44、Management 46、AppEdit 59、Pinyin 123、Launch 32、Toast 11、Polish 19、Import 37、Categories 34、Settings 44、Migrations 30、ReleaseSafety 13、Logging 11、PerformanceBehavior 13、Appearance 39、QuickLauncher 69。

Settings 兼容测试原先通过 Close 结束子进程；新的默认驻留造成超时。测试终止改用 RequestExplicitExit 后 44 项通过，没有删除断言。其他依赖退出的旧探针同样适配，分类测试超时会结束其自有子进程。实际 X 驻留/退出由新生命周期测试覆盖。

所有测试使用隔离根目录。真实注册表验证先保存原值及类型，测试发布 EXE 注册与删除，finally 恢复并核验。其他 UI 测试使用 `Software\NovaLauncher\Verification\<root hash>\Run`，不写生产 Run。真实库通过在线备份复制后升级，核对旧数据及新增默认值，生产数据库未直接用于实验。

日志位于 `artifacts/verification/8.6/`，最终复核 Release 为 0 warnings / 0 errors，构建记录为 `build-final.log`；发布包位于 `artifacts/publish/NovaLauncher-0.1.0-preview.1-win-x64-self-contained.zip`。

## 待人工验证与边界

实际 Windows 注销/重启没有执行，状态为 **Awaiting Manual Reboot Verification**：

1. 从保留位置的 Portable EXE 打开 Settings，开启“开机启动”。
2. 检查 HKCU Run 的 NovaLauncher 值指向该 EXE，并含 `--startup`。
3. 注销 Windows 或重启。
4. 登录 Windows。
5. 确认 Nova 主窗口没有自动弹出。
6. 确认通知区出现 Nova 图标（可能位于折叠区域）。
7. 按 Ctrl+Alt+Space。
8. 确认 Quick Launcher 打开、搜索框聚焦且可搜索启动。
9. Task Manager 确认仅一个 Nova 实例。

完全退出后热键不可用，属于设计行为。多显示器、管理员权限前台应用、独占全屏游戏继续保留人工验证；独占全屏可能限制前台激活。真实 Clean Machine Test 未进行。Run 注册状态不代表可以绕过 Windows 启动策略或任务管理器禁用；Windows 也可能延迟 Run 程序启动。Portable 删除目录可能留下失效启动项，移动后需要手动启动一次修复。

NotifyIcon 框架包含 TaskbarCreated 后重加图标的处理。本轮未重启用户 Explorer，因此不将实际 Explorer 恢复标为通过，也未添加自定义恢复机制。

参考：[Microsoft Run / RunOnce](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)、[NotifyIcon 概述](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-overview-windows-forms)、[NotifyIcon 框架源码](https://github.com/dotnet/winforms/blob/e83409daa530605da4eb5f847c6740a520325d25/src/System.Windows.Forms/src/System/Windows/Forms/NotifyIcon.cs)。
