# Nova Launcher 0.1.0-preview.1 — Preview Candidate

## 8.7.1 闭环更新（当前状态）

**NOT READY FOR PREVIEW**。用户已人工确认 Profile → Settings 与 Glass Alt+Tab 正常；两项自动失败均归类 TEST HARNESS ISSUE，修正验证方式后完整 Portable Smoke 12 项、系统窗口专项 8 项通过。历史旧库由候选 EXE 升级与重启已 PASS；针对已升级库要求重复迁移的旧脚本为 NOT APPLICABLE。Startup ON/OFF 两轮真实 Windows Restart 仍为 NOT TESTED。产品与透明实现未修改，候选 SHA256 保持不变。详情见 [8.7.1闭环报告](Preview-Release-Gate-Closure-8.7.1.md)。下文 8.7 失败统计保留为历史记录，不代表这三项仍未闭环。

**状态：NOT READY FOR PREVIEW。** 候选包仅用于验收，未创建 Tag 或 GitHub Release。完整 Gate 与失败记录见 [Clean Machine 报告](Clean-Machine-Test-8.7.md)；真实干净 Windows 和登录启动由用户人工测试，尚未收到结果。

| 属性 | 值 |
| --- | --- |
| Version | 0.1.0-preview.1 |
| Release Type | Portable Preview（候选） |
| Target | Windows x64；Windows 10/11 干净机兼容性待验 |
| Primary Package | Self-contained win-x64，多文件 ZIP；保留整个解压目录 |
| Source Commit | 16fde14f7476329273050d3afada78e167ac662e |
| Global Hotkey | Ctrl + Alt + Space，Nova 运行期间有效 |
| Local Quick Launcher | Ctrl + K |
| User Data | %LOCALAPPDATA%\NovaLauncher |
| Startup 默认 | Off，HKCU Run 为真实状态来源 |
| CloseToTray 默认 | On；真正退出使用托盘“退出 Nova” |
| ZIP | NovaLauncher-0.1.0-preview.1-win-x64-portable.zip |
| Size | 77,813,436 bytes（约 74.21 MiB） |
| SHA256 | 7898C3EAA257D38E9BF12D835DCA86B4DD9C0F496DC6EE642F95CC8966DA109C |

仓库候选路径：`artifacts/publish/NovaLauncher-0.1.0-preview.1-win-x64-portable.zip`。ZIP 491 个文件与全新 self-contained 发布目录逐项一致，含运行时，不要求安装 .NET Desktop Runtime；此打包属性已在开发机确认，不能替代干净机启动证明。

## Release Notes（待发布草稿）

Nova Launcher 0.1.0-preview.1 提供个人 Windows 应用启动与整理：

- EXE/LNK 导入与应用启动，应用名称和图标管理。
- 全部应用、分类、收藏、最近使用与启动统计。
- Quick Launcher，中文、拼音及首字母搜索。
- 可配置 Global Hotkey，默认 Ctrl+Alt+Space。
- System Tray 驻留、关闭到托盘、可选 Windows 登录后台启动。
- Profile 名称与头像，自定义背景及壁纸背景。
- **Experimental Glass Mode**：实时显示后窗内容，保留高可读的局部控件。
- SQLite Persistence，用户数据存于 LOCALAPPDATA，与程序目录分离。

解压后直接运行 NovaLauncher.exe。X 默认隐藏到托盘；托盘“退出 Nova”完全退出。开机启动需要在 Settings 主动开启。升级时先从托盘退出旧程序，备份用户数据，再把新包完整解压到新程序目录；保留 LOCALAPPDATA，避免混用旧 DLL。

## 发布 Gate 与限制

Release restore/build：0 warnings / 0 errors。22 组回归 803 项通过；连同窗口、热键、托盘、包和历史备份升级验证，30 组完整通过，共 943 项断言。仍有 3 组 FAIL/前提不适用：完整 UI 冒烟关闭步骤、透明窗口 Alt+Tab 专项、要求旧 Schema 的真实库升级脚本。具体前提、失败和替代验证见报告，不将其隐藏为全绿。

真实 Clean Machine 与 Startup 开启/关闭后的两次登录结果：**NOT TESTED / Awaiting Manual Reboot Verification**。因此当前 **NOT READY FOR PREVIEW**。没有确认产品 P0/P1，但未澄清失败和缺失 Gate 足以阻止发布；不宣称不存在缺陷。

Glass 实验性且 Resize 有额外开销；Nova 未运行时全局热键无效；Portable 移动/删除可能影响登录启动路径；Windows 前台策略可能限制管理员/全屏应用唤醒。多显示器、管理员前台、独占全屏与 Explorer 重启恢复待人工验证。Single-file 仅部分验证，**不标 Recommended**。没有 Installer 或 Auto Update。

所有核心 Gate 完成并获得用户最终确认后，才可准备对外发布；本轮不会自动提交、打 Tag 或发布 GitHub Release。
