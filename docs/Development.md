# Nova Launcher — 开发流程

## 5.2 最终收尾（2026-10-16）

用户已验收收藏系统，按授权以 `feat: add application favorites` 提交。最终 Release Build 再次确认 **0 warnings / 0 errors**；重新执行 Migration **30 项**、收藏 **27 项**，另含独立重启子进程 **4 项**，全部通过。确认星标鼠标/键盘操作不启动应用、Home/Category/Favorites 筛选互斥、空收藏页面及保存失败状态正确。

真实库只读检查再次确认 IsFavorite 列及原 QQ/微信启动统计；真实升级全部旧字段、分类和图标完整性及收藏重启保留，已在下文首次升级验证中通过。收尾没有再次修改真实收藏状态。提交内容仅包含本阶段收藏实现、新 Migration、配套验证、截图和文档，旧 Migration 不变，无无关修改。不开始下一阶段。

## 5.2 应用收藏系统（2026-10-16）

基线 `1dee8c1527dec2725d59ee8e8a8c44cb89cf10bd`。新增 `20261016100247_AddApplicationFavorites`，仅增加 Applications.IsFavorite（bool → INTEGER NOT NULL DEFAULT 0）；旧 Migration/Designer 未修改。收藏状态从 Entity 经 Model 到 Card VM 完整映射，独立列保存、提交成功再更新 UI，失败保留原状态并显示 Toast。WorkspaceMode 统一 Home/Category/Favorites，不查询数据库过滤，不改变分类业务。未开始最近使用等其他功能，**5.2 已通过用户验收，最终收尾提交已获授权。**

Release Build：**0 warnings / 0 errors**。六套隔离验证全部通过：收藏 **27**、Migration **30**、分类 **33**、持久化 **25**、启动 **32**、导入/OLE **37**，共 **184 项主进程断言**；另有独立 WPF 重启子进程验证。没有新增应用 NuGet 依赖。

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --favorites docs/screenshots/5.2
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --migrations
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --categories TestResults/5.2/categories
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --persistence TestResults/5.2/persistence
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --launch TestResults/5.2/launch
dotnet run --project NovaLauncher.Verification -c Release --no-build -- TestResults/5.2/import
```

收藏验证覆盖：已有 5.1 库的全部原字段/分类保留及 false 默认值、模型快照、数据库列定义、新导入、原生鼠标收藏/取消收藏不启动、动态无障碍名称、Tab 焦点遍历、原生 Enter/Space、成功无 Toast、三种互斥导航、分类与收藏独立、收藏页取消后即时消失、专属空状态、收藏页导入不自动收藏、SQLite 真实写锁失败与恢复、小窗口布局及独立进程恢复收藏。旧启动套件通过原生鼠标与键盘确认卡片主体仍正常启动测试探针。

测试初始因桌面遮挡安全停止，未向其他窗口发送点击；用户确认桌面准备后继续。实测发现 Home 收藏变更触发不必要的视图刷新会重建控件并丢失焦点，已限定仅 Favorites 收藏变化刷新成员，最终鼠标和键盘验证通过。未实机切换系统高对比度、混合 DPI 或断电测试；没有声称覆盖这些场景。

### 真实数据库验证

按本次用户明确授权执行 `--real-favorites`：先创建 SQLite 在线备份，再通过两个独立生产 App WPF 进程执行升级、QQ 收藏、正常完全关闭、重启和 Favorites 页面检查。升级后所有旧记录默认 false，随后仅将 QQ 的 IsFavorite 改为 true。两次窗口验证均通过，全部旧应用列、CategoryId、所有分类行和图标 PNG SHA-256 保持一致；没有启动 QQ 或微信。

| 应用 | 原 LaunchCount | 原 LastLaunchTime | CategoryId | 验证后 IsFavorite |
| --- | --- | --- | --- | --- |
| QQ | 2 | 2026-10-15 06:55:12.358584-08:00 | NULL | true |
| 微信 | 0 | NULL | NULL | false |

在线备份：`%LOCALAPPDATA%\NovaLauncher\Data\Backups\nova-before-favorites-20261016T100726Z-8a5448360c524890af1a269d92d41410.db`。真实验证父进程 5 项断言、两个子进程分别 5/4 项通过。`--real-favorites` 是首次真实升级验收入口，会修改真实收藏状态，不应作为常规回归重复执行；日常使用隔离的 `--favorites`。

已检查真实 WPF 截图：[默认](screenshots/5.2/favorites-default.png)、[Hover](screenshots/5.2/favorites-hover.png)、[收藏页](screenshots/5.2/favorites-page.png)、[空状态](screenshots/5.2/favorites-empty.png)、[小窗口](screenshots/5.2/favorites-small.png)、[QQ 重启后](screenshots/5.2/real-favorite-restarted.png)。RenderTargetBitmap 截图用于布局检查，不证明 Experimental DWM 效果。临时 EF 工具及重复回归截图收尾清理，保留交付截图及真实数据库备份。

## 5.1 最终收尾（2026-10-16）

用户已验收分类功能及 Sidebar UI。最终重新执行 Release Build：**0 warnings / 0 errors**；Migration **30 项**、分类 **33 项**主进程断言全部通过，另有独立重启子进程 **3 项**通过。确认动态分类、统一的新建分类入口、分类筛选、应用分类更新与重启持久化正常。测试使用隔离临时数据库，未再次操作真实用户数据库。

提交范围仅包含 5.1 分类实现、新 Migration、Sidebar 微调、配套验证、截图与文档，无无关修改；按授权使用 `feat: add application categories` 提交，不开始下一阶段。

## 5.1 Sidebar 微调验证（2026-10-16）

本次生产代码仅调整 SidebarView.xaml 与 ShellTheme.xaml 的操作行布局及样式；其余应用源码与微调前的 SHA256 清单一致。验证程序仅增加默认与 Hover 截图采集，并同步设计说明。未修改数据库、Migration、分类业务、筛选、卡片分类浮层、Sidebar 宽度或背景系统。

Release build 为 **0 warnings / 0 errors**；现有分类验证 **33 项断言通过**，退出码 0，使用隔离临时数据库。已检查真实 WPF 窗口截图：[默认状态](screenshots/5.1/sidebar-refinement/sidebar-action-default.png)、[Hover 状态](screenshots/5.1/sidebar-refinement/sidebar-action-hover.png)。UI 与分类功能已通过用户验收，最终提交已获授权。

## 5.1 应用分类系统（2026-10-16）

基线 `1a0ecdc1ee3ccfcf4048b60549e661f31ba50624`。本轮实现分类创建、动态侧栏、应用分类分配及本地筛选，未开发收藏、最近使用页面、排序、重命名/删除或复杂菜单。**已通过用户验收，按授权完成最终收尾提交。** 未新增 NuGet；InitialApplications 与其 Designer 保持不变。

新增 Migration `20261016043853_AddApplicationCategories`：Categories 的 Id/Name/CreatedAt/SortOrder，Name 唯一索引及校验，Applications 可空 CategoryId、索引与 SetNull 外键。默认“游戏”“应用”通过本 Migration 一次性 InsertData 创建为普通记录，重启不 Seed。新导入默认未分类。迁移验证夹具改为固定基线 SQL，整个解决方案不再调用 EnsureCreated。

复现（常规验证使用隔离临时数据库；UI 套件需交互式桌面，屏幕截图需允许屏幕访问）：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --migrations
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --categories docs/screenshots/5.1
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --persistence TestResults/5.1/persistence
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --launch TestResults/5.1/launch
dotnet run --project NovaLauncher.Verification -c Release --no-build -- TestResults/5.1/import
```

已验证：迁移套件 **30 项**，分类套件 **33 项**（另含独立 WPF 子进程中的 3 项），持久化 **25 项**，启动/交互 **32 项**，导入/真实 OLE/卡片回归 **37 项**，五个套件共 **157 项**主进程断言通过。锁定还原通过，最终 Release Build **0 warnings / 0 errors**。迁移覆盖全新库、Legacy Bootstrap 到最新版本和已有 5.0 历史表升级；分类覆盖默认幂等、空白/过长/重复名称、数据库唯一约束、Trim、Enter/Esc、即时 Sidebar 更新、游戏及自定义分类筛选、未分类、已有 Card VM 复用、上下导航单一选择、实际 Popup 选择不启动应用、独立进程重启分类与图标恢复、无效 CategoryId、数据库写锁失败后保持原分类/无幽灵分类，以及 SetNull 行为。

测试发现上下两组 ListBox 使用同一选择属性会残留视觉选中态，已改为分别映射当前唯一选择并通过验证。真实 Popup 使用独立 HWND，RenderTargetBitmap 只画主窗口；因此额外以屏幕捕获保存包含浮层的完整图。沙箱内屏幕捕获不可用，获工具权限后实际捕获通过。分类 UI 键盘输入使用真实控件的 WPF routed key events；原启动回归使用原生鼠标和 Enter/Space，不声称全部新交互均为人工点击验收。

真实数据库升级已完成：验证命令 `--real-categories` 先为 `%LOCALAPPDATA%\NovaLauncher\Data\nova.db` 创建 SQLite 在线备份，再启动两个独立的生产 App 进程。两次启动均确认所有旧字段逐值一致、默认分类各一条、现有 CategoryId=NULL、原图标仍显示、两条 Migration 已应用；PNG 文件 SHA-256 升级前后完全相同。没有启动 QQ/微信，也没有给真实应用分配测试分类。

| 应用 | 升级前后 LaunchCount | 升级前后 LastLaunchTime | 新 CategoryId |
| --- | --- | --- | --- |
| QQ | 2 | 2026-10-15 06:55:12.358584-08:00 | NULL |
| 微信 | 0 | NULL | NULL |

本次真实升级备份：`%LOCALAPPDATA%\NovaLauncher\Data\Backups\nova-before-categories-20261016T045508Z-58c282d6ddd644e499fe559449c8161d.db`。这是明确授权的真实升级验收备份，不表示每次正常 Migration 启动都备份；不写入仓库。`--real-categories` 会使用真实用户目录，只用于明确授权的验证，常规回归使用 `--categories`。

截图已检查：[真实用户升级后](screenshots/5.1/real-upgraded.png)、[新建分类](screenshots/5.1/new-category.png)、[重复提示](screenshots/5.1/duplicate-category.png)、[游戏筛选](screenshots/5.1/games-filter.png)、[分类浮层](screenshots/5.1/category-picker-desktop.png)、[最小窗口](screenshots/5.1/categories-small.png)。卡片保持 136×148 DIP，名称两行、原统计和 Hover 层级保留。范围与关系见 Product.md / Architecture.md，视觉与技能使用见 Design.md。

边界：没有实机修改 NTFS ACL、断电/强杀、跨进程实时同步分类或系统高对比度设置；已覆盖数据库写锁导致的真实写入失败、无效外键及重启。后续删除仅验证外键语义，本阶段没有删除 UI。临时工具、验证数据和无用回归截图在收尾清理，保留本阶段交付截图。

## 5.0 数据库迁移前置任务（2026-10-15）

基线 `5168131f15bbeebe755dc302bf3fbd1dc0924264`。本轮仅升级数据库基础设施，不实现新业务。5.0 已通过用户验收，最终 Release 构建再次确认 0 warnings / 0 errors；修改范围只含迁移基础设施、验证及相关文档，按用户授权提交 `chore: migrate database lifecycle to EF Core migrations`，不开发分类功能。生产 `EnsureCreated` 正式废弃，所有后续 Schema 变化都必须新增 EF Core Migration。完整检测、备份、Baseline Bootstrap 和失败策略见 Architecture.md 的 5.0 章节。

新增 `Microsoft.EntityFrameworkCore.Design 9.0.16`，版本与 Sqlite 一致，MIT / PrivateAssets=all；更新锁文件。基线 `20261016040336_InitialApplications` 与旧 Applications 完全一致，包含 Designer 与 ModelSnapshot；设计时 Factory 不打开或初始化用户库。

常规验证（隔离临时库，不写真实用户数据）：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --migrations
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --persistence TestResults/5.0/persistence
```

最终锁定还原通过，Release Build **0 warnings / 0 errors**；迁移套件 **30 项断言通过**，持久化回归 **25 项断言通过**。`git diff --check` 通过，生产代码没有 EnsureCreated / EnsureDeleted 调用。最终提交已获用户明确授权。

迁移测试覆盖新库/空 Applications、正确历史记录、快照一致、旧库全部字段（非零 LaunchCount、null 与带 offset/小数精度的 LastLaunchTime）、WAL 在线备份、备份无历史表、多次重启只备份一次、索引/类型/CHECK/NULL 不兼容、并发初始化互斥、备份目录失败、初始化失败不重复重试、日志与友好错误、已有空历史表导致真正 Migrate SQL 失败且原行保留。验证临时文件结束后自动清理。

持久化回归 **25 项断言通过**，包括新库启动、EXE/LNK 导入、去重、真实测试进程启动与统计保存、三次独立 WPF 进程关闭/重启、图标缓存、损坏数据及写入失败。使用隔离数据库和测试探针，没有启动 QQ 或微信，也没有改变真实用户启动统计。没有修改 UI 布局，因此没有重新运行鼠标/键盘/OLE 全套视觉回归。

真实库升级已实际执行：`--inspect-db` 只读记录升级前结构与 DisplayName/LaunchCount/LastLaunchTime；`--real-migration` 启动三个独立的生产 App WPF 实例（默认用户数据路径），比较所有数据库列及窗口 ViewModel 卡片信息。**三个实例均通过，原有两行所有字段完全一致，仅产生一次备份，历史表只有一条基线记录。** 第一次完成 Legacy Bootstrap，第二、三次不再备份或重复登记。

| DisplayName | 升级前后 LaunchCount | 升级前后 LastLaunchTime |
| --- | --- | --- |
| QQ | 2 | 2026-10-15 06:55:12.358584-08:00 |
| 微信 | 0 | NULL |

备份位于 `%LOCALAPPDATA%\NovaLauncher\Data\Backups\nova-before-migrations-20261016T0412249790147Z-84ce5e372ba147ebbac520cff2bf3ed0.db`（文件名使用 UTC；本记录日期为用户时区）。原库和备份都不进入 Git。

`--real-migration` 是对真实旧库的一次性验收入口，确实会更新真实数据库，只在明确授权验证时使用；现在真实库已升级，不要为重跑该场景删除历史表或恢复覆盖原库。常规测试使用 `--migrations` 即可。只读检查可执行 `dotnet run --project NovaLauncher.Verification -c Release --no-build -- --inspect-db`。

后续新增迁移：安装固定 `dotnet-ef 9.0.16` 工具（例如 `dotnet tool install --global dotnet-ef --version 9.0.16`），然后执行：

```powershell
dotnet ef migrations add <Name> --project NovaLauncher.App --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project NovaLauncher.App
```

审查生成的 Up/Down、Designer 和 Snapshot，执行新库及旧版库升级测试。不得修改固定基线或把 Bootstrap 的参考切换成最新模型；不得直接对尚未 Bootstrap 的真实旧库运行 `dotnet ef database update`。运行时由初始化服务控制升级。初始 Migration 的 Down 是 EF 生成的删表回滚，生产启动不会调用它，不用于保留用户数据的回退。

失败诊断：查看 `Data/migration-errors.log`；保留原库及备份，修复权限/结构问题后重新启动。备份失败不登记历史，事务失败回滚登记；已有历史表则不触发 Legacy 备份。被强杀时若 EF 遗留锁，先确认无升级进程再人工诊断，应用不自动清空锁表。未实测断电、强杀或真实 NTFS 拒绝 ACL；已覆盖隔离目录不可写入情形、SQL 失败、WAL 与并发初始化。

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

## 4.3 验证记录（2026-10-15）

最终收尾：用户已确认 4.3 通过最终人工验收，并授权提交 `feat: persist applications with SQLite`。未再修改数据库架构；Architecture.md 已明确当前使用 EnsureCreated、下一次 Schema 扩展前必须切换到 EF Core Migrations。检查仓库无运行数据库或已跟踪的用户图标缓存，git check-ignore 确认 nova.db、WAL、SHM、journal、其他 db-* 附属文件及 Cache/Icons 均被排除。最终 Release build 为 **0 warnings / 0 errors**。以下“不自动提交”描述为此前交付时的历史边界，以本次授权为准。

提交前 Toast 修正：`dotnet run --project NovaLauncher.Verification -c Release --no-build -- --toast docs/screenshots/4.3/toast`，**11 项断言通过**、退出码 0。使用隔离 SQLite 库和真实 OLE 拖放，覆盖首次 EXE 添加、同 EXE 重复、同目标 LNK 重复、2 秒自动淡出、连续拖拽重置计时、单实例 Toast、释放时取消计时、不抢焦点、不拦截鼠标、显示/隐藏前后卡片位置尺寸一致。Release build **0 warnings / 0 errors**。已检查[实际 Toast 截图](screenshots/4.3/toast/duplicate-toast.png)。本次未重新运行完整持久化/启动套件，以下 94 项为前次 4.3 验证记录；Toast 修正不修改这些业务实现。仍不自动提交。

基线 `480f357eb95fb74b1dc3cb7b88237b3389620dba`。本轮不自动提交。新增 EF Core SQLite 9.0.16 与锁文件，使用 EnsureCreated；详细 schema、许可证、Context 生命周期及升级限制见 Architecture.md 的 4.3 章节。

Windows 11、125% DPI、SDK 9.0.318。锁定还原通过，最终解决方案 Release build **0 warnings / 0 errors**。自动断言：持久化 **25 项**、原导入/卡片 **37 项**、原启动/交互 **32 项**，共 **94 项**，三个验证命令均退出码 0。

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --persistence
dotnet run --project NovaLauncher.Verification -c Release --no-build -- docs/screenshots/4.3/import-regression
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --launch docs/screenshots/4.3/launch-regression
```

持久化验证使用 `%TEMP%\NovaLauncher-Verification-<guid>` 下隔离数据库、PNG 缓存和测试探针，不写正式用户库。正式路径计算验证为 `%LOCALAPPDATA%\NovaLauncher\Data\nova.db`。所有 WPF 验证窗口通过构造注入隔离 Persistence Service，业务默认路径没有环境变量覆盖。临时文件在测试结束后清理。

- 首次启动创建 Data/nova.db 和 Cache/Icons；EXE/LNK 导入后数据库有记录，三个应用恢复且顺序稳定。
- 三次独立 WPF 进程启动：首次导入并启动测试程序、正常关闭；第二次恢复卡片、PNG、LaunchCount 和完整 LastLaunchTime，并再次启动 LNK；第三次在 EXE 被删除后启动，记录保留且 IsAvailable=false。首次进程在 LNK 请求在途时关闭，验证 App 退出前等待统计写入。
- 同目标 EXE/LNK、大小写重复不增加记录；绕过 VM 直接向 Repository 插入仍被唯一索引拒绝。旧统计快照不覆盖新次数。
- 删除/破坏 PNG 后重新提取；缓存目录被文件占用导致写失败时，数据库和内存图标仍可用。
- 单条时间值损坏时，其余记录继续加载；整个数据库损坏和数据目录不可创建时返回轻提示；保存失败无幽灵卡片。统计写入失败不发 LaunchFailed、不回滚成功次数。
- 原 4.1 回归使用真实 OLE CF_HDROP，经 View → VM → 新 SQLite 链路；原 4.2 回归使用实际鼠标、Enter、Space，验证 EXE 工作目录、LNK 参数/工作目录、快速点击保护与失败不计数。测试前检查输入目标窗口，未向其他应用发送点击。

实际截图已人工检查：[恢复卡片](screenshots/4.3/restored.png)、[启动后 Hover](screenshots/4.3/launch-regression/launched-hover.png)。固定卡片尺寸、两行名称、统计弱层级、自动换行保持不变；未新增“已添加 X 个应用”提示。截图使用运行窗口 RenderTargetBitmap，不证明 Experimental DWM 透明效果。

边界：交互是自动驱动真实桌面窗口，不宣称用户手工 Explorer 拖放验收；未实机修改 NTFS ACL 来复现拒绝写权限，已验证不可创建目录、损坏库和缓存写失败。未测试断电、强杀进程、多实例统计合并或后续 schema 升级。正常关闭会等待在途任务，强制终止不能保证未提交数据。

独立进程测试曾发现 Closing 中同步再次 Close 的 WPF 重入异常，已改为 Dispatcher 延后关闭并通过重验。新增 .gitignore 的 db-journal 和 Cache/Icons 排除项。未修改 ApplicationImportService、ShortcutResolver、ApplicationIconService、ApplicationLaunchService、ContentView 拖放适配、MainWindow、Sidebar 或背景系统。

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
