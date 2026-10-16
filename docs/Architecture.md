# Nova Launcher — 架构设计

## 当前边界

NovaLauncher.sln 包含 NovaLauncher.App、NovaLauncher.Verification 和仅用于测试的 NovaLauncher.LaunchProbe，目标框架为 net9.0-windows。4.3 在稳定的 4.1 导入与 4.2 启动链路外围接入 EF Core + SQLite、PNG 缓存及恢复。当前持久化行为以 4.3 章节为准；后续 4.2、4.1、3.x 描述保留为历史基线。

## 4.3 Persistence Pipeline

重复导入 UX：App 为每个 Workspace 注入 `IToastService / ToastService`，ContentViewModel 暴露 Toast 状态。内存去重和持久化层返回重复结果均调用 Show；Service 管理通知文本、可观察 IsOpen 和可取消的 2 秒计时，连续通知替换内容并重新计时，Workspace 释放时取消任务。服务在 UI 同步上下文使用；ToastView 仅负责透明度动画和可见性，ContentView.xaml.cs 不含通知文案或逻辑。覆盖层 Canvas 不占布局空间。当前只接入重复导入，接口可供后续错误通知复用；未修改表结构、Repository、缓存或统计链路。

```text
Drop → ApplicationImportService → LauncherApplication
 → ApplicationPersistenceService.AddAsync → ApplicationRepository.AddAsync
 → EF Core → SQLite commit → PNG Cache → ObservableCollection → ApplicationCard

Application Launch → successful result → Model Launch Statistics
 → ApplicationPersistenceService.SaveStatisticsAsync
 → ApplicationRepository.UpdateAsync → SQLite

Startup → show Window → InitializeAsync → Repository → SQLite
 → Persistence Service + Icon Cache → ContentViewModel → Collection → Card
```

新增直接依赖 `Microsoft.EntityFrameworkCore.Sqlite 9.0.16`，适用于 net9.0-windows，EF Core/Microsoft.Data.Sqlite 为 MIT；SQLitePCLRaw 2.1.10 包为 Apache-2.0，SQLite 引擎为公有领域。许可证已核对还原包 nuspec；传递版本均记录于 packages.lock.json。未安装 Design 包或其他 ORM。替代方案是直接使用 Microsoft.Data.Sqlite 编写 SQL，但本阶段按用户要求采用 EF Core。[包与兼容框架](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/9.0.16)。

数据根目录通过 `Environment.SpecialFolder.LocalApplicationData` 获得；本机正式路径为 `C:\Users\王倚烽\AppData\Local\NovaLauncher\Data\nova.db`。首次运行自动创建目录及数据库，无需服务器或 SQL 操作。当前数据库初始化采用 **EnsureCreated**（调用 `EnsureCreatedAsync`），原因是当前处于早期开发阶段，Schema 简单，且没有历史数据库版本。

**在下一次数据库 Schema 扩展之前，必须切换到 EF Core Migrations。** EnsureCreated 不执行 Schema 升级；切换前须设计备份和既有数据库的迁移基线，不能直接混用 EnsureCreated 与 Migrate，更不能通过删库升级。本轮仅记录迁移要求，不修改数据库架构。[官方限制](https://learn.microsoft.com/en-us/ef/core/managing-schemas/ensure-created)。

真实用户数据仅位于 LOCALAPPDATA，仓库不保存运行数据。`.gitignore` 排除 `*.db`、`*.db-*`（含 WAL、SHM、journal 及其他数据库附属临时文件）和 `**/Cache/Icons/`；验证使用隔离临时目录并清理。

`Applications` 表结构：

| 字段 | SQLite 类型与约束 |
| --- | --- |
| Id | TEXT，Guid，主键 |
| DisplayName | TEXT NOT NULL |
| ExecutablePath / SourcePath | TEXT NOT NULL，CHECK 非空白 |
| TargetPathKey | TEXT NOT NULL，CHECK 非空白，唯一索引 |
| SourceType | INTEGER NOT NULL，CHECK IN (0,1)，Executable / Shortcut |
| LaunchCount | INTEGER NOT NULL，CHECK >= 0 |
| LastLaunchTime | TEXT NULL，DateTimeOffset（保留 offset） |
| DateAdded | TEXT NOT NULL，UTC DateTimeOffset |
| SortOrder | INTEGER NOT NULL，导入时分配，加载排序 |
| IconCachePath | TEXT NULL，受管理 PNG 路径 |

TargetPathKey 在 Repository 写入时统一由最终目标 `Path.GetFullPath(...).ToUpperInvariant()` 生成。数据库唯一索引防止 EXE/LNK 同目标和大小写重复，包括绕过 UI 或并发写入。没有合并硬链接或其他路径别名；SQLite 原生 NOCASE 仅覆盖 ASCII，故使用独立规范化键。排序相同时以 Id 稳定排序。

`LauncherDbContextFactory : IDbContextFactory<LauncherDbContext>` 只持有路径，每次 Repository 操作创建并 using 释放 Context；无全局 Context。查询 AsNoTracking，统计用条件 ExecuteUpdate，只更新次数和时间，旧快照不能覆盖较新次数。Repository 提供 InitializeAsync / GetAllAsync / AddAsync / UpdateAsync / ExistsAsync。SQLite 驱动的 async 实际仍执行同步 I/O，因此 Repository 将整个操作放入 Task.Run，不阻塞 Dispatcher。[官方 async 说明](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)。

App 手动装配 Factory → Repository → Persistence Service → ContentViewModel；VM/View 不访问数据库。App 先显示窗口，再异步恢复；导入等待初始加载完成，数据库成功后才加卡片，保存失败显示轻提示。损坏数据库不删除、不重建覆盖，窗口仍能打开。单条实体转换失败会逐行隔离读取，其余有效条目继续加载；错误记 Trace。目标文件不存在时保留记录和卡片，IsAvailable=false，启动仍由原服务校验路径并提示。

启动成功先更新 Model 并通知绑定，再保存统计；写入错误独立 Trace，不发 LaunchFailed，不回滚成功次数。正常窗口关闭先等待在途导入/启动与统计写入，再异步关闭窗口，避免 Closing 事件重入；不改变 MainWindow 文件或启动服务。强杀进程/断电不能保证尚未提交的数据。多实例统计合并暂不实现。

## 4.3 Icon Cache Pipeline

```text
Executable → 原 ApplicationIconService / SHDefExtractIconW
 → PNG bytes → Cache/Icons/<application-id:N>.png
 → IconCachePath → SQLite

Restart → managed cache path → PNG decode validation
 → missing/corrupt/unreadable: extract target icon again → rewrite PNG
 → extraction fails: null Icon → default Nova vector icon
```

根目录为 `%LOCALAPPDATA%\NovaLauncher\Cache\Icons`，使用 Guid 文件名避免同名 exe 和非法字符冲突。DB 不保存 WPF 对象、HICON 或 PNG blob。读取只接受该 Id 对应的受管理路径，不跟随库中任意外部路径。缓存目录、读写、PNG 解码和重提取均在后台；缓存失败记录 Trace，不撤销已成功保存的应用记录。DB 预先记录确定性缓存路径，后续启动可修复缺失文件。图标提取及 HICON 释放原实现保持不变。

## 4.2 Application Launch Pipeline（历史基线）

```text
ApplicationCard (Button: 单击 / Enter / Space)
      ↓
ApplicationCardViewModel.LaunchCommand (每卡片独立 guard)
      ↓
IApplicationLaunchService → ApplicationLaunchService (后台 STA)
      ↓
EXE → ProcessStartInfo(ExecutablePath, UseShellExecute=true)
LNK → ProcessStartInfo(SourcePath, UseShellExecute=true) → Windows Shell
      ↓
ApplicationLaunchResult
      ↓
成功：LauncherApplication with { LastLaunchTime, LaunchCount }
失败：LaunchFailed → ContentViewModel 轻提示，统计不变
      ↓
绑定通知 → Hover / 键盘聚焦信息刷新
```

App.OnStartup 注入 ApplicationLaunchService，经 ContentViewModel 传递给每张卡片；只增加依赖装配，不修改导入服务、快捷方式解析、图标提取、去重或 Drop 适配。MainWindow、Sidebar、ShellBackground 和 DesktopBackdrop 均未修改。

ApplicationLaunchService 校验启动路径和导入时记录的目标仍存在；EXE 设置 WorkingDirectory 为目标所在目录；LNK 使用原始 SourcePath，不用解析目标替代它，也不覆盖快捷方式内的 Arguments 或 WorkingDirectory。Process.Start 在独立 STA 线程调用，不重定向输出、不等待目标退出；返回的 Process 包装立即 Dispose，仅释放句柄，不终止进程。取消令牌仅阻止尚未提交的启动请求，无法撤回 Shell 已接受的请求。

成功定义为 Process.Start / Shell 请求返回且未抛出异常，不等同于程序完成初始化或持续运行。Shell 复用已有进程时可能返回 null，仍视为请求被接受；相关行为见 [Process.Start 官方文档](https://learn.microsoft.com/dotnet/api/system.diagnostics.process.start)。权限不足、路径失效、Shell 拒绝等异常记录 Trace，返回简洁错误，不向用户显示堆栈。

LauncherApplication 新增 `long LaunchCount` 和 `DateTimeOffset? LastLaunchTime`，默认 0/null；均为不依赖 WPF 的模型字段，**当前仅为会话数据，4.3 将接入 SQLite 持久化**。ApplicationCardViewModel 在成功后以 record with 更新模型快照，LaunchCount +1，LastLaunchTime = DateTimeOffset.Now，并通知绑定。失败保留原值，不记录运行时长。

LaunchCommand 使用 AsyncRelayCommand；IsLaunching 在 await 前设置，同时防护直接 ExecuteAsync 调用。每次请求至少保持 400ms guard，Shell 请求未返回时继续防重；结束后解除，每张卡片互不锁定。关闭应用会取消卡片待执行请求与延迟，解除错误事件订阅。

LaunchTimeFormatter 在显示层将完整时间格式化为“从未 / 刚刚 / N分钟前 / 今天 HH:mm / 昨天 HH:mm / M月d日”，跨年包含年份；进入 Hover/焦点时刷新，停留时每 30 秒刷新，离开或卸载后停止计时器。

## 4.1 Application Import Pipeline（历史基线）

```text
File Drop (ContentView: WPF/OLE FileDrop 适配)
   ↓
Validation (.exe / .lnk，存在性)
   ↓
Shortcut Resolution (IShellLinkW + IPersistFile，只读)
   ↓
Target Validation (绝对路径、.exe、PE ExecutableImage，拒绝 DLL)
   ↓
Metadata Extraction (FileVersionInfo / 快捷方式文件名)
   ↓
Icon Extraction (SHDefExtractIconW → PNG)
   ↓
LauncherApplication
   ↓
ContentViewModel (最终目标路径去重，ObservableCollection)
   ↓
ApplicationCardViewModel (PNG → frozen ImageSource / Nova 默认图标)
   ↓
ApplicationCardView (ItemsControl + WrapPanel)
```

`LauncherApplication` 包含 Id、DisplayName、ExecutablePath、SourcePath、Icon、SourceType；Icon 是可空 PNG 字节数组，Model 不引用 WPF。SourceType 为 Executable 或 Shortcut。空 Icon 在卡片适配层转换为可缩放的 Nova 默认矢量图标。

服务接口为 IApplicationImportService、IShortcutResolver、IApplicationIconService。App.OnStartup 手动装配服务和 ContentViewModel，再交给 MainWindowViewModel.Content；没有 Service Locator 或新增依赖。ContentView code-behind 仅转换 WPF FileDrop、转交拖放状态和调用 ViewModel，不解析文件或图标；MainWindow 的业务变化只有 Content 绑定。

ApplicationImportService 每次文件导入使用后台 STA 线程，避免磁盘、版本资源与 Shell COM 阻塞 UI。ContentViewModel 在 WPF 上下文 await，逐个更新集合；批次串行，导入期间拒绝新的批次。关闭应用取消令牌，停止等待和后续集合更新；正在执行的同步原生调用不能被强行中断，结束后释放资源。解析失败返回结构化错误并写 Trace，混合批次继续处理其他条目。

EXE 名称优先选择去除首尾空白后的 ProductName，再 FileDescription，再不带扩展名的文件名。空白、控制字符或超过 160 字符的元数据不采用；Windows 工具共享的 Windows Operating System 产品名也不作为应用名。LNK 始终优先使用不带 .lnk 的文件名。

ShortcutResolver 通过 Windows Shell COM 的 IPersistFile.Load(STGM_READ) 和 IShellLinkW.GetPath(SLGP_RAWPATH) 获取路径，展开环境变量；不调用 Resolve，不搜索移动后的目标、不弹窗、不更新快捷方式、不执行目标。COM RCW 在 finally 中释放。空目标、失效目标、非 .exe 或无效 PE 安全失败；快捷方式参数和工作目录本阶段不用于启动，也不持久化。

ApplicationIconService 使用 SHDefExtractIconW 请求 256 像素图标，经 WPF Imaging 复制像素并编码 PNG。HICON 在 finally 中调用 DestroyIcon，无论编码成功与否都释放；转换后的 BitmapImage 使用 OnLoad 脱离流并 Freeze。原始程序只有小图标时仍可能被系统放大，不承诺补出原文件不存在的细节。

去重键是解析并 Path.GetFullPath 规范化后的 ExecutablePath，比较使用 OrdinalIgnoreCase；同目标 EXE / LNK 和大小写差异视为同一个应用。未按文件 ID 合并硬链接、8.3 短路径或其他不同路径别名。没有持久化，重启为空。

原生接口依据：[IShellLinkW.GetPath](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-getpath)、[SHDefExtractIcon](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shdefextracticona)。

新增实现：Models/LauncherApplication.cs；Services/IApplicationImportService.cs、ApplicationImportService.cs、ShortcutResolver.cs、ApplicationIconService.cs；ViewModels/ContentViewModel.cs、ApplicationCardViewModel.cs；Views/ApplicationCardView.xaml 与 code-behind。Verification 工程引用 App，复用真实服务和真实窗口，提供无需新增测试框架包的可执行断言与 OLE 验证；不是 dotnet test 工程。

## 3.x 项目结构（历史基线）

```text
NovaLauncher.sln
global.json                         # 固定 SDK 9.0.318
.gitignore                          # 排除构建产物及本地数据
NovaLauncher.App/
├── NovaLauncher.App.csproj          # WPF / nullable / implicit usings
├── packages.lock.json              # NuGet 依赖锁定
├── App.xaml
├── App.xaml.cs                     # 启动装配与窗口生命周期
├── AssemblyInfo.cs                 # WPF 默认主题资源查找配置
├── Views/
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── SidebarView.xaml / .xaml.cs
│   ├── ContentView.xaml / .xaml.cs
│   └── ShellBackgroundView.xaml / .xaml.cs
├── ViewModels/
│   ├── ViewModelBase.cs
│   ├── MainWindowViewModel.cs
│   └── SidebarViewModel.cs
├── Models/                         # 后续业务模型
├── Services/                       # 后续应用服务
├── Repositories/                   # 后续持久化抽象及实现
├── Data/                           # 后续数据库初始化与迁移
├── Resources/                      # 后续图片等静态资源
├── Themes/ShellTheme.xaml           # 语义画刷、字体、控件模板
└── Helpers/
    ├── HoverFeedback.cs            # 控件实例级 Hover 反馈
    ├── DesktopBackdropController.cs # DWM 请求、能力检测与安全回退
    └── ShellPreferences.cs         # 系统动画与高对比度适配
scripts/Test-UiShell.ps1             # 实际窗口回归与截图
docs/screenshots/                   # 默认、Hover、最小尺寸截图
```

Models、Services、Repositories、Data 和 Resources 五个尚无实现的目录通过 .gitkeep 保留；不添加空服务类或占位业务接口。实际数据库仍应写入用户数据目录，不能写入源码 Data 目录。

## 3.x MVVM 与依赖（历史基线）

App.OnStartup 创建 MainWindowViewModel 并设置 MainWindow.DataContext，再显示主窗口。App.xaml 不使用 StartupUri，避免重复创建窗口；ShutdownMode 为 OnMainWindowClose。

ViewModelBase 继承 CommunityToolkit.Mvvm 的 ObservableObject。MainWindowViewModel 暴露 Title 与 Sidebar；SidebarViewModel 持有只读 NavigationItem 列表及可观察 SelectedItem。NavigationItem 为 UI 导航描述，不是业务模型。SidebarView 使用 ListBox 双向绑定选择。3.1 的 ContentView 是完整启动工作区，仅显示静态拖放提示，不再显示导航标题，不使用页面路由、数据加载或业务命令。

MainWindow 使用 WindowChrome 保留原生窗口行为，code-behind 通过 SystemCommands 执行窗口操作，并在布局尺寸变化时限制 SidebarColumn.MaxWidth。GridSplitter 调整现有星号列宽，最小 210 DIP、最大 35%，属于纯视图行为。App 注册并在退出时解除系统偏好通知；ShellPreferences 只更新资源，HoverFeedback 只负责实例级动画，均不进入 ViewModel。

ShellBackgroundView 封装背景呈现，提供 Mode（Desktop / CustomImage / SolidColor）、BackgroundImage（ImageSource）、SolidBackground（Brush）依赖属性。默认 Desktop 通过独立 DesktopBackdropController 请求 DWM Desktop Acrylic；该控制器管理 HWND、系统偏好通知和不透明回退。IsBackdropActive 只表示原生调用成功，不证明实际模糊效果。组件不解析文件、不读取用户设置、不执行图片导入；MainWindow 仅组合背景与前景层。

唯一直接 NuGet 依赖为 [CommunityToolkit.Mvvm 8.4.2](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2)，采用 MIT 许可，用于复用属性通知及后续命令能力，避免自行维护重复实现；当前目标框架下没有传递包依赖。未添加 DI 容器、SQLite 驱动、导航框架或第三方控件库。

当前使用自定义标题栏、浅色画刷、圆角和白色半透明表面叠加，已接入官方 DWM Desktop Acrylic（Windows 11 build 22621+）并保留 fallback；本机实际截图仍为均匀灰色，透视验收未通过，根因待定位。详情见 Design.md。net9.0-windows 不表示已验证旧版 Windows 的兼容性。

## 模块与依赖

| 计划模块 | 职责 | 允许依赖 |
| --- | --- | --- |
| NovaLauncher.Core | 应用模型、应用用例、服务及 Repository 接口 | .NET 基础类型 |
| NovaLauncher.Infrastructure | SQLite、进程启动、文件与图标访问 | Core |
| NovaLauncher.App | WPF Views、ViewModels、主题资源、窗口材质适配和启动装配 | Core；启动装配处引用 Infrastructure |
| NovaLauncher.Tests | 核心与 ViewModel 行为、SQLite 集成验证 | 被测模块 |

以上是后续扩展方案，Core、Infrastructure 和 Tests 项目当前均未创建。现阶段使用 App 内目录组织职责，后续随业务复杂度再拆分；届时 Core 不引用 WPF 或 SQLite，ViewModel 不引用基础设施实现，应用启动入口负责连接接口与实现。

## MVVM 与数据流

View 通过 Binding 显示 ViewModel 的状态，通过 ICommand 触发操作。ViewModel 调用应用用例和服务接口，Repository 负责持久化，结果返回 ViewModel 更新绑定属性。code-behind 仅处理与控件或窗口生命周期直接相关的视觉行为。

加载流程：ViewModel 请求应用列表 → Repository 查询 SQLite → 返回模型 → UI 线程更新可观察集合。大型列表后续按实测需要引入分页、虚拟化或图标缓存。

启动流程已在 4.2 实现，见 Application Launch Pipeline；支持已导入 EXE 与原始 LNK，URI、打包应用和任意命令脚本不属于当前范围。

## 接口职责建议

- IAppRepository：查询、保存、删除应用条目，隔离 SQLite。
- IAppLaunchService：封装进程启动、目标有效性检查与错误结果。
- IIconService：提供图标获取和缓存能力；WPF 图像转换保留在 UI 适配层。
- ISettingsRepository：持久化后续确认的用户设置。
- IWindowMaterialService：留在 WPF 平台层，管理材质能力检测、应用与实色回退，不放入业务模型。

## SQLite 数据规划

建议将数据库放在 %LocalAppData%\NovaLauncher\nova-launcher.db；仓库和安装目录不保存运行数据。

| 候选数据表 | 字段意图 |
| --- | --- |
| Applications | Id、DisplayName、ExecutablePath、Arguments、WorkingDirectory、SortOrder、CreatedAtUtc、UpdatedAtUtc |
| Settings | Key、Value；设置项确定后再定义类型与校验 |

收藏或分类经确认后再扩展字段或关系。图标优先缓存为用户目录下的文件，数据库只保存必要引用；具体缓存键和失效策略在实现时确定。

数据库访问采用参数化 SQL，连接按明确生命周期释放，写入按需串行化并使用事务。迁移显式记录版本，迁移前备份，失败时保留原始数据并给出错误，不通过删除用户数据库恢复启动。SQLite 驱动的异步能力需要验证，不能仅因方法名带 Async 就认为不会阻塞 UI。

## 线程与错误边界

文件访问、图标提取和数据库操作避免阻塞 UI；可取消操作传播取消信号。ViewModel 的绑定集合更新回到 UI 线程，启动操作执行期间限制重复触发。

服务区分路径失效、访问失败、数据库失败与操作取消。UI 显示可理解的反馈，诊断日志记录必要上下文，不记录敏感参数。关闭窗口时取消后台任务并释放资源。

## 后续验证与决策

业务开发前评审首期功能，视觉开发前确定最低 Windows 版本、材质实现路径和相关依赖版本。材质方案需要验证窗口拖动、缩放、最大化、DPI 变化及关闭透明效果时的行为。

后续业务阶段验证 ViewModel 命令状态和错误反馈、启动服务结果映射、SQLite CRUD 与迁移回滚，再在真实 WPF 窗口检查布局和交互。

第二阶段验证（2026-10-14）：SDK 9.0.318、WindowsDesktop Runtime 9.0.20；锁定还原通过，Release 构建 0 警告、0 错误。实际启动生成的 exe，通过 Windows UI Automation 确认窗口标题及两行绑定文本均正确；关闭主窗口后进程以退出码 0 结束。此次未验证最终 UI、业务逻辑或发布包。

第三阶段验证（2026-10-14）：Release 构建 0 警告、0 错误；Test-UiShell.ps1 通过六项导航与标题绑定、方向键导航、标题栏原生命中测试、最大化/还原/最小化/关闭、调整窗口大小及 25% 侧栏比例检查。默认与最小尺寸截图已人工检查，最小尺寸六个入口均可见；无新增 NuGet 依赖。

3.2：新增 app.manifest 声明 Windows 10/11 兼容性；NuGet 未变化。Release 构建和原生请求/强制回退两条路径的窗口回归通过，但实际壁纸透视尚未可靠生效，作为 Experimental 保留，不阻塞 UI Shell 收尾或后续核心功能开发。

## 背景架构（3.2 收尾）

```text
ShellBackground                         # 由 ShellBackgroundView 组合，与 MainWindow 布局分离
├── DesktopBackdrop                     # Desktop 模式；DesktopBackdropController，Experimental
├── CustomImageBackground               # CustomImage 模式；仅预留，暂不实现
└── SolidColorBackground                 # SolidColor 模式；浅白 Surface，也是统一 fallback
```

以上是背景职责划分，不增加空服务或额外框架。BackgroundMode 固定为 Desktop、CustomImage、SolidColor。Desktop 保留现有 DWM API、Windows 版本检测及失败回退；API 成功不代表壁纸透视生效。CustomImage 的 ImageSource 属性仅为未来输入契约，不加载或呈现图片。SolidColor 在没有输入画刷时使用主题 BackgroundFallbackBrush（#F9FAFC），原生合成目标的回退色读取同一资源，避免重复硬编码。

背景平台逻辑留在 DesktopBackdropController，MVVM 不依赖 HWND；主窗口只组合 ShellBackgroundView 和现有前景。Desktop Backdrop 可以后续研究，但不阻塞核心功能开发。没有新增 NuGet 或业务功能。
