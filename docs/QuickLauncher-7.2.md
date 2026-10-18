# 7.2 Quick Launcher 交付记录

2026-10-18（用户时区）；基线 b081a2b。已通过用户最终验收，按授权提交 feat: add quick launcher overlay，不开始下一阶段。保持 .NET 9 / WPF / MVVM / SQLite，没有新增依赖、Schema 或 Migration。

## 结构与行为

QuickLauncherOverlay.xaml/.cs 位于 MainWindow 的内容覆盖层，QuickLauncherViewModel 由 ContentViewModel 持有并释放，共享已加载 Applications；不修改 WorkspaceMode、SelectedCategory、Sidebar 或原页面滚动容器。

顶部原 TextBox 改成 QuickSearchTrigger Button，仅调用 OpenCommand。唯一 Query 在 QuickLauncherViewModel。窗口内 Ctrl+K 打开并聚焦，重复按则全选；Settings 也直接打开，关闭后仍为 Settings。面板宽 580 DIP、水平居中、内容区顶边偏移 18%（约窗口上方四分之一），小窗口限制最大高度并滚动。

空 Query：最近使用按 LastLaunchTime DESC 取 6 项；没有最近使用则取收藏 6 项；都为空显示“输入应用名称开始搜索”。非空 Query 使用原 ApplicationSearchService 做 Trim 后的 OrdinalIgnoreCase Contains、完全/前缀/包含排序，同级最近启动时间、名称、Id，再取 Top8。服务本体未修改，卡片 VM 和模型实例复用。

Query 改变默认 SelectedIndex=0，无结果为 -1；↑↓不循环、边界 Clamp。源变化后保留仍存在的选中项，否则安全回到首项。Enter 和鼠标整行单击执行原 ApplicationCardViewModel.LaunchCommand；成功后关闭并清空，原 LaunchCount/LastLaunchTime/Home/Recent 更新链路不变。失败保留 Query、选择和错误提示。异步启动使用会话序号，旧请求完成不会关闭用户重新打开的面板。

Esc/外部点击/关闭按钮关闭，Tab 在面板内循环，关闭后尽量恢复原控件焦点。结果行只有图标、名称和分类，支持 Hover/Selected 与可访问名称；本版没有结果右键菜单。原 Workspace 卡片的完整管理功能不变。

## IME

View 观察 WPF TextComposition 的开始、更新和完成，并观察 WM_IME_STARTCOMPOSITION/WM_IME_ENDCOMPOSITION；不吞原生消息。组合期、ImeProcessed/DeadCharProcessed 和提交键未释放时不处理 Enter/Space/方向键/Esc，让输入法正常处理。普通命令键在组合完成后恢复。

本机安装 zh-CN / en-US。实际中文 IME 输入 weixin，组合文本为 wei'xin，Space 提交“微信”，面板保持打开、未启动应用；再次组合后 Enter 确认也未关闭或启动。补充 WPF 组合事件断言逐一验证 Enter、Space、Down、Up、Esc 未被处理。覆盖本机输入法；不宣称验证所有第三方 IME。截图来自 WPF，不包含系统候选窗口的独立 HWND。

## 旧代码清理

移除 WorkspaceMode.Search、ContentViewModel 的 SearchQuery/保存原页状态/SearchResults 投影、Sidebar.PrepareSearch、Workspace TextBox 键盘事件、搜索 Grid/专属空状态/SearchLabel，以及 SearchWrapPanel。旧 SearchVerification/--search 入口由 QuickLauncherVerification/--quick-launcher 替代；7.1 截图和历史文档保留。ApplicationSearchService 保持不变，没有两套搜索体验。

## 验证

Release Build：**0 warnings / 0 errors**，使用独立 OutputPath 避免锁定运行中的用户窗口。

快速启动专项 **69 项断言通过**：Ctrl+K、重复聚焦/全选、入口鼠标打开、居中与焦点恢复、Recent/Favorites/提示、中文/英文/数字及排序、Top8/默认选择/↑↓边界、空结果、真实 Enter 与鼠标启动、四类页面和 Settings 保持、Esc/外部点击、Hover、重命名/移除同步、真实 IME Space/Enter、三种背景、小窗口、300 应用、失败保留与异步会话保护。

既有回归共 **439 项断言通过**：Home29、Categories34、Favorites31、Recent22、Settings36、Profile44、Appearance39、Management46、Application Editing59、Launch32、Toast11、Polish19、Import/OLE37。连同专项共 **508 项主进程断言**，另含既有独立进程检查。保留正常窗口恢复、拖拽、图标、右键管理与数据保护行为；Home 300 卡片初次布局本轮 2466 ms，原性能边界不变。

300 应用测试（六次输入；毫秒）：

| 测量 | 最小 | 最大 |
| --- | ---: | ---: |
| 纯匹配/排序/Top8 | 0.057 | 0.618 |
| Query 更新、投影和同步绑定 | 0.96 | 21.85 |
| 后续布局和 Render yield | 3.75 | 26.11 |

同次输入的投影加布局最高约 43.76 ms；是墙钟测量，不是 GPU 帧率。最多 8 个结果视觉行，底层 Home 原卡片仍保留。存储 Load 次数保持 1，无输入时 SQLite 查询。没有额外 debounce。

复现（需要交互式 Windows 桌面，会操作测试窗口、IME、鼠标和启动探针）：

~~~powershell
dotnet build NovaLauncher.sln -c Release --no-restore -p:OutputPath=D:/openAI/codex/NovarLauncher/artifacts/7.2/build/
& artifacts/7.2/build/NovaLauncher.Verification.exe --quick-launcher docs/screenshots/7.2
~~~

测试使用隔离数据库和 LaunchProbe，示例“QQ/微信”等是探针的显示名称，未启动真实用户应用。DesktopWallpaper 搜索截图使用可复现的壁纸源，原 Appearance 回归另验证 Windows 壁纸 API。未实机切换混合 DPI 或高对比度。默认、CustomImage、DesktopWallpaper 均采用不透明文本 Surface；没有大幅模糊背景。技能按已读取 ui-ux-pro-max 内置规则执行检查，本机 Python 不可用，没有新增技能检索输出。

## 截图

实际 WPF RenderTargetBitmap 截图，已检查：

- [空 Query / 最近使用](screenshots/7.2/quick-empty-query.png)
- [Top8 搜索结果](screenshots/7.2/quick-results.png)
- [键盘选中第二项](screenshots/7.2/quick-keyboard-selection.png)
- [CustomImage / Settings 保持](screenshots/7.2/quick-custom-image.png)
- [DesktopWallpaper](screenshots/7.2/quick-desktop-wallpaper.png)
- [最小窗口](screenshots/7.2/quick-small.png)
- [中文组合输入](screenshots/7.2/quick-ime-composition.png)
- [提交“微信”后](screenshots/7.2/quick-ime-committed.png)

## 最终收尾（已验收）

用户确认全部验收项通过，并授权提交 feat: add quick launcher overlay。再次构建默认 Release 输出：**0 warnings / 0 errors**；沙箱内首次构建因 SDK 路径访问受限失败，允许访问后构建成功。

本轮重跑专项：**69 项断言全部通过**，包含原生中文 IME、快捷键与焦点、鼠标入口、选择/启动/关闭、底层页面保持、Top6/Top8 和内存搜索。首次运行在 Ctrl+K 焦点断言失败，未修改代码再次运行完整通过；具体瞬时原因未确定，保留该桌面交互复跑记录。此前 **439 项既有回归**通过记录仍适用，收尾没有修改业务代码。

300 应用本轮纯匹配排序 0.058–1.120 ms，投影/绑定 0.92–24.51 ms，后续布局 3.88–26.93 ms，同次投影加布局最高 35.30 ms；存储读取保持一次、视觉结果最多八行。已复核 ApplicationSearchService 未改动，旧 SearchMode/Workspace 搜索 Grid 已清理，Schema、Migration、持久化模型与依赖无变化。工作区仅包含本阶段代码、验证、截图与文档；最终测试临时截图清理后提交。
