# Nova Launcher Product Audit 8.0

审计日期：2026-10-18（America/Anchorage）。基线：`38f77dcdbd7d3729d6de8491ad981e64591691fd`。

本阶段仅检查、测试、分析与文档；未修改产品源码、Schema 或 Migration，未提交 Git。测试写入隔离临时数据库；真实用户数据库只读。下文区分本次实测、源码结论、历史证据与未验证边界；Implemented 表示已实现，不表示所有系统配置均已验收。

## Executive Summary

Nova 已形成可用的本地启动器闭环：EXE/LNK 导入、启动与统计、分类/收藏/最近使用、首页聚合、中文/拼音 Quick Launcher、资料、窗口设置和图片背景均已实现。架构按 View → ViewModel → Service → Repository 分工，当前规模下合理，但尚不是正式发布完成的产品。原始 Product 第一版中的文件选择添加、启动路径/参数编辑仍缺失，不能用“已实现当前阶段”代替“原始产品范围全部完成”。

**适合进入正式发布准备，不适合直接宣告正式版发布。** 最严重的三个差距：多实例覆盖已保存设置（隔离实测复现）、发布身份和干净机器验收未完成、300 卡片普通浏览页明显布局延迟。正式日志也缺失，使持久化失败难以追踪。

本次现有验证工具 **17 组、686 项主进程断言通过**，另有工具自身的子进程断言；补充贯通流程 8 项主进程、5 项独立 App 进程断言通过。Release Build **0 warnings / 0 errors**。framework-dependent、win-x64 self-contained、win-x64 single-file 发布生成均成功，但未在无 SDK/无 Runtime 的干净机器验收运行。

300 条补充基准：Home 首次布局 **1867.02 ms**，Category **1889.28 ms**，Favorites **656.11 ms**，SQLite/缓存图标加载 **223.52 ms**；独立 App 从托管入口到数据就绪及首轮布局 **5952.88 ms**。另一个既有 Home 套件测得 **3309 ms**，说明约 1.9 秒是历史量级，不是稳定上限。主要瓶颈是非虚拟化卡片视觉树与绑定/布局，查询匹配本身不是当前首要瓶颈。

## Scope and Evidence

已核查 AGENTS.md、Agent.md、README.md、Product、Architecture、Design、Development，以及 Settings-6.2、Appearance-6.3、Management-6.4、ApplicationEditing-6.5、Search-7.1、QuickLauncher-7.2、PinyinSearch-7.3、SkillIntegration 与阶段记录。UI 审计读取 `ui-ux-pro-max/SKILL.md`，采用内置焦点、可读性和一致性规则；本机 Python 检索不可用，未宣称检索成功。技能不改变 WPF 技术栈。

源证据均相对仓库根目录；当前代码优先于文档中标注为历史的旧行为。本次查看了新生成的 Home 小窗口、ContextMenu、贯通流程 Settings 截图；实际原生键鼠、IME、菜单定位由现有验证工具覆盖。高对比度、混合 DPI、第三方 IME、断电/强杀恢复不在本次实机覆盖范围。

初始工作区干净。构建首次受 SDK 目录沙箱读取权限影响，允许构建访问后通过。部分 UI 套件在沙箱内原生输入超时，改在交互式桌面执行后通过，没有更改产品或放宽断言。第一次批量调用存在参数拼接错误，误入默认导入模式，已中止；该不完整结果不计入通过数，产生的历史截图覆盖在收尾恢复。后续均给定独立截图目录。

## Current Feature Matrix

| 模块 | 功能逐项状态 | 入口 / 边界 |
| --- | --- | --- |
| 应用导入 | .exe 拖拽 Implemented；.lnk 拖拽 Implemented；Shortcut 解析 Implemented；名称读取 Implemented；图标提取 Implemented；重复检测 Implemented；Toast/导入状态反馈 Implemented | ContentView 拖入；只接受有效本地 EXE 与指向 EXE 的 LNK。文件选择添加 Missing |
| 应用启动 | EXE 启动 Implemented；LNK Shell 启动 Implemented；LaunchCount Implemented；LastLaunchTime Implemented；快速重复启动保护 Implemented | 卡片左键/Enter/Space；Quick Launcher。每卡片至少 400 ms；多实例统计合并 Partial |
| 持久化 | SQLite Implemented；EF Core Implemented；Migrations Implemented；Legacy EnsureCreated 升级 Implemented；数据库备份 Partial；Repository Implemented | 自动加载/保存；仅 legacy bootstrap 自动备份，没有通用用户备份恢复入口 |
| 图标 | 自动提取 Implemented；Icon Cache Implemented；Custom Icon Implemented；恢复默认 Implemented | 自动导入/加载；卡片菜单选择或恢复 |
| 应用管理 | 重命名 Implemented；收藏 Implemented；取消收藏 Implemented；分类归属 Implemented；打开文件位置 Implemented；应用信息 Implemented；移除 Implemented | 共享卡片右键菜单；启动路径/参数编辑 Missing |
| 分类 | 默认分类 Implemented；动态分类 Implemented；Settings 分类管理 Partial；分类筛选 Implemented | Settings 可创建；Sidebar 筛选；重命名/删除/拖动排序 Missing |
| 首页 | Recent Top6 Implemented；Favorites Top6 Implemented；All Applications Implemented | Sidebar 首页；前两段为空隐藏；全空时拖入提示 |
| 收藏 | Favorites 页面 Implemented；空状态 Implemented | Sidebar 收藏 / Home 查看全部 |
| 最近使用 | 时间倒序 Implemented；重启恢复 Implemented；空状态 Implemented | Sidebar 最近使用 / Home 查看全部 |
| 搜索 | 顶部入口 Implemented；Ctrl+K Implemented；Overlay Implemented；Top8 Implemented；键盘导航 Implemented；中文 IME Implemented（本机覆盖）；中文直接搜索 Implemented；拼音 Implemented；首字母 Implemented | 窗口内 Ctrl+K；Settings 顶部按钮隐藏但快捷键可用；系统级全局热键 Missing |
| 用户资料 | Sidebar 头像 Implemented；Profile Flyout Implemented；DisplayName Implemented；自定义头像 Implemented；恢复默认头像 Implemented | Sidebar 底部头像；单一本地资料，无账户系统 |
| 设置 | 窗口尺寸 Implemented；位置 Implemented；StartupPage Implemented；数据目录 Implemented；外观入口 Implemented；主题切换 Placeholder | 头像 → 设置；深色模式未实现 |
| 背景 | Default Implemented；DesktopWallpaper Implemented；CustomImage Implemented；Overlay Implemented；Blur Implemented；Stretch Implemented；恢复默认 Implemented；DesktopBackdrop Experimental | Settings → 背景；图片背景不是真透明 |
| 窗口 | 自定义 TitleBar Implemented；最小化 Implemented；最大化 Implemented；还原 Implemented；关闭 Implemented；Sidebar Resize Implemented | 标题栏与分隔条；未持久化最大化状态和 Sidebar 宽度 |
| 发布配套 | 发布命令能力 Implemented（本次生成）；正式发布流程 Partial；品牌 App Icon Missing；Installer Missing；Logging System Missing | 临时 publish 成功不等于已有发布交付体系 |

不计算笼统完成百分比：当前阶段的主业务链路完成度高，但正式发布、原始产品范围和多环境质量还有明确缺口。

## Navigation Map

| Workspace / View | 入口 | 退出 | ViewModel |
| --- | --- | --- | --- |
| MainWindow | 启动程序 | 标题栏关闭 | MainWindowViewModel |
| HomeView | 首页 / StartupPage.Home | Sidebar 任意页面 | HomeViewModel，由 ContentViewModel 持有 |
| ContentView：Category | Sidebar 动态分类 | 其他 Sidebar 项 | ContentViewModel |
| ContentView：Favorites | 收藏 / Home 查看全部 | 其他 Sidebar 项 | ContentViewModel |
| ContentView：Recent | 最近使用 / Home 查看全部 | 其他 Sidebar 项 | ContentViewModel |
| SettingsView | Profile Flyout 设置 | Sidebar 首页/分类/收藏/最近 | SettingsViewModel |
| CategoryManagementView | Settings 分类管理 | 切页；创建编辑 Esc/取消 | CategoryManagerViewModel |
| AppearanceView | Settings 背景展开 | 收起/切页 | AppearanceViewModel |
| QuickLauncherOverlay | 顶部按钮 / Ctrl+K | Esc、外部点击、关闭按钮、成功启动 | QuickLauncherViewModel |
| ProfileView / Flyout | Sidebar 头像 | Esc、外部点击、失焦、进入设置 | UserProfileViewModel |
| RenamePopup | 卡片菜单重命名 | 保存成功、取消、Esc、外部点击 | ApplicationCardViewModel |
| ApplicationInfo / InfoPopup | 卡片菜单信息 | 关闭、Esc、外部点击 | ApplicationCardViewModel |
| RemoveConfirmation / RemovePopup | 卡片菜单移除 | 取消、Esc、外部点击、移除成功卸载 | ApplicationCardViewModel |
| ApplicationCardView | Home / 分类 / 收藏 / 最近投影 | 随父页面布局/集合更新 | ApplicationCardViewModel |
| ShellBackgroundView | MainWindow 自动组合 | 随主窗口关闭 | Settings.Appearance.Background / AppearanceService |
| AvatarView、ToastView | 资料/Sidebar；操作反馈 | 父控件关闭；计时消失 | 资料绑定；ToastService |
| SidebarView | MainWindow 常驻 | 随主窗口关闭 | SidebarViewModel |

没有发现无法返回的 Workspace。Settings 不设 Back，但所有 Sidebar 导航可用。Sidebar 两组 ListBox 派生自同一个 SelectedItem；SettingsItem 仍作为头像导航内部状态使用，不是旧可见导航残留。未发现第二套 Workspace 搜索状态。

Sidebar 最终结构为 Logo → Home → 动态 Category → Favorites → Recent → 底部 Profile Avatar。没有旧 Settings 导航按钮或旧 New Category 入口。“游戏”是合理的普通默认类别；“应用”过宽，易与首页“全部应用”混淆。建议先补分类重命名，再把新用户默认“应用”改为“工具”或取消该默认类别；不自动删除已有用户分类。

## Architecture

当前解决方案为 NovaLauncher.App、NovaLauncher.Verification、NovaLauncher.LaunchProbe 三个项目；Core/Infrastructure 独立程序集只是历史规划，当前以 App 内目录分层。Models 不依赖 WPF；Views/VM、Services、Persistence、Themes、Helpers 职责基本明确。App.xaml.cs 是显式依赖装配入口，没有 Service Locator。

业务链路：View → VM ICommand → 接口/Service → Repository → 短生命周期 DbContext → SQLite；平台启动、ShellLink、图标、壁纸及窗口位置在服务/Helper 中。View code-behind 主要负责键鼠、Popup、IME、焦点、窗口和视觉状态。WPF 图像适配仍在 VM/Windows 服务侧，属于现有单平台设计，不应为了拆项目本身制造重构。

ContentViewModel 负责集合、去重、投影和子 VM 生命周期；Home 与 Quick Launcher 订阅同一 ApplicationCardViewModel 集合。Home、Category、Favorites、Recent 复用同一 ApplicationCardView 类型和相同应用 VM，**不是共享同一视觉控件实例**。Home 同一应用可在三个 Section 各生成 View，这增加视觉树负担。

数据库 I/O 放到后台，UI 集合回 Dispatcher；图标及图片读取大多后台执行。仍存在逐应用串行读取、逐条 AddCard 引发投影更新，以及 UserProfileService 的少量目录创建/Move/Delete 留在 UI 续体的问题。App 等待内容、资料、设置和背景初始化后才 Show，首窗等待与 Home 布局必须分开量测。

最重要的架构缺口是没有实例级一致性策略。初始化文件锁只保护 bootstrap/Migrate，不能防止多个 App 同时运行及覆盖缓存设置。

## Database

生产 SQLite 路径为 `%LOCALAPPDATA%\NovaLauncher\Data\nova.db`。本次以只读连接查询 `sqlite_schema`，确认实际包含以下六张表；Schema 与当前 Migration/Snapshot 一致。没有运行真实库迁移或更改数据。

### Applications

| 字段 | SQLite 类型 | 约束 / 含义 |
| --- | --- | --- |
| Id | TEXT NOT NULL | PK，Guid |
| DisplayName | TEXT NOT NULL | 显示名称；重命名服务限制 1–48 字符，数据库无等价长度 CHECK |
| ExecutablePath | TEXT NOT NULL | 目标 EXE；trim 后非空 |
| SourcePath | TEXT NOT NULL | 原 EXE 或 LNK；trim 后非空 |
| SourceType | INTEGER NOT NULL | CHECK IN (0,1) |
| IconCachePath | TEXT NULL | 自动图标缓存指针 |
| CustomIconPath | TEXT NULL | 自定义图标指针 |
| LaunchCount | INTEGER NOT NULL | CHECK >=0 |
| LastLaunchTime | TEXT NULL | DateTimeOffset |
| CategoryId | TEXT NULL | FK Categories.Id；ON DELETE SET NULL |
| IsFavorite | INTEGER NOT NULL | SQL DEFAULT 0 |
| DateAdded | TEXT NOT NULL | DateTimeOffset |
| SortOrder | INTEGER NOT NULL | 存储排序序号，尚无拖动排序 UI |
| TargetPathKey | TEXT NOT NULL | trim 非空；UNIQUE |

显式索引：`IX_Applications_TargetPathKey UNIQUE`、`IX_Applications_CategoryId`。路径键为 GetFullPath 后 ToUpperInvariant；同 EXE 的不同参数快捷方式仍视为重复，不支持一个 EXE 多套启动配置。没有按文件 ID 合并硬链接/8.3 路径别名。

### Categories / UserProfiles

| 表 | 全部字段 | 约束 |
| --- | --- | --- |
| Categories | Id TEXT NOT NULL；Name TEXT NOT NULL；CreatedAt TEXT NOT NULL；SortOrder INTEGER NOT NULL | Id PK；IX_Categories_Name UNIQUE；Name=trim(Name)，length 1–24；名称区分大小写 |
| UserProfiles | Id INTEGER NOT NULL；DisplayName TEXT NOT NULL；AvatarPath TEXT NULL；UpdatedAt TEXT NOT NULL | Id PK，CHECK Id=1，无自增；名称 trim 一致且 1–24 |

“游戏”“应用”是迁移插入的普通行，没有系统分类标志。分类删除外键策略已具备，但产品尚无删除入口。

### AppSettings

| 字段 | SQLite 类型 | 默认值 / 校验 |
| --- | --- | --- |
| Id | INTEGER NOT NULL | PK；CHECK=1，无自增 |
| StartupPage | TEXT NOT NULL | 模型/建行 Home；Home/Recent/Favorites |
| RememberWindowSize | INTEGER NOT NULL | 模型/建行 true |
| RememberWindowPosition | INTEGER NOT NULL | 模型/建行 true |
| WindowWidth | REAL NULL | null → 1120 DIP |
| WindowHeight | REAL NULL | null → 720 DIP |
| WindowLeft | REAL NULL | null → 主屏居中 |
| WindowTop | REAL NULL | null → 主屏居中 |
| BackgroundMode | TEXT NOT NULL | 模型/建行 Default |
| ThemeMode | TEXT NOT NULL | 模型/建行 Light |
| BackgroundImagePath | TEXT NULL | null |
| BackgroundOverlayOpacity | REAL NOT NULL | SQL DEFAULT 0.72；范围 0.2–0.95 |
| BackgroundBlurRadius | REAL NOT NULL | SQL DEFAULT 12；范围 0–30 |
| BackgroundStretch | TEXT NOT NULL | SQL DEFAULT UniformToFill；或 Uniform |
| UpdatedAt | TEXT NOT NULL | UTC DateTimeOffset |

Home/true/Default/Light 是应用建行默认，不应误写为这些列都有 SQL DEFAULT。实际 SQLite 的 0.72 DDL 文本显示为浮点表示 `0.71999999999999997`，不是配置漂移。

### EF infrastructure

`__EFMigrationsHistory`：MigrationId TEXT NOT NULL PK；ProductVersion TEXT NOT NULL。EF 模型长度分别为 150/32，SQLite DDL 不据此限制字符串长度。

真实库还存在 `__EFMigrationsLock`：Id INTEGER NOT NULL PK；Timestamp TEXT NOT NULL。这是 EF SQLite 迁移基础设施，不是应用业务表。不能把只列四张业务表的 Snapshot 当作完整运行库结构。

### Migration audit

| 时间戳 / 名称 | 功能 |
| --- | --- |
| 20261016040336_InitialApplications | 初始 Applications 11 字段、PK/CHECK、目标路径唯一索引 |
| 20261016043853_AddApplicationCategories | Categories、种子类别、CategoryId/FK/索引，SetNull |
| 20261016100247_AddApplicationFavorites | IsFavorite，默认 false |
| 20261017021040_AddUserProfile | 单行 UserProfiles |
| 20261017025537_AddAppSettings | 原 11 字段 AppSettings |
| 20261017084703_AddBackgroundAppearance | 背景路径、遮罩、模糊、Stretch |
| 20261017143447_AddCustomApplicationIcon | Applications.CustomIconPath |

顺序正常。生产源码没有可执行 EnsureCreated；`DatabaseInitializationService.cs:84` 只是解释旧库验证的注释。测试通过冻结 InitialApplications SQL 生成 legacy fixture，不使用当前模型假冒旧 Schema。

Legacy 无历史表且 Schema 严格匹配时，先在线 BackupDatabase 并校验，再事务登记基线，最后 Migrate。已有历史表的正常升级直接 Migrate，**没有自动逐次升级备份**。数据库失败保留原文件、缓存初始化失败，写 Data/migration-errors.log；迁移锁等待有 30 秒超时。没有用户级恢复流程，Down Migration 也不是备份恢复替代品。

Repository：ApplicationRepository、CategoryRepository、UserProfileRepository、AppSettingsRepository；每操作创建/using 释放 Context，无全局 Context。读取 AsNoTracking；连接 Pooling=false，DefaultTimeout=5；统计只更新统计列。参数化 SQL、唯一索引和外键形成基本数据保护。

## Local Storage

源码定义的完整目录职责如下；后附本次实际文件清单。按运行状态，目录/辅助文件可不存在。

```text
%LOCALAPPDATA%\NovaLauncher\
├─ Data\
│  ├─ nova.db
│  ├─ nova.db-wal / nova.db-shm / nova.db-journal
│  ├─ nova.db.initialization.lock
│  ├─ migration-errors.log                 # 迁移失败时
│  └─ Backups\nova-before-*.db
├─ Cache\Icons\<ApplicationId:N>.png
├─ CustomIcons\<ApplicationId:N>-<VersionGuid:N>.png
├─ Profile\avatar-<guid>.png               # 不是固定 avatar.png
└─ Background\background-<guid>.png
```

DB、CustomIcons、Profile、Background 是用户数据；Backups 是恢复资产，不可当缓存随意删除。WAL 可能包含尚未 checkpoint 的已提交数据，不应只复制/删除主 db 就认定备份完整。Cache/Icons 可重新提取，但原 EXE 不可访问时不保证恢复原图。初始化锁和迁移锁也不能在运行中随意清理。当前没有正式 Logs 目录。

文件生命周期：移除记录先成功删除 DB，再清理该 ID 的自动缓存及严格校验归属的自定义图标；不删除 EXE/LNK。更换图标/背景先写新版本，再提交指针，最后删除旧文件，失败保留旧状态并清候选。恢复默认头像或图标在 DB 提交成功后清旧图。背景仅切换 Default/DesktopWallpaper 时保留自定义路径以便切回；“恢复默认”才清空并清理。

仍可能有孤儿 `.tmp`/版本 PNG：进程在落盘与提交之间中断、删除被占用或权限失败时，没有启动扫描/回收。普通成功替换不会每次积累，但失败累计与备份保留没有容量/周期策略。自动缓存按应用 ID 稳定命名，正常不会因每次启动产生新文件。

## Search

Pipeline：QuickLauncherViewModel.SearchQuery → ApplicationSearchService → ApplicationSearchIndex → Match/Rank → Top8 → QuickLauncherOverlay。唯一用户 Query 在 QuickLauncherViewModel；Service.Query 是派生匹配输入，不是另一套 UI 状态。

| Rank | 键与匹配 |
| --- | --- |
| 0 / 1 / 2 | OriginalName 完全 / 前缀 / 包含 |
| 3 / 4 / 5 | NormalizedName 完全 / 前缀 / 包含 |
| 6 / 7 | FullPinyin 完全 / 前缀 |
| 8 / 9 | PinyinInitials 完全 / 前缀 |
| 10 / 11 | FullPinyin 包含 / PinyinInitials 包含 |

同档按 LastLaunchTime DESC、DisplayName（OrdinalIgnoreCase）、Id。原名称优先，派生 Key 去空白转小写但保留符号数字；每应用只取最佳档位，排序后 Take(8)。空 Query Recent Top6；无最近则 Favorites Top6；都为空提示输入名称。

ToolGood.Words.Pinyin **3.1.0.4，MIT**，许可副本位于 App/ThirdParty 并随输出复制。离线生成全拼与首字母，加载/新增建索引、重命名替换、移除/Reset/Dispose 释放；查询不读 SQLite、不重新转换拼音。多音字采用库默认词组/单字读音，不枚举全部读音，不保证语义消歧和生僻字覆盖；原名仍可查。

7.1 Workspace SearchMode、Search Grid、专属空状态及 SearchWrapPanel 已删除。7.1 文档和截图是历史证据，不是运行代码残留。Quick Launcher 通过 WPF Composition 与 WM_IME 消息保护组合输入，提交键未松开时不启动；本次本机原生 IME Space/Enter 检查通过，不代表所有第三方 IME。

## Background System

| 模式 | 真实行为 | 状态 |
| --- | --- | --- |
| Default | 稳定浅色 Surface，不加载背景图 | Implemented |
| DesktopWallpaper | **读取 Windows 壁纸作为 Nova 背景，并不是真实窗口透明** | Implemented，有主/系统静态壁纸限制 |
| CustomImage | 导入自有 PNG，独立 Blur、白色 Overlay、Fill/Fit | Implemented |
| DesktopBackdrop | **真正系统 Backdrop 实验代码，目前未可靠实现桌面透视** | Experimental / Internal |

DesktopWallpaper 使用 SPI_GETDESKWALLPAPER；非空路径失效时尝试 Windows TranscodedWallpaper。只启动/切换/手动刷新，不追踪动态壁纸，也不按显示器选择。Window.Opacity 保持 1；Blur 只作用于图片，不模糊前景文字/控件。

AppearanceService 管图片来源与设置，DesktopBackdropController 管 HWND/DWM，两者并非完全重复。但实验控制器仍随 ShellBackgroundView 构造并注册事件，稳定路径用 SolidColor 禁用材质。Models.BackgroundMode 与 Views.BackgroundMode 同名且值不同，旧 ImageSource 属性留存，容易误接；建议未来明确命名并隔离实验路径。

## Application Management

卡片左键/Enter/Space 启动；右键打开菜单；Hover/键盘聚焦显示 LastLaunchTime 与 LaunchCount，活动时每 30 秒刷新。没有旧星标视觉控件和分类 Hover 按钮；FavoriteGlyph 和收藏/分类 Command 仍被菜单使用。

完整菜单顺序：收藏/取消收藏 → 移动到分类（含未分类）→ 重命名 → 更换图标 → 恢复默认图标（有自定义时）→ 打开文件位置 → 应用信息 → 从启动器移除。重命名 trim 后 1–48 字符；自定义 PNG/JPG/JPEG/BMP/ICO 转自有 PNG；移除有内部确认，默认焦点在取消。

LaunchButton 与菜单独立；没有重复执行 LaunchCommand 的路径。实际右键、Shift+F10、Apps 键及菜单操作回归通过，不触发 Launch。ContextMenu/子菜单采用 WPF Popup 工作区边缘定位，子菜单限高滚动；最大化与右下角定位由 Management 套件验证。三种背景的 Overlay 由 Quick Launcher 套件验证；没有完成“每个菜单 × 每种背景 × 所有 DPI/屏幕边缘”的笛卡尔组合验收。

打开文件位置选中的是已解析目标 EXE，不是 LNK 源；应用信息只读。自定义图标优先于缓存/EXE 提取，最后回退 Nova 默认矢量图。移除等待在途启动/收藏/分类写入并阻止新写入，删除成功才移除 UI，专项已覆盖不会被旧统计写回复活。

## Settings

字段及默认值见 Database。AppSettingsService.Validate 对无效枚举回退；Theme 强制 Light；遮罩/Blur clamp，尺寸范围最小 800×560、通用上限 7680，拒绝非有限位置及绝对值超过 100000。EF 读取未知字符串枚举可能先物化失败，最终回退整份默认设置，不能承诺任何坏字段都仅逐字段回退。

WindowPlacementService 要求至少 80×24 DIP 标题栏可见；否则回主屏，再限制到工作区。最大化/最小化时保存 RestoreBounds；记忆开关关闭时忽略旧值。负坐标、工作区缩小、拔除副屏场景使用输入模拟，本机实际 HWND 恢复由独立进程覆盖；混合 DPI 实机尚未覆盖。

设置保存失败回滚并 Toast。当前主题按钮仍绑定 AppearanceCommand，提示“外观设置将在下一阶段提供”；背景已实现，该文案过宽，应改成仅说明主题切换未实现。**该命令仍被 SettingsView.xaml:14 使用，不是死代码。**

## User Profile

单本地记录 Id=1；DisplayName trim 后 1–24 UTF-16 字符，默认来自本机用户名或 User。Sidebar 常驻圆形头像，名称在 Tooltip/Flyout；没有登录或云账户含义。

自定义头像支持 PNG/JPG/JPEG/BMP，版本文件持久化，恢复默认后清旧头像。已存文件损坏时回默认并 Trace，选择新损坏文件则向用户报错。更换/恢复提交顺序基本安全；同步 Move/Delete 的少量 UI I/O、进程中断孤儿文件是后续债务。

## Performance

本次 Windows 本机 Release，单次取样，不是 P95、冷磁盘基准或 GPU FPS。临时审计 harness 复用产品 View/VM 和现有夹具，结束后删除；没有优化代码。10 → 100 → 300 顺序运行，10 条包含首次中文词典/JIT 成本，后两组词典已热。故不能从 10 条 Load 更慢推断规模倒挂。

### 10 / 100 / 300 matrix（ms）

| 指标 / 测量口径 | 10 | 100 | 300 |
| --- | ---: | ---: | ---: |
| 独立 App 托管入口 → ReadyTask + 首次布局/Render yield | 2269.30 | 3037.04 | 5952.88 |
| 真实临时 SQLite + 有效 PNG 缓存加载（存储已热） | 66.01 | 71.33 | 223.52 |
| 内存记录 → Content 初始化/索引（无磁盘） | 262.02 | 4.97 | 30.59 |
| Home 首次 DataContext + UpdateLayout | 257.14 | 1126.90 | 1867.02 |
| Category 切换 + 布局（所有应用同分类） | 97.01 | 981.93 | 1889.28 |
| Favorites 切换 + 布局（半数收藏） | 53.82 | 397.32 | 656.11 |
| Recent 模式刷新/排序（未布局） | 3.88 | 29.15 | 95.28 |
| Recent 切换含布局（全部有时间） | 77.52 | 782.89 | 1449.77 |
| Quick Launcher Open + 布局/Render yield | 29.42 | 45.52 | 213.11 |
| 普通搜索 App 000：Query + 布局/Render yield | 11.02 | 20.47 | 21.32 |
| 拼音搜索 gzwx：Query + 布局/Render yield | 3.20 | 4.85 | 19.55 |
| ContextMenu IsOpen + 布局/Render yield | 150.06 | 54.16 | 59.20 |

内存视觉测试使用默认图标/Default 背景、每条都有最近记录、半数收藏；Category 全量匹配，属于偏重切换场景。磁盘/启动测试另外构造有效 EXE 文件副本及小 PNG 缓存，不启动这些文件；每个规模通过新 App 进程注入同一隔离路径的 persistence/category/profile/settings，App 依原启动装配显示窗口。启动计时不包含 OS CreateProcess 到托管入口，包含 .NET/WPF/EF/拼音初始化；不是正式发布包冷启动。菜单是程序打开真实 ContextMenu，不是测鼠标反应延迟。Quick Open 的 213 ms 可能包含待处理布局/渲染，单样本不足以归因于搜索。

既有 Home 套件另一场景测得 **3309 ms**（300 卡片首次布局）；历史 6.x 约 1.9 秒，7.2 为 2466 ms，本次并未改善性能。初次菜单和字典有明显预热影响，不用最小值作承诺。

### Search / Blur supplements

本次拼音专项 300 条：纯匹配/排序 **0.157–1.971 ms**，Query 投影/同步绑定 **1.32–10.27 ms**，布局/Render yield **6.38–78.99 ms**。1000 条补充测试有 **439.75 ms** 单次布局延迟，不宣称大规模全程流畅。

词典首次初始化 **231.15 ms**，累计分配 **207,857,080 bytes**，强制 GC 后堆增量 **43,168,360 bytes**（含运行时/JIT，不是独占库常驻内存）。300 条派生字符串 UTF-16 数据仅 **40,800 bytes**，不能以此掩盖共享词典成本。热索引构建 **2.20 ms**，查询复用 Key。

Blur12：30 次缩放布局 **722 ms**；82 卡片 30 次滚动 Render yield **96 ms**。未测 Blur30+高分辨率+多屏的长期 GPU/电量，不能承诺复杂背景无性能影响。

### Bottleneck assessment

1. 强证据：Home/Content 的 ItemsControl + WrapPanel 无虚拟化，300 条全量构建卡片；每卡片包含 ContextMenu、三个 Popup、常态/详情层及 Binding。Category/Favorites/Recent 切换同样会创建/更新大量视图。
2. 次要但可见：集合逐项新增、Home/Quick 投影刷新、卡片图像适配和冷词典初始化。数据加载逐项等待图标/存在性，缺缓存时比本次热 PNG 场景更差。
3. 尚未精确拆分：WPF Measure/Arrange、模板实例化、Binding 和图片解码的各自占比；没有 profiler 火焰图，不能给出百分比归因。
4. Blur 仅图片层，与无背景也慢的 Home 首次布局不是同一个瓶颈；搜索纯算法在 300 条为毫秒级，应排在卡片呈现之后。

## Resource Safety

HICON 在 ApplicationIconService finally 中 DestroyIcon；ShellLink COM 在 finally FinalReleaseComObject；Process 包装及时 Dispose。BitmapImage 使用 OnLoad、using 流、Freeze；DbContext 每次创建并释放。图标 HICON 压测和文件连续替换/解除锁专项通过，没有发现明确的持续原生句柄泄漏。

QuickLauncher/Index/Home 的集合和属性订阅具备清理路径；卡片卸载停止 DispatcherTimer、关闭 Popup。Profile Loaded/Unloaded 对称。Sidebar 匿名 CollectionChanged、MainWindowVM 导航订阅、AppearanceVM 服务订阅未显式解订阅，但目前与主窗口同寿命：属于未来多窗口/重新装配的生命周期债，不足以定性为当前高风险泄漏。

高风险候选边界：图片 OnLoad 解码之后才检查像素限制；自动缓存没有与自定义图片相同的输入上限。正常自有 PNG 很小，外部篡改/异常大文件可能造成瞬时内存压力，未做恶意图片或长时间 soak 测试。没有证据把正常字典初始化的大分配误判为泄漏。

## Error Handling

| 失败路径 | 当前结果 | 反馈 / 缺口 |
| --- | --- | --- |
| DB 无法读取/损坏 | 保留文件，返回加载失败 | Content 状态信息 + Trace；未静默重建空库 |
| Migration 失败 | 保留原库，停止初始化重试 | 友好升级错误 + Data/migration-errors.log |
| 单行格式损坏 | 跳过坏行，其余恢复 | Repository 只 Trace；上层已拿到过滤列表，可能不提示遗漏 |
| EXE 不存在 / Shell 启动失败 | 不记成功统计 | Toast/轻提示 + Trace |
| LNK 损坏/目标无效 | 拒绝导入，批次其他项继续 | 导入状态错误 + Trace |
| 自动/自定义图标读取失败 | 缓存→提取→默认回退 | Trace；自动恢复不逐条 Toast |
| 已存头像损坏 | 默认头像 | Trace，无直接 Toast；新选择损坏头像会提示 |
| 背景损坏/缺失 | 会话 Default 显示，保存指针不变 | Toast + Trace |
| 设置保存失败 | 恢复已提交值 | Toast；正常关闭失败短暂停留提示 |
| 分类创建/归属失败 | 旧集合/归属保留 | 内联错误/Toast + Trace |
| 收藏失败 | 保留旧值 | Toast + Trace |
| 重命名/自定义图标失败 | 保留旧值/旧文件 | Toast + Trace |
| 应用移除失败 | 卡片、图标保留 | Toast + Trace |
| 统计写入失败 | 启动仍成功，内存次数继续 | 仅 Trace，无重试/用户可见“统计未保存” |
| 旧图片清理失败 | DB 操作不撤销 | 仅 Trace，可能孤儿文件 |

已测失败注入未导致整体崩溃；**不能声称所有异常绝不会崩溃**。App.xaml.cs 的 async void Closing 只有 finally，没有最终 catch，意外 Flush 异常仍有未处理边界；未建立统一崩溃诊断/恢复机制。

### Logging

**Logging System = Missing。** 当前大量 Trace.TraceError/Warning 没有正式文件 Listener 或日志轮转/留存策略。迁移专项 migration-errors.log 是例外，不等于完整日志系统；NuGet 中传递引入 Microsoft.Extensions.Logging 也不等于配置了应用日志。发布准备应增加本地诊断，例如 `%LOCALAPPDATA%\NovaLauncher\Logs`，记录错误上下文与版本并限制容量，避免记录敏感启动参数；本阶段未实现。

## Dependencies

App 锁文件共 **49 NuGet：4 直接 + 45 传递**；Verification 项目引用继承运行依赖，LaunchProbe 无 NuGet。未发现明显未使用的直接包。

| 直接包 | Version | 用途 / 评估 |
| --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | ObservableObject / RelayCommand，使用中 |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.16 | SQLite provider，使用中 |
| Microsoft.EntityFrameworkCore.Design | 9.0.16 | Migration 工具，PrivateAssets=all；不能因业务不调用就判冗余 |
| ToolGood.Words.Pinyin | 3.1.0.4 | 离线拼音，MIT 文件随包输出 |

完整传递包分组如下；一行多个包均采用该行版本，均非直接依赖。设计工具依赖与运行依赖不能混为全部随产品运行。

| 包 | Version | 用途族 |
| --- | --- | --- |
| Humanizer.Core | 2.14.1 | EF 设计工具命名 |
| Microsoft.Bcl.AsyncInterfaces | 7.0.0 | 异步接口兼容 |
| Microsoft.Build.Framework | 17.8.43 | 构建工具接口 |
| Microsoft.Build.Locator | 1.7.8 | 工具定位 MSBuild |
| Microsoft.CodeAnalysis.Analyzers | 3.3.4 | 编译分析 |
| Microsoft.CodeAnalysis.Common；Microsoft.CodeAnalysis.CSharp；Microsoft.CodeAnalysis.CSharp.Workspaces；Microsoft.CodeAnalysis.Workspaces.Common；Microsoft.CodeAnalysis.Workspaces.MSBuild | 4.8.0 | 设计工具 Roslyn |
| Microsoft.Data.Sqlite.Core | 9.0.16 | SQLite ADO.NET |
| Microsoft.EntityFrameworkCore；Microsoft.EntityFrameworkCore.Abstractions；Microsoft.EntityFrameworkCore.Analyzers；Microsoft.EntityFrameworkCore.Relational；Microsoft.EntityFrameworkCore.Sqlite.Core | 9.0.16 | EF 核心/关系型/provider |
| Microsoft.Extensions.Caching.Abstractions；Microsoft.Extensions.Caching.Memory；Microsoft.Extensions.Configuration.Abstractions；Microsoft.Extensions.DependencyInjection；Microsoft.Extensions.DependencyInjection.Abstractions；Microsoft.Extensions.DependencyModel；Microsoft.Extensions.Logging；Microsoft.Extensions.Logging.Abstractions；Microsoft.Extensions.Options；Microsoft.Extensions.Primitives | 9.0.16 | EF/工具基础设施 |
| Mono.TextTemplating | 3.0.0 | 设计时模板 |
| SQLitePCLRaw.bundle_e_sqlite3；SQLitePCLRaw.core；SQLitePCLRaw.lib.e_sqlite3；SQLitePCLRaw.provider.e_sqlite3 | 2.1.10 | SQLite 原生库/provider |
| System.CodeDom | 6.0.0 | 设计工具 |
| System.Collections.Immutable | 7.0.0 | 不可变集合 |
| System.Composition；System.Composition.AttributedModel；System.Composition.Convention；System.Composition.Hosting；System.Composition.Runtime；System.Composition.TypedParts | 7.0.0 | 工具组合基础设施 |
| System.IO.Pipelines | 7.0.0 | I/O 基础设施 |
| System.Memory | 4.5.3 | 内存基础设施 |
| System.Reflection.Metadata | 7.0.0 | 元数据读取 |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | 基础运行支持 |
| System.Text.Json | 9.0.16 | JSON 基础设施 |
| System.Threading.Channels | 7.0.0 | 通道基础设施 |

这是本地锁定依赖审计，未进行在线漏洞数据库及所有传递许可证核验，不声称完整 SBOM/安全认证。

## Test Coverage

验证体系是自定义可执行 WPF/控制台工程，**不是 dotnet test 测试项目**。19 个 C# 文件有 588 个静态 Check 调用点（排除定义）；包含循环、子进程和 real-only 分支，不能当成独立测试数。当前没有标准测试发现、统一 CI 报告或覆盖率百分比。

| 本次套件 / 模式 | 主进程断言 | 主要性质 |
| --- | ---: | --- |
| Persistence / --persistence | 25 | 临时 SQLite、失败注入、独立进程恢复 |
| Migration / --migrations | 30 | 临时 legacy/schema/迁移失败保护 |
| Home / --home | 29 | WPF、投影、300 卡片布局 |
| Categories / --categories | 34 | SQLite、菜单、分类创建、重启 |
| Favorites / --favorites | 31 | SQLite、菜单、投影、重启 |
| Recent / --recent | 22 | 顺序、统计、重启、纯 VM 切换 |
| Settings / --settings | 36 | SQLite、原生交互、位置模拟、失败回滚 |
| Profile / --profile | 44 | 资料、原生文件选择器、失败回滚 |
| Background / --appearance | 39 | 图片、原生选择器、参数、Windows 壁纸 API |
| Management / --management | 46 | 菜单、边缘定位、Explorer、真实数据只读对比 |
| Application Editing / --app-edit | 59 | 更名、图标、移除、失败/并发等待、子进程 |
| Quick Launcher / --quick-launcher | 69 | 键鼠、原生 IME、排序/Top8、背景、会话保护 |
| Pinyin / --pinyin | 123 | 名称/拼音排序、索引生命周期、300/1000 条 |
| Launch / --launch | 32 | 真 EXE/LNK LaunchProbe、失败、防重 |
| Toast / --toast | 11 | 显示、计时、布局 |
| Polish / --polish | 19 | WPF 布局和交互 |
| Import / 无模式 | 37 | 真实 OLE、PE/Shortcut 校验、资源压测 |
| **合计** | **686** | 全部通过；不累加失败/中止尝试 |

7.1 原 --search 已被替代，不再单独存在；其能力由 Quick/Pinyin 覆盖。历史 7.3 的 631 不含本次追加重跑的 Persistence25 与 Migration30；不是本次少测或历史报告可直接照抄。

模拟边界：锁库/失败仓库、部分壁纸来源、部分 file picker 与屏幕工作区是模拟；LaunchProbe、OLE、Windows 原生对话框和本机中文 IME 是实交互。测试隔离目录在退出时清理；未调用会写真实库的 --real-*、preview 或 Test-UiShell 默认启动路径。

### Integrated user flow

另建临时审计工具，复用真实产品类型与现有测试帮助方法，执行：隔离 WPF → 真实 OLE 拖入“微信.lnk”（目标是 LaunchProbe）→ 分类 → 收藏 → 真实探针启动 → Recent → 原生 Ctrl+K → 中文与 wx 查询 → 原生菜单重命名 → 自定义图标 → Profile 设置入口 → 自定义背景 → 头像及名称 → Flush → 独立 App 进程加载同一临时库。

**8 项主进程 + 5 项子进程断言通过**。子进程确认名称、分类、收藏、次数/时间、自定义图标、资料/头像、Recent 启动页、自定义背景恢复；选择源图已删除，自有文件仍正常。

这是一条混合自动化贯通检查：分类/收藏和部分设置由真实 ICommand/Service 驱动，图片选择使用预设路径，原生文件选择已由专项覆盖。父测试器在 Flush 后隐藏测试窗口并启动独立 App 验证进程，没有先杀掉整个父测试进程；因此它证明独立进程持久化恢复，**不等同于所有步骤均由人工点击且父进程彻底退出后的完整黑盒验收**。正常关闭/重启另有现有子进程测试；强杀、断电恢复仍未验证。没有启动用户真实应用。

### Remaining coverage gaps

干净机器运行发布包、安装/卸载/升级、混合 DPI/多屏拔插、高对比主题实机、第三方 IME、长时内存/句柄、并发实例完整 UI、强杀/断电与备份恢复。对这些项不填“通过”。

## UI / UX Findings

主颜色、字体、间距、圆角、Card、Scrollbar、Toast、Empty State 已有语义资源，四个应用页面一致复用；Settings 和 Quick Launcher 使用合理的层级差异，不应把所有不同字号视为缺陷。

| 级别 | 问题 | 证据 / 建议 |
| --- | --- | --- |
| High | 深色高对比方案下，图片文字底可能仍白色，而文字切为系统白色 | ShellPreferences.cs 的 BrushKeys 未包括 AppearanceLabelSurface；ShellTheme.xaml:122 固定 #CCFFFFFF；高对比隐藏图片却不清 HasImage/NeedsTextSurface。源码组合风险，待实机验证，不写成已发生缺陷 |
| Medium | 300 卡片页面切换阻塞明显 | 本次性能表；优先虚拟化/减少模板成本 |
| Medium | “应用”分类与“全部应用”概念冲突；误建分类无法改名/删除 | Sidebar、CategoryManagementView；增加分类生命周期后再调整默认语义 |
| Medium | 分类子菜单不勾选当前分类 | MenuItem 未绑定 IsChecked；需打开信息才明确归属 |
| Medium | 仅拖放添加，键盘用户无法完成首次使用 | ContentView 只有 FileDrop，没有选择文件/粘贴入口 |
| Low | 主题按钮 Toast 仍笼统说“外观设置将在下一阶段提供” | SettingsView.xaml:14 → AppearanceCommand；背景已实现，应限定为主题切换 |
| Low | Popup Width360/380、Padding18、字号16/18，以及 Quick 的20/15/12等局部常量 | 按组件规范归并为语义 token，保留有意义层级，不盲目统一 |
| Low | Quick 阴影与 Management 高对比去阴影策略不一致 | 两套 Surface 模板；需统一高对比处理 |

没有发现重复可见 Settings/New Category 入口。旧阶段文档保留很多“当前规范”标题及早期行为，阅读时易误认为同时有效；建议后续统一“当前快照/历史记录”边界，本阶段不改其他文档。

## Accessibility and Shortcuts

| 范围 | 快捷键 / 行为 |
| --- | --- |
| 主窗口 | Ctrl+K 打开/聚焦 Quick Launcher；重复全选。仅窗口内 |
| ApplicationCard | Enter / Space 启动；Shift+F10 / Apps 打开菜单 |
| ContextMenu | 方向键、Enter、Esc；WPF 原生语义 |
| Quick Launcher | Up/Down 非循环选择；Enter 启动；Esc 关闭；Tab/Shift+Tab 面板内循环 |
| 重命名 | Enter 保存；Esc 取消 |
| 分类创建 | Enter 创建；Esc 取消并回新建按钮 |
| Profile 编辑 | Enter 保存；Esc 先取消编辑，非编辑时关闭 |
| 信息/移除 Popup | Esc 关闭；移除默认焦点在取消 |
| Sidebar | ListBox 方向键；Tab 在导航与头像间移动 |
| Sidebar Splitter | 焦点后方向键调整 |
| Settings | Space 切换 CheckBox；ComboBox/Slider 原生键盘操作 |
| 窗口原生系统行为 | 自定义 TitleBar 调用 SystemCommands；系统快捷键不属于 Nova 自定义热键 |

卡片、导航、标题栏按钮、搜索输入、资料、设置和 Slider 有 AutomationProperties.Name；Toast/错误使用 LiveSetting。Quick Launcher 保存并恢复可见可用控件焦点。核心浏览、启动、搜索、设置键盘链路较完整，初始导入不完整。

未发现本机 Ctrl+K/菜单快捷键冲突；Quick IME 组合期保留 Enter/Space/方向键/Esc。Rename/Profile/Category 编辑没有 Quick 同等级显式 IME 防护，依赖 WPF 原生事件，需补第三方输入法覆盖。Info Popup 没有和 Rename/Remove/Profile 一样的 TabNavigation=Cycle，焦点边界不一致。未做屏幕阅读器完整走查，不能以存在 AutomationProperties 就宣称全面无障碍达标。

## Known Limitations

DesktopBackdrop 透视不可靠；DesktopWallpaper 不是真透明且不支持每屏/动态壁纸；拼音多音字与生僻字有限；300 卡片非虚拟化布局慢；复杂 Blur/GPU 长时表现未覆盖；没有 Installer、正式 Logging、自动更新、托盘或系统级全局热键；分类只能创建；不支持脚本/URI/UWP 导入、同 EXE 多参数配置、路径修复/启动参数编辑；仅浅色主题；没有通用备份恢复 UI；多实例存在设置覆盖与统计少计；孤儿文件无回收；关闭最大化状态不持久化。

## Experimental Features

明确 Experimental 的是 **DesktopBackdrop / DesktopBackdropController**，仅保留内部系统材质实验路径，不应在发布说明中称为已稳定实现“桌面透视”。稳定图片背景、Quick Launcher、拼音不因有已知限制就一律归 Experimental；Theme 是 Placeholder，不能和 Experimental 混用。

## Dead Code Candidates

| 对象 | 判断 | 证据 |
| --- | --- | --- |
| 旧 Workspace SearchMode / SearchWrapPanel / Search Grid / 专用空状态 | 已删除 | WorkspaceMode 仅五种，App 当前源码无旧实现 |
| 旧收藏星标控件、Category Hover 按钮 | 已删除 | ApplicationCardView 仅菜单入口 |
| FavoriteButtonStyle | Dead Code | ShellTheme.xaml:273，仅定义，无引用 |
| CategoryPopupWidth | Dead Code | ShellTheme.xaml:310，仅定义 |
| IsCategoryPickerOpen | Dead Code 候选（高置信） | ApplicationCardViewModel.cs:51 定义，132/144 只写 false，View 不消费 |
| FavoriteGlyph / ToggleFavoriteCommand / ChangeCategoryCommand | Still Used | 当前菜单绑定 |
| Sidebar SettingsItem | Still Used | Profile 设置导航的内部状态 |
| Sidebar 旧 New Category 可见入口 | 已删除 | 新入口在 Settings，属于正常功能 |
| Settings AppearanceCommand | Still Used | 当前浅色主题 Placeholder 按钮和测试使用，文案过时 |
| ShellBackgroundView.BackgroundImage DP | Dead Code 候选 | 声明/访问器，无当前渲染消费 |
| Views.BackgroundMode.CustomImage | Dead Code 候选 | 旧枚举值，无当前生产绑定使用 |
| SolidBackground | Uncertain / 预留 | XAML 仍绑定，但未见生产赋值 |
| DesktopBackdropController | Still Used / Experimental | 随背景生命周期构造；不能按“默认禁用”直接删除 |
| SolidColorBackground / GlassTint fallback | Still Used | 稳定 Default 与实验回退仍依赖 |

本阶段未删除任何候选代码。

## Missing Feature Assessment

| 功能 | 优先级 | 理由 |
| --- | --- | --- |
| 系统托盘 | Should Have | 常驻启动器有价值，但当前普通窗口模式可交付 |
| 开机启动 | Nice To Have | 应在常驻/托盘策略明确后可选启用 |
| 全局系统快捷键 | Should Have | 从其他应用唤起更符合启动器日常用途；当前 Ctrl+K 只是窗口内 |
| 应用自动扫描 | Nice To Have | 手动导入可满足初版，扫描带来去重与误收录复杂度 |
| 拖动排序 | Nice To Have | 有存储序号，但不是发布安全前置条件 |
| 分类重命名/删除 | Should Have | 修正误建和长期整理的基础能力 |
| 深色模式 | Nice To Have | 当前浅色主题明确；先保证高对比适配 |
| 自动更新 | Nice To Have | 首个小规模可手动更新；之后再设计签名/回滚 |
| 备份/恢复 | Should Have | 用户图标/分类/资料有维护价值；升级前可恢复保障本身是发布准备必需工作 |
| 导入/导出 | Nice To Have | 便于迁移但需处理资源路径/打包语义 |
| 日志 | Must Have | 故障诊断和用户支持必需，当前 Trace 不足 |
| Crash Recovery | Should Have | 先实现不毁数据/可诊断/明确恢复，再考虑自动恢复 |
| 文件选择添加 | Should Have | 原始产品要求与纯键盘首次使用缺口 |
| 启动路径/参数编辑 | Should Have | 原始 Product 的程序移动修复场景未完成 |

这些是产品优先级，不是全部 Release Blockers。没有必要为了填分类强行将某项标 Not Necessary；以上候选均有合理场景。

## Technical Debt

| 优先级 | 项目 | 证据与完成判据 |
| --- | --- | --- |
| P0 | 本轮未确认数据库整体丢失或稳定可复现崩溃 | 不把推测当已知 P0；仍需故障恢复验收 |
| P1 | 多实例设置覆盖/统计少计 | App 无单实例互斥；AppSettingsRepository.cs:28 SetValues 整对象；ApplicationRepository.cs:124 绝对次数条件更新。先确定单实例或并发控制，并发复现不再覆盖才关闭 |
| P1 | 发布身份、品牌图标、版本与部署验收缺失 | App.csproj 无明确品牌/版本/publish profile；干净机器安装或便携运行、升级保留数据验证通过 |
| P1 | 正式日志与故障可恢复性 | Logging Missing；补稳定诊断、启动/关闭错误边界和升级备份策略 |
| P2 | Home/Category/Favorites/Recent 非虚拟化 | 300 条实测明显卡顿；减少视觉实例并保持菜单/键盘语义 |
| P2 | 坏行跳过无提示、统计写失败只 Trace | ApplicationRepository.cs:79 与 ApplicationPersistenceService.cs:58/147；返回恢复摘要和可理解反馈 |
| P2 | 高对比候选缺陷、键盘导入、分类生命周期 | 见 UI/Accessibility；实机证据与功能边界明确 |
| P2 | gitignore 对用户图片/临时文件覆盖不足 | 当前真实数据在仓库外，未发现泄露；未来复制入仓库易误提交 |
| P3 | 孤儿 PNG/.tmp 回收、备份保留策略 | 路径归属核验后清扫，避免删用户文件 |
| P3 | 实验背景与重复命名、旧资源/属性 | 先确认依赖，再独立清理 |
| P3 | 同寿命事件订阅、Profile 少量同步 I/O、token 归并 | 不影响当前单窗口正确性，但应降低未来重构风险 |

并发复现使用同一隔离 DB 的两个独立 AppSettingsService：A/B 都 Load(Home)，A 保存 Favorites，B 只修改 WindowWidth，重新读取结果却为 Home；10/100/300 三组均复现。它模拟多实例缓存边界，不是同时打开两个真实用户窗口。产品正常关闭会走同类整份设置保存，触发路径真实存在。

## Release Readiness

### Build / publish

| 检查 | 结果 |
| --- | --- |
| Release solution build | 通过，0 warnings / 0 errors |
| framework-dependent publish | 通过；本机生成发布目录 |
| win-x64 self-contained publish | 通过；本机生成发布目录 |
| win-x64 single-file + native extraction | 通过；exe 144,273,354 bytes，另有 PDB / ThirdParty 许可文件 |
| 发布包干净机器启动/SQLite/升级 | 未验证；不能把 publish 成功等同于可分发质量验收 |
| 正式可执行名 | 当前 NovaLauncher.App.exe，未设置为 NovaLauncher.exe |
| 正式发布目录/profile/脚本 | 仓库未建立；本次临时 publish 目录清理，不作为正式交付包 |
| Installer | **Missing**；未发现 MSIX/MSI/Setup.exe 工程，本阶段未制作 |

本次命令使用临时输出和临时 NuGetLockFilePath，没有改已提交锁文件。Single-file 明确设置 IncludeNativeLibrariesForSelfExtract=true，因此不承诺完全无磁盘提取；SQLite 原生库、许可、临时解压目录权限及升级均仍需目标环境验证。

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet publish NovaLauncher.App/NovaLauncher.App.csproj -c Release --no-restore --self-contained false -o artifacts/audit8/publish-fdd
dotnet publish NovaLauncher.App/NovaLauncher.App.csproj -c Release -r win-x64 --self-contained true -p:RestorePackagesWithLockFile=false -p:NuGetLockFilePath=D:/openAI/codex/NovarLauncher/artifacts/audit8/publish.lock.json -o artifacts/audit8/publish-scd
# 同上加 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true，输出 publish-single
```

当前常规产物仍在 `NovaLauncher.App/bin/Release/net9.0-windows`（以及本次 RID 构建的 win-x64 子目录）；有可执行程序不等于已有正式品牌发行包。

### Icon / version

App.csproj 未设置 ApplicationIcon；MainWindow 未指定 Icon，仓库无正式 .ico。Sidebar、标题栏自绘 N Logo 和默认应用卡片矢量图不等于 EXE/任务栏品牌图标。窗口/任务栏未明确配置正式 Nova 图标；EXE 使用 SDK 默认 apphost 资源，应列发布前任务，不把截图中的 N 当作证据。

实际生成 AssemblyVersion/FileVersion **1.0.0.0**，InformationalVersion **1.0.0+38f77dc…**，Product/Company/Title **NovaLauncher.App**。Settings 关于读取 InformationalVersion 去掉 `+` 后缀，因此显示 **1.0.0**；这是 SDK 默认，不代表产品已达到正式 1.0。

建议首个预发布显式 **0.1.0**，功能迭代 **0.x**，发布阻塞消除且支持范围验收后才 **1.0.0**；AssemblyVersion 可按兼容策略稳定，File/InformationalVersion 对应实际发布。当前没有独立打包版本配置，本阶段不修改。

### Git audit

`.gitignore` 已覆盖 bin/obj、*.db、WAL/SHM/journal、db-*、初始化锁、Cache/Icons、*.log、TestResults。尚未显式覆盖 CustomIcons、Profile、Background、整个 Cache/Logs、artifacts 和通用临时测试数据。真实用户数据位于仓库外，因此当前没有这些用户资源入库证据；不能承诺复制进仓库也不会误提交。

审计前 HEAD 与用户基线一致，工作区干净；全历史 21 个提交已检查，见附录。无 commit/push。收尾仅新增本报告，产品代码、Migration、数据库、锁文件和历史截图无最终差异。

## Release Blockers

| Blocker | 为什么真正阻碍正式发布 | 关闭条件 |
| --- | --- | --- |
| R1 多实例覆盖用户设置 | 可正常操作触发持久化状态回退，已隔离复现；不只是视觉瑕疵 | 单实例策略或正确并发字段更新/冲突处理；统计合并一并验证 |
| R2 正式发布身份与目标环境交付未验收 | 默认 1.0.0/内部产品名、缺品牌 App Icon；只有本机临时打包，没有可审查的正式制品和干净机器证据 | 明确版本/图标/名称/目标架构/分发方式，干净机器运行及升级保留数据通过 |

未发现“无法编译或无法 publish”阻塞，不应把它虚构成问题。Installer 缺失本身不必阻塞便携版；托盘、自动更新、深色模式、分类扩展、真实透明均不列硬阻塞。日志/升级恢复为发布准备高优先级要求；300 卡片性能是明显 P2，是否成为发布门槛取决于明确承诺的规模，不能把所有体验债一律升级 Blocker。

## Recommended Next Steps

建议下一阶段为 **8.1：发布安全与交付基线**，先处理一致性与可验证交付，不继续扩展功能。

1. **修复多实例一致性**：确定实例策略，防止关闭时覆盖其他实例设置和少计统计；保留最小并发回归。
2. **建立正式发布制品与验收流程**：Nova 品牌图标、名称、0.x 版本、win-x64 策略；干净机器运行、升级与用户数据保留。先选便携或安装方式，不默认必须做 Installer。
3. **补日志与恢复保障**：持久化诊断、统计写失败反馈、坏行恢复摘要、启动/退出错误边界、正常升级备份/恢复验证。
4. **优化普通页面卡片布局**：以本报告 10/100/300 基准为对照，优先虚拟化/模板实例成本，再评估图标和冷字典；保护键盘/菜单/投影语义。
5. **补关键可用性缺口**：分类重命名/删除、纯键盘添加入口、高对比实机问题；统一主题占位文案和当前分类反馈。

Experimental transparency 不进入 Top5：已有稳定图片背景，它的收益低于真实数据一致性、交付与可用性问题。

## Appendix A — Complete Git History

```text
15bde8a docs: initialize project documentation
db8e4d0 feat: create WPF project foundation
cecc947 feat: create launcher UI shell
a9bfe83 feat: create launcher UI shell
58bf6dc feat: add application drag and drop import
480f357 feat: add application launching and session statistics
5168131 feat: persist applications with SQLite
1a0ecdc chore: migrate database lifecycle to EF Core migrations
1dee8c1 feat: add application categories
01e83f3 feat: add application favorites
b8208d7 feat: add recent applications view
fb313ce feat: add aggregated home dashboard
08fe861 style: polish launcher UI and interactions
a8017ff feat: add local user profile
b176796 feat: add application settings
911ee64 feat: add background appearance customization
944776a feat: add application context menu and category management
f217def feat: add application management actions
b081a2b feat: add global application search
70d7e62 feat: add quick launcher overlay
38f77dc feat: add pinyin application search
```

## Appendix B — Reproduction and Boundaries

既有套件复现方式：`NovaLauncher.Verification/bin/Release/net9.0-windows/NovaLauncher.Verification.exe --<mode> <独立截图目录>`；Migration 单独 `--migrations`；导入不传模式。必须 Windows 交互式桌面，串行执行原生 UI 测试。不使用访问真实数据的 --real-*、preview、Test-UiShell 默认启动入口。

补充性能工具只为本次审计生成且已清理，故仓库没有可直接运行的永久 `--audit-performance` 参数。测量方法、输入规模、边界和原始数字已记录在 Performance；若下一阶段建立长期基准，应将同口径工具正式纳入测试体系，而不是将本次临时 harness 冒充已提交能力。

补充贯通流程同样为临时工具，不改变正式 Verification 入口；8/5 断言不并入既有 686 总数。未经实测的环境已明确列出，不以历史截图代替本次结果。

## Appendix C — Actual Local Storage Inventory

本次只读盘点（路径相对 %LOCALAPPDATA%\NovaLauncher）。目录不存在不等于功能未实现；当前未有 CustomIcons 和 Logs 目录。

```text
Background\
Background\background-97440e6ae65d45db9b840502503356f1.png (69448 bytes)
Cache\
Cache\Icons\
Cache\Icons\834c2641bbb54747af58375279a2146f.png (17064 bytes)
Cache\Icons\d49acd8f29c8448b85bcbcd35ec4e60f.png (17282 bytes)
Cache\Icons\e70b267d8aa54c0fb8064ed41f4ed92d.png (65514 bytes)
Cache\Icons\f6808029454b4986b8f730faac485703.png (10432 bytes)
Data\
Data\Backups\
Data\Backups\nova-before-app-edit-20261018T025507Z-fe20d09c1839423b913b80d1a611d81b.db (53248 bytes)
Data\Backups\nova-before-appearance-20261017T085916Z-c05710fb8f964c42b19ac449c7c3f592.db (53248 bytes)
Data\Backups\nova-before-categories-20261016T045508Z-58c282d6ddd644e499fe559449c8161d.db (28672 bytes)
Data\Backups\nova-before-categories-20261016T045508Z-58c282d6ddd644e499fe559449c8161d.db-shm (32768 bytes)
Data\Backups\nova-before-categories-20261016T045508Z-58c282d6ddd644e499fe559449c8161d.db-wal (0 bytes)
Data\Backups\nova-before-favorites-20261016T100726Z-8a5448360c524890af1a269d92d41410.db (53248 bytes)
Data\Backups\nova-before-favorites-20261016T100726Z-8a5448360c524890af1a269d92d41410.db-shm (32768 bytes)
Data\Backups\nova-before-favorites-20261016T100726Z-8a5448360c524890af1a269d92d41410.db-wal (0 bytes)
Data\Backups\nova-before-home-20261016T153952Z-f4cb6c845f7844c49a69ec703d04237a.db (53248 bytes)
Data\Backups\nova-before-migrations-20261016T0412249790147Z-84ce5e372ba147ebbac520cff2bf3ed0.db (16384 bytes)
Data\Backups\nova-before-profile-20261017T022554Z-de47ea20353c429cab17e90365c791e9.db (53248 bytes)
Data\Backups\nova-before-recent-20261016T151545Z-1c50c54efe2d41f4aa6dd15911d9c612.db (53248 bytes)
Data\Backups\nova-before-settings-20261017T030253Z-6773d16b8b354594b0a8f511724fc523.db (53248 bytes)
Data\nova.db (53248 bytes)
Data\nova.db-shm (32768 bytes)
Data\nova.db-wal (0 bytes)
Data\nova.db.initialization.lock (0 bytes)
Profile\
Profile\avatar-b5b47ad539554394918c3464d49058f0.png (131015 bytes)
```

数据保护复核：本次记录的 23 个真实数据文件与收尾 23 个文件，SHA256 对比差异 0。其中包含主数据库、WAL/SHM、备份、缓存图标、头像和背景。该哈希基线在前期隔离套件开始后采集；Management 套件另有真实数据前后只读比较，不能称为从首条工具调用起的全程取证。
