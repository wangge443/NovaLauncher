# 8.5 Global Hotkey 验收记录

状态：已通过用户最终验收，已授权收尾提交 `feat: add global quick launcher hotkey`。基线为 `d625eace1d4862a23828106b9f6d5a2e41e37126`。验证日期：2026-10-23。版本仍为 `0.1.0-preview.1`。

## 实现与交互

Windows → RegisterHotKey / WM_HOTKEY → HotkeyRegistrar → GlobalHotkeyService → WindowActivationService → MainWindow.OpenQuickLauncher → QuickLauncherOverlay.OpenAndFocus → QueryBox。

1. **WPF 接入**：主窗口 Show、HWND 可用后，在 UI Dispatcher 上创建 message-only HwndSource，监听 WM_HOTKEY。该消息窗口独立于 Glass 主窗口，因此切换背景、替换透明宿主不丢失注册。使用 MOD_NOREPEAT；没有低级键盘 Hook、Raw Input 或键盘轮询。
2. **服务职责**：IHotkeyRegistrar 隔离 Win32；HotkeyRegistrar 负责注册、注销、消息分发和资源释放；GlobalHotkeyService 负责状态、冲突、切换事务与日志；GlobalHotkeyViewModel 负责录制、开关、取消和重试。正常退出注销，进程终止后由 Windows 释放。
3. **默认键**：Ctrl + Alt + Space。Ctrl + K 仍仅在 Nova 窗口内生效。两者最终调用同一个 OpenQuickLauncher。
4. **配置结构**：复用 SQLite AppSettings，以整数 Virtual Key 与 Win32 modifier flags 保存，不保存 WPF Key 字符串。现有设置没有 JSON 扩展槽。
5. **Migration**：新增且仅新增 `20261022141212_AddGlobalHotkeySettings`，只给 AppSettings 增加下表三个字段，不改变其他业务表。

| 字段 | SQLite 类型 | 默认值 |
|---|---|---|
| GlobalHotkeyEnabled | INTEGER NOT NULL | 1 / true |
| GlobalHotkeyModifiers | INTEGER NOT NULL | 3 / Ctrl + Alt |
| GlobalHotkeyKey | INTEGER NOT NULL | 32 / VK_SPACE |

6. **旧用户兼容**：沿用现有备份与 EF Migration 启动流程。旧数据自动得到默认键，其他设置保留。非法 VK、修饰键、SQLite 错误类型及超大整数只使热键配置回退，不清空背景、窗口或业务数据。读取记录诊断，之后正常保存可修复异常字段。
7. **冲突**：RegisterHotKey 失败不退出 Nova；Settings 显示组合和错误说明，提供修改和重试。启动冲突仍可用 Ctrl + K。启用失败不把界面伪装成注册成功。
8. **修改流程**：点“修改”进入录制，按实际组合键；至少包含修饰键，禁止裸字母、裸 Space/Enter、Esc、F12 及明显系统保留组合。Esc 取消。显示统一为 `Ctrl + Alt + Space`。录制期间抑制旧全局键唤醒。
9. **保留旧键**：先占用候选注册，再保存设置，成功后释放旧注册；冲突或保存失败释放候选并保留原配置和旧键。关闭时保存关闭状态并注销，开启时立即尝试注册。
10. **最小化恢复**：复用 SystemCommands.RestoreWindow，恢复原来的 Normal / Maximized，不无条件最大化。
11. **置前**：复用 Show、Activate、SetForegroundWindow，不永久 Topmost，不移动窗口到主显示器。原单实例 Named Mutex / Named Pipe 协议未改；热键不启动另一个 Nova 进程。
12. **Quick Launcher 复用**：沿用现有 Overlay、搜索索引、搜索结果与启动命令。新会话为空查询；重复调用保留并全选查询，始终一个 Overlay；Esc 返回原页。设置、分类、收藏、最近使用状态保留。
13. **Glass**：沿用 8.4.3 材质、True Transparency 与 Window.Opacity=1。热键唤醒不重建透明宿主。全局唤醒测试确认 HWND 和 alpha 设置保留；28 项透明回归通过，含 Default、DesktopWallpaper、CustomImage 与 Glass。
14. **退出限制**：X 仍退出。Nova 完全退出后热键不可用，热键不会启动已退出的 Nova。本阶段无托盘、开机启动、后台服务、每应用快捷键、Installer 或自动更新。
15. **性能**：运行中非最小化，预热一次后取 8 次。按键发送到焦点可用的中位数 **54.24 ms**；处理热键事件开始到窗口可见 **0.02 ms**、Overlay 打开 **17.53 ms**、输入框聚焦 **17.53 ms**。两种测量起点不同，前者包括输入注入、系统调度和验证等待。不是冷启动、独占全屏或多机基准。卡片实例保持不变；搜索回归确认不重读存储、不复制卡片。日志只记录组合、状态、错误码及耗时，不记录 Query 或录制的文字输入。
16. **Release Build**：`dotnet build NovaLauncher.sln -c Release --no-restore`（独立 OutputPath）成功，**0 warnings / 0 errors**。Self-contained win-x64 publish 使用 `-warnaserror` 成功；发布包启动冒烟 6 项通过。未做真实 Clean Machine Test，不能据此声称完全无环境依赖问题。
17. **自动验证数量**：原有 20 套回归 **728 项**；新增热键抽象/持久化/迁移验证 **46 项**。另有真实 Windows 热键 **42 项**、独立进程/Chrome **15 项**、真实库备份副本升级 **11 项**、Glass **28 项**、发布包启动冒烟 **6 项**。各组是不同层次的断言，非 876 个独立用户场景。
18. **实机结果**：默认注册、前台/外部窗口唤醒、Normal/Maximized/Minimized、隐藏窗口、重复全选、Esc、页面保持、按键录制取消、冲突保留原键、关闭注销、重启恢复、第二实例、强制结束后注册、启动冲突下 Ctrl+K、真实 Query → Enter 子进程启动均通过。Chrome 前台及 F11 全屏均真实验证，断言检查实际前台 HWND 和全屏覆盖尺寸。
19. **Settings 截图**：[Default](screenshots/8.5/hotkey-settings-default.png)、[Glass](screenshots/8.5/hotkey-settings-glass.png)。
20. **外部前台唤醒截图**：[Chrome](screenshots/8.5/global-from-browser.png)、[Chrome 全屏](screenshots/8.5/global-from-browser-fullscreen.png)、[Glass 最小化恢复](screenshots/8.5/global-glass-restored.png)。`global-from-external.png` 是同进程独立 companion 窗口，不能替代 Chrome 独立进程证据。截图展示触发后的 Nova；触发前前台归属由测试断言证明。

## 数据与证据

真实 nova.db 在升级验证前通过 SQLite online backup 备份到 `artifacts/verification/8.5/real-upgrade/real-before-8.5-20261022T143427Z.db`；仅 `isolated-real-copy` 执行迁移。11 项断言逐项比较 Applications、Categories、UserProfiles、AppSettings 的所有旧字段，并检查真实源库未变。**没有在真实生产库上运行 8.5 升级**；用户启动验收版时才按正常流程升级。备份与数据库不随发布包分发。

测试记录集中在 `artifacts/verification/8.5/`：`build.log`、`hotkey-live.log`、`process.log`、`real-upgrade.log`、`glass.log`、`publish.log`、`publish-smoke.log` 和 `regression/`。性能对应日志为 `fixture-300/Logs/nova-20261023T0903445545390-e7839307b57f437caec814f286fcd54e.log`，取最后一条实际启动探针前的 8 个稳态样本。

原有回归：Persistence 25、All Applications 26、Favorites 31、Recent 22、Profile 44、Management 46、App Edit 59、Pinyin 123、Launch 32、Toast 11、Polish 19、Import 37、Categories 34、Settings 44、Migrations 30、Release Safety 13、Logging 11、Performance Behavior 13、Appearance 39、Quick Launcher 69。

验证中发现原生输入测试仅判断 WPF IsActive 不足以保证系统前台，Quick Launcher / Appearance 测试改用实际前台 HWND 确认。热键测试增加状态切换后的等待；端到端启动探针使用绝对输出路径并通过输入框更新查询。初始失败日志保留用于排查，最终断言没有删除或放宽。新热键 Settings 复用现有样式，未修改 Glass 资源、透明机制、原搜索或渐进渲染架构。

## 复现入口

在交互式 Windows 桌面顺序执行，不能让多个 UI 测试同时抢占前台。测试数据路径使用绝对路径，并带 `.nova-verification` 标记；不要把真实用户目录传给测试命令。

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore -p:OutputPath=D:\openAI\codex\NovarLauncher\artifacts\build\8.5\
artifacts/build/8.5/NovaLauncher.Verification.exe --hotkey
artifacts/build/8.5/NovaLauncher.Verification.exe --hotkey-live D:\openAI\codex\NovarLauncher\artifacts\verification\8.5\fixture-300 docs/screenshots/8.5
powershell.exe -NoProfile -STA -File scripts/Test-GlobalHotkey.ps1
```

## 已知限制与待人工验证

- Nova 未运行时没有全局热键；X 退出行为不变。
- 所有 Win 组合在本阶段拒绝，避免占用系统保留组合；F12 保留给调试器。这是有意收紧的合法性策略。[Microsoft RegisterHotKey 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- 被系统或其他程序吞掉的按键可能到不了 WPF 录制控件，此时用 Esc 取消并换组合；不引入键盘 Hook 绕过。
- Windows 可能限制前台切换；不默认提升权限、不使用强抢前台绕过。[Microsoft SetForegroundWindow 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- Chrome F11 已实测；视频播放器全屏、独占全屏游戏、管理员前台应用与普通权限 Nova 的组合、真实双显示器位置恢复仍需人工验证。代码保持原窗口位置，但不能据此宣称多屏实测通过。
- 发布包在本机启动验证通过，真实干净机器、Single-file 完整验证不在本次完成项中。

本阶段已通过最终验收；保留多显示器、管理员前台应用、独占全屏游戏人工验证项。不进入系统托盘或开机启动阶段。
