# Nova Launcher — 架构设计

## 6.1 UserProfile（当前架构）

数据流：ProfileView → UserProfileViewModel → IUserProfileService / UserProfileService → IUserProfileRepository / UserProfileRepository → SQLite。View 只处理 Popup 锚点、焦点与窗口事件；Windows 文件选择器通过 IAvatarFilePicker 注入。App 等数据库初始化完成后加载 Profile，退出等待启动与资料写入任务。

新增 Migration：20261017021040_AddUserProfile；未修改旧 Migration，不使用 EnsureCreated。

| UserProfiles 字段 | SQLite 类型与约束 |
| --- | --- |
| Id | INTEGER 主键，固定 1，禁止自动生成 |
| DisplayName | TEXT NOT NULL，Trim 后长度 1–24 |
| AvatarPath | TEXT NULL；null 表示默认 Nova 矢量头像 |
| UpdatedAt | TEXT NOT NULL，映射 DateTimeOffset |

默认记录使用参数化 INSERT OR IGNORE 幂等创建；名称优先 Environment.UserName，异常、空值或超长回退 User。Profile 读取失败时保留 User 与默认头像，不阻断应用列表。

头像流：File Picker → 格式/解码校验 → PNG 转换 → Local Profile Storage → AvatarPath → WPF ImageSource。存储在 %LOCALAPPDATA%\NovaLauncher\Profile\avatar-{guid}.png，不放入图标缓存。使用版本文件替代建议的固定 avatar.png：先写临时文件并重命名，再更新 SQLite，成功后清理旧文件；数据库失败删除新文件并保留旧文件。恢复默认先提交 null，删除失败仅记日志。图片最长边 512，输入限 50 MB / 3200 万像素。服务串行处理资料操作。

BitmapImage 使用 OnLoad，释放流后 Freeze，避免锁住源文件。缺失或损坏的已保存头像回退默认显示。名称和头像仅在持久化成功后更新 UI；名称保存失败恢复旧名称。

Settings 使用独立 WorkspaceMode 与简单占位模板，Sidebar 不显示 Settings 项，也不显示虚构选中态；可正常返回任意已有导航。以下架构说明为历史记录。

## 5.4 Home Projections

本节更新当前边界：Home 使用独立 HomeView/HomeViewModel，ContentView 保留拖放、Toast 和空状态外壳，通过模板切换首页与原筛选列表；同一时刻仅实例化当前页面。设置保留原全部列表占位。

~~~text
Application Collection
        ↓ Home Projections（共享 ApplicationCardViewModel 引用）
Home
├── Recent Applications (Top 6)
├── Favorite Applications (Top 6)
└── All Applications（源集合只读包装）
~~~

HomeViewModel 监听源集合增删、Move、Reset，以及 LastLaunchTime、DisplayName、IsFavorite 属性；Recent 与独立 Recent 页共用 RecentApplicationComparer，按时间倒序、OrdinalIgnoreCase 名称、Guid 排序。Favorite 取源顺序前六个收藏。投影以 Insert/Remove/Move 更新，不复制模型、卡片或查询 SQLite；未变控件保持。当前无可变 SortOrder 卡片属性，顺序由源集合决定，Move 会触发补位。Dispose 解除集合和属性订阅。

ShowRecentCommand/ShowFavoritesCommand 发出导航事件，由 MainWindowViewModel 选择既有 Sidebar 项，沿原导航切换 Workspace。FilteredApplications 集合变化通知空状态，支持源 Reset。

同一 ViewModel 可出现于多个 Section，因此分类 Popup 的打开状态改为每个 ApplicationCardView 本地 ToggleButton 状态，避免同时弹出多个浮层；打开时同步选择、保存后关闭、Esc 及卸载关闭均属视图适配，仍调用原分类命令。分类/收藏/启动持久化服务、Schema、Migration 和依赖均未修改。下文 5.3 及更早章节保留历史架构。

## 当前边界

NovaLauncher.sln 包含 NovaLauncher.App、NovaLauncher.Verification 和仅用于测试的 NovaLauncher.LaunchProbe，目标框架为 net9.0-windows。5.3 增加 Recent 内存视图与独立排序，保留 5.1 分类、5.2 收藏及现有数据库生命周期，不改变 Schema。

## 5.3 Recent Pipeline

```text
WorkspaceMode.Home      → All Applications, source order
WorkspaceMode.Category  → CategoryId filter, source order
WorkspaceMode.Favorites → IsFavorite filter, source order
WorkspaceMode.Recent    → LastLaunchTime != null, LastLaunchTime DESC

Application Launch Success → LastLaunchTime updated → PropertyChanged
    → ContentViewModel (only in Recent) → ICollectionView.Refresh
    → Recent filter + descending time sort
    → existing statistics persistence → SQLite → reload after restart
```

沿用原 ListCollectionView 与完整 Applications；CustomSort 仅在 Recent 生效，比较 DateTimeOffset 的实际时间（不比较格式化字符串），相同时间再按 OrdinalIgnoreCase DisplayName、Guid Id 排序。SelectWorkspace 在 DeferRefresh 内同时设置 CustomSort 与 Filter，由结束延迟作用域触发一次刷新，之后更新空状态通知；不在延迟期间显式调用 Refresh。切换至其他模式清空 CustomSort，恢复源集合顺序，全程不读取 SQLite。

ContentViewModel 为加载/导入的每张卡片订阅 PropertyChanged，只响应 Recent 模式中的 LastLaunchTime 变化，Dispose 时解除订阅。继续使用原启动成功分支更新属性并持久化统计；失败不更新时间，不会重排。Recent 不写 CategoryId/IsFavorite，不新增卡片或时间格式化实现。正常关闭仍等待启动与统计写入，重启由原持久化恢复时间后应用相同筛选与排序。

数据库/Entity/Model/Repository/Migration/Snapshot 均不改。异常时间沿用原仓库逐行恢复策略，跳过损坏记录并给出已有轻提示，其他应用可继续进入 Recent；失效路径仍作为 IsAvailable=false 卡片显示，启动时沿用原错误处理。

## 5.2 Favorite Pipeline

```text
Application
    ├── CategoryId: Guid?
    └── IsFavorite: bool

WorkspaceMode.Home      → All Applications
WorkspaceMode.Category  → CategoryId Filter
WorkspaceMode.Favorites → IsFavorite Filter

FavoriteButton → ToggleFavoriteCommand → ApplicationCardViewModel
    → ApplicationPersistenceService.SetFavoriteAsync
    → ApplicationRepository.UpdateFavoriteAsync → SQLite commit
    → latest Application with IsFavorite → star state → Favorites view refresh
```

Migration `20261016100247_AddApplicationFavorites` 只对 Applications 增加 IsFavorite，SQLite 定义为 `INTEGER NOT NULL DEFAULT 0`，EF/Entity/Model 映射为 bool。旧 Migration 与 Designer 不改；当前 Snapshot 更新。旧记录及新导入默认 false，原分类、元数据、统计和图标缓存不变。

Repository 每次独立 Context 在后台只更新 IsFavorite 并检查行数，分类仍只更新 CategoryId，统计仍只更新次数/时间。VM 先保存再确认 UI，成功从最新 Application record 合并单一属性，避免并发字段覆盖；失败保留内存状态并显示“收藏状态保存失败”。命令与 VM 保存标志防重入，正常关闭 FlushAsync 等待收藏命令结束，无成功 Toast。

ContentViewModel 保留同一 Applications 与 ListCollectionView，通过 WorkspaceMode switch 选择唯一过滤条件；SelectWorkspace 同步模式和选中分类。Sidebar 仍只有一个 SelectedItem，分类与收藏模式不交叉组合，不重新查询数据库。只有处于 Favorites 时，收藏变更才刷新筛选；Home/Category 的成员不变，保留控件与键盘焦点。Favorites 取消收藏立即消失但不删除源 Card VM。

FavoriteButton、CategoryButton 与 LaunchButton 为兄弟控件，原生 Click/键盘命令不会路由经过启动按钮。View 没有新增数据库操作；星标动态 AutomationProperties.Name 为“收藏/取消收藏 + 应用名”。分类入口移至左上给右上星标留出空间，分类业务不变。

## 5.1 Category Pipeline

```text
Category 1 ── * Application
               CategoryId: Guid? → null means uncategorized

Navigation → SelectedCategory → Filtered Application View

Sidebar editor → CategoryManagerViewModel → CategoryService
    → CategoryRepository → EF Core → SQLite commit → Category Collection → Sidebar

Card category picker → ChangeCategoryCommand → ApplicationPersistenceService
    → ApplicationRepository.UpdateCategoryAsync → SQLite commit
    → latest application model with CategoryId → refresh filtered view
```

新 Migration：`20261016043853_AddApplicationCategories`。保留已提交 InitialApplications / Designer 不变，仅更新当前 ModelSnapshot。新增 `Categories`：Id TEXT Guid 主键、Name TEXT NOT NULL 唯一索引、CreatedAt TEXT NOT NULL DateTimeOffset、SortOrder INTEGER NOT NULL。Name 有长度 1–24 和 ASCII Trim CHECK，服务先做 .NET Trim 和 UTF-16 长度校验；完全相同名称由 SQLite BINARY 唯一索引拒绝，大小写不同允许。Category 不依赖 WPF。

Applications 新增可空 TEXT CategoryId 与索引，外键引用 Categories.Id，`ON DELETE SET NULL`；本阶段没有分类删除入口，测试在隔离库验证未来删除不会删除应用。EF SQLite 为添加外键执行其标准表重建流程，迁移复制全部旧字段并恢复原有索引/CHECK，不删除数据库文件或重新初始化数据库。旧记录 CategoryId 为 NULL。

同一 Migration 通过 InsertData 创建“游戏”“应用”，采用固定 Guid、固定 CreatedAt 和 SortOrder=0/1，避免设计时值漂移。只在 Migration 首次应用时插入，不使用每次启动 Seed、名称修复或 HasData 更新记录；未来管理这些普通分类时不会被启动逻辑重新创建。自定义分类 Guid.NewGuid，CreatedAt=UtcNow，SortOrder=当前最大值+1，仅用于稳定显示，没有排序交互。

CategoryRepository 提供 GetAllAsync / AddAsync / ExistsByNameAsync，每次操作独立 Context，后台 Task.Run 执行 SQLite I/O；分类服务由 App 注入，不在 View/VM 操作数据库。应用先完成既有数据库初始化与恢复，再加载分类。ApplicationRepository.UpdateCategoryAsync 校验分类存在、检查应用更新行数，数据库外键兜底；只更新 CategoryId。统计更新仍只写次数/时间，因此互不覆盖。

CategoryManagerViewModel 持有 ObservableCollection<CategoryViewModel> 和供卡片共享的 Choices；保存成功才追加，失败不产生幽灵导航。SidebarViewModel 将首页、数据库分类与占位导航分组显示，共享唯一 SelectedItem；两组选择属性把非所属组映射成 null，确保视觉上只选中一项。默认名称沿用游戏/应用图标，其余统一文件夹图标。新建区位于分类和占位导航之间，侧栏整体滚动，保持小窗口可访问。

ContentViewModel 保留完整 Applications 集合，单独建立 ListCollectionView 作为 FilteredApplications。SelectedCategory 与卡片 CategoryChanged 仅刷新本地视图；不重新查询全部应用，不创建新 Card VM，不移除源集合。首页/占位导航清空筛选；分类空状态提示用户从首页分配，拖入任何位置都创建未分类记录。

分类选择按数据库提交成功后再更新内存；等待期间禁用选择列表，失败 Toast“分类修改失败”，重新通知 CurrentCategoryChoice 恢复已提交选择。成功使用最新 Application record with 修改 CategoryId，保留并发启动已更新的统计。新分类保存的重复名称/异常显示“该分类已存在”或“分类保存失败”，错误边界捕获并记录 Trace。窗口正常关闭通过 FlushAsync 等待初始加载、分类创建、分类分配、导入及启动统计写入。

View code-behind 仅适配 Enter/Esc、焦点、Popup 和选择事件到命令；分类入口是 LaunchButton 的兄弟控件，不会透传成启动。无新增 NuGet，无 MessageBox，无直接 ALTER TABLE 或 EnsureCreated 调用。

## 5.0 数据库生命周期

```text
Fresh Install → EF Core Migrations → SQLite

Legacy EnsureCreated DB → Schema Detection → Backup
    → Baseline Bootstrap → EF Core Migrations → SQLite

已有 __EFMigrationsHistory → EF Core Migrations → SQLite
```

`EnsureCreated` 正式废弃，不得再调用它创建或修改 Schema。所有后续结构变化必须新增 Migration，不能编辑已经应用的基线或删库升级。5.1 将验证夹具也冻结为初始 Migration SQL，不再通过演进中的 DbContext 模型创建所谓旧版数据库。

`Persistence/DatabaseInitializationService` 负责整个生命周期；原 Repository.InitializeAsync 委托该服务，App 经既有 Persistence Service 加载入口触发，不把迁移逻辑放入 App、ViewModel 或窗口。初始化在后台执行；每个 Repository 缓存初始化 Task（包括失败），避免导入反复重试。数据库旁的 `nova.db.initialization.lock` 以 FileShare.None 串行化不同进程的完整升级；文件可保留，句柄释放即解锁。占用时立即失败，下一次启动可重试。

固定基线为 `20261016040336_InitialApplications`，由 EF Core 9.0.16 生成 Migration、Designer 和 ModelSnapshot。只定义原有 Applications 的 11 列、主键、TargetPathKey 唯一索引和两个 CHECK 约束，未增加业务结构。

无历史表且非空的库必须精确匹配基线：服务把固定初始 Migration 的 SQL 应用于内存 SQLite，再比较 sqlite_schema 中所有非内部对象的类型、名称及建表/索引 SQL。比较忽略 SQL token 之间的排版空白，保留 token 边界、引号和字符串内容；覆盖列类型、NULL、主键、唯一性、CHECK、默认值及额外对象。即使语义等价但不是受支持的旧版格式，也保守拒绝自动登记；不尝试修复未知 Schema。参考来自不可变基线，不能改为当前 DbContext 模型。

Legacy 检测、备份和历史登记期间持有 SQLite immediate 事务，阻止其他写入。备份从另一只读连接使用 `SqliteConnection.BackupDatabase` 读取已提交快照（包含 WAL），写入 `Data/Backups/nova-before-migrations-<UTC timestamp>-<guid>.db`，CreateNew 防覆盖，并检查 `PRAGMA integrity_check`。备份失败则停止登记；不会删除原库，也不自动覆盖恢复。

兼容且备份成功后，使用 EF `IHistoryRepository.GetCreateScript()` 建立历史表，并通过参数化 INSERT 登记固定 MigrationId / ProductVersion=9.0.16。DDL 与登记在同一事务提交，只用于 Legacy Bootstrap，受控 SQL 集中在初始化服务。之后 `MigrateAsync` 看到基线已应用，跳过 Applications 的 CREATE，只运行待应用迁移。新库直接由 Migrations 建表；已有历史表即使为空也不猜测为 Legacy，正常迁移失败时保留数据。

启动失败通过现有轻提示显示“数据库升级失败，现有数据未被删除”，详细异常写 Trace 和 `Data/migration-errors.log`，不向普通用户显示堆栈。没有自动重试循环；EF SQLite 迁移锁等待设 30 秒取消上限。异常退出后 EF 的 `__EFMigrationsLock` 可能残留锁记录，需要先确认没有运行中的升级再人工检查，应用不会擅自删锁或删库。历史表成功提交后，后续启动不再 Bootstrap 或备份；若在提交前异常退出，下一次启动可安全重试且备份文件不会被覆盖。

新增直接依赖 `Microsoft.EntityFrameworkCore.Design 9.0.16`（MIT，与 Sqlite 同版本，PrivateAssets=all），用于正式生成迁移和快照；设计时 Factory 只创建 Context，不初始化数据库。替代方案是手写 Migration/Snapshot，但容易漂移，故采用官方工具。开发工具 `dotnet-ef 9.0.16` 是工具而非应用依赖；全部 NuGet 传递依赖见锁文件。

官方依据：[Migrations 生命周期](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)、[管理迁移及历史登记](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing)、[应用迁移](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)。

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

4.3 新增直接依赖 `Microsoft.EntityFrameworkCore.Sqlite 9.0.16`，适用于 net9.0-windows，EF Core/Microsoft.Data.Sqlite 为 MIT；SQLitePCLRaw 2.1.10 包为 Apache-2.0，SQLite 引擎为公有领域。许可证已核对还原包 nuspec；传递版本均记录于 packages.lock.json。Design 包在 5.0 加入，未添加其他 ORM。替代方案是直接使用 Microsoft.Data.Sqlite 编写 SQL，但本项目按用户要求采用 EF Core。[包与兼容框架](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/9.0.16)。

数据根目录通过 `Environment.SpecialFolder.LocalApplicationData` 获得；本机正式路径为 `C:\Users\王倚烽\AppData\Local\NovaLauncher\Data\nova.db`。首次运行自动创建目录及数据库，无需服务器或 SQL 操作。4.3 曾采用 EnsureCreated；5.0 已改为上文的 Migrations 生命周期。

5.0 已完成该迁移前置要求；今后禁止通过 EnsureCreated 修改 Schema，也禁止通过删库升级。[官方限制](https://learn.microsoft.com/en-us/ef/core/managing-schemas/ensure-created)。

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
