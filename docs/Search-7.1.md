# 7.1 全局应用搜索交付记录

2026-10-17（用户时区）；基线 f217def。用户已验收并授权最终收尾提交 feat: add global application search，不开始下一阶段。技术栈、依赖、数据库 Schema、Migration 与背景系统保持不变。

## 7.1 最终收尾（已验收）

用户已验收并授权提交 feat: add global application search。再次构建 Release：**0 warnings / 0 errors**。默认输出因正在运行的 Nova 锁定 apphost 失败，改用独立 OutputPath 构建成功，保留用户窗口。使用该产物重跑搜索专项，**43 项断言全部通过**；300 应用六次查询加布局为 **210.0、84.3、8.3、99.1、145.5、16.5 ms**，保持已验收性能范围。

复核搜索入口显示/隐藏、Ctrl+K、空/非空 Query、Workspace 保存与恢复、Esc、原 LaunchCommand 启动首项、Sidebar、纯内存匹配/排序、重命名与移除同步、原卡片管理及三种背景。此前 **402 项现有回归**仍适用，收尾没有修改业务实现；Schema、Migration 和持久化模型无变化。工作区仅包含本阶段实现、验证、截图与文档；清理本轮独立构建及测试输出，按授权提交，不开始下一阶段。

## 实现

- 入口：ContentView 的 Workspace 顶部，320×40 DIP。Home、Category、Favorites、Recent 显示，Settings 隐藏。
- 状态：非空 Trim Query 进入 WorkspaceMode.Search，保存此前 Mode、Category 和 Home Dashboard 标记。清空、Clear、搜索框 Esc 恢复；Sidebar 保留原页面选中项。主动切换页面结束搜索。
- 匹配：只查内存中全部 ApplicationCardViewModel 的 DisplayName，OrdinalIgnoreCase Contains，支持中文、英文、数字。
- 排序：完全匹配 → 前缀 → 包含；同级 LastLaunchTime 降序 → DisplayName（OrdinalIgnoreCase）→ Id。
- Ctrl+K：MainWindow.PreviewKeyDown，仅窗口内；聚焦并全选。Settings 下通过 Sidebar 恢复最后应用页，无记录则 Home。
- Enter：搜索框 KeyDown → ContentViewModel.LaunchSearchResultAsync → 首项 LaunchCommand → ApplicationLaunchService。成功保留 Query/结果，原次数和时间写入流程保持不变。
- Esc：有文本时清空并恢复；无文本时清除键盘及逻辑焦点，不关闭窗口。Tab/Shift+Tab 支持访问结果并跨虚拟化视口；未加入 Up/Down 选择。
- ApplicationSearchService：新增纯内存匹配/排序服务，没有 SQLite 或进程依赖。结果增量同步已有卡片引用，原卡片右键菜单、自定义图标与管理命令继续复用。
- 性能：搜索专用 SearchWrapPanel 只呈现视口卡片，沿用尺寸 token 和自动换行，回收容器；普通浏览页布局不变。
- 数据：搜索不访问 SQLite，不新增 Migration、不持久化 Query。测试仅使用隔离数据和启动探针，没有启动真实 QQ/微信。

Global Search Pipeline：

Ctrl+K / SearchBox → SearchQuery → ApplicationSearchService → Application Collection → Match + Rank → Search Results → ApplicationCardView

## 验证

Windows 11、125% DPI、.NET SDK 9.0.318。最终 Release Build **0 warnings / 0 errors**。现有可执行验证工程，不声称运行 dotnet test。

搜索专项 **43 项断言通过**。包含紧凑入口、实际 Ctrl+K/全选、中文/英文/数字/大小写/Trim/Contains 与分级排序、空结果、Clear、两种 Esc、四类页面恢复和分类保留、Settings 恢复、Enter 真实进程与统计、Tab 访问、搜索菜单、收藏/分类、两类重命名、移除保留源文件、真实 OLE 匹配/不匹配导入、三种背景与窄窗口、300 条内存数据不重复读取、稳定顺序、视口实例数量、滚动至末项、Tab 跨视口、缩放、滚动后缩小结果回到顶部。

现有回归 **402 项断言通过**，合计 **445 项主进程断言**，另含既有独立进程检查：

| 套件 | 断言 |
| --- | ---: |
| Home | 29 |
| Categories | 34 |
| Favorites | 31 |
| Recent | 22 |
| Settings | 36 |
| Profile | 44 |
| Appearance | 39 |
| Management | 46 |
| Application Editing | 59 |
| Launch | 32 |
| Toast | 11 |
| Polish | 19 |

300 应用完整查询、布局及 Render yield 的六次测量：**230.0、103.5、32.7、83.3、143.7、24.3 ms**。多数低于 150 ms，第一次改变大量可见卡片约 230 ms，仍有优化空间，不宣称所有输入低于 100 ms。输入没有额外 debounce；存储 Load 次数为 1，卡片 ViewModel 与模型引用均保留。300 条结果仅实现视口内不足 40 个卡片，末页实测 20 个，最后一项可见。Home 原有 300 卡片首次布局本轮为 2518 ms，该历史性能问题不属于本次搜索优化。

实现过程中发现全量刷新约 600 ms，改为搜索视口渲染与增量投影；新增滚动断言发现回收容器未重新插入视觉树，已修复并通过末页、跨视口 Tab 与全部专项重验。初次 SDK 目录沙箱访问失败，使用所需构建权限后通过。

复现（需要交互式 Windows 桌面）：

~~~powershell
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.Verification -c Release --no-build -- --search docs/screenshots/7.1
~~~

现有回归分别传入 --home、--categories、--favorites、--recent、--settings、--profile、--appearance、--management、--app-edit、--launch、--toast、--polish，并指定独立截图输出目录。临时回归截图和日志已清理，保留本页交付截图。没有改变真实用户设置；Management 套件包含真实数据只读前后比较。

## 截图与边界

以下为实际运行 WPF 的 RenderTargetBitmap 截图，已逐张检查；测试名称使用启动探针的默认 Nova 图标，不是用户真实应用图标。DesktopWallpaper 搜索截图使用可复现的 WallpaperStub 图像；既有 Appearance 回归另验证 Windows 壁纸 API。未实机切换高对比度/混合 DPI，未对第三方中文输入法组合键作自动化验收。

- [Home 搜索入口](screenshots/7.1/home-search.png)
- [有结果及 Focus](screenshots/7.1/search-results.png)
- [无结果](screenshots/7.1/search-empty.png)
- [CustomImage 低遮罩背景](screenshots/7.1/search-custom-image.png)
- [DesktopWallpaper](screenshots/7.1/search-desktop-wallpaper.png)
- [最小窗口](screenshots/7.1/search-small.png)
