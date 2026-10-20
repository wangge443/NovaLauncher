# Nova Launcher — Portable Preview 发布基线（8.1）

版本 **0.1.0-preview.1**。基线 `b55b477cc81e9edf77b4516c4c0f4cc82e155f20`（已验收 Product Audit 8.0）。8.1 已于 2026-10-20 通过开发验收，用户授权收尾提交；不进入 8.2。推荐主要交付形式为 **Self-contained win-x64 Portable Preview**。

## 版本与身份

`Version.props` 是唯一产品版本来源。AssemblyVersion / FileVersion 为 `0.1.0.0`；InformationalVersion 为 `0.1.0-preview.1`，构建工具可附加 Git revision。Settings About、启动日志、发布清单和 ZIP 名称读取程序集元数据，不维护第二份 UI 版本。

Product 为 `Nova Launcher`，AssemblyName 为 `NovaLauncher`，Description / File Description 为 `Personal Windows Application Launcher`。不设置虚构 Company。`Resources/Nova.ico` 复用 Sidebar 的 N 路径，包含 16–256 像素尺寸；EXE、窗口及任务栏复用该图标。`scripts/New-AppIcon.ps1` 可重建，不附带字体文件。

## Build / Publish

在 Windows 上安装 `global.json` 固定的 .NET SDK 9.0.318：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
powershell.exe -NoProfile -File scripts/publish.ps1 -Mode All -IncludeSingleFile
```

脚本执行 win-x64 锁定还原、Release publish、warnings-as-errors、包内容检查、文件元数据检查、SHA256 清单及 ZIP。关闭 trimming 与 PDB 输出。不依赖 Visual Studio 发布配置。

| 输出 | 用途 | ZIP 大小（本次构建） |
| --- | --- | --- |
| framework-dependent | 已安装兼容 .NET 9 Desktop Runtime x64 的机器 | 5,104,007 bytes |
| self-contained | 主要 Portable Preview；随包携带运行时 | 66,215,490 bytes |
| single-file | 可选评估产物，保留独立许可文件 | 61,770,904 bytes |

FDD 未安装运行时时预期由标准 .NET apphost 提示缺少运行时；未在无运行时机器实测，不提供自制安装器。SCD 携带运行时，仍依赖兼容 Windows 系统组件。Single-file 使用 `IncludeNativeLibrariesForSelfExtract=true`，原生 WPF/SQLite 组件需要可写解包缓存；不代表所有文件都只剩一个 EXE。

```text
artifacts/publish/
  framework-dependent/  NovaLauncher.exe、程序集、runtimeconfig、SQLite、许可、release-manifest.json
  self-contained/       上述内容及 .NET/WPF runtime
  single-file/          NovaLauncher.exe、ThirdParty/许可、release-manifest.json
  NovaLauncher-0.1.0-preview.1-win-x64-framework-dependent.zip
  NovaLauncher-0.1.0-preview.1-win-x64-self-contained.zip
  NovaLauncher-0.1.0-preview.1-win-x64-single-file.zip
```

所有发布输出均被 Git 忽略。脚本拒绝数据库、WAL/SHM、用户 Profile/Background/CustomIcons/Cache/Logs、源代码、测试程序、PDB 和字体。清单包含每个运行文件的 SHA256。发布包不包含真实用户数据。

## 单实例与写入安全

`SingleInstanceCoordinator` 以 Windows SID + SessionId 派生 Local Named Mutex 与 CurrentUserOnly Named Pipe。主 UI 线程持有/释放 mutex；第二进程在创建窗口、读取设置或初始化数据库之前判断所有权。协议仅支持固定 ACTIVATE；第二进程授予前台权限，主窗口 Restore / Show / Activate / SetForegroundWindow，第二进程退出。早期启动通知延迟到窗口就绪。连接有超时，主实例中途死亡时重新尝试取得所有权；支持 abandoned mutex。

AppSettingsRepository 比较调用者 original 与 requested，仅将变化字段交给 EF 更新；SQLite 写事务在读取前取得写锁，避免独立仓库之间读后升级写锁的竞争；回读数据库合并结果并返回给服务。AppSettingsService 原有串行 gate 保留。并发测试曾发现 `SQLITE_BUSY`，增加写事务后连续 20 轮通过；测试没有忽略保存失败。

LaunchCount 保留既有条件快照更新：仅当数据库计数小于提交计数时更新统计字段。共享卡片 VM、每卡启动防重入、单实例使计数来源唯一；较旧快照不会降低计数/时间，也不会重新插入已删除应用。本轮未改为增量 SQL，避免破坏异步快照队列语义。单实例范围是用户 Session，不承诺同一用户多个并行登录 Session 的计数合并。

已有迁移历史且有 pending migration 时先执行 SQLite backup 并校验，再迁移。无 pending migration 不反复备份。迁移失败保留原数据库与备份；不新增 Schema/Migration，不要求删除用户数据。

## 用户数据、日志与升级

正式运行仍写 `%LOCALAPPDATA%\NovaLauncher`，不写程序目录：

```text
NovaLauncher/
  Data/nova.db
  Data/Backups/
  Profile/
  Background/
  CustomIcons/
  Cache/Icons/
  Logs/nova-*.log
```

日志使用有界 Channel（512 条）、非阻塞 TryWrite、后台 UTF-8 文件写入。每个文件最大 1 MiB，最多 10 个文件，跨启动清理；队列满时丢弃并计数，磁盘失败不会递归写日志或导致应用崩溃。UTC 时间、级别、静态事件上下文、异常类型/HResult/方法堆栈与 SQLite 数字错误码可用于诊断。不记录用户名称、完整启动参数、异常消息或源路径。旧迁移诊断 `Data/migration-errors.log` 另限 1 MiB。

覆盖启动/版本、数据库初始化/迁移、导入、启动/持久化/设置/背景/Profile 失败、单实例激活、关闭。注册 Dispatcher、TaskScheduler 与 AppDomain 异常记录；fatal 路径最多等待一秒落盘，不设置 Dispatcher `Handled`，不强行继续运行。

升级步骤：正常关闭 Nova，备份用户数据，将新包完整解压到新程序目录，运行新 `NovaLauncher.exe`。不要把旧程序目录中的 DLL 混入新包，不删除 LOCALAPPDATA。8.1 不提供自动更新或 Installer。

## 验证与边界

验证日志与截图保存在忽略目录 `artifacts/verification/8.1/`。完整既有回归和本阶段安全测试合计 **710 项主进程断言**（重复压力轮次不重复计数），另有发布实际 EXE 检查。包括 Migration 30、日志 11、release safety 13；完整功能计数见 Development。

此前 UI 回归失败按原断言修复测试时序：滑块在可见且获得焦点后发送键；Ctrl+K 在 Ctrl 按下后等待输入分发。分类和版本期望修正后的设置回归均通过。没有更改产品键盘处理代码。

实际发布验证入口：

```powershell
powershell.exe -NoProfile -STA -File scripts/Test-Publish.ps1 -PublishDirectory artifacts/publish/self-contained -OutputDirectory artifacts/verification/8.1/publish-scd
```

脚本操作交互式桌面，复制真正的 publish EXE 到临时目录，使用有标记的隔离数据根目录，核对用户默认数据目录没有变化；不在测试进程内构造产品 ViewModel。内部 `--verification-data-root` 只接受绝对路径及 `.nova-verification` 标记，拒绝真实数据根目录及其祖先/后代，不是业务导入 CLI。

本次实际结果（2026-10-19，本地时区）：

| 验证 | 结果与证据 |
| --- | --- |
| SCD 完整连续 smoke | 12 项检查通过；`publish-scd-final.log` / `publish-scd-final/result.json`。真实复制 EXE、OLE 导入、真实 LaunchProbe 启动、分类/收藏/Recent、右键重命名、Ctrl+K/全拼、图标/头像/背景、关闭与重启、首字母搜索均通过。截图已检查，默认用户目录文件元数据未变化 |
| 单实例 | 同一隔离根的第二 EXE 正常退出，原窗口从最小化恢复并成为前台；强制终止该测试进程后重新启动成功 |
| FDD | 在本机已安装运行时条件下通过启动/SQLite/单实例/恢复检查；实际加载 `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\9.0.20\coreclr.dll` |
| SCD runtime | 实际加载复制目录内的 `coreclr.dll` |
| Single-file | 启动、SQLite、单实例、恢复检查通过；独立 coreclr 模块路径未由该枚举暴露，不据此宣称运行时来源已实测。未执行其完整功能 smoke，保持可选评估输出，不作为主要交付包 |
| Fresh / pending migration | 另以实际 SCD EXE 验证：7 条 Migration、1 条默认 Profile、默认 AppSettings；旧 5 条 Migration 夹具升级后有 1 个迁移前备份。`publish-boundary.log`、`fresh-snapshot.json`、`pending-snapshot.json` |
| 旧版升级 | 从 `git archive b55b477` 构建旧版持久化 API 夹具，创建应用、7 次启动统计、收藏、Profile、背景、CustomIcon，再由实际 SCD EXE 打开/关闭。`before.json` 与 `after.json` 逻辑快照及资产 SHA256 一致（排除设置 UpdatedAt；禁用记忆窗口以固定几何）。不是用新版代码伪造旧版数据，也未声称运行过旧版完整 UI。`upgrade-result.log` |
| 发布性能 | 相同 300 条目夹具，进程启动至窗口出现：Release 5883 ms，SCD 5587 ms；本次顺序测量未见明显变慢，不等同纯布局时间，不代表统计基准。原 Home 300 卡片布局回归为 2581 ms。`performance.json` |
| 身份 | 实际 EXE FileVersionInfo：Product/Description 正确，Company 为空，FileVersion 0.1.0.0，ProductVersion 包含 Preview 与基线 Git revision。`metadata.json` |

最初 WinForms OLE 辅助器出现嵌套消息循环挂起，已改为独立 WPF `--external-drop` 验证进程，外层最多等待 15 秒，失败后仅终止该辅助进程。最终完整 smoke 连续通过；不以此前中断、人工准备夹具的后半段结果冒充完整连续结果。脚本的 `-ResumeRoot` 仅用于标记隔离夹具的续跑；续跑结果单独标记，不是 Fresh Run。原生文件对话框按目标进程 HWND 设置文件名，避免依赖键盘布局。

最终 SCD 截图：`artifacts/verification/8.1/publish-scd-final/settings-background.png`、`restarted.png`、`pinyin.png`。EXE 属性以实际 `metadata.json` 提供，未制作冒充系统属性窗口的截图。

**Release Gate：真实 Clean Machine Test 尚未完成，必须在正式对外发布前执行并通过；开发验收不解除该门槛。** 当前环境没有 Windows Sandbox / 干净 VM。复制目录测试和 SCD 加载本地 coreclr 只能证明本机路径独立性及运行时来源，不能冒充无 SDK / 无 Desktop Runtime 的新 Windows 验收。

人工补验：在全新 Windows 11 x64 VM 中、不安装 SDK 或 Desktop Runtime，复制 SCD ZIP，解压到含中文和空格的目录，执行完整 smoke、重启和单实例测试；确认所有用户数据位于 LOCALAPPDATA。另在安装 .NET 9 Desktop Runtime 的 VM 验证 FDD，并在未安装时记录标准 apphost 提示。Single-file 首次/再次启动和原生解包权限也需补验。

Known limitations：未签名 Portable Preview、没有 Installer / 自动更新 / 托盘 / 开机启动；DesktopBackdrop 保持 Experimental，DesktopWallpaper 是静态方案；300 卡片性能优化留在 8.2。

## Dead Code Cleanup Backlog

仅记录，不在本轮删除：FavoriteButtonStyle、CategoryPopupWidth、IsCategoryPickerOpen、ShellBackgroundView.BackgroundImage DP、旧 Views.BackgroundMode.CustomImage。清理前重新核对 XAML 动态资源、绑定与测试；DesktopBackdropController 不按默认禁用认定为死代码。
