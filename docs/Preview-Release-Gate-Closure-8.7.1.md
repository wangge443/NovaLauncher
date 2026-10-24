# 8.7.1 Preview Release Gate Closure

**NOT READY FOR PREVIEW**。ProfileSettings、Glass Alt+Tab 和历史旧库升级三项已经闭环；Startup ON / OFF 两轮真实 Windows Restart 均未测试。未发现需要修改产品代码的证据，候选 ZIP 保持不变。

日期：2026-10-23（America/Anchorage）。产品基线：`16fde14f7476329273050d3afada78e167ac662e`，版本 `0.1.0-preview.1`。本轮仅修改验证器、测试脚本和验收文档；无产品代码、透明实现、Schema、Migration 或依赖变更，不自动提交、打 tag 或发布。

## 13 项结论

| # | 项目 | 结论 |
| ---: | --- | --- |
| 1 | ProfileSettings 失败归类 | **TEST HARNESS ISSUE**，脚本错误切换已打开的 Profile Flyout，非控件被删或改名。 |
| 2 | 手工 Profile → Settings | **PASS**，用户本轮回复“正常”；相同候选程序的完整自动 Smoke 也已通过。 |
| 3 | Glass Alt+Tab 人工结果 | **PASS**，用户确认切出、切回后交互和透明正常。该结论来源是用户人工反馈，不是把自动操作称为人工。 |
| 4 | Glass 自动失败根因 | **TEST HARNESS ISSUE**：WPF IsActive 与真实系统前台不一致；旧脚本忽略 SetForegroundWindow 失败，未建立可靠的最近激活窗口顺序，就按固定延时发送 Alt+Tab 并断言目标 HWND。修正前提和输入时序后严格 HWND 检查通过。 |
| 5 | 是否修改透明实现 | **否**。仅修改 NovaLauncher.Verification 中系统窗口测试。 |
| 6 | 历史数据库升级 | **PASS**，8.7 已由同一候选 EXE 升级独立历史备份，重启后所有旧字段保留，Migration 一次、迁移前备份一次。 |
| 7 | 旧脚本为何不适用已升级库 | 当前库已有全部 9 个 Migration，无 pending migration；正常初始化不会重复迁移或再造迁移前备份。旧专用脚本无条件要求备份数为 1，前提不成立。 |
| 8 | Startup ON 真正 Restart | **NOT TESTED / Awaiting Manual Reboot Verification**，用户回复“未测”。 |
| 9 | Startup OFF 真正 Restart | **NOT TESTED / Awaiting Manual Reboot Verification**，用户回复“未测”。 |
| 10 | P0 | 本轮未发现、未确认 P0。不能用此替代尚未执行的重启测试。 |
| 11 | P1 | 本轮未发现、未确认 P1；两项 UI 故障已归类验证器问题。 |
| 12 | 是否需要产品代码修复 | **不需要**，当前证据不支持改产品代码。 |
| 13 | ZIP SHA256 | **保持不变**：`7898C3EAA257D38E9BF12D835DCA86B4DD9C0F496DC6EE642F95CC8966DA109C`。 |

## ProfileSettings 闭环

`ProfileView.xaml` 仍定义 `AutomationProperties.AutomationId="ProfileSettings"`，头像 `ProfileAvatar` 的事件调用 ToggleProfileFlyoutCommand。没有发现旧名称或旧 VisualTree 契约被移除。

完整 Smoke 的重启后检查会打开 Profile Flyout，以确认名称、头像和恢复默认入口。随后 Close-Nova 调 Open-Settings；旧 Open-Settings 无条件再次点击头像，将已经打开的 Flyout 关闭，再找 ProfileSettings，自然找不到。

唯一修正位于 `scripts/Test-Publish.ps1`：先查询可调用的 ProfileSettings，只有不存在时才点击头像，随后调用 Settings。没有改正常产品 UI。

验证：

- 用户手工 Profile → Settings：正常。
- 对 8.7 留下的原始失败夹具使用 ResumeRoot，8 个检查通过，最终退出完成。
- 使用相同正式候选程序、全新隔离数据，完整 Portable Smoke **12 个检查通过**，包含 Profile/背景持久化、退出重启及最终关闭。
- 首次全量复跑遇到 OLE helper effect=None，未到 Profile 阶段；保留其日志，未更改导入产品逻辑。后续全新完整复跑已通过。

证据：`artifacts/verification/8.7.1/publish-resume.log`、`publish-resume/result.json`、`publish-full.log`、`publish-full/result.json`。首次失败记录仍为 `publish-smoke.log`。

## Glass Alt+Tab 闭环

旧测试诊断捕获到：

```text
foreground=265312 nova=2559064 companion=920094
foregroundAccepted=False novaActive=True companionActive=False
```

即 WPF 报告 Nova 活跃，但 `GetForegroundWindow()` 并不是 Nova，`SetForegroundWindow` 也没有成功。另一次诊断第一次切出通过、第二次返回失败，显示原流程没有稳定地建立系统前台顺序。没有证据表明 AllowsTransparency 会改变 HWND 相等语义；不能通过放宽目标 HWND 断言掩盖问题。

验证器现在：

1. 明确显示测试 HWND，按 companion → Nova 顺序用真实鼠标点击建立前台顺序；校验点击命中的根窗口，遮挡时拒绝输入。
2. 仅在准备点击期间临时置顶，发送 Alt+Tab 前两窗口均恢复非 Topmost，并断言 Nova 确实是系统前台。
3. 按键按下与释放之间留出处理时间；切换后等待实际目标前台 HWND，保持原有严格断言。不在 Alt+Tab 后调用 Activate 来伪造切换成功。
4. 额外检查返回后仍可见、原 HWND 不变、AllowsTransparency 正常、Window.Opacity=1，以及 Ctrl+K 能打开 Quick Launcher 并聚焦输入框。

最终日志：

```text
AltTab before: foreground=135028 title=Nova Launcher
AltTab away: foreground=593554 title=Nova Alt-Tab verification companion
AltTab return: foreground=135028 title=Nova Launcher
PASS: 8 system-window assertions
```

8 项包括前台准备、Alt+Tab 双向切换、Glass 属性、返回后键盘交互、Win+D 两次切换和 Win+Left 贴靠。Win+Z 截图也已目视确认系统布局面板出现。用户另已人工确认真实切换正常，未报告黑/白屏、透明丢失、冻结或不可交互。

证据：`system-diagnostic.log` / `system-diagnostic-2.log` 保留旧验证器诊断；最终为 `system.log`。[返回后 Quick Launcher](screenshots/8.7.1/alt-tab-restored-quick-launcher.png)、[Snap Layout](screenshots/8.7.1/snap-layout-panel.png)。截图为开发机验证器证据，不能替代用户人工结果或真实干净机证明。

只重新构建 Verification 项目，使用 `BuildProjectReferences=false` 和隔离 OutputPath，结果 0 warnings / 0 errors。未重新编译、Publish 或打包 Release Candidate。

## 历史数据库升级：PASS 与正确测试前提

有效验证链为：

```text
真实历史旧版本备份（不含 AddCloseToTraySetting）
→ 复制到带验证标记的隔离数据目录
→ 正式候选 ZIP 中的 NovaLauncher.exe --startup
→ 自动 Migration / 原字段逐项比较
→ 结束测试进程并重新启动同一候选
→ 数据仍保留，Migration 只有一次，迁移前备份只有一次
```

8.7 的 `published-upgrade.log` 记录 15 项通过检查：历史前提 1 项，加上升级后和重新启动后各 7 项。涉及 Applications、Categories、UserProfiles、AppSettings 的全部旧字段，含收藏、最近使用、Profile、背景、快捷键等持久化设置；新增 CloseToTray 默认 true。生产库未用于升级实验。

`--tray-real-upgrade` 是针对仍处于旧 Schema 的真实库副本设计的专用测试；不能将已升级的当前库作为需要再次产生 Migration 的输入。该失败现在标为 **NOT APPLICABLE（输入前提不满足）**，不是产品升级 FAIL。复现升级时使用保留历史备份和 `artifacts/verification/8.7/check-upgrade.py`，不要删除 MigrationHistory、降级或直接改生产库来满足旧断言。无新 Schema/Migration 时不重复备份是预期行为。

## 唯一尚待执行的本轮闭环项：两轮真实 Restart

用户已回复 Startup “未测”。保持同一正式候选 ZIP，不用手工运行 --startup 或重启进程代替。

**第一轮：Startup ON**

1. 正式候选包在固定位置解压，打开 Settings，开启开机启动。
2. 检查 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `NovaLauncher` 值为 `"实际 NovaLauncher.exe 完整路径" --startup`。
3. 真正 Windows Restart，然后登录。
4. 确认主窗口不主动出现、托盘出现、Task Manager 只有一个 Nova 主进程。
5. 按 Ctrl+Alt+Space，确认 Nova 恢复、Quick Launcher 打开、搜索框获得焦点。

**第二轮：Startup OFF**

1. Settings 关闭开机启动，确认 Run 的 NovaLauncher 值删除。
2. 再次真正 Windows Restart，然后登录。
3. 确认 Nova 不自动运行。最终保持 Startup Off。

结果栏：Startup ON ____；Startup OFF ____；Windows 版本/Build ____；证据 ____。目前两项均为 **NOT TESTED**。

## 候选与变更边界

- 文件：`artifacts/publish/NovaLauncher-0.1.0-preview.1-win-x64-portable.zip`
- 大小：77,813,436 bytes。
- SHA256：`7898C3EAA257D38E9BF12D835DCA86B4DD9C0F496DC6EE642F95CC8966DA109C`。
- 本轮没有重新生成候选。运行时使用的 self-contained 目录与 ZIP 内容核对一致。
- 产品项目 `NovaLauncher.App` 无 diff；变更仅为 `Test-Publish.ps1`、`TransparencyVerification.cs`、文档及验证截图。
- 不将本轮 12 项 Smoke、8 项系统检查及重复续跑简单加到 8.7 总数中，避免重复计算同一验收路径。

本报告更新 8.7 的三个失败归类；原始失败证据保留。Startup 两轮真实重启结果收到前，最终结论保持 **NOT READY FOR PREVIEW**。
