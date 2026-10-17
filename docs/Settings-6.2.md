# Nova Launcher — 6.2 设置中心交付记录

本阶段实现独立 Settings Workspace 与 SQLite 设置持久化，已通过用户验收，按授权提交 feat: add application settings；不开始下一阶段。背景、主题仅提供占位入口，不接入 6.3 功能。

## 数据与调用链

Profile Flyout → Settings Workspace → SettingsViewModel → IAppSettingsService / AppSettingsService → IAppSettingsRepository / AppSettingsRepository → SQLite。

新增 Migration：`20261017025537_AddAppSettings`。由 EF Core 9.0.16 工具生成，只创建 AppSettings；旧 Migration 不变，不使用 EnsureCreated，不删除或重建数据库。

| 字段 | SQLite 类型 | 默认值与约束 |
| --- | --- | --- |
| Id | INTEGER NOT NULL | 主键，固定 1，CHECK Id = 1，无自增 |
| StartupPage | TEXT NOT NULL | Home；仅支持 Home / Recent / Favorites |
| RememberWindowSize | INTEGER NOT NULL | true |
| RememberWindowPosition | INTEGER NOT NULL | true |
| WindowWidth / WindowHeight | REAL NULL | null；使用 MainWindow 1120 × 720 DIP |
| WindowLeft / WindowTop | REAL NULL | null；主屏工作区居中 |
| BackgroundMode | TEXT NOT NULL | Default；6.3 预留 |
| ThemeMode | TEXT NOT NULL | Light；6.3 预留 |
| UpdatedAt | TEXT NOT NULL | UTC DateTimeOffset |

默认值由模型和 Repository 的参数化 INSERT OR IGNORE 幂等创建；枚举以名称存储，避免枚举排序变化改变数据库语义。Repository 在后台使用独立 DbContext；AppSettingsEntity 复用不依赖 WPF 的 AppSettings record 字段。服务串行处理写入，仅提交成功才替换 Current。

SettingsView 仅布局/绑定，code-behind 只有 InitializeComponent。SettingsViewModel 提供两个记忆开关、三项 StartupPages、外观占位和打开目录命令、程序集版本、路径。修改时先显示新值并暂时禁用设置编辑，异步提交后确认；失败恢复已提交值并 Toast“设置保存失败”。窗口关闭等待未完成设置操作。

## 窗口与启动

既有数据库初始化/应用恢复 → 加载资料和设置 → 应用启动页 → SourceInitialized 恢复几何 → Show。窗口对象预先创建用于装配，但读取完成之前不显示。设置读失败回退默认并给出 Toast，不阻断 Launcher 核心功能。

只在 Closing 保存一次最终边界；最大化/最小化保存 RestoreBounds，不持久化最大化状态。关闭对应记忆开关后不更新该组坐标，下次启动忽略旧值并用默认尺寸/居中位置。

尺寸校验拒绝非有限值，最小 800 × 560 DIP，通用上限 7680 DIP，再按目标工作区限制。特殊小工作区会同步降低窗口 MinWidth/MinHeight，保证可见。Win32 EnumDisplayMonitors/GetMonitorInfo 获取所有工作区，在 HWND SourceInitialized 后按 CompositionTarget.TransformFromDevice 转为 WPF 坐标。标题栏至少 80 × 24 DIP 位于工作区才采用旧位置，否则主屏回退；最终将整个窗口限制到选中工作区，负坐标副屏有效。当前沿用原有 DPI 模式，不新增系统 DPI 设置。

StartupPage 在启动时映射到原 Sidebar 首页/收藏/最近使用项；当前会话修改不强制跳页。Settings 时普通导航全部无 Active，点击任意已有导航即可离开。

数据目录由 ApplicationDataPaths.RootDirectory 给出，数据库为其下 Data/nova.db。打开操作经 IDataDirectoryService 安全创建目录，再用 Windows Shell（UseShellExecute）打开。异常 Toast“无法打开数据目录”。版本读取应用程序集 AssemblyInformationalVersion，去掉提交哈希后显示，没有 XAML 硬编码版本。

## UI

单页纵向 ScrollViewer，外观 / 启动与窗口 / 数据与存储 / 关于四段，没有第二个 Sidebar。沿用 WorkspacePadding=24；SettingsRow 最小 64 DIP、Padding 16/12、圆角 8，说明文字可换行。小窗口路径分行，右侧控件不与文字重叠。外观显示“默认”“浅色”和“即将支持”，点击仅 Toast。

CheckBox 保留原生 Toggle/Space 语义，40 × 22 轨道、48 × 32 点击区；ComboBox 保留原生键盘/选择语义并应用浅色圆角模板；按钮沿用 QuietButton。交互项均有 AutomationProperties.Name 与焦点样式，纯布局 ContentControl 不参与键盘焦点。

已读取 ui-ux-pro-max SKILL、quick-reference 与 pro-rules；尝试设计系统/WPF 查询时 py 报告 No installed Python found，采用技能内置一致性、层级、键盘、换行和语义色规则，不声称检索成功。实机截图为 WPF RenderTargetBitmap，不证明 Experimental DesktopBackdrop 合成效果。

## 验证与边界

设置专项：36 项断言通过，包含默认幂等、Migration/Snapshot、Flyout Enter 进入、各导航退出、Space 开关、Selector 键盘与弹出三选项、即时保存、SQLite 锁失败回滚开关及选择、数据目录打开失败、外观占位、负坐标副屏/拔除副屏/工作区缩小/非法几何/标题栏不可见、最大化 RestoreBounds、无效启动页、读取失败回退、小窗口滚动、程序集版本及旧数据完整性。

真实数据库：先在线备份，四个独立生产 App WPF 进程完成升级与关闭/重启。逐次对比 Applications、Categories、UserProfiles 全字段，以及图标和头像 SHA256，全部保持；收藏、启动次数与最近时间包含在 Applications 全字段内。AppSettings 单行、无待应用 Migration。Favorites 重启恢复 940 × 640 与位置；关闭记忆后恢复默认尺寸与居中位置；Recent、Home 重启导航通过。Windows Shell 真实打开目录通过。测试后恢复原设置，后续只读 UI 预览的正常关闭按产品语义记录当前窗口边界。

备份：`%LOCALAPPDATA%\NovaLauncher\Data\Backups\nova-before-settings-20261017T030253Z-6773d16b8b354594b0a8f511724fc523.db`。

Release Build：0 warnings / 0 errors。Migration 回归：30 项通过。其余回归结果见 Development.md 的本阶段记录。

多显示器移除、负坐标、缩放/分辨率变化采用工作区输入模拟验证，未实际拔插显示器或切换混合 DPI；未实机切换高对比度。真实重启已覆盖当前桌面的 HWND 恢复流程。

## 复现

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --settings docs/screenshots/6.2
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --migrations
# 以下入口会访问真实用户数据库；real-settings 会备份并临时修改设置后恢复。
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --real-settings
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --settings-preview
```

截图：[真实设置页](screenshots/6.2/real-settings.png)、[真实数据目录与关于](screenshots/6.2/real-settings-storage.png)、[最小窗口](screenshots/6.2/settings-small.png)、[小窗口底部](screenshots/6.2/settings-storage-about.png)。
